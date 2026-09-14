using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Configuration;
using Godot;

/// <summary>
/// Browse and pick a sector special by number and description, ported
/// from UDB's real <c>EffectBrowserForm</c> - a flat, live-filtered list
/// (verified against source: real sector specials carry no category at
/// all, unlike linedef actions, so there's nothing to group by - no tree
/// needed the way <see cref="TextureBrowserDialog"/>'s per-resource
/// grouping needed one). Single-click selects; double-click (or Enter, via
/// <see cref="Tree.ItemActivated"/>) confirms and closes exactly like OK -
/// the same interaction model <see cref="TextureBrowserDialog"/> already
/// established for this project's browse dialogs.
///
/// UDB's own on-screen field label is "Special:" even though its internal
/// code calls this concept "Effect" throughout (<c>EffectBrowserForm</c>,
/// window title "Edit Effect") - this project has only ever used
/// "Special" in user-facing text, so the column headers here say
/// "Special"/"Description" rather than reusing UDB's internal wording.
/// UDB's own real "Generalized Effects" tab is deliberately not built -
/// it only exists behind a <c>generalizedsectors</c> config flag neither
/// bundled Doom/Doom2 config enables (confirmed: only 2 of UDB's 51
/// shipped configs turn it on at all).
/// </summary>
public partial class SectorSpecialBrowserDialog : AcceptDialog
{
	private LineEdit _filterEdit;
	private Tree _tree;

	private IReadOnlyList<SectorSpecialInfo> _allSpecials = Array.Empty<SectorSpecialInfo>();
	private readonly Dictionary<TreeItem, SectorSpecialInfo> _specialByTreeItem = new();
	private Action<string> _onSelected;

	public override void _Ready()
	{
		_filterEdit = GetNode<LineEdit>("Container/FilterEdit");
		_tree = GetNode<Tree>("Container/Tree");

		_tree.Columns = 2;
		_tree.HideRoot = true;
		_tree.ColumnTitlesVisible = true;
		_tree.SetColumnTitle(0, "Special");
		_tree.SetColumnTitle(1, "Description");

		_filterEdit.TextChanged += _ => RefreshList();
		_tree.ItemActivated += OnItemActivated;

		Confirmed += OnConfirmed;
	}

	public void Browse(IGameConfiguration gameConfiguration, string currentValue, Action<string> onSelected)
	{
		_allSpecials = gameConfiguration.GetSectorSpecials();
		_onSelected = onSelected;

		_filterEdit.Text = "";
		RefreshList();
		SelectCurrentValue(currentValue);

		PopupCentered();
	}

	private void RefreshList()
	{
		var filter = _filterEdit.Text.Trim();

		_tree.Clear();
		_specialByTreeItem.Clear();
		var root = _tree.CreateItem();

		foreach (var special in _allSpecials)
		{
			var combined = $"{special.Number} {special.Title}";
			if (filter.Length > 0 && !combined.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

			var item = _tree.CreateItem(root);
			item.SetText(0, special.Number.ToString());
			item.SetText(1, special.Title);
			_specialByTreeItem[item] = special;
		}
	}

	private void SelectCurrentValue(string currentValue)
	{
		if (!int.TryParse(currentValue.Trim(), out var number)) return;

		foreach (var (item, special) in _specialByTreeItem)
		{
			if (special.Number != number) continue;
			item.Select(0);
			_tree.ScrollToItem(item);
			return;
		}
	}

	private void OnItemActivated() => ConfirmSelection();

	private void OnConfirmed() => ConfirmSelection();

	private void ConfirmSelection()
	{
		var selected = _tree.GetSelected();
		if (selected == null || !_specialByTreeItem.TryGetValue(selected, out var special)) return;

		_onSelected?.Invoke(special.Number.ToString());
		Hide();
	}
}
