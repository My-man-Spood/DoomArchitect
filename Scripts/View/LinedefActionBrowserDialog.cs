using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Configuration;
using Godot;

/// <summary>
/// Browse and pick a linedef action, ported from UDB's real
/// <c>ActionBrowserForm</c> - a filterable tree grouped by category
/// (<see cref="CategoryOrder"/>), matching UDB's own real grouping rather
/// than a single flat list. With the real GZDoom/Hexen action table now
/// authored close to its full ~190-action breadth (see
/// <c>Includes/ZDoomGeneric.cfg</c>), a flat list would mean scrolling
/// through all of them to find one - real UDB avoids exactly this by
/// grouping into category folders, collapsed by default so only the
/// categories you actually expand (or that the live filter matches) show
/// their actions. UDB's own real form also has a separate "Generalized"
/// tab for Boom generalized specials - deliberately not built here, same
/// reasoning as the Sector Special browser's own omitted Generalized
/// Effects support.
/// </summary>
public partial class LinedefActionBrowserDialog : AcceptDialog
{
	/// <summary>
	/// Real UDB category keys (verified against <c>Hexen_linedefs.cfg</c>'s
	/// own top-level block names/titles), in that file's own real order,
	/// paired with a short display title - these are plain taxonomy labels
	/// ("Door", "Floor", ...), not creative prose, so reusing UDB's own
	/// real category names here is the same kind of safe, objective reuse
	/// as a UDMF field name.
	/// </summary>
	private static readonly (string Key, string Title)[] CategoryOrder =
	{
		("misc", "Misc"),
		("use", "Use"),
		("door", "Door"),
		("floor", "Floor"),
		("ceiling", "Ceiling"),
		("stairs", "Stairs"),
		("pillar", "Pillar"),
		("platform", "Platform"),
		("polyobj", "Polyobjects"),
		("teleport", "Teleport"),
		("thing", "Thing"),
		("scroll", "Scroll"),
		("light", "Light"),
		("earthquake", "Earthquake"),
		("script", "Script"),
		("sector", "Sector"),
		("line", "Line"),
		("end", "End"),
	};

	private LineEdit _filterEdit;
	private Tree _tree;

	private IReadOnlyList<LinedefActionInfo> _allActions = Array.Empty<LinedefActionInfo>();
	private readonly Dictionary<TreeItem, LinedefActionInfo> _actionByTreeItem = new();
	private Action<string> _onSelected;

	public override void _Ready()
	{
		_filterEdit = GetNode<LineEdit>("Container/FilterEdit");
		_tree = GetNode<Tree>("Container/Tree");

		_tree.Columns = 2;
		_tree.HideRoot = true;
		_tree.ColumnTitlesVisible = true;
		_tree.SetColumnTitle(0, "Action");
		_tree.SetColumnTitle(1, "Title");
		_tree.SetColumnExpandRatio(0, 1);
		_tree.SetColumnExpandRatio(1, 3);

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

	/// <summary>
	/// Rebuilds the whole tree - one collapsible category folder per real
	/// action category (skipping any with zero matches), each containing
	/// its own actions sorted by number. Every category is collapsed by
	/// default with no filter typed (so opening the dialog never dumps all
	/// ~190 actions on screen at once); typing a filter auto-expands every
	/// category that still has a surviving match, so search results are
	/// immediately visible without a manual expand click.
	/// </summary>
	private void RefreshList()
	{
		var filter = _filterEdit.Text.Trim();
		var filtering = filter.Length > 0;

		_tree.Clear();
		_actionByTreeItem.Clear();
		var root = _tree.CreateItem();

		var byCategory = _allActions.ToLookup(a => a.Category);

		foreach (var (key, title) in CategoryOrder)
		{
			var actions = byCategory[key]
				.Where(a => !filtering || $"{a.Number} {a.Title} {title}".Contains(filter, StringComparison.OrdinalIgnoreCase))
				.OrderBy(a => a.Number)
				.ToList();
			if (actions.Count == 0) continue;

			var categoryItem = _tree.CreateItem(root);
			categoryItem.SetText(0, title);
			categoryItem.SetSelectable(0, false);
			categoryItem.SetSelectable(1, false);
			categoryItem.Collapsed = !filtering;

			foreach (var action in actions)
			{
				var item = _tree.CreateItem(categoryItem);
				item.SetText(0, action.Number.ToString());
				item.SetText(1, action.Title);
				_actionByTreeItem[item] = action;
			}
		}
	}

	private void SelectCurrentValue(string currentValue)
	{
		if (!int.TryParse(currentValue.Trim(), out var number)) return;

		foreach (var (item, action) in _actionByTreeItem)
		{
			if (action.Number != number) continue;
			item.GetParent().Collapsed = false;
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
