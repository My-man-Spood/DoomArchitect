using System.Linq;
using Godot;

/// <summary>
/// The app's own root shell: a static, always-present <see cref="MainMenuBar"/>
/// (File/Edit/Map/Preferences - genuinely app-level chrome, since there's
/// only ever one map open at a time and actions like "Save Map"/"Open
/// Script..." apply regardless of which tab happens to be showing; it
/// used to live inside <c>MapDocument.tscn</c> itself, which made it
/// disappear along with the rest of that tab's content whenever a Script
/// tab was active - a real, reported bug, not a deliberate choice), above
/// a tab strip (<see cref="TabBar"/> - the plain label/icon/close-button
/// strip, not <c>TabContainer</c>, which only manages <see cref="Control"/>-
/// type children as pages - the map document's own root is a <c>Node3D</c>,
/// since only one <see cref="Camera3D"/> can be <see cref="Camera3D.Current"/>
/// across the whole viewport at a time and <c>CanvasLayer</c> content
/// renders independently of normal scene-tree visibility either way, so
/// hosting it needs <see cref="MapView.SetTabActive"/>'s own explicit
/// toggling regardless of which container manages the strip), above a
/// shared content area. Both the menu bar and the strip live on their own
/// <c>CanvasLayer</c>s (15 and 20 respectively, both above every map
/// document's own "UI" layer at 10) so they reliably render in front
/// rather than fighting a map's own toolbar for draw order. The target
/// order, top to bottom, is: the menu bar, then the tab strip, then
/// whichever tab is active - for a Map tab, that's its own toolbar
/// (mode/grid/test-map buttons) then the 2D/3D view; <see cref="AlignMapToolbarBelowTabStrip"/>
/// pushes that map's own "TopBar" (now just the toolbar) down by the
/// menu bar's and strip's combined height, measured fresh rather than a
/// guessed constant, so it never fights either of them for the same
/// screen-space position <c>CanvasLayer</c> content would otherwise land
/// at by default. The map editor itself (<see cref="MapView"/> and
/// everything else it owns) is wrapped here completely unchanged
/// internally - this class only ever reparents/shows/hides it as one
/// tab's content, and now also owns wiring its <see cref="MainMenuBar"/>
/// up front since that's no longer part of the map document's own scene.
///
/// Exactly one Map tab exists today, created once on <see cref="_Ready"/>
/// and not closable (there's no "no map open" empty state yet) - several
/// simultaneous Map tabs, a WAD-browser tab type, and a sprite-editor tab
/// type are all real, tracked future work - see TODO/documents-and-tabs.md.
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
	private FileDialog _openScriptDialog;

	private readonly System.Collections.Generic.List<Node> _tabContents = new();
	private int _activeTab = -1;

	public override void _Ready()
	{
		_menuBarPanel = GetNode<Control>("MenuBarLayer/MenuBarPanel");
		_mainMenuBar = GetNode<MainMenuBar>("MenuBarLayer/MenuBarPanel/MenuBar");
		_tabBar = GetNode<TabBar>("TabStripLayer/TabStripPanel/TabBar");
		_tabStripPanel = GetNode<Control>("TabStripLayer/TabStripPanel");
		_contentArea = GetNode<Control>("ContentArea");
		_openScriptDialog = GetNode<FileDialog>("OpenScriptDialog");

		_tabBar.TabClosePressed += OnTabClosePressed;
		_tabBar.TabChanged += OnTabChanged;
		_tabBar.TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowActiveOnly;
		_openScriptDialog.FileSelected += OnScriptFileSelected;
		_mainMenuBar.OpenScriptRequested = () => _openScriptDialog.PopupCentered();

		AddMapTab();
	}

	private void AddMapTab()
	{
		var mapDocument = GD.Load<PackedScene>(MapDocumentScenePath).Instantiate<MapView>();
		_contentArea.AddChild(mapDocument);
		_tabContents.Add(mapDocument);

		mapDocument.MainMenuBar = _mainMenuBar;
		_mainMenuBar.Initialize(mapDocument.OpenMapMenu, mapDocument.Overlay);

		// Deferred: neither the menu bar's nor the tab strip panel's own
		// height (its themed TabBar's natural minimum can exceed
		// custom_minimum_size, which is only ever a floor) is settled
		// until this frame's layout pass finishes.
		CallDeferred(MethodName.AlignMapToolbarBelowTabStrip, mapDocument);

		_tabBar.AddTab("Map", GD.Load<Texture2D>("res://Assets/Icons/document_map.svg"));

		SwitchTo(0);
	}

	/// <summary>
	/// The menu bar and tab strip both already sit above everything else on
	/// their own <c>CanvasLayer</c>s - this just pushes the map's own
	/// "TopBar" (now just its toolbar, since the menu bar moved up to
	/// <see cref="_menuBarPanel"/>) down out of the way of both, the same
	/// screen-space-position conflict a <c>CanvasLayer</c> would otherwise
	/// produce. <see cref="_contentArea"/> (today only ever actually filled
	/// by a Control-rooted tab like <c>ScriptDocument</c> - a Map tab's own
	/// 3D content ignores its Control parent's offset entirely) only has to
	/// clear the strip, not the map's own toolbar below it.
	/// </summary>
	private void AlignMapToolbarBelowTabStrip(MapView mapDocument)
	{
		var menuBarHeight = _menuBarPanel.Size.Y;
		var tabStripHeight = _tabStripPanel.Size.Y;

		_tabStripPanel.OffsetTop = menuBarHeight;
		mapDocument.GetNode<Control>("UI/TopBar").OffsetTop = menuBarHeight + tabStripHeight;
		_contentArea.OffsetTop = menuBarHeight + tabStripHeight;
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
		scriptDocument.LoadFile(path);
		scriptDocument.NavigateToFileRequested += OnScriptNavigationRequested;
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

	private void OnTabChanged(long tab) => SwitchTo((int)tab);

	private void SwitchTo(int index)
	{
		if (_activeTab == index) return;

		if (_activeTab >= 0) Deactivate(_tabContents[_activeTab]);
		_activeTab = index;
		_tabBar.CurrentTab = index;
		Activate(_tabContents[index]);
	}

	private static void Activate(Node content)
	{
		if (content is MapView mapView) mapView.SetTabActive(true);
		else if (content is Control control)
		{
			control.Visible = true;
			control.ProcessMode = Node.ProcessModeEnum.Inherit;
		}
	}

	private static void Deactivate(Node content)
	{
		if (content is MapView mapView) mapView.SetTabActive(false);
		else if (content is Control control)
		{
			control.Visible = false;
			control.ProcessMode = Node.ProcessModeEnum.Disabled;
		}
	}

	/// <summary>The Map tab (index 0) never closes - there's no "no map open" empty state yet.</summary>
	private void OnTabClosePressed(long tab)
	{
		if (tab == 0) return;

		var index = (int)tab;
		var content = _tabContents[index];
		if (_activeTab == index) Deactivate(content);

		content.QueueFree();
		_tabContents.RemoveAt(index);
		_tabBar.RemoveTab(index);

		if (_activeTab == index)
		{
			_activeTab = -1;
			SwitchTo(Mathf.Clamp(index - 1, 0, _tabContents.Count - 1));
		}
		else if (_activeTab > index)
		{
			_activeTab--;
		}
	}
}
