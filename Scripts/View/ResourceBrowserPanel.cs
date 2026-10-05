using System;
using System.Collections.Generic;
using System.Linq;
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
/// The toolbar's "Add Script"/"Add Library" buttons and the context menu's
/// matching entries are a real, working skeleton (enabled/disabled by the
/// current selection's own kind, exactly as they'll need to be once wired
/// for real) but their actual behavior - creating a lump, a `#library`
/// template - is explicitly follow-up work, not part of this pass; both
/// currently just surface <see cref="ShowNotYetImplemented"/>. "Open"
/// (double-click or the context menu - see <see cref="OpenRequested"/>) is
/// real: a <c>MapGroup</c> node, a <c>SCRIPTS</c>/<c>ZSCRIPT</c> lump, or a
/// loose <c>.acs</c>/<c>.bcs</c>/<c>.zs</c>/<c>zscript</c> file - see
/// <see cref="BuildOpenRequest"/> for the exact gating. Deliberately
/// excludes anything living inside a <c>.pk3</c> archive this pass - no
/// write path exists for one anywhere in this codebase yet, and opening
/// something you can't save back is worse than not offering it; that's
/// also why <see cref="BuildOpenRequest"/> gates a loose file by whether
/// <see cref="IResourceContainer.ResolveAbsolutePath"/> actually resolves
/// it, which a <c>Pk3Container</c> entry never does.
/// </summary>
public partial class ResourceBrowserPanel : PanelContainer
{
	private Tree _tree;
	private Button _addScriptButton;
	private Button _addLibraryButton;
	private Button _collapseAllButton;
	private PopupMenu _contextMenu;
	private AcceptDialog _stubDialog;

	/// <summary>What one tree item actually is, for both the "currently open" highlight and the "Open" action - the node itself, which top-level resource it descends from, and that resource's own real source path.</summary>
	private readonly record struct TreeItemContext(ResourceTreeNode Node, IResourceContainer Container, string SourcePath);

	private readonly Dictionary<TreeItem, TreeItemContext> _itemContexts = new();

	/// <summary>The resources <see cref="Refresh"/> was last given - cached so <see cref="ClearCurrentMapHighlight"/> can re-show the same tree content with just a different (or no) "currently open" highlight, without needing a fresh resource list from whoever's asking.</summary>
	private IReadOnlyList<NamedResource> _lastResources = Array.Empty<NamedResource>();

	/// <summary>What right-clicking resolved the current context menu popup to - set fresh by <see cref="OnTreeItemMouseSelected"/> each time it opens, read by <see cref="OnContextMenuIdPressed"/> (wired once in <see cref="_Ready"/>, not re-subscribed per popup - a per-popup closure subscription would accumulate across every right-click instead of replacing the last one).</summary>
	private ResourceOpenRequest _contextMenuOpenRequest;

	private static readonly string[] OpenableLumpNames = { "SCRIPTS", "ZSCRIPT" };
	private static readonly string[] OpenableFileExtensions = { ".acs", ".bcs", ".zs" };

	/// <summary>The app's own established accent color (`Assets/BaseTheme.tres` - a `LineEdit` focus border and a `Button`'s pressed-icon tint both already use it) - reused here rather than inventing a new one, for the currently open map's own tree item.</summary>
	private static readonly Color OpenMapAccentColor = new(0.85f, 0.55f, 0.3f);

	/// <summary>Fired by a double-click (<see cref="Tree.ItemActivated"/>) or the context menu's own "Open" item, only when <see cref="BuildOpenRequest"/> actually resolved the clicked node to something openable - <c>AppShell</c> owns what "open" actually does for each kind (a new/focused Map tab, a new/focused script tab), since that's a cross-cutting tab-management concern this panel has no business deciding on its own.</summary>
	public event Action<ResourceOpenRequest> OpenRequested;

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
		// Off by default (confirmed via GodotSharp reflection) - without
		// it, right-clicking never updates Tree's own GetSelected() at
		// all, which made the context menu a silent no-op on every right-
		// click, not just an unselected one; set for the visual selection
		// highlight to correctly follow a right-click too, even though
		// OnTreeItemMouseSelected itself no longer depends on it (see its
		// own remarks).
		_tree.AllowRmbSelect = true;

		_tree.ItemSelected += OnTreeSelectionChanged;
		_tree.NothingSelected += OnTreeSelectionChanged;
		_tree.ItemMouseSelected += OnTreeItemMouseSelected;
		_tree.ItemActivated += OnTreeItemActivated;
		_addScriptButton.Pressed += ShowNotYetImplemented;
		_addLibraryButton.Pressed += ShowNotYetImplemented;
		_collapseAllButton.Pressed += CollapseAll;
		_contextMenu.IdPressed += OnContextMenuIdPressed;

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
		_lastResources = resources;
		_tree.Clear();
		_itemContexts.Clear();
		UpdateToolbarButtons(null);

		var root = _tree.CreateItem();
		foreach (var resource in resources)
		{
			AddTreeItem(root, resource.Container.BuildTree(resource.DisplayName), resource.Container, resource.SourcePath, currentMapContainer, currentMapName, currentMapWadPath);
		}
	}

	/// <summary>
	/// Re-shows the same tree content <see cref="Refresh"/> last built, with
	/// no "currently open" highlight - for when the active tab changes to
	/// something that isn't a loaded map at all (a Script tab, or no tab),
	/// which has no new resource list of its own to show but still needs
	/// whichever map *was* highlighted to stop looking open now that
	/// nothing actually is. Without this, closing or switching away from a
	/// map tab left its own tree item highlighted indefinitely - nothing
	/// ever told the browser that map wasn't open anymore, since this panel
	/// only ever hears about loads/resource changes, never about tab
	/// activation or closure.
	/// </summary>
	public void ClearCurrentMapHighlight() => Refresh(_lastResources, null, null, null);

	private void AddTreeItem(
		TreeItem parent, ResourceTreeNode node, IResourceContainer owningContainer, string ownerSourcePath,
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
		_itemContexts[item] = new TreeItemContext(node, owningContainer, ownerSourcePath);

		if (IsCurrentlyOpenMap(node, owningContainer, currentMapContainer, currentMapName, currentMapWadPath))
		{
			item.SetCustomColor(0, OpenMapAccentColor);
			item.SetTooltipText(0, "Currently open");
		}

		foreach (var child in node.Children)
		{
			AddTreeItem(item, child, owningContainer, ownerSourcePath, currentMapContainer, currentMapName, currentMapWadPath);
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
		foreach (var item in _itemContexts.Keys)
		{
			if (item.GetChildCount() > 0) item.Collapsed = true;
		}
	}

	private void OnTreeSelectionChanged() => UpdateToolbarButtons(CurrentSelection());

	private ResourceTreeNode CurrentSelection() => CurrentSelectionContext()?.Node;

	private TreeItemContext? CurrentSelectionContext()
	{
		var selected = _tree.GetSelected();
		return selected != null && _itemContexts.TryGetValue(selected, out var context) ? context : null;
	}

	/// <summary>"Add Script" only makes sense on a map's own group (it would create/open that one map's `SCRIPTS` lump); "Add Library" only on a whole container (a library isn't scoped to one map - see this project's own TODO notes on the real `#import` semantics).</summary>
	private void UpdateToolbarButtons(ResourceTreeNode selected)
	{
		_addScriptButton.Disabled = selected?.Kind != ResourceTreeNodeKind.MapGroup;
		_addLibraryButton.Disabled = selected?.Kind is not (
			ResourceTreeNodeKind.WadContainer or ResourceTreeNodeKind.Pk3Container or ResourceTreeNodeKind.DirectoryContainer);
	}

	/// <summary>
	/// Right-click context menu - a real "Open" alongside the "Add Script"/
	/// "Add Library" skeleton, gated by whichever node the click itself
	/// landed on. Resolved directly via <see cref="Tree.GetItemAtPosition"/>
	/// on the signal's own <paramref name="mousePosition"/>, not
	/// <see cref="CurrentSelectionContext"/>/<see cref="Tree.GetSelected"/> -
	/// confirmed <see cref="Tree.AllowRmbSelect"/> defaults to false, which
	/// meant a right-click never updated the Tree's own selection at all,
	/// making the whole menu a silent no-op on every right-click, not just
	/// an unselected one.
	/// </summary>
	private void OnTreeItemMouseSelected(Vector2 mousePosition, long mouseButtonIndex)
	{
		if (mouseButtonIndex != (long)MouseButton.Right) return;

		var item = _tree.GetItemAtPosition(mousePosition);
		if (item == null || !_itemContexts.TryGetValue(item, out var context)) return;
		var selected = context.Node;
		_contextMenuOpenRequest = BuildOpenRequest(context);

		_contextMenu.Clear();
		_contextMenu.AddItem("Open", 0);
		_contextMenu.SetItemDisabled(0, _contextMenuOpenRequest == null);
		_contextMenu.AddItem("Add Script", 1);
		_contextMenu.SetItemDisabled(1, selected.Kind != ResourceTreeNodeKind.MapGroup);
		_contextMenu.AddItem("Add Library", 2);
		_contextMenu.SetItemDisabled(2, selected.Kind is not (
			ResourceTreeNodeKind.WadContainer or ResourceTreeNodeKind.Pk3Container or ResourceTreeNodeKind.DirectoryContainer));

		_contextMenu.Popup(new Rect2I((Vector2I)DisplayServer.MouseGetPosition(), Vector2I.Zero));
	}

	private void OnContextMenuIdPressed(long id)
	{
		switch (id)
		{
			case 0:
				if (_contextMenuOpenRequest != null) OpenRequested?.Invoke(_contextMenuOpenRequest);
				break;
			case 1:
			case 2:
				ShowNotYetImplemented();
				break;
		}
	}

	private void OnTreeItemActivated()
	{
		var context = CurrentSelectionContext();
		if (context == null) return;

		var request = BuildOpenRequest(context.Value);
		if (request != null) OpenRequested?.Invoke(request);
	}

	/// <summary>
	/// What's openable this pass: a <see cref="ResourceTreeNodeKind.MapGroup"/>
	/// (always); a <see cref="ResourceTreeNodeKind.Lump"/> named
	/// <c>SCRIPTS</c>/<c>ZSCRIPT</c> (explicitly not <c>BEHAVIOR</c>, which
	/// is compiled ACS bytecode, not text - opening/editing/saving it as a
	/// <c>CodeEdit</c> would corrupt it); a <see cref="ResourceTreeNodeKind.File"/>
	/// with a recognized extension/name, only when
	/// <see cref="IResourceContainer.ResolveAbsolutePath"/> actually
	/// resolves it - which a <c>Pk3Container</c> entry never does, so a
	/// <c>.pk3</c>-embedded script is correctly excluded with no extra
	/// special-casing (see this class's own remarks on why that's out of
	/// scope this pass); or a <c>.wad</c>-named <c>File</c> - the real
	/// GZDoom/ZDoom convention of a folder resource's own <c>maps/MAP01.wad</c>,
	/// one little WAD per map, shown as a plain file leaf rather than
	/// expanded into its own lump structure (see
	/// <c>OpenMapMenu.OnMapOptionsConfirmed</c>'s own dedup) - opened the
	/// same way a top-level <c>MapGroup</c> is, by reading just far enough
	/// to find its own map name. Everything else - containers, folders,
	/// unrecognized file/lump names - returns null.
	/// </summary>
	private static ResourceOpenRequest BuildOpenRequest(TreeItemContext context)
	{
		var node = context.Node;

		switch (node.Kind)
		{
			case ResourceTreeNodeKind.MapGroup:
				return new ResourceOpenRequest { SourcePath = context.SourcePath, MapName = node.DisplayName };

			case ResourceTreeNodeKind.Lump when node.LumpIndex.HasValue
				&& OpenableLumpNames.Any(name => name.Equals(node.DisplayName, StringComparison.OrdinalIgnoreCase))
				&& context.Container is WadFile wad:
			{
				var lump = wad.Lumps[node.LumpIndex.Value];
				return new ResourceOpenRequest
				{
					SourcePath = context.SourcePath,
					LumpName = lump.Name,
					LumpIndex = node.LumpIndex.Value,
					LumpData = lump.Data,
				};
			}

			case ResourceTreeNodeKind.File when node.DisplayName.EndsWith(".wad", StringComparison.OrdinalIgnoreCase):
				return BuildNestedMapWadRequest(context);

			case ResourceTreeNodeKind.File when IsOpenableFileName(node.DisplayName):
			{
				var resolved = context.Container.ResolveAbsolutePath(node.Path);
				return resolved != null ? new ResourceOpenRequest { SourcePath = context.SourcePath, FilePath = resolved } : null;
			}

			default:
				return null;
		}
	}

	/// <summary>
	/// A nested <c>maps/MAP01.wad</c>-style file leaf resolves to the map
	/// it holds, not a loose file - <see cref="ResourceOpenRequest.SourcePath"/>
	/// becomes *this file's own* resolved path here (what
	/// <c>OpenMapMenu.OpenSpecificMap</c> actually needs to read), not the
	/// owning folder's path <paramref name="context"/> otherwise carries -
	/// the field's real meaning throughout is "the WAD to open", which for
	/// a top-level <c>MapGroup</c> happens to already be the owning
	/// resource's own path, but isn't here. Real I/O (reads just enough of
	/// the file to find its own map name(s), taking the first) - swallows
	/// a read failure as "not openable" rather than letting a stray or
	/// corrupt <c>.wad</c> sitting in a folder break the whole context
	/// menu/double-click action.
	/// </summary>
	private static ResourceOpenRequest BuildNestedMapWadRequest(TreeItemContext context)
	{
		var resolved = context.Container.ResolveAbsolutePath(context.Node.Path);
		if (resolved == null) return null;

		try
		{
			var wad = WadFile.Read(resolved);
			var mapName = wad.FindUdmfMapNames().Concat(wad.FindClassicMapNames()).FirstOrDefault();
			return mapName != null ? new ResourceOpenRequest { SourcePath = resolved, MapName = mapName } : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>A recognized script-file extension, or the bare GZDoom-convention ZScript entry-point name (with or without a <c>.txt</c> extension) - matches <c>zscript</c>/<c>zscript.txt</c> case-insensitively, same as every other name match in this codebase.</summary>
	private static bool IsOpenableFileName(string displayName)
	{
		var extension = System.IO.Path.GetExtension(displayName);
		if (OpenableFileExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase))) return true;

		var baseName = System.IO.Path.GetFileNameWithoutExtension(displayName);
		return baseName.Equals("zscript", StringComparison.OrdinalIgnoreCase)
			&& (extension.Length == 0 || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase));
	}

	private void ShowNotYetImplemented()
	{
		_stubDialog.DialogText = "Not implemented yet - this is a skeleton for a future pass.";
		_stubDialog.PopupCentered();
	}
}
