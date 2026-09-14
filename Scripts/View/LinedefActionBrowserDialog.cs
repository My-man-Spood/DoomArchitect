using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Configuration;
using Godot;

/// <summary>
/// Browse and pick a linedef action by number/title/category, ported from
/// UDB's real <c>ActionBrowserForm</c> - same live-filtered list shape as
/// <see cref="SectorSpecialBrowserDialog"/>, plus a third Category column
/// since real linedef actions actually have one (unlike sector specials,
/// which carry none at all). UDB's own real form also has a separate
/// "Generalized" tab for Boom generalized specials - deliberately not
/// built here, same reasoning as the Sector Special browser's own omitted
/// Generalized Effects support.
/// </summary>
public partial class LinedefActionBrowserDialog : AcceptDialog
{
	private LineEdit _filterEdit;
	private Tree _tree;

	private IReadOnlyList<LinedefActionInfo> _allActions = Array.Empty<LinedefActionInfo>();
	private readonly Dictionary<TreeItem, LinedefActionInfo> _actionByTreeItem = new();
	private Action<string> _onSelected;

	public override void _Ready()
	{
		_filterEdit = GetNode<LineEdit>("Container/FilterEdit");
		_tree = GetNode<Tree>("Container/Tree");

		_tree.Columns = 3;
		_tree.HideRoot = true;
		_tree.ColumnTitlesVisible = true;
		_tree.SetColumnTitle(0, "Action");
		_tree.SetColumnTitle(1, "Title");
		_tree.SetColumnTitle(2, "Category");

		_filterEdit.TextChanged += _ => RefreshList();
		_tree.ItemActivated += OnItemActivated;

		Confirmed += OnConfirmed;
	}

	public void Browse(IGameConfiguration gameConfiguration, string currentValue, Action<string> onSelected)
	{
		_allActions = gameConfiguration.GetLinedefActions();
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
		_actionByTreeItem.Clear();
		var root = _tree.CreateItem();

		foreach (var action in _allActions)
		{
			var combined = $"{action.Number} {action.Title} {action.Category}";
			if (filter.Length > 0 && !combined.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;

			var item = _tree.CreateItem(root);
			item.SetText(0, action.Number.ToString());
			item.SetText(1, action.Title);
			item.SetText(2, action.Category);
			_actionByTreeItem[item] = action;
		}
	}

	private void SelectCurrentValue(string currentValue)
	{
		if (!int.TryParse(currentValue.Trim(), out var number)) return;

		foreach (var (item, action) in _actionByTreeItem)
		{
			if (action.Number != number) continue;
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
		if (selected == null || !_actionByTreeItem.TryGetValue(selected, out var action)) return;

		_onSelected?.Invoke(action.Number.ToString());
		Hide();
	}
}
