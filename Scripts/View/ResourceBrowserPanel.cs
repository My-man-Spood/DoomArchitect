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
	private PopupMenu _contextMenu;
	private AcceptDialog _stubDialog;

	private readonly Dictionary<TreeItem, ResourceTreeNode> _nodeByTreeItem = new();

	public override void _Ready()
	{
		_tree = GetNode<Tree>("Layout/Tree");
		_addScriptButton = GetNode<Button>("Layout/Toolbar/AddScriptButton");
		_addLibraryButton = GetNode<Button>("Layout/Toolbar/AddLibraryButton");
		_contextMenu = GetNode<PopupMenu>("ContextMenu");
		_stubDialog = GetNode<AcceptDialog>("StubDialog");

		_tree.Columns = 1;
		_tree.HideRoot = true;

		_tree.ItemSelected += OnTreeSelectionChanged;
		_tree.NothingSelected += OnTreeSelectionChanged;
		_tree.ItemMouseSelected += OnTreeItemMouseSelected;
		_addScriptButton.Pressed += ShowNotYetImplemented;
		_addLibraryButton.Pressed += ShowNotYetImplemented;
		_contextMenu.IdPressed += _ => ShowNotYetImplemented();

		UpdateToolbarButtons(null);
	}

	/// <summary>Clears and rebuilds the whole tree from scratch - one top-level item per resource, in the order given (the map's own WAD is always last in that order - see <c>OpenMapMenu</c>'s own remarks - so it naturally sorts to the bottom, matching it being the thing most recently/directly relevant).</summary>
	public void Refresh(IReadOnlyList<NamedResource> resources)
	{
		_tree.Clear();
		_nodeByTreeItem.Clear();
		UpdateToolbarButtons(null);

		var root = _tree.CreateItem();
		foreach (var resource in resources)
		{
			AddTreeItem(root, resource.Container.BuildTree(resource.DisplayName));
		}
	}

	private void AddTreeItem(TreeItem parent, ResourceTreeNode node)
	{
		var item = _tree.CreateItem(parent);
		item.SetText(0, node.DisplayName);
		item.SetIcon(0, ResourceTreeIcons.For(node));
		_nodeByTreeItem[item] = node;

		foreach (var child in node.Children) AddTreeItem(item, child);
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
