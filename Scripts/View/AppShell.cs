using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.IO;
using DoomArchitect.Settings;
using Godot;

/// <summary>
/// The app's own root shell: a static, always-present <see cref="MainMenuBar"/>
/// (File/Edit/Map/Preferences - genuinely app-level chrome; actions like
/// "Save Map"/"Open Script..." should stay reachable no matter which tab
/// happens to be showing, which a menu bar living inside a map document's
/// own tab couldn't do - a real, reported bug: it disappeared along with
/// the rest of that tab's content whenever a Script tab was active) running
/// the full window width, with everything else below it split into two
/// columns, VSCode-Explorer-style: <see cref="_resourceBrowserPanel"/> on
/// the left, running the full remaining height; a tab strip
/// (<see cref="TabBar"/> - the plain label/icon/close-button strip, not
/// <c>TabContainer</c>, which only manages <see cref="Control"/>-type
/// children as pages - a map document's own root is a <c>Node3D</c>,
/// since only one <see cref="Camera3D"/> can be <see cref="Camera3D.Current"/>
/// across the whole viewport at a time and <c>CanvasLayer</c> content
/// renders independently of normal scene-tree visibility either way, so
/// hosting it needs <see cref="MapView.SetTabActive"/>'s own explicit
/// toggling regardless of which container manages the strip) confined to
/// the right of the browser's own column, above a shared content area
/// occupying the same column below it. The menu bar and the strip both
/// live on their own <c>CanvasLayer</c>s (15 and 20 respectively, both
/// above every map document's own "UI" layer at 10) so they reliably
/// render in front rather than fighting a map's own toolbar for draw
/// order. <see cref="AlignMapToolbarBelowTabStrip"/> pushes that map's own
/// "TopBar" (now just the toolbar) down by the menu bar's and strip's
/// combined height, measured fresh rather than a guessed constant, so it
/// never fights either of them for the same screen-space position
/// <c>CanvasLayer</c> content would otherwise land at by default - and
/// pushes <see cref="_resourceBrowserPanel"/> down by only the menu bar's
/// own height, not the strip's too, since the browser's own column runs
/// past the strip rather than starting below it (see
/// <see cref="UpdateContentAreaLeftOffset"/> for the strip's own matching
/// horizontal offset, kept past the browser's own column rather than
/// spanning the full window width above it).
///
/// More than one Map tab can exist at once - each gets its own
/// <see cref="SubViewportContainer"/>/<see cref="SubViewport"/> wrapper
/// (tracked in <see cref="_mapViewportContainers"/>, keyed by the
/// <see cref="MapView"/> it hosts), since two <see cref="MapView"/>s can't
/// safely share one <see cref="SubViewport"/> - each owns its own
/// <c>TopDownCamera</c>/<c>PerspectiveCamera</c>, and only one
/// <see cref="Camera3D"/> can be <see cref="Camera3D.Current"/> per
/// <see cref="Viewport"/> at a time. Every tab (Map or Script) is
/// closable, including closing down to zero tabs entirely - there's no
/// "no map open" empty-state UI, a plain background is the accepted
/// state. <see cref="MainMenuBar.SetActiveMap"/> is re-pointed at
/// whichever Map tab is actually active (or cleared to null when none
/// is) every time the active tab changes, so "Save Map"/"Map Options..."/
/// etc. always act on whichever map the user is actually looking at - see
/// <see cref="Activate"/>/<see cref="Deactivate"/>/<see cref="OnTabClosePressed"/>.
/// </summary>
public partial class AppShell : Control
{
	private const string MapDocumentScenePath = "res://Scenes/MapDocument.tscn";
	private const string ScriptDocumentScenePath = "res://Scenes/UI/ScriptDocument.tscn";

	private Control _menuBarPanel;
	private MainMenuBar _mainMenuBar;
	private TabBar _tabBar;
	private Control _tabStripPanel;
	private Control _contentArea;
	private ResourceBrowserPanel _resourceBrowserPanel;
	private TextureButton _browserToggleButton;
	private FileDialog _openScriptDialog;

	/// <summary>Each Map tab's own SubViewport wrapper - see this class's own remarks.</summary>
	private readonly Dictionary<MapView, SubViewportContainer> _mapViewportContainers = new();

	private readonly List<Node> _tabContents = new();
	private int _activeTab = -1;

	private bool _immersive3DEnabled;
	private bool _isImmersive;
	private bool _resourceBrowserVisibleBeforeImmersive;
	private bool _browserToggleHovered;
	private bool _browserPanelHovered;

	public override void _Ready()
	{
		_menuBarPanel = GetNode<Control>("MenuBarLayer/MenuBarPanel");
		_mainMenuBar = GetNode<MainMenuBar>("MenuBarLayer/MenuBarPanel/MenuBar");
		_tabBar = GetNode<TabBar>("TabStripLayer/TabStripPanel/TabBar");
		_tabStripPanel = GetNode<Control>("TabStripLayer/TabStripPanel");
		_contentArea = GetNode<Control>("ContentArea");
		_resourceBrowserPanel = GetNode<ResourceBrowserPanel>("ResourceBrowserPanel");
		_browserToggleButton = GetNode<TextureButton>("BrowserToggleLayer/BrowserToggleButton");
		_openScriptDialog = GetNode<FileDialog>("OpenScriptDialog");

		_tabBar.TabClosePressed += OnTabClosePressed;
		_tabBar.TabChanged += OnTabChanged;
		_tabBar.TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowActiveOnly;
		_openScriptDialog.FileSelected += OnScriptFileSelected;

		_mainMenuBar.BuildMenus();
		_mainMenuBar.OpenScriptRequested = () => _openScriptDialog.PopupCentered();
		// No existing OpenMapMenu instance to act on when no Map tab is
		// active (one lives inside each MapView's own scene, nowhere
		// else) - create a fresh, blank one first, then show the dialog
		// the user actually asked for on it.
		_mainMenuBar.CreateMapTabRequested = showOpenDialog =>
		{
			var mapView = CreateMapViewTab();
			// Deferred - same reasoning as MainMenuBar.RunWithDiscardConfirmationIfDirty's
			// own remarks: this whole lambda still runs synchronously inside
			// the File menu's own IdPressed, so showing either dialog here
			// directly is exposed to the exact same wrong-monitor risk.
			if (showOpenDialog) Callable.From(mapView.OpenMapMenu.ShowOpenFileDialog).CallDeferred();
			else Callable.From(mapView.OpenMapMenu.ShowNewMapDialog).CallDeferred();
		};

		_browserToggleButton.Pressed += OnToggleResourceBrowser;
		_browserToggleButton.MouseEntered += () => SetBrowserToggleHover(true);
		_browserToggleButton.MouseExited += () => SetBrowserToggleHover(false);
		_resourceBrowserPanel.MouseEntered += () => SetBrowserPanelHover(true);
		_resourceBrowserPanel.MouseExited += () => SetBrowserPanelHover(false);
		_resourceBrowserPanel.OpenRequested += OnResourceOpenRequested;
		_resourceBrowserPanel.AddScriptRequested += OnAddScriptRequested;
		Resized += UpdateBrowserToggleButton;

		_immersive3DEnabled = AppSettingsFile.Load().GetImmersive3DView();
		_mainMenuBar.Immersive3DViewToggled = enabled =>
		{
			_immersive3DEnabled = enabled;
			UpdateMapViewportLayout();
		};

		CreateMapViewTab();
		CallDeferred(MethodName.UpdateContentAreaLeftOffset);
	}

	/// <summary>
	/// Lives outside any one tab's own input handling on purpose - the
	/// resource browser is a permanent, tab-independent fixture (see this
	/// class's own remarks), so its own toggle keybinding belongs at the
	/// level that actually owns it, not duplicated into every tab type
	/// that might otherwise want to poll for it.
	/// </summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("toggle_resource_browser")) OnToggleResourceBrowser();
	}

	private void OnToggleResourceBrowser()
	{
		_resourceBrowserPanel.Visible = !_resourceBrowserPanel.Visible;
		UpdateContentAreaLeftOffset();
		UpdateMapViewportLayout();
	}

	/// <summary>
	/// A single collapse-and-reopen control, living outside the browser
	/// (on its own <c>CanvasLayer</c>, deliberately above the base layer
	/// everything else here sits on - the real bug an earlier, in-panel-
	/// only collapse button had: hiding the whole panel took its own
	/// toggle button down with it, leaving only the undiscoverable
	/// keybinding; a second, separately-added button fixed reachability
	/// but, being a plain sibling <c>Control</c> added to the tree before
	/// a Map tab's own code-created <c>SubViewportContainer</c>, still
	/// ended up drawn *behind* it once the map view expanded to reclaim
	/// the browser's space - a real, reported bug, not a guess). Tracks
	/// the browser's own right edge (so it sits just outside it when
	/// open, and at the screen's left edge when closed), vertically
	/// centered on the browser's own height, and - per request - reads as
	/// an almost-invisible edge handle rather than a normal toolbar
	/// button: nearly transparent at rest, fading to full opacity while
	/// either it or the browser itself is hovered (see
	/// <see cref="SetBrowserToggleHover"/>/<see cref="SetBrowserPanelHover"/>).
	/// Hidden entirely in immersive mode, same as every other piece of
	/// chrome that mode hides.
	/// </summary>
	private void UpdateBrowserToggleButton()
	{
		_browserToggleButton.Visible = !_isImmersive;

		const float buttonWidth = 16f;
		const float buttonHeight = 48f;
		var topOfBrowser = _menuBarPanel.Size.Y;
		var centerY = (topOfBrowser + Size.Y) / 2f;

		_browserToggleButton.OffsetLeft = _resourceBrowserPanel.Visible ? _resourceBrowserPanel.Size.X : 0;
		_browserToggleButton.OffsetRight = _browserToggleButton.OffsetLeft + buttonWidth;
		_browserToggleButton.OffsetTop = centerY - buttonHeight / 2f;
		_browserToggleButton.OffsetBottom = centerY + buttonHeight / 2f;
		_browserToggleButton.FlipH = !_resourceBrowserPanel.Visible;
	}

	private void SetBrowserToggleHover(bool hovered)
	{
		_browserToggleHovered = hovered;
		UpdateBrowserToggleOpacity();
	}

	private void SetBrowserPanelHover(bool hovered)
	{
		_browserPanelHovered = hovered;
		UpdateBrowserToggleOpacity();
	}

	/// <summary>Near-invisible at rest (0.25 alpha) - only reveals itself at full opacity while the pointer is actually over the toggle or the browser it controls, per request; a short tween rather than an instant snap so it reads as a deliberate reveal, not a flicker.</summary>
	private void UpdateBrowserToggleOpacity()
	{
		var revealed = _browserToggleHovered || _browserPanelHovered;
		CreateTween().TweenProperty(_browserToggleButton, "modulate:a", revealed ? 1f : 0.25f, 0.12);
	}

	/// <summary>
	/// Reclaims the resource browser's own real measured width for
	/// <see cref="_contentArea"/> and <see cref="_tabStripPanel"/> when
	/// collapsed, gives it back when expanded - never a hand-picked
	/// constant, same reasoning as <see cref="AlignMapToolbarBelowTabStrip"/>'s
	/// own vertical offset. The tab strip only ever starts past the
	/// browser's own column, not above it - the browser itself runs the
	/// full height below the menu bar (see <see cref="AlignMapToolbarBelowTabStrip"/>'s
	/// own <c>_resourceBrowserPanel.OffsetTop</c>), so it would otherwise
	/// sit underneath a full-width tab strip rather than beside it. Each
	/// Map tab's own viewport container has its own, separate layout
	/// logic in <see cref="UpdateMapViewportLayout"/>.
	/// </summary>
	private void UpdateContentAreaLeftOffset()
	{
		var browserWidth = _resourceBrowserPanel.Visible ? _resourceBrowserPanel.Size.X : 0;
		_contentArea.OffsetLeft = browserWidth;
		_tabStripPanel.OffsetLeft = browserWidth;
	}

	/// <summary>
	/// Docks the *active* Map tab's own viewport container beside the
	/// resource browser and below the tab strip/menu bar - or, in
	/// immersive mode, expands it to the full window and hides everything
	/// else - re-run on every input that could change either:
	/// <see cref="MapView.In3DChanged"/>, the "Immersive 3D View"
	/// preference being toggled, the active tab itself changing, and the
	/// resource browser's own collapse state changing. Immersive mode is
	/// derived fresh from whichever tab is active right now
	/// (<see cref="MapView.In3D"/>), not a sticky field of its own - a
	/// background tab's own 3D state (if any) is irrelevant, and a sticky
	/// field would otherwise go stale the moment the active tab changes to
	/// something that isn't a <see cref="MapView"/> at all. A container's
	/// own right/bottom anchors are fixed at 1 once in
	/// <see cref="CreateMapViewTab"/> - only its left/top offsets ever need
	/// recomputing here, so window resizes alone don't need to trigger
	/// this.
	/// </summary>
	/// <summary>The currently active map tab's own configured resources, or none if there isn't one - what a newly-opened script tab's own <c>#include</c>/<c>#import</c> resolution should search beyond a real on-disk sibling file (see <see cref="ScriptDocument.SetIncludeResourcePaths"/>), matching real compilation's own resource search.</summary>
	private IReadOnlyList<string> CurrentMapResourcePaths() =>
		(_activeTab >= 0 && _activeTab < _tabContents.Count ? _tabContents[_activeTab] as MapView : null)
			?.OpenMapMenu.CurrentResourcePaths ?? System.Array.Empty<string>();

	private void UpdateMapViewportLayout()
	{
		var activeMapView = _activeTab >= 0 && _activeTab < _tabContents.Count ? _tabContents[_activeTab] as MapView : null;
		var container = activeMapView != null ? _mapViewportContainers.GetValueOrDefault(activeMapView) : null;
		var wantImmersive = activeMapView != null && activeMapView.In3D && _immersive3DEnabled;

		if (wantImmersive && !_isImmersive)
		{
			_resourceBrowserVisibleBeforeImmersive = _resourceBrowserPanel.Visible;
		}
		else if (!wantImmersive && _isImmersive)
		{
			_resourceBrowserPanel.Visible = _resourceBrowserVisibleBeforeImmersive;
		}

		_isImmersive = wantImmersive;
		_menuBarPanel.Visible = !wantImmersive;
		_tabStripPanel.Visible = !wantImmersive;
		if (wantImmersive) _resourceBrowserPanel.Visible = false;
		UpdateBrowserToggleButton();

		if (container == null) return;

		if (wantImmersive)
		{
			container.OffsetLeft = 0;
			container.OffsetTop = 0;
		}
		else
		{
			container.OffsetLeft = _resourceBrowserPanel.Visible ? _resourceBrowserPanel.Size.X : 0;
			container.OffsetTop = _menuBarPanel.Size.Y + _tabStripPanel.Size.Y;
		}
	}

	/// <summary>
	/// Instantiates a blank <see cref="MapView"/> (its own <c>_Ready()</c>
	/// creates a sample map, same as every Map tab has always started
	/// with), wraps it in its own <see cref="SubViewportContainer"/>/
	/// <see cref="SubViewport"/>, wires it up, adds a tab, and switches to
	/// it. Shared by the initial startup tab and every subsequently opened
	/// one (<see cref="OpenMapTab"/>, <see cref="MainMenuBar.CreateMapTabRequested"/>).
	/// </summary>
	private MapView CreateMapViewTab()
	{
		var mapDocument = GD.Load<PackedScene>(MapDocumentScenePath).Instantiate<MapView>();

		// Resolved by path, not via MapView.OpenMapMenu (null until
		// mapDocument's own _Ready() runs) - and subscribed here, before
		// mapDocument ever enters the tree below, so a MapLoaded fired
		// synchronously from inside that _Ready() call is never missed.
		// That's a real path, not a hypothetical one: the dev-only
		// --file/--map command-line flags (see OpenMapMenu.LoadFromCommandLine)
		// load a map synchronously from inside MapView._Ready() itself, which
		// is exactly what AddChild(mapViewportContainer) below triggers - a
		// subscription added *after* that call misses that very first (and
		// for this launch path, only) firing entirely, leaving the browser
		// panel permanently empty despite a map genuinely being loaded. The
		// normal, interactive File > Open Map... flow was never at risk -
		// its MapLoaded fires from a dialog confirmation on a later frame,
		// long after this method returns.
		var openMapMenu = mapDocument.GetNode<OpenMapMenu>("UI/OpenMapMenu");
		openMapMenu.MapLoaded += (_, _, _, resources) =>
		{
			_resourceBrowserPanel.Refresh(resources, openMapMenu.CurrentMapContainer, openMapMenu.CurrentMapName, openMapMenu.CurrentWadPath);
			UpdateTabTitle(mapDocument);
		};
		openMapMenu.MapResourcesChanged += (_, _, resources) =>
		{
			_resourceBrowserPanel.Refresh(resources, openMapMenu.CurrentMapContainer, openMapMenu.CurrentMapName, openMapMenu.CurrentWadPath);
			UpdateTabTitle(mapDocument);
		};
		// Flushes an open, dirty script tab for this exact lump straight to
		// disk before OpenMapMenu compiles it - this class is the only one
		// that knows about other tabs at all, which is why OpenMapMenu
		// raises this as an event instead of just doing it itself.
		openMapMenu.ScriptsLumpSaving += (wadPath, lumpIndex) =>
		{
			var open = _tabContents.OfType<ScriptDocument>().FirstOrDefault(d => d.IsLumpFrom(wadPath, lumpIndex));
			if (open != null && open.IsDirty) open.Save();
		};
		openMapMenu.ScriptsCompiled += (wadPath, lumpIndex, errors) =>
		{
			_tabContents.OfType<ScriptDocument>().FirstOrDefault(d => d.IsLumpFrom(wadPath, lumpIndex))?.ShowCompileErrors(errors);
		};
		// Subscribed here, not after AddChild below - same reasoning as the
		// two subscriptions above (catches a synchronous dev --file/--map
		// load); see MapView.LoadMap's own remarks on why it additionally
		// fires DirtyChanged explicitly right after reassigning its undo
		// stack, correcting a real multi-subscriber ordering hazard this
		// lambda would otherwise be exposed to.
		mapDocument.DirtyChanged += () => UpdateTabTitle(mapDocument);

		var mapSubViewport = new SubViewport();
		var mapViewportContainer = new SubViewportContainer
		{
			Stretch = true,
			AnchorRight = 1f,
			AnchorBottom = 1f,
			OffsetRight = 0f,
			OffsetBottom = 0f,
		};
		mapViewportContainer.AddChild(mapSubViewport);
		mapSubViewport.AddChild(mapDocument);

		// Tab bookkeeping before AddChild, not after: AddChild below can
		// itself synchronously fire MapLoaded (the same --file/--map
		// command-line path the remarks above already call out), and
		// UpdateTabTitle needs _tabContents/_tabBar to already know
		// about this tab by then - otherwise IndexOf comes back -1, the
		// title update silently no-ops, and the tab is stuck reading the
		// placeholder "Map" forever, since that one-time synchronous load
		// is the only MapLoaded this tab will ever get. Confirmed live -
		// exactly what the startup tab hit under that launch path.
		_mapViewportContainers[mapDocument] = mapViewportContainer;
		_tabContents.Add(mapDocument);

		// AddTab can itself synchronously emit TabChanged (confirmed live -
		// going from zero tabs to one, Godot's own TabBar auto-selects the
		// new tab as current and signals it) - which would run
		// OnTabChanged/SwitchTo/Activate against mapDocument before it's
		// even entered the tree (AddChild hasn't run yet), crashing in
		// MapView.SetTabActive on fields _Ready() hasn't set up. Same
		// problem, same fix as OnTabClosePressed's own RemoveTab - this
		// method already calls SwitchTo explicitly once mapDocument is
		// actually ready, so the signal-driven path is never needed here.
		_tabBar.TabChanged -= OnTabChanged;
		_tabBar.AddTab("Map", GD.Load<Texture2D>("res://Assets/Icons/document_map.svg"));
		_tabBar.TabChanged += OnTabChanged;

		AddChild(mapViewportContainer);

		mapDocument.MainMenuBar = _mainMenuBar;
		mapDocument.In3DChanged += _ => UpdateMapViewportLayout();

		// Deferred: neither the menu bar's nor the tab strip panel's own
		// height (its themed TabBar's natural minimum can exceed
		// custom_minimum_size, which is only ever a floor) is settled
		// until this frame's layout pass finishes.
		CallDeferred(MethodName.AlignMapToolbarBelowTabStrip, mapDocument);

		SwitchTo(_tabContents.Count - 1);

		return mapDocument;
	}

	/// <summary>
	/// A small, filled-circle prefix on a tab's own label while it has
	/// unsaved changes - the same "dirty indicator" convention VSCode and
	/// most other editors use, applied uniformly across every tab type
	/// here (see <see cref="UpdateTabTitle"/>), not just one.
	/// </summary>
	private const string DirtyIndicatorPrefix = "● ";

	/// <summary>
	/// Reflects a tab's current content in its own label - whichever real
	/// map it actually holds (e.g. "MAP01") instead of the generic "Map"
	/// every Map tab starts with, or a Script tab's own file/lump/pk3-entry
	/// name - prefixed with <see cref="DirtyIndicatorPrefix"/> while it has
	/// unsaved changes. Needed for Map tabs now that more than one can be
	/// open at once, where "Map" alone no longer tells them apart; harmless
	/// to also run on a resource-only change (the map name itself never
	/// changes then) - simpler than trying to only wire it for a genuine
	/// fresh load. Returns silently for a tab that's already been removed
	/// (or any content kind that doesn't have a tab label at all) -
	/// <see cref="ResourceOpenRequest"/> and <see cref="MapView.DirtyChanged"/>/
	/// <see cref="ScriptDocument.DirtyChanged"/> can all legitimately fire
	/// after a tab's own close has already run.
	/// </summary>
	private void UpdateTabTitle(Node content)
	{
		var index = _tabContents.IndexOf(content);
		if (index < 0) return;

		var (baseTitle, isDirty) = content switch
		{
			MapView mapView => (mapView.OpenMapMenu.CurrentMapName ?? "Map", mapView.IsDirty),
			ScriptDocument scriptDocument => (scriptDocument.DisplayName, scriptDocument.IsDirty),
			_ => ((string)null, false),
		};
		if (baseTitle == null) return;

		_tabBar.SetTabTitle(index, isDirty ? DirtyIndicatorPrefix + baseTitle : baseTitle);
	}

	/// <summary>
	/// Opens a specific, already-known map (the resource browser's own
	/// "Open" action on a <c>MapGroup</c> tree node) - focuses its
	/// existing tab if it's already open (matched by real WAD path + map
	/// name, same normalized-path comparison <c>DirectoryResource.ContainsFile</c>
	/// already uses elsewhere), otherwise creates a brand-new blank tab
	/// and loads it there via <see cref="OpenMapMenu.OpenSpecificMap"/> -
	/// the same interactive Map Options confirmation every other "open a
	/// map" entry point already shows, not a silent auto-confirm.
	/// </summary>
	private void OpenMapTab(string wadPath, string mapName)
	{
		var existing = _tabContents.OfType<MapView>().FirstOrDefault(mapView =>
			mapView.OpenMapMenu.CurrentWadPath != null
			&& string.Equals(
				System.IO.Path.GetFullPath(mapView.OpenMapMenu.CurrentWadPath), System.IO.Path.GetFullPath(wadPath),
				System.StringComparison.OrdinalIgnoreCase)
			&& string.Equals(mapView.OpenMapMenu.CurrentMapName, mapName, System.StringComparison.OrdinalIgnoreCase));

		if (existing != null)
		{
			SwitchTo(_tabContents.IndexOf(existing));
			return;
		}

		var mapView = CreateMapViewTab();
		// Deferred, not immediate: this new tab's own SubViewport (created
		// moments ago, inside CreateMapViewTab) hasn't been laid out yet
		// this same frame - same reason AlignMapToolbarBelowTabStrip is
		// already deferred. Popping the Map Options dialog before then
		// (confirmed live - Godot errors "Window spawned at invalid
		// position") centers it against that not-yet-sized viewport
		// instead of the real one. Queued after CreateMapViewTab's own
		// AlignMapToolbarBelowTabStrip call, so the layout is already
		// correct by the time this runs.
		CallDeferred(MethodName.OpenSpecificMapDeferred, mapView, wadPath, mapName);
	}

	private void OpenSpecificMapDeferred(MapView mapView, string wadPath, string mapName) =>
		mapView.OpenMapMenu.OpenSpecificMap(wadPath, mapName);

	/// <summary>
	/// The menu bar and tab strip both already sit above everything else on
	/// their own <c>CanvasLayer</c>s - this just pushes the map's own
	/// "TopBar" (now just its toolbar, since the menu bar moved up to
	/// <see cref="_menuBarPanel"/>) down out of the way of both, the same
	/// screen-space-position conflict a <c>CanvasLayer</c> would otherwise
	/// produce. <see cref="_contentArea"/> (today only ever actually filled
	/// by a Control-rooted tab like <c>ScriptDocument</c>) only has to clear
	/// the strip, not the map's own toolbar below it; a Map tab's own
	/// equivalent offset is computed by <see cref="UpdateMapViewportLayout"/>.
	/// <see cref="_resourceBrowserPanel"/> only clears the menu bar, not the
	/// strip too - it runs the full height below the menu bar, VSCode-
	/// Explorer-style, with the tab strip confined to the right of it
	/// instead of spanning above it (see <see cref="UpdateContentAreaLeftOffset"/>).
	/// </summary>
	private void AlignMapToolbarBelowTabStrip(MapView mapDocument)
	{
		var menuBarHeight = _menuBarPanel.Size.Y;
		var tabStripHeight = _tabStripPanel.Size.Y;

		_tabStripPanel.OffsetTop = menuBarHeight;
		mapDocument.GetNode<Control>("UI/TopBar").OffsetTop = menuBarHeight + tabStripHeight;
		_contentArea.OffsetTop = menuBarHeight + tabStripHeight;
		_resourceBrowserPanel.OffsetTop = menuBarHeight;
		UpdateMapViewportLayout();
	}

	private void OnScriptFileSelected(string path)
	{
		OpenScriptTab(path);
		SwitchTo(_tabContents.Count - 1);
	}

	/// <summary>
	/// The shared tab-creation body <see cref="OnScriptFileSelected"/>
	/// already had, extracted so <see cref="OnScriptNavigationRequested"/>
	/// (a cross-file go-to-definition jump - see
	/// <see cref="ScriptDocument.NavigateToFileRequested"/>) can open a
	/// tab for a file the user never explicitly opened via the file
	/// dialog, the exact same way. Does not switch to it or navigate
	/// anywhere - callers decide that part themselves.
	/// </summary>
	private ScriptDocument OpenScriptTab(string path)
	{
		var scriptDocument = GD.Load<PackedScene>(ScriptDocumentScenePath).Instantiate<ScriptDocument>();
		scriptDocument.Visible = false;
		// AddChild before LoadFile - Instantiate() doesn't run _Ready() until
		// the node actually enters the tree, and LoadFile needs _pathLabel/
		// _codeEdit, which _Ready() is what resolves.
		_contentArea.AddChild(scriptDocument);
		scriptDocument.SetIncludeResourcePaths(CurrentMapResourcePaths());
		scriptDocument.LoadFile(path);
		scriptDocument.NavigateToFileRequested += OnScriptNavigationRequested;
		scriptDocument.DirtyChanged += () => UpdateTabTitle(scriptDocument);
		_tabContents.Add(scriptDocument);

		_tabBar.AddTab(scriptDocument.DisplayName, GD.Load<Texture2D>("res://Assets/Icons/document_script.svg"));

		return scriptDocument;
	}

	/// <summary>
	/// A go-to-definition jump into a different file than the one it
	/// came from (<see cref="ScriptDocument.NavigateToFileRequested"/>) -
	/// focuses that file's own tab if it's already open, otherwise opens
	/// a new one for it, then jumps to the resolved position either way.
	/// </summary>
	private void OnScriptNavigationRequested(string path, int line, int column)
	{
		var existing = _tabContents.OfType<ScriptDocument>()
			.FirstOrDefault(d => string.Equals(d.FilePath, path, System.StringComparison.OrdinalIgnoreCase));
		var target = existing ?? OpenScriptTab(path);

		SwitchTo(_tabContents.IndexOf(target));
		target.NavigateTo(line, column);
	}

	/// <summary>
	/// Dispatches a real "Open" action from the resource browser - a
	/// <c>MapGroup</c> node opens (or focuses) a dedicated Map tab; a
	/// <c>File</c>/<c>Lump</c> node opens (or focuses) a script tab.
	/// </summary>
	private void OnResourceOpenRequested(ResourceOpenRequest request)
	{
		if (request.MapName != null)
		{
			OpenMapTab(request.SourcePath, request.MapName);
			return;
		}

		if (request.FilePath != null)
		{
			var existingFile = _tabContents.OfType<ScriptDocument>()
				.FirstOrDefault(d => string.Equals(d.FilePath, request.FilePath, System.StringComparison.OrdinalIgnoreCase));
			SwitchTo(_tabContents.IndexOf(existingFile ?? OpenScriptTab(request.FilePath)));
			return;
		}

		if (request.Pk3EntryPath != null)
		{
			var existingEntry = _tabContents.OfType<ScriptDocument>()
				.FirstOrDefault(d => d.IsPk3EntryFrom(request.SourcePath, request.Pk3EntryPath));
			SwitchTo(_tabContents.IndexOf(existingEntry ?? OpenPk3EntryScriptTab(request)));
			return;
		}

		var existingLump = _tabContents.OfType<ScriptDocument>()
			.FirstOrDefault(d => d.IsLumpFrom(request.SourcePath, request.LumpIndex));
		SwitchTo(_tabContents.IndexOf(existingLump ?? OpenLumpScriptTab(request)));
	}

	/// <summary>
	/// A genuinely empty lump is pointless if the user clicks "Add Script"
	/// and then changes their mind without typing anything - seeded with a
	/// real, useful starting line instead of nothing. Confirmed `#include`,
	/// not `#import`, is the real directive for a header file like this
	/// (not a compiled-library import), against this project's own BCS
	/// work (TODO/bcs-lsp-foundation.md).
	/// </summary>
	private static readonly byte[] NewScriptsLumpBoilerplate =
		System.Text.Encoding.UTF8.GetBytes("#include \"zcommon.acs\"\n\n");

	/// <summary>
	/// The resource browser's "Add Script" action - creates the target
	/// map's own <c>SCRIPTS</c> lump (<see cref="WadFile.WithAddedScriptsLump"/>),
	/// backs up (<c>.bak</c>) then overwrites the WAD (matching the
	/// existing WAD-lump-save convention - this is a WAD operation, not a
	/// PK3 one), refreshes the browser so the new lump actually shows up,
	/// then opens it the exact same way an already-existing SCRIPTS lump
	/// opens.
	/// </summary>
	private void OnAddScriptRequested(ResourceAddScriptRequest request)
	{
		var wad = WadFile.Read(request.WadPath);
		var (newLumps, insertedIndex) = WadFile.WithAddedScriptsLump(wad.Lumps, request.MapMarkerLumpIndex, NewScriptsLumpBoilerplate);
		var bytes = WadWriter.Write(newLumps);

		if (System.IO.File.Exists(request.WadPath)) System.IO.File.Move(request.WadPath, request.WadPath + ".bak", overwrite: true);
		System.IO.File.WriteAllBytes(request.WadPath, bytes);

		// Needed when request.WadPath is a nested maps/MAP01.wad - the
		// browser's own tree-walk now re-points through this exact cache
		// for a nested WAD (ResourceBrowserPanel.AddTreeItem), so without
		// this it would keep serving the pre-write instance on the very
		// next refresh below, even though the enclosing folder's own
		// DirectoryResource re-reads everything else fresh already.
		ResourceContainerCache.Invalidate(request.WadPath);
		RefreshBrowserForExternallyChangedWad(request.WadPath);

		OnResourceOpenRequested(new ResourceOpenRequest
		{
			SourcePath = request.WadPath,
			LumpName = "SCRIPTS",
			LumpIndex = insertedIndex,
			LumpData = NewScriptsLumpBoilerplate,
		});
	}

	/// <summary>
	/// The browser's own tree is driven by whichever Map tab is currently
	/// active, not re-walked on its own - every existing lump-save path
	/// (<c>ScriptDocument.SaveLump</c>/<c>SavePk3Entry</c>) only ever
	/// changes a lump's bytes, never the tree *structure*, so nothing ever
	/// needed to trigger a refresh before this. Rebuilds just the one
	/// <see cref="NamedResource"/> whose <see cref="NamedResource.SourcePath"/>
	/// matches the WAD that was just written (a fresh <see cref="WadFile.Read"/> -
	/// <see cref="WadFile"/> is immutable once constructed, there's no
	/// in-place way to add to an existing instance), leaving every other
	/// already-loaded resource (the IWAD, a mod folder/pk3, a sibling
	/// map's own WAD) untouched. <c>OpenMapMenu.CurrentMapContainer</c> is
	/// passed through unchanged regardless of whether it happens to be the
	/// same WAD - <see cref="ResourceBrowserPanel.Refresh"/> uses it purely
	/// to resolve/highlight "the currently open map" by path/name, not by
	/// object identity, and it'll correctly re-read fresh the next time
	/// this map is actually saved or reopened anyway (<c>OpenMapMenu</c>'s
	/// own established "always read fresh" convention). Does not touch
	/// <c>OpenMapMenu</c>'s own fields at all - this is a display-only
	/// refresh of what the browser shows, exactly like <see cref="Activate"/>'s
	/// own existing <c>Refresh</c> calls already are.
	/// </summary>
	private void RefreshBrowserForExternallyChangedWad(string wadPath)
	{
		if (_tabContents[_activeTab] is not MapView mapView) return;

		var freshContainer = WadFile.Read(wadPath);
		var updated = mapView.OpenMapMenu.CurrentNamedResources
			.Select(r => string.Equals(r.SourcePath, wadPath, System.StringComparison.OrdinalIgnoreCase)
				? new NamedResource(r.DisplayName, freshContainer, r.SourcePath)
				: r)
			.ToList();

		_resourceBrowserPanel.Refresh(updated, mapView.OpenMapMenu.CurrentMapContainer, mapView.OpenMapMenu.CurrentMapName, mapView.OpenMapMenu.CurrentWadPath);
	}

	/// <summary>Lump-backed counterpart of <see cref="OpenScriptTab"/> - same tab-strip wiring, populated from already-read bytes (<see cref="ScriptDocument.LoadLump"/>) instead of a disk path.</summary>
	private ScriptDocument OpenLumpScriptTab(ResourceOpenRequest request)
	{
		var scriptDocument = GD.Load<PackedScene>(ScriptDocumentScenePath).Instantiate<ScriptDocument>();
		scriptDocument.Visible = false;
		_contentArea.AddChild(scriptDocument);
		scriptDocument.SetIncludeResourcePaths(CurrentMapResourcePaths());
		scriptDocument.LoadLump(request.SourcePath, request.LumpIndex, request.LumpName, request.LumpData);
		scriptDocument.DirtyChanged += () => UpdateTabTitle(scriptDocument);
		_tabContents.Add(scriptDocument);

		_tabBar.AddTab(scriptDocument.DisplayName, GD.Load<Texture2D>("res://Assets/Icons/document_script.svg"));

		return scriptDocument;
	}

	/// <summary>PK3-entry-backed counterpart of <see cref="OpenLumpScriptTab"/> - same tab-strip wiring, populated from already-read bytes (<see cref="ScriptDocument.LoadPk3Entry"/>) instead of a WAD lump.</summary>
	private ScriptDocument OpenPk3EntryScriptTab(ResourceOpenRequest request)
	{
		var scriptDocument = GD.Load<PackedScene>(ScriptDocumentScenePath).Instantiate<ScriptDocument>();
		scriptDocument.Visible = false;
		_contentArea.AddChild(scriptDocument);
		scriptDocument.SetIncludeResourcePaths(CurrentMapResourcePaths());
		scriptDocument.LoadPk3Entry(request.SourcePath, request.Pk3EntryPath, request.Pk3EntryData);
		scriptDocument.DirtyChanged += () => UpdateTabTitle(scriptDocument);
		_tabContents.Add(scriptDocument);

		_tabBar.AddTab(scriptDocument.DisplayName, GD.Load<Texture2D>("res://Assets/Icons/document_script.svg"));

		return scriptDocument;
	}

	private void OnTabChanged(long tab) => SwitchTo((int)tab);

	/// <summary>
	/// <paramref name="index"/> can arrive from Godot's own <see cref="TabBar.TabChanged"/>
	/// signal (<see cref="OnTabChanged"/>), not just this class's own
	/// calls - bounds-checked defensively since <c>TabBar</c> can fire
	/// that signal on its own, synchronously, as a side effect of other
	/// operations (confirmed live for <see cref="TabBar.RemoveTab"/> - see
	/// <see cref="OnTabClosePressed"/>'s own remarks), with no guarantee
	/// its own idea of "current" matches this class's <see cref="_tabContents"/>
	/// at that exact moment.
	/// </summary>
	private void SwitchTo(int index)
	{
		if (_activeTab == index || index < 0 || index >= _tabContents.Count) return;

		if (_activeTab >= 0 && _activeTab < _tabContents.Count) Deactivate(_tabContents[_activeTab]);
		_activeTab = index;
		_tabBar.CurrentTab = index;
		Activate(_tabContents[index]);
	}

	private void Activate(Node content)
	{
		if (content is MapView mapView)
		{
			mapView.SetTabActive(true);
			if (_mapViewportContainers.TryGetValue(mapView, out var container)) container.Visible = true;
			_mainMenuBar.SetActiveMap(mapView.OpenMapMenu, mapView.Overlay);
			UpdateMapViewportLayout();
			// Re-shows *this* tab's own resources/highlight - without this,
			// switching between two already-loaded Map tabs left whichever
			// one loaded/changed its resources *last* showing in the
			// browser, regardless of which tab was actually now active.
			_resourceBrowserPanel.Refresh(
				mapView.OpenMapMenu.CurrentNamedResources, mapView.OpenMapMenu.CurrentMapContainer,
				mapView.OpenMapMenu.CurrentMapName, mapView.OpenMapMenu.CurrentWadPath);
			if (AppSettingsFile.Load().GetAutoRevealActiveTab())
			{
				_resourceBrowserPanel.RevealActiveMap(
					mapView.OpenMapMenu.CurrentMapContainer, mapView.OpenMapMenu.CurrentMapName, mapView.OpenMapMenu.CurrentWadPath);
			}
		}
		else if (content is Control control)
		{
			control.Visible = true;
			control.ProcessMode = Node.ProcessModeEnum.Inherit;
			// Not a map tab (or no tab at all) - "Save Map"/"Map Options..."
			// etc. would otherwise keep silently targeting whichever map
			// tab was active before this one.
			_mainMenuBar.SetActiveMap(null, null);
			// Same reasoning for the browser's own "currently open" highlight
			// - a Script tab has no map of its own to show as open.
			_resourceBrowserPanel.ClearCurrentMapHighlight();
			if (content is ScriptDocument scriptDocument && AppSettingsFile.Load().GetAutoRevealActiveTab())
			{
				_resourceBrowserPanel.RevealActiveScript(scriptDocument);
			}
		}
	}

	private void Deactivate(Node content)
	{
		if (content is MapView mapView)
		{
			mapView.SetTabActive(false);
			if (_mapViewportContainers.TryGetValue(mapView, out var container)) container.Visible = false;
		}
		else if (content is Control control)
		{
			control.Visible = false;
			control.ProcessMode = Node.ProcessModeEnum.Disabled;
		}
	}

	/// <summary>Every tab is closable, including the last one - closing down to zero just leaves a plain background, which is fine; there's no "no map open" empty-state UI to build for it.</summary>
	private void OnTabClosePressed(long tab)
	{
		var index = (int)tab;
		var content = _tabContents[index];
		if (_activeTab == index) Deactivate(content);

		// A Map tab's own SubViewportContainer wrapper is a *parent* of
		// the MapView (AppShell -> container -> SubViewport -> MapView),
		// not a sibling of it - freeing the MapView alone would leave the
		// wrapper (and the SubViewport inside it) behind.
		if (content is MapView mapView && _mapViewportContainers.Remove(mapView, out var container))
		{
			container.QueueFree();
		}
		else
		{
			content.QueueFree();
		}

		_tabContents.RemoveAt(index);

		// RemoveTab can itself synchronously emit TabChanged (confirmed
		// live - Godot's own TabBar auto-reselects a neighboring tab when
		// the removed one was the current one, and signals it) *before*
		// this method's own bookkeeping below has updated _activeTab -
		// OnTabChanged/SwitchTo would then run against a stale _activeTab
		// against the already-shrunk _tabContents, indexing past its end.
		// Disconnected for the duration so this method stays the one,
		// consistent source of truth for what happens next; restored
		// right after regardless of which branch below actually runs.
		_tabBar.TabChanged -= OnTabChanged;
		_tabBar.RemoveTab(index);
		_tabBar.TabChanged += OnTabChanged;

		if (_activeTab == index)
		{
			_activeTab = -1;
			if (_tabContents.Count > 0)
			{
				SwitchTo(Mathf.Clamp(index - 1, 0, _tabContents.Count - 1));
			}
			else
			{
				_mainMenuBar.SetActiveMap(null, null);
				_resourceBrowserPanel.ClearCurrentMapHighlight();
			}
		}
		else if (_activeTab > index)
		{
			_activeTab--;
		}
	}
}
