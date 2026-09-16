using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Editing;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;

/// <summary>
/// The action-number + 5-fixed-argument-slot editor UDB's real
/// <c>ArgumentsControl</c> shares between its Linedef and Thing dialogs
/// (verified directly: UDB's own control has parallel <c>SetValue(Linedef,...)</c>/
/// <c>SetValue(Thing,...)</c> overloads funneling into the same internal
/// logic, and a Thing's own arg metadata comes from the exact same
/// <see cref="ActionInfo"/> table a Linedef's action number does) -
/// extracted here from what was originally <c>LinedefEditDialog</c>'s own
/// inline copy, once a second real consumer (the Thing dialog) needed the
/// identical behavior. Operates directly on <see cref="UniFields"/> (never
/// a <see cref="Linedef"/>- or <see cref="Thing"/>-specific member), the
/// same generalization shape <see cref="MapTagsEditor"/> already proved
/// out for its own two real consumers.
///
/// Each of the fixed 5 argument rows pairs a clickable label
/// (<see cref="ArgRow.LabelButton"/>) with *both* a
/// <see cref="StepperLineEdit"/> (plain numeric) and an
/// <see cref="OptionButton"/> (enum dropdown) - see this class's own
/// remarks inline at <see cref="ToggleArgView"/> for why a manual toggle
/// exists at all (Godot has no equivalent to UDB's real editable-combo-box
/// <c>ArgumentBox</c>). Action/arguments are always OK-only (no visual
/// effect to preview in either host dialog) - <see cref="BuildCommands"/>
/// is the only place anything is ever actually written, called once from
/// the host dialog's own OK handler.
/// </summary>
public partial class ActionArgumentsEditor : VBoxContainer
{
	private sealed record ElementSnapshot(long ActionNumber, long[] Args);

	private sealed record ArgRow(Button LabelButton, StepperLineEdit NumberEdit, OptionButton EnumEdit);

	private LineEdit _actionEdit;
	private Label _actionNameLabel;
	private Button _actionBrowseButton;
	private ArgRow[] _argRows;

	private LinedefActionBrowserDialog _actionBrowserDialog;

	private readonly HashSet<int> _touchedArgs = new();

	private IReadOnlyList<UniFields> _elements = Array.Empty<UniFields>();
	private List<ElementSnapshot> _snapshots = new();
	private IGameConfiguration _gameConfiguration;

	private ActionInfo _currentAction;
	private IReadOnlyList<ArgumentInfo> _currentArgInfos = DefaultArgInfos();

	public override void _Ready()
	{
		_actionEdit = GetNode<LineEdit>("ActionRow/ActionEdit");
		_actionNameLabel = GetNode<Label>("ActionRow/ActionNameLabel");
		_actionBrowseButton = GetNode<Button>("ActionRow/ActionBrowseButton");

		_argRows = new ArgRow[5];
		for (var i = 0; i < 5; i++)
		{
			var rowPath = $"ArgsGrid/Arg{i}Row";
			_argRows[i] = new ArgRow(
				GetNode<Button>($"{rowPath}/ArgLabel"),
				GetNode<StepperLineEdit>($"{rowPath}/ArgNumberEdit"),
				GetNode<OptionButton>($"{rowPath}/ArgEnumEdit"));
		}

		_actionEdit.TextChanged += _ => UpdateActionUi();
		_actionBrowseButton.Pressed += BrowseAction;

		for (var i = 0; i < 5; i++)
		{
			var slot = i;
			_argRows[slot].NumberEdit.TextChanged += _ => _touchedArgs.Add(slot);
			_argRows[slot].EnumEdit.ItemSelected += _ => _touchedArgs.Add(slot);
			_argRows[slot].LabelButton.Pressed += () => ToggleArgView(slot);

			// Godot's own theme-default "disabled" font color (used here purely
			// to block clicks on a non-toggleable label, not to signal "this
			// argument is invalid") reads far too dim to comfortably read a
			// perfectly normal, in-use argument's own title - override it to a
			// gentler dim than the theme default. Needs enough contrast against
			// the fully-bright, actually-clickable case to still read as "not
			// interactive" at a glance (0.85 turned out too close to full
			// brightness for that), while staying well short of the theme
			// default's much heavier dimming - distinct from (and stacking
			// under) the separate Modulate-based dimming UpdateActionUi already
			// applies for a genuinely *unused* slot.
			_argRows[slot].LabelButton.AddThemeColorOverride("font_disabled_color", new Color(1, 1, 1, 0.6f));
		}
	}

	private static IReadOnlyList<ArgumentInfo> DefaultArgInfos() =>
		Enumerable.Range(0, 5).Select(i => new ArgumentInfo($"Argument {i + 1}", Used: false, EnumOptions: null)).ToList();

	/// <summary>Sets up for a fresh selection - <paramref name="elements"/> is each selected linedef's/thing's own <see cref="UniFields"/> bag directly (the host projects <c>.Fields</c> itself), matching <see cref="MapTagsEditor"/>'s own established shape.</summary>
	public void Setup(IReadOnlyList<UniFields> elements, IGameConfiguration gameConfiguration)
	{
		_elements = elements;
		_gameConfiguration = gameConfiguration;

		_snapshots = elements.Select(fields => new ElementSnapshot(
			fields.GetInteger("special", 0),
			Enumerable.Range(0, 5).Select(i => fields.GetInteger($"arg{i}", 0)).ToArray())).ToList();

		_touchedArgs.Clear();

		_actionEdit.Text = SharedOrBlank(_snapshots.Select(s => (double)s.ActionNumber));
		UpdateActionUi();
	}

	/// <summary>
	/// Re-derives <see cref="_currentAction"/>/<see cref="_currentArgInfos"/>
	/// from whatever's currently typed in <see cref="_actionEdit"/> - a
	/// blank, unparsable, or unrecognized (including cross-selection
	/// "mixed") action number simply falls back to
	/// <see cref="DefaultArgInfos"/> (all 5 slots generic and disabled),
	/// same "blank means don't know/don't touch" convention as everywhere
	/// else in this project's dialogs.
	/// </summary>
	private void UpdateActionUi()
	{
		var text = _actionEdit.Text.Trim();
		_currentAction = long.TryParse(text, out var number) ? _gameConfiguration?.GetAction((int)number) : null;
		_currentArgInfos = _currentAction?.Args ?? DefaultArgInfos();
		_actionNameLabel.Text = _currentAction?.Title ?? "";

		for (var i = 0; i < 5; i++)
		{
			var info = _currentArgInfos[i];
			var row = _argRows[i];
			var isEnum = info.EnumOptions != null;

			row.LabelButton.Text = info.Title;
			row.LabelButton.Modulate = info.Used ? Colors.White : new Color(1, 1, 1, 0.5f);
			row.LabelButton.Disabled = !isEnum;
			row.LabelButton.MouseDefaultCursorShape = isEnum ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
			row.NumberEdit.Visible = !isEnum;
			row.EnumEdit.Visible = isEnum;
			row.NumberEdit.Editable = info.Used;
			row.EnumEdit.Disabled = !info.Used;

			if (isEnum)
			{
				row.EnumEdit.Clear();
				foreach (var option in info.EnumOptions!)
				{
					row.EnumEdit.AddItem(option.Title);
					row.EnumEdit.SetItemMetadata(row.EnumEdit.ItemCount - 1, option.Value);
				}
			}
		}

		RefreshArgValueDisplays();
	}

	/// <summary>
	/// Shows each selected element's own current stored <c>argN</c> value,
	/// shared-or-blank across the selection for a plain numeric slot; an
	/// enum slot only gets a pre-selected item when every selected element
	/// already agrees on a value that's actually one of that enum's real
	/// options (otherwise it's left showing the dropdown's own first item
	/// purely cosmetically - never written unless the user actually
	/// touches it, see <see cref="BuildCommands"/>).
	/// </summary>
	private void RefreshArgValueDisplays()
	{
		for (var i = 0; i < 5; i++)
		{
			var row = _argRows[i];
			var info = _currentArgInfos[i];
			var values = _snapshots.Select(s => s.Args[i]).ToList();
			if (values.Count == 0) continue;

			if (info.EnumOptions != null)
			{
				var shared = values.All(v => v == values[0]) ? (long?)values[0] : null;
				if (shared == null) continue;

				var matchIndex = -1;
				for (var idx = 0; idx < row.EnumEdit.ItemCount; idx++)
				{
					if (row.EnumEdit.GetItemMetadata(idx).AsInt64() != shared.Value) continue;
					matchIndex = idx;
					break;
				}

				if (matchIndex >= 0) row.EnumEdit.Selected = matchIndex;
			}
			else
			{
				row.NumberEdit.Text = SharedOrBlank(values.Select(v => (double)v));
			}
		}
	}

	/// <summary>
	/// Manually flips one argument row between its numeric and enum view -
	/// UDB's own real <c>ArgumentBox</c> is a single WinForms *editable*
	/// combo box (<c>ComboBoxStyle.DropDown</c>), so typing any raw integer
	/// always works even for an enum-backed argument (confirmed directly
	/// in <c>ArgumentBox.cs</c>/<c>EnumOptionHandler.cs</c> - an
	/// unrecognized typed value just becomes a synthesized one-off enum
	/// entry showing that raw number). Godot has no equivalent editable-
	/// combo control, so this reproduces the same end capability (type any
	/// exact number, even for an enum-backed arg) via two separate
	/// purpose-built controls and a manual switch between them instead of
	/// one hybrid control - same real capability, different control shape
	/// for this project's stack, flagged per this project's own "UDB is
	/// the north star" convention rather than left silent. A no-op when
	/// the argument has no enum options to toggle to at all
	/// (<see cref="ArgRow.LabelButton"/> is disabled in that case, so this
	/// should never actually fire then, but the plain-numeric-only guard
	/// stays here too as a direct safety net). Carries the value across
	/// the switch rather than resetting it: enum-to-number copies the
	/// exact currently-selected value (blank if nothing's selected, i.e.
	/// a mixed/blank multi-select state - never a fabricated zero);
	/// number-to-enum snaps to the *nearest* real enum value rather than
	/// requiring an exact match, since the whole point of allowing a typed
	/// number is that it may not be one of the named options.
	/// </summary>
	private void ToggleArgView(int slot)
	{
		var info = _currentArgInfos[slot];
		if (info.EnumOptions == null) return;

		var row = _argRows[slot];
		if (row.EnumEdit.Visible)
		{
			var selected = row.EnumEdit.Selected;
			row.NumberEdit.Text = selected >= 0 ? row.EnumEdit.GetItemMetadata(selected).AsInt64().ToString(CultureInfo.InvariantCulture) : "";
			row.NumberEdit.Visible = true;
			row.EnumEdit.Visible = false;
		}
		else
		{
			var typed = NumericFieldExpression.Resolve(row.NumberEdit.Text, 0.0);
			if (typed.HasValue)
			{
				var nearestIndex = FindNearestEnumIndex(row.EnumEdit, (long)Math.Round(typed.Value));
				if (nearestIndex >= 0) row.EnumEdit.Selected = nearestIndex;
			}

			row.EnumEdit.Visible = true;
			row.NumberEdit.Visible = false;
		}
	}

	private static int FindNearestEnumIndex(OptionButton enumEdit, long value)
	{
		var bestIndex = -1;
		var bestDistance = long.MaxValue;

		for (var idx = 0; idx < enumEdit.ItemCount; idx++)
		{
			var distance = Math.Abs(enumEdit.GetItemMetadata(idx).AsInt64() - value);
			if (distance >= bestDistance) continue;

			bestDistance = distance;
			bestIndex = idx;
		}

		return bestIndex;
	}

	/// <summary>
	/// Setting <see cref="LineEdit.Text"/> directly doesn't raise
	/// <c>TextChanged</c> (the same plain Godot behavior already relied on
	/// elsewhere, e.g. <see cref="SectorEditDialog.BrowseTexture"/>) - so
	/// the callback also calls <see cref="UpdateActionUi"/> itself,
	/// exactly reproducing what typing the action number by hand would
	/// have done (relabeling the 5 argument slots, in particular).
	/// </summary>
	private void BrowseAction()
	{
		_actionBrowserDialog ??= CreateActionBrowserDialog();
		_actionBrowserDialog.Browse(_gameConfiguration, _actionEdit.Text, number =>
		{
			_actionEdit.Text = number;
			UpdateActionUi();
		});
	}

	private LinedefActionBrowserDialog CreateActionBrowserDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/LinedefActionBrowserDialog.tscn").Instantiate<LinedefActionBrowserDialog>();
		AddChild(dialog);
		return dialog;
	}

	/// <summary>
	/// Never live-applied (no visual effect to preview in either host
	/// dialog) - resolved here, for the first time, against each
	/// element's original <c>Fields</c> value, matching every other
	/// OK-only field in this project's dialogs. A mixed/blank action
	/// field resolves to each element's own original action (never
	/// written), and since <see cref="_currentArgInfos"/> then falls back
	/// to all-generic-unused slots in that case too, no argument write is
	/// attempted either - a mixed selection's own individual action
	/// numbers (and whatever arguments belong to them) are simply left
	/// alone, exactly matching UDB's real practice of only editing what
	/// the dialog can actually make sense of.
	/// </summary>
	public IReadOnlyList<ICommand> BuildCommands()
	{
		var commands = new List<ICommand>();

		for (var e = 0; e < _elements.Count; e++)
		{
			var fields = _elements[e];
			var snapshot = _snapshots[e];

			var newAction = NumericFieldExpression.ResolveInteger(_actionEdit.Text, snapshot.ActionNumber) ?? snapshot.ActionNumber;
			if (newAction != snapshot.ActionNumber)
			{
				commands.Add(new SetFieldCommand(fields, "special", newAction == 0 ? null : new UniValue(UniversalType.Integer, newAction)));
			}

			for (var i = 0; i < 5; i++)
			{
				var row = _argRows[i];
				long newValue;

				// Reads whichever control is *currently visible*, not whether the
				// argument is statically enum-capable - a manual toggle (see
				// ToggleArgView) can put an enum-backed argument into number view,
				// and a typed custom value there must not be silently ignored.
				if (row.EnumEdit.Visible)
				{
					if (!_touchedArgs.Contains(i)) continue;
					newValue = row.EnumEdit.GetItemMetadata(row.EnumEdit.Selected).AsInt64();
				}
				else
				{
					newValue = NumericFieldExpression.ResolveInteger(row.NumberEdit.Text, snapshot.Args[i]) ?? snapshot.Args[i];
				}

				if (newValue == snapshot.Args[i]) continue;
				commands.Add(new SetFieldCommand(fields, $"arg{i}", newValue == 0 ? null : new UniValue(UniversalType.Integer, newValue)));
			}
		}

		return commands;
	}

	private static string SharedOrBlank(IEnumerable<double> values)
	{
		var list = values.ToList();
		return list.Count > 0 && list.All(v => v == list[0]) ? FormatNumber(list[0]) : "";
	}

	private static string FormatNumber(double value) =>
		value == Math.Floor(value) ? ((long)value).ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
}
