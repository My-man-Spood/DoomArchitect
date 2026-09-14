using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;

/// <summary>
/// Ports UDB's real <c>TagsSelector</c> control (verified against
/// <c>Source/Core/Controls/TagsSelector.cs</c> and its real usage in
/// <c>SectorEditFormUDMF.cs</c>, not guessed from icons) - a primary
/// "Tag N:" row (editable field + spinner + New/Unused/Clear) plus a
/// "Tags:" row of read-only clickable chips (one per extra tag) with
/// Clear All/Add/Remove buttons. Kept Sector-specific for now (scoped to
/// <see cref="Sector"/> directly, not a generic "tagged element"
/// interface) since Core has no such abstraction yet and there's no
/// Linedef dialog to justify inventing one before it's needed - UDB
/// itself shares this exact control between the Sector and Linedef
/// dialogs, so generalize this if/when that dialog gets built.
///
/// Multi-selection model matches UDB's real one exactly (its own
/// <c>List&lt;List&lt;int&gt;&gt;</c>/<c>SetValues</c>/<c>ApplyTo</c>): one tag
/// list per selected sector, edited by *slot index* in lockstep - typing
/// a value, New, Unused, or Clear writes into that same slot for every
/// selected sector's own list simultaneously. A slot's displayed value is
/// the first sector's own value at that index, unless another selected
/// sector disagrees there (including a sector whose own list doesn't
/// reach that index) - then it shows mixed. Slot count is always the
/// first sector's own tag count, matching UDB's real display logic.
///
/// UDB's real `&gt;=`/`&lt;=` (per-selection-position ascending/descending
/// range) and `++`/`--` (per-selection-position offset) tag-distribution
/// grammars are deliberately not ported - a distinct "assign each element
/// a different value based on its position in the collection" feature,
/// the same category <see cref="NumericFieldExpression"/>'s own
/// `+++`/`---` variant was already declined for the same reason. Typing
/// anything other than a blank field or a plain absolute integer here is
/// simply a no-op.
///
/// Never live-applied to the selected sectors (no visual effect to
/// preview, matching Special/Gravity's existing precedent) - all real
/// writes happen once, in <see cref="BuildCommands"/>, called from the
/// host dialog's own OK handler.
/// </summary>
public partial class SectorTagsEditor : VBoxContainer
{
	private Label _tagLabel;
	private StepperLineEdit _tagValueEdit;
	private Button _newButton;
	private Button _unusedButton;
	private Button _clearButton;
	private Button _clearAllButton;
	private Container _chipsContainer;
	private Button _chipButtonTemplate;
	private Button _addButton;
	private Button _removeButton;

	private IReadOnlyList<Sector> _sectors = Array.Empty<Sector>();
	private MapData _map;
	private List<List<long>> _originalTags = new();
	private List<List<long>> _workingTags = new();
	private int _activeSlot;

	public override void _Ready()
	{
		_tagLabel = GetNode<Label>("Row1/TagLabel");
		_tagValueEdit = GetNode<StepperLineEdit>("Row1/TagValueEdit");
		_newButton = GetNode<Button>("Row1/NewButton");
		_unusedButton = GetNode<Button>("Row1/UnusedButton");
		_clearButton = GetNode<Button>("Row1/ClearButton");
		_clearAllButton = GetNode<Button>("Row2/ClearAllButton");
		_chipsContainer = GetNode<Container>("Row2/ChipsContainer");
		_chipButtonTemplate = GetNode<Button>("Row2/ChipButtonTemplate");
		_addButton = GetNode<Button>("Row2/AddButton");
		_removeButton = GetNode<Button>("Row2/RemoveButton");

		_tagValueEdit.TextChanged += OnTagValueTextChanged;
		_newButton.Pressed += () => SetSlotValue(_activeSlot, TagAllocator.FindFree(_map.GetUsedTags()));
		_unusedButton.Pressed += () => SetSlotValue(_activeSlot, TagAllocator.FindFree(_map.GetUsedSectorTags()));
		_clearButton.Pressed += () => SetSlotValue(_activeSlot, 0);
		_clearAllButton.Pressed += ClearAllTags;
		_addButton.Pressed += AddTag;
		_removeButton.Pressed += RemoveActiveTag;
	}

	public void SetSectors(IReadOnlyList<Sector> sectors, MapData map)
	{
		_sectors = sectors;
		_map = map;
		_activeSlot = 0;

		_originalTags = sectors
			.Select(s => MapDataTagQueries.ParseTags(s.Fields).DefaultIfEmpty(0).ToList())
			.ToList();
		_workingTags = _originalTags.Select(list => new List<long>(list)).ToList();

		RefreshChips();
		RefreshActiveSlotDisplay();
	}

	/// <summary>Always the first selected sector's own tag count - matches UDB's real display logic exactly (see this class's own remarks).</summary>
	private int SlotCount => _workingTags.Count > 0 ? _workingTags[0].Count : 0;

	/// <summary>Null means "the selected sectors disagree here" (mixed) - shown as blank in the active-slot field and "???" on that slot's chip.</summary>
	private long? GetDisplayValue(int slot)
	{
		var reference = _workingTags[0][slot];
		for (var i = 1; i < _workingTags.Count; i++)
		{
			if (slot >= _workingTags[i].Count || _workingTags[i][slot] != reference) return null;
		}

		return reference;
	}

	/// <summary>Writes <paramref name="value"/> into every selected sector's own list at <paramref name="slot"/>, padding any sector whose list doesn't yet reach that far - real UDB assumes equal-length lists, this project's sectors aren't guaranteed to start with matching tag counts.</summary>
	private void SetSlotValue(int slot, long value)
	{
		foreach (var list in _workingTags)
		{
			while (list.Count <= slot) list.Add(0);
			list[slot] = value;
		}

		RefreshChips();
		RefreshActiveSlotDisplay();
	}

	private void OnTagValueTextChanged(string text)
	{
		var trimmed = text.Trim();
		if (trimmed.Length == 0) return;
		if (long.TryParse(trimmed, out var value)) SetSlotValue(_activeSlot, value);
	}

	/// <summary>Unused-among-this-element's-own-current-tags (not map-wide) - matches UDB's real <c>GetNewTag(existingTags)</c> call for Add; delegates to a map-wide New instead when the list is still just the default single `0` slot, also matching UDB's real shortcut.</summary>
	private void AddTag()
	{
		if (SlotCount == 1 && _workingTags[0][0] == 0)
		{
			SetSlotValue(0, TagAllocator.FindFree(_map.GetUsedTags()));
			return;
		}

		var existing = new HashSet<long>(_workingTags[0]);
		var newValue = TagAllocator.FindFree(existing);
		foreach (var list in _workingTags) list.Add(newValue);
		_activeSlot = SlotCount - 1;

		RefreshChips();
		RefreshActiveSlotDisplay();
	}

	private void RemoveActiveTag()
	{
		if (SlotCount <= 1) return;

		foreach (var list in _workingTags)
		{
			if (_activeSlot < list.Count) list.RemoveAt(_activeSlot);
		}

		_activeSlot = Math.Min(_activeSlot, SlotCount - 1);
		RefreshChips();
		RefreshActiveSlotDisplay();
	}

	private void ClearAllTags()
	{
		foreach (var list in _workingTags)
		{
			list.Clear();
			list.Add(0);
		}

		_activeSlot = 0;
		RefreshChips();
		RefreshActiveSlotDisplay();
	}

	private void RefreshActiveSlotDisplay()
	{
		_tagLabel.Text = $"Tag {_activeSlot + 1}:";
		_tagValueEdit.Text = GetDisplayValue(_activeSlot)?.ToString() ?? "";
		_removeButton.Disabled = SlotCount <= 1;
	}

	/// <summary>Rebuilt from scratch on every change, same pattern as <see cref="SectorEditDialog.RebuildFlagsCheckboxes"/> - each chip is a <see cref="Node.Duplicate"/> of the hidden <see cref="_chipButtonTemplate"/> so its styling stays editable in the Godot editor.</summary>
	private void RefreshChips()
	{
		foreach (var child in _chipsContainer.GetChildren()) child.QueueFree();

		for (var i = 0; i < SlotCount; i++)
		{
			var slot = i;
			var chip = (Button)_chipButtonTemplate.Duplicate();
			chip.Visible = true;
			chip.Text = GetDisplayValue(slot)?.ToString() ?? "???";
			chip.ButtonPressed = slot == _activeSlot;
			chip.Pressed += () =>
			{
				_activeSlot = slot;
				RefreshChips();
				RefreshActiveSlotDisplay();
			};
			_chipsContainer.AddChild(chip);
		}
	}

	/// <summary>
	/// One <see cref="SetFieldCommand"/> per changed field per sector,
	/// exactly the pattern <see cref="SectorEditDialog.OnConfirmed"/>
	/// already uses for every other OK-only field - called once here
	/// (not per-sector by the caller) since this control is already
	/// all-sectors-aware.
	/// </summary>
	public IReadOnlyList<ICommand> BuildCommands()
	{
		var commands = new List<ICommand>();

		for (var i = 0; i < _sectors.Count; i++)
		{
			var sector = _sectors[i];
			var original = _originalTags[i];
			var final = _workingTags[i];
			if (original.SequenceEqual(final)) continue;

			var oldPrimary = original.Count > 0 ? original[0] : 0;
			var newPrimary = final.Count > 0 ? final[0] : 0;
			if (newPrimary != oldPrimary)
			{
				commands.Add(new SetFieldCommand(sector.Fields, "id", newPrimary == 0 ? null : new UniValue(UniversalType.Integer, newPrimary)));
			}

			var oldExtra = string.Join(' ', original.Skip(1));
			var newExtra = string.Join(' ', final.Skip(1));
			if (newExtra != oldExtra)
			{
				commands.Add(new SetFieldCommand(sector.Fields, "moreids", newExtra.Length == 0 ? null : new UniValue(UniversalType.String, newExtra)));
			}
		}

		return commands;
	}
}
