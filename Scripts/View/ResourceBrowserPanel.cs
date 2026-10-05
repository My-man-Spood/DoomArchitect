using System.Collections.Generic;
using DoomArchitect.Core.IO;
using Godot;

/// <summary>
/// The permanent, VSCode-style resource browser - one real, navigable tree
/// per currently-loaded <see cref="NamedResource"/> (a WAD/PK3/loose
/// folder), built fresh from each container's own <see cref="IResourceContainer.BuildTree"/>
/// every time <see cref="Refresh"/> is called (<see cref="AppShell"/> calls
/// it from <c>OpenMapMenu.MapLoaded</c>/<c>MapResourcesChanged</c>). Lives
/// outside the tab system entirely - this scene is only ever instantiated
/// once, as a permanent sibling of the tab content area, never itself a
/// tab.
///
/// This pass is deliberately scope-limited to visibility and structure:
/// the toolbar's "Add Script"/"Add Library" buttons and the context menu's
/// matching entries are a real, working skeleton (enabled/disabled by the
/// current selection's own kind, exactly as they'll need to be once wired
/// for real) but their actual behavior - creating a lump, a lump-backed
/// script document, a `#library` template - is explicitly follow-up work,
/// not part of this pass; both currently just surface
/// <see cref="ShowNotYetImplemented"/>.
/// </summary>
public partial class ResourceBrowserPanel : PanelContainer
{
	private Tree _tree;
	private Button _addScriptButton;
	private Button _addLibraryButton;
	private Button _collapseAllButton;
	private PopupMenu _contextMenu;
	private AcceptDialog _stubDialog;

	private readonly Dictionary<TreeItem, ResourceTreeNode> _nodeByTreeItem = new();

	/// <summary>The app's own established accent color (`Assets/BaseTheme.tres` - a `LineEdit` focus border and a `Button`'s pressed-icon tint both already use it) - reused here rather than inventing a new one, for the currently open map's own tree item.</summary>
	private static readonly Color OpenMapAccentColor = new(0.85f, 0.55f, 0.3f);

	public override void _Ready()
	{
		_tree = GetNode<Tree>("Layout/Tree");
		_addScriptButton = GetNode<Button>("Layout/Toolbar/AddScriptButton");
		_addLibraryButton = GetNode<Button>("Layout/Toolbar/AddLibraryButton");
		_collapseAllButton = GetNode<Button>("Layout/Toolbar/CollapseAllButton");
		_contextMenu = GetNode<PopupMenu>("ContextMenu");
		_stubDialog = GetNode<AcceptDialog>("StubDialog");

		_tree.Columns = 1;
		_tree.HideRoot = true;

		_tree.ItemSelected += OnTreeSelectionChanged;
		_tree.NothingSelected += OnTreeSelectionChanged;
		_tree.ItemMouseSelected += OnTreeItemMouseSelected;
		_addScriptButton.Pressed += ShowNotYetImplemented;
		_addLibraryButton.Pressed += ShowNotYetImplemented;
		_collapseAllButton.Pressed += CollapseAll;
		_contextMenu.IdPressed += _ => ShowNotYetImplemented();

		UpdateToolbarButtons(null);
	}

	/// <summary>
	/// Clears and rebuilds the whole tree from scratch - one top-level item
	/// per resource, in the given order (the map's own WAD is always last
	/// in that order - see <c>OpenMapMenu</c>'s own remarks - so it
	/// naturally sorts to the bottom, matching it being the thing most
	/// recently/directly relevant). <paramref name="currentMapContainer"/>/
	/// <paramref name="currentMapName"/>/<paramref name="currentMapWadPath"/>
	/// (all <c>OpenMapMenu.CurrentMapContainer</c>/<c>CurrentMapName</c>/
	/// <c>CurrentWadPath</c> - null when nothing's loaded yet, e.g. a brand
	/// new unsaved map) identify whichever item is the map actually open
	/// right now, so it can be visually marked - see
	/// <see cref="IsCurrentlyOpenMap"/> for the two ways that can match.
	/// </summary>
	public void Refresh(IReadOnlyList<NamedResource> resources, IResourceContainer currentMapContainer, string currentMapName, string currentMapWadPath)
	{
		_tree.Clear();
		_nodeByTreeItem.Clear();
		UpdateToolbarButtons(null);

		var root = _tree.CreateItem();
		foreach (var resource in resources)
		{
			AddTreeItem(root, resource.Container.BuildTree(resource.DisplayName), resource.Container, currentMapContainer, currentMapName, currentMapWadPath);
		}
	}

	private void AddTreeItem(
		TreeItem parent, ResourceTreeNode node, IResourceContainer owningContainer,
		IResourceContainer currentMapContainer, string currentMapName, string currentMapWadPath)
	{
		var item = _tree.CreateItem(parent);
		item.SetText(0, node.DisplayName);
		item.SetIcon(0, ResourceTreeIcons.For(node));
		// Collapsed by default, same as every level below it - matches a
		// VSCode Explorer's own starting state, and keeps a freshly loaded
		// map's full lump breakdown from dumping itself onto the screen
		// before the user has asked to see it.
		item.Collapsed = node.Children.Count > 0;
		_nodeByTreeItem[item] = node;

		if (IsCurrentlyOpenMap(node, owningContainer, currentMapContainer, currentMapName, currentMapWadPath))
		{
			item.SetCustomColor(0, OpenMapAccentColor);
			item.SetTooltipText(0, "Currently open");
		}

		foreach (var child in node.Children)
		{
			AddTreeItem(item, child, owningContainer, currentMapContainer, currentMapName, currentMapWadPath);
		}
	}

	/// <summary>
	/// Two different ways a tree item can be the currently open map,
	/// depending on whether its own WAD got a top-level entry here at all
	/// (see <c>OpenMapMenu.OnMapOptionsConfirmed</c>'s own dedup): a real
	/// top-level <see cref="ResourceTreeNodeKind.MapGroup"/>, matched by the
	/// exact same <see cref="IResourceContainer"/> instance
	/// <paramref name="currentMapContainer"/> is (not by path string, since
	/// that's the cheaper, unambiguous check available there); or a
	/// <see cref="ResourceTreeNodeKind.File"/> leaf nested inside a folder/
	/// PK3 (the deduped case - a nested <c>maps/MAP01.wad</c> doesn't get
	/// expanded into its own lump structure, just shown as a plain file),
	/// matched by resolving its own real on-disk path and comparing that
	/// against <paramref name="currentMapWadPath"/> instead, since there's
	/// no shared container instance to compare by reference there.
	/// </summary>
	private static bool IsCurrentlyOpenMap(
		ResourceTreeNode node, IResourceContainer owningContainer,
		IResourceContainer currentMapContainer, string currentMapName, string currentMapWadPath)
	{
		if (node.Kind == ResourceTreeNodeKind.MapGroup)
		{
			return currentMapName != null
				&& ReferenceEquals(owningContainer, currentMapContainer)
				&& node.DisplayName.Equals(currentMapName, System.StringComparison.OrdinalIgnoreCase);
		}

		if (node.Kind == ResourceTreeNodeKind.File && currentMapWadPath != null)
		{
			var resolved = owningContainer.ResolveAbsolutePath(node.Path);
			return resolved != null
				&& string.Equals(System.IO.Path.GetFullPath(resolved), System.IO.Path.GetFullPath(currentMapWadPath), System.StringComparison.OrdinalIgnoreCase);
		}

		return false;
	}

	private void CollapseAll()
	{
		foreach (var item in _nodeByTreeItem.Keys)
		{
			if (item.GetChildCount() > 0) item.Collapsed = true;
		}
	}

	private void OnTreeSelectionChanged() => UpdateToolbarButtons(CurrentSelection());

	private ResourceTreeNode CurrentSelection()
	{
		var selected = _tree.GetSelected();
		return selected != null && _nodeByTreeItem.TryGetValue(selected, out var node) ? node : null;
	}

	/// <summary>"Add Script" only makes sense on a map's own group (it would create/open that one map's `SCRIPTS` lump); "Add Library" only on a whole container (a library isn't scoped to one map - see this project's own TODO notes on the real `#import` semantics).</summary>
	private void UpdateToolbarButtons(ResourceTreeNode selected)
	{
		_addScriptButton.Disabled = selected?.Kind != ResourceTreeNodeKind.MapGroup;
		_addLibraryButton.Disabled = selected?.Kind is not (
			ResourceTreeNodeKind.WadContainer or ResourceTreeNodeKind.Pk3Container or ResourceTreeNodeKind.DirectoryContainer);
	}

	/// <summary>
	/// Right-click context-menu skeleton - the same two actions the
	/// toolbar offers, gated the same way by whichever node the click
	/// itself landed on (which <see cref="Tree.ItemMouseSelected"/>
	/// already selects before this fires, so <see cref="CurrentSelection"/>
	/// reflects the clicked item, not whatever was selected before).
	/// </summary>
	private void OnTreeItemMouseSelected(Vector2 mousePosition, long mouseButtonIndex)
	{
		if (mouseButtonIndex != (long)MouseButton.Right) return;

		var selected = CurrentSelection();
		if (selected == null) return;

		_contextMenu.Clear();
		_contextMenu.AddItem("Add Script", 0);
		_contextMenu.SetItemDisabled(0, selected.Kind != ResourceTreeNodeKind.MapGroup);
		_contextMenu.AddItem("Add Library", 1);
		_contextMenu.SetItemDisabled(1, selected.Kind is not (
			ResourceTreeNodeKind.WadContainer or ResourceTreeNodeKind.Pk3Container or ResourceTreeNodeKind.DirectoryContainer));

		_contextMenu.Popup(new Rect2I((Vector2I)DisplayServer.MouseGetPosition(), Vector2I.Zero));
	}

	private void ShowNotYetImplemented()
	{
		_stubDialog.DialogText = "Not implemented yet - this is a skeleton for a future pass.";
		_stubDialog.PopupCentered();
	}
}
