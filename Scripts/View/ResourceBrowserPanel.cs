using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.IO;
using DoomArchitect.Settings;
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
/// "Add Script" (toolbar button or context menu, on a <c>MapGroup</c>
/// with no <c>SCRIPTS</c> lump of its own yet - see
/// <see cref="AddScriptRequested"/>) is real too: creates that map's own
/// <c>SCRIPTS</c> lump, seeded with a small boilerplate rather than left
/// empty, and opens it. "Add Library" (a whole container, not scoped to
/// one map - a library isn't) is still a skeleton, surfacing
/// <see cref="ShowNotYetImplemented"/> - creating a real `#library`
/// template is separate follow-up work. "Open"
/// (double-click or the context menu - see <see cref="OpenRequested"/>) is
/// real: a <c>MapGroup</c> node, a <c>SCRIPTS</c>/<c>ZSCRIPT</c> lump, a
/// loose <c>.acs</c>/<c>.bcs</c>/<c>.zs</c>/<c>zscript</c>/<c>SCRIPTS</c>
/// file (on disk or inside a real <c>.pk3</c> zip archive, via
/// <see cref="Pk3File.WithReplacedEntry"/>/<see cref="Pk3Writer"/> - see
/// <see cref="BuildOpenRequest"/> for the exact gating). A nested
/// <c>maps/MAP01.wad</c>-style file *inside* a zipped <c>.pk3</c> (as
/// opposed to a loose folder, which already works via
/// <see cref="BuildNestedMapWadRequest"/>) is explicitly still out of
/// scope - not a meaningful real-world case for a pre-zipped pk3
/// distribution, and <see cref="IResourceContainer.ResolveAbsolutePath"/>
/// returning null for a <c>Pk3Container</c> entry already excludes it
/// with no extra special-casing needed.
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

	/// <summary>
	/// Which nodes were expanded right before the most recent <see cref="Refresh"/> -
	/// <see cref="Refresh"/> tears down and rebuilds every <see cref="TreeItem"/>
	/// from scratch every time (a map load, a resource change, opening/adding
	/// a lump, switching tabs), which used to silently collapse the whole
	/// tree back to its default state on every single one of those, even
	/// though nothing the user had drilled into actually changed. Keyed by
	/// <see cref="ExpandedKey"/>, not by any <see cref="TreeItem"/> - those
	/// don't survive a rebuild, the underlying node's own identity does.
	/// </summary>
	private readonly HashSet<string> _expandedKeys = new();

	/// <summary>What right-clicking resolved the current context menu popup to - set fresh by <see cref="OnTreeItemMouseSelected"/> each time it opens, read by <see cref="OnContextMenuIdPressed"/> (wired once in <see cref="_Ready"/>, not re-subscribed per popup - a per-popup closure subscription would accumulate across every right-click instead of replacing the last one).</summary>
	private ResourceOpenRequest _contextMenuOpenRequest;

	/// <summary>The raw context the current context menu popup landed on - "Add Script"/"Add Library" need the node/container/source path themselves, not an <see cref="ResourceOpenRequest"/> (which only ever gets built for the *Open*-shaped cases, see <see cref="BuildOpenRequest"/>).</summary>
	private TreeItemContext? _contextMenuContext;

	private static readonly string[] OpenableLumpNames = { "SCRIPTS", "ZSCRIPT" };
	private static readonly string[] OpenableFileExtensions = { ".acs", ".bcs", ".zs" };

	/// <summary>The app's own established accent color (`Assets/BaseTheme.tres` - a `LineEdit` focus border and a `Button`'s pressed-icon tint both already use it) - reused here rather than inventing a new one, for the currently open map's own tree item.</summary>
	private static readonly Color OpenMapAccentColor = new(0.85f, 0.55f, 0.3f);

	/// <summary>Fired by a double-click (<see cref="Tree.ItemActivated"/>) or the context menu's own "Open" item, only when <see cref="BuildOpenRequest"/> actually resolved the clicked node to something openable - <c>AppShell</c> owns what "open" actually does for each kind (a new/focused Map tab, a new/focused script tab), since that's a cross-cutting tab-management concern this panel has no business deciding on its own.</summary>
	public event Action<ResourceOpenRequest> OpenRequested;

	/// <summary>Fired by the "Add Script" toolbar button or context-menu item, only when it was actually enabled (a <c>MapGroup</c> with no <c>SCRIPTS</c> lump of its own yet) - <c>AppShell</c> owns the actual WAD write/tab-open, same separation of concerns as <see cref="OpenRequested"/>.</summary>
	public event Action<ResourceAddScriptRequest> AddScriptRequested;

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
		_addScriptButton.Pressed += OnAddScriptButtonPressed;
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
		SnapshotExpandedState();
		_tree.Clear();
		_itemContexts.Clear();
		UpdateToolbarButtons(null);

		var root = _tree.CreateItem();
		foreach (var resource in resources)
		{
			AddTreeItem(root, resource.Container.BuildTree(resource.DisplayName), resource.Container, resource.SourcePath, currentMapContainer, currentMapName, currentMapWadPath);
		}
	}

	/// <summary>Records every currently-expanded item's own identity (not the doomed-to-be-discarded <see cref="TreeItem"/> itself) so the imminent rebuild can restore it - see <see cref="_expandedKeys"/>.</summary>
	private void SnapshotExpandedState()
	{
		_expandedKeys.Clear();
		foreach (var (item, context) in _itemContexts)
		{
			if (!item.Collapsed && item.GetChildCount() > 0) _expandedKeys.Add(ExpandedKey(context.SourcePath, context.Node.Path));
		}
	}

	/// <summary>A node's own stable identity across a rebuild - the container it actually belongs to (already re-pointed past a nested WAD boundary by the time <see cref="AddTreeItem"/> reads this) plus its own relative path/lump-name within that container (null only for a container's own synthetic root, which <see cref="ResourceTreeNode.Path"/> never sets).</summary>
	private static string ExpandedKey(string sourcePath, string nodePath) => $"{sourcePath}\0{nodePath}";

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
		// A nested maps/MAP01.wad, expanded in place by DirectoryResource's
		// own ExpandNestedWads - its own Path is set (unlike the synthetic
		// top-level root WadFile.BuildTree produces, which never sets one),
		// so everything from here down needs to target the nested file
		// specifically, not the owning folder: Open/Add Script/the
		// "currently open" match all key off owningContainer/ownerSourcePath,
		// which this re-points once, right here, rather than needing any of
		// them to special-case it themselves.
		if (node.Kind == ResourceTreeNodeKind.WadContainer && node.Path != null)
		{
			var resolved = owningContainer.ResolveAbsolutePath(node.Path);
			if (resolved != null)
			{
				owningContainer = ResourceContainerCache.Open(resolved);
				ownerSourcePath = resolved;
			}
		}

		var item = _tree.CreateItem(parent);
		item.SetText(0, node.DisplayName);
		item.SetIcon(0, ResourceTreeIcons.For(node));
		// Collapsed by default, same as every level below it - matches a
		// VSCode Explorer's own starting state, and keeps a freshly loaded
		// map's full lump breakdown from dumping itself onto the screen
		// before the user has asked to see it - unless this exact node was
		// already expanded right before this rebuild (see _expandedKeys),
		// in which case it stays that way instead of silently re-collapsing
		// on every refresh.
		item.Collapsed = node.Children.Count > 0 && !_expandedKeys.Contains(ExpandedKey(ownerSourcePath, node.Path));
		_itemContexts[item] = new TreeItemContext(node, owningContainer, ownerSourcePath);

		if (IsCurrentlyOpenMap(node, owningContainer, ownerSourcePath, currentMapContainer, currentMapName, currentMapWadPath))
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
	/// Two different ways a tree item can be the currently open map: a
	/// real <see cref="ResourceTreeNodeKind.MapGroup"/> - a top-level one
	/// (see <c>OpenMapMenu.OnMapOptionsConfirmed</c>'s own dedup), matched
	/// by the exact same <see cref="IResourceContainer"/> instance
	/// <paramref name="currentMapContainer"/> is (the cheaper, unambiguous
	/// check available there); or one nested inside a
	/// <c>maps/MAP01.wad</c>-style file expanded by
	/// <see cref="DoomArchitect.Core.IO.PathTreeBuilder.ExpandNestedWads"/> -
	/// reference equality does NOT hold there even once
	/// <see cref="AddTreeItem"/> re-points <c>owningContainer</c> to that
	/// nested WAD, since <c>OpenMapMenu.PromptMapOptionsForPendingMap</c>
	/// deliberately reads that one map's own per-map WAD with a bare
	/// <c>WadFile.Read</c> (a fresh instance every time, by design - see
	/// its own remarks on texture-identity), never through the same
	/// <see cref="ResourceContainerCache"/> the tree's own re-point goes
	/// through - so this falls back to comparing <paramref name="ownerSourcePath"/>
	/// (that nested WAD's own resolved absolute path) against
	/// <paramref name="currentMapWadPath"/> instead, the same path-based
	/// identity the <see cref="ResourceTreeNodeKind.File"/> branch below
	/// already uses for the same underlying reason. A
	/// <see cref="ResourceTreeNodeKind.File"/> leaf - everything the
	/// nested expansion doesn't apply to (outside a top-level
	/// <c>maps/</c> folder, or one that failed to parse as a real WAD) -
	/// is matched by resolving its own real on-disk path the same way.
	/// </summary>
	private static bool IsCurrentlyOpenMap(
		ResourceTreeNode node, IResourceContainer owningContainer, string ownerSourcePath,
		IResourceContainer currentMapContainer, string currentMapName, string currentMapWadPath)
	{
		if (node.Kind == ResourceTreeNodeKind.MapGroup)
		{
			if (currentMapName == null || !node.DisplayName.Equals(currentMapName, System.StringComparison.OrdinalIgnoreCase)) return false;

			return ReferenceEquals(owningContainer, currentMapContainer)
				|| (currentMapWadPath != null && PathsEqual(ownerSourcePath, currentMapWadPath));
		}

		if (node.Kind == ResourceTreeNodeKind.File && currentMapWadPath != null)
		{
			var resolved = owningContainer.ResolveAbsolutePath(node.Path);
			return resolved != null && PathsEqual(resolved, currentMapWadPath);
		}

		return false;
	}

	private static bool PathsEqual(string a, string b) =>
		string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), System.StringComparison.OrdinalIgnoreCase);

	/// <summary>Selects/scrolls to the currently-open map's own node, reusing <see cref="IsCurrentlyOpenMap"/> - the exact same rule already driving that map's color-highlight.</summary>
	public void RevealActiveMap(IResourceContainer currentMapContainer, string currentMapName, string currentMapWadPath) =>
		RevealMatching(context => IsCurrentlyOpenMap(context.Node, context.Container, context.SourcePath, currentMapContainer, currentMapName, currentMapWadPath));

	/// <summary>Selects/scrolls to <paramref name="script"/>'s own node, by running <see cref="BuildOpenRequest"/> - the same resolution "Open" already uses - in reverse: searching for whichever node would produce a request matching this already-open tab.</summary>
	public void RevealActiveScript(ScriptDocument script) =>
		RevealMatching(context =>
		{
			var request = BuildOpenRequest(context);
			return request switch
			{
				{ FilePath: not null } => string.Equals(script.FilePath, request.FilePath, StringComparison.OrdinalIgnoreCase),
				{ Pk3EntryPath: not null } => script.IsPk3EntryFrom(request.SourcePath, request.Pk3EntryPath),
				{ MapName: null, LumpName: not null } => script.IsLumpFrom(request.SourcePath, request.LumpIndex),
				_ => false,
			};
		});

	/// <summary>Force-expands only the matched item's own ancestors (not a blanket expand-all), then selects and scrolls to it - a silent no-op when nothing matches (e.g. a loose script opened outside any configured resource).</summary>
	private void RevealMatching(Func<TreeItemContext, bool> predicate)
	{
		foreach (var (item, context) in _itemContexts)
		{
			if (!predicate(context)) continue;

			for (var ancestor = item.GetParent(); ancestor != null; ancestor = ancestor.GetParent())
			{
				ancestor.Collapsed = false;
			}

			item.Select(0);
			_tree.ScrollToItem(item);
			return;
		}
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

	/// <summary>"Add Script" only makes sense on a map's own group that doesn't already have one (it would create/open that one map's `SCRIPTS` lump - a second would be a nonsensical duplicate); "Add Library" only on a whole container (a library isn't scoped to one map - see this project's own TODO notes on the real `#import` semantics).</summary>
	private void UpdateToolbarButtons(ResourceTreeNode selected)
	{
		_addScriptButton.Disabled = selected?.Kind != ResourceTreeNodeKind.MapGroup || HasScriptsLump(selected);
		_addLibraryButton.Disabled = selected?.Kind is not (
			ResourceTreeNodeKind.WadContainer or ResourceTreeNodeKind.Pk3Container or ResourceTreeNodeKind.DirectoryContainer);
	}

	private static bool HasScriptsLump(ResourceTreeNode mapGroup) =>
		mapGroup != null && mapGroup.Children.Any(c => c.Kind == ResourceTreeNodeKind.Lump
			&& c.DisplayName.Equals("SCRIPTS", StringComparison.OrdinalIgnoreCase));

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
		_contextMenuContext = context;

		_contextMenu.Clear();
		_contextMenu.AddItem("Open", 0);
		_contextMenu.SetItemDisabled(0, _contextMenuOpenRequest == null);
		_contextMenu.AddItem("Add Script", 1);
		_contextMenu.SetItemDisabled(1, selected.Kind != ResourceTreeNodeKind.MapGroup || HasScriptsLump(selected));
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
				if (_contextMenuContext != null) RaiseAddScriptRequested(_contextMenuContext.Value);
				break;
			case 2:
				ShowNotYetImplemented();
				break;
		}
	}

	/// <summary>The toolbar button's own counterpart to the context menu's "Add Script" - acts on the tree's current selection (<see cref="CurrentSelectionContext"/>) rather than wherever a right-click landed.</summary>
	private void OnAddScriptButtonPressed()
	{
		var context = CurrentSelectionContext();
		if (context != null) RaiseAddScriptRequested(context.Value);
	}

	private void RaiseAddScriptRequested(TreeItemContext context) =>
		AddScriptRequested?.Invoke(new ResourceAddScriptRequest { WadPath = context.SourcePath, MapMarkerLumpIndex = context.Node.LumpIndex!.Value });

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
	/// scope this pass); a <c>.wad</c>-named <c>File</c> - the real
	/// GZDoom/ZDoom convention of a folder resource's own <c>maps/MAP01.wad</c>,
	/// one little WAD per map, shown as a plain file leaf rather than
	/// expanded into its own lump structure (see
	/// <c>OpenMapMenu.OnMapOptionsConfirmed</c>'s own dedup) - opened the
	/// same way a top-level <c>MapGroup</c> is, by reading just far enough
	/// to find its own map name; or a nested, already-expanded
	/// <c>maps/MAP01.wad</c> root (a <see cref="ResourceTreeNodeKind.WadContainer"/>
	/// with its own <see cref="ResourceTreeNode.Path"/> set - see
	/// <see cref="AddTreeItem"/>'s own re-point) - a one-map-per-file
	/// convention in practice, so double-clicking the WAD itself jumps
	/// straight to its first map rather than making that one extra
	/// unfold-then-click round trip to reach the single <c>MapGroup</c>
	/// child it almost always has. Everything else - other containers,
	/// folders, unrecognized file/lump names - returns null.
	/// </summary>
	private static ResourceOpenRequest BuildOpenRequest(TreeItemContext context)
	{
		var node = context.Node;

		switch (node.Kind)
		{
			case ResourceTreeNodeKind.MapGroup:
				return new ResourceOpenRequest { SourcePath = context.SourcePath, MapName = node.DisplayName };

			case ResourceTreeNodeKind.WadContainer when node.Path != null:
			{
				var firstMap = node.Children.FirstOrDefault(c => c.Kind == ResourceTreeNodeKind.MapGroup);
				return firstMap != null ? new ResourceOpenRequest { SourcePath = context.SourcePath, MapName = firstMap.DisplayName } : null;
			}

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
				// A Pk3Container entry has no standalone on-disk path to
				// resolve (ResolveAbsolutePath always returns null for one -
				// see Pk3File's own remarks), so it needs its own branch here
				// rather than falling through to the loose-file one below;
				// FindByPath already does the exact normalized-path lookup
				// and decompression this needs, no new container method
				// required.
				if (context.Container is Pk3File pk3)
				{
					var data = pk3.FindByPath(node.Path);
					return data != null
						? new ResourceOpenRequest { SourcePath = context.SourcePath, Pk3EntryPath = node.Path, Pk3EntryData = data }
						: null;
				}

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

	/// <summary>
	/// A recognized script-file extension, or a bare GZDoom-convention
	/// entry-point name (with or without a <c>.txt</c> extension) -
	/// matches <c>zscript</c>/<c>zscript.txt</c> and <c>scripts</c>/
	/// <c>scripts.txt</c> case-insensitively, same as every other name
	/// match in this codebase. The bare-<c>scripts</c> case was a
	/// pre-existing gap until the PK3 write-back pass (a PK3's own
	/// root-level <c>SCRIPTS</c> file, mirroring the WAD lump name
	/// convention, needs exactly this) - it equally fixes a loose
	/// <c>SCRIPTS</c> file sitting in a <c>DirectoryContainer</c>-backed
	/// folder mod, which was never openable either.
	/// </summary>
	private static bool IsOpenableFileName(string displayName)
	{
		var extension = System.IO.Path.GetExtension(displayName);
		if (OpenableFileExtensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase))) return true;

		var baseName = System.IO.Path.GetFileNameWithoutExtension(displayName);
		var isBareEntryPointName = baseName.Equals("zscript", StringComparison.OrdinalIgnoreCase)
			|| baseName.Equals("scripts", StringComparison.OrdinalIgnoreCase);
		return isBareEntryPointName
			&& (extension.Length == 0 || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase));
	}

	private void ShowNotYetImplemented()
	{
		_stubDialog.DialogText = "Not implemented yet - this is a skeleton for a future pass.";
		_stubDialog.PopupCentered();
	}
}
