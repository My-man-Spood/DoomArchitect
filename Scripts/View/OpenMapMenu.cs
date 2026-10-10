using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DoomArchitect.Core.Compilers;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Textures;
using DoomArchitect.Core.ZDoom;
using DoomArchitect.Settings;
using Godot;

/// <summary>
/// Owns the "open a WAD, pick a map, confirm its game configuration and
/// resources" flow - no longer a visible toolbar button itself (that's
/// now <c>File &gt; Open Map...</c> in <see cref="MainMenuBar"/>, which
/// calls <see cref="ShowOpenFileDialog"/>), just the node that holds the
/// flow's state and dialogs. Reports a freshly loaded map via
/// <see cref="MapLoaded"/>, and a resource/game-config change to the
/// *already loaded* map (via <see cref="ShowMapOptionsForCurrentMap"/>,
/// wired to <c>Map &gt; Map Options...</c>) via
/// <see cref="MapResourcesChanged"/> instead - the latter must not reset
/// undo history or the camera the way loading a genuinely different map
/// does, so it's a distinct event <c>MapView</c> handles differently
/// (see <see cref="MapView.RefreshResources"/>).
///
/// A WAD with only one map skips straight to the Map Options prompt; one
/// with several (like a real IWAD) first pops a small map picker. The Map
/// Options prompt itself always appears regardless of map count, rather
/// than silently auto-picking - pre-filled from this map's own real mod
/// root's remembered <c>.dbs</c> settings if present (<see cref="ModRootDetector"/> -
/// the enclosing pk3-style folder for the real GZDoom/ZDoom per-map-WAD
/// convention, or the WAD itself for a true standalone WAD), else from
/// the game configuration's app-wide default resources, but always
/// requiring confirmation. Confirming saves both back, so opening the
/// same map again remembers its resources, opening a *different* map
/// from the same mod folder already gets that folder's own shared
/// resources for free, and opening a different, previously-unopened mod
/// for the same game configuration is pre-filled with the same default.
///
/// <see cref="LoadFromCommandLine"/> is a separate, fully non-interactive
/// entry point for the dev-only <c>--file</c>/<c>--map</c> launch
/// arguments (see <see cref="CommandLineOptions"/>) - it skips every
/// dialog above entirely rather than reusing this flow's UI.
/// </summary>
public partial class OpenMapMenu : PanelContainer
{
	public event Action<MapData, TextureSet, IGameConfiguration, IReadOnlyList<NamedResource>> MapLoaded;
	public event Action<TextureSet, IGameConfiguration, IReadOnlyList<NamedResource>> MapResourcesChanged;

	/// <summary>Fired after a successful Save/Save As/Save Into - lets <see cref="MainMenuBar"/> mark the undo stack clean without this class needing to know about it.</summary>
	public event Action MapSaved;

	/// <summary>Fired right before a save compiles this map's own <c>SCRIPTS</c> lump - lets <see cref="AppShell"/> flush an open, dirty script tab for it first (this class has no visibility into other tabs), mirroring UDB's own implicit-save-before-compiling.</summary>
	public event Action<string, int> ScriptsLumpSaving;

	/// <summary>Fired right after that compile attempt resolves - an empty error list means success. Lets <see cref="AppShell"/> tint the matching open tab's own lines the same way live diagnostics already are.</summary>
	public event Action<string, int, IReadOnlyList<ScriptCompileError>> ScriptsCompiled;

	/// <summary>
	/// <paramref name="SourceWadPath"/> is null for an entry found in an
	/// already-loaded <see cref="_pendingWad"/> (the normal single-file open
	/// flow) - non-null for one found while scanning a folder's own
	/// <c>maps/*.wad</c> files (see <see cref="OnDirSelected"/>), where each
	/// entry can come from a genuinely different little per-map WAD and none
	/// of them is read until the user actually picks one.
	/// </summary>
	private readonly record struct MapEntry(string Name, bool IsUdmf, string SourceWadPath = null);

	private FileDialog _fileDialog;
	private FileDialog _saveFileDialog;
	private FileDialog _saveIntoFileDialog;
	private AcceptDialog _errorDialog;
	private ConfirmationDialog _overwriteConfirmDialog;
	private ConfirmationDialog _mapCollisionConfirmDialog;
	private MapSelectDialog _mapSelectDialog;
	private MapOptionsDialog _mapOptionsDialog;
	private NewMapDialog _newMapDialog;

	private WadFile _pendingWad;
	private string _pendingWadPath;
	private IReadOnlyList<MapEntry> _pendingMaps;
	private string _pendingFileName;
	private MapData _pendingMapData;
	private string _pendingMapName;
	private string _pendingNamespace;
	private IReadOnlyList<UdmfBlock> _pendingUnknownBlocks;
	private bool _isRevisitingCurrentMap;

	/// <summary>
	/// The real "mod root" this map's own settings should be scoped to -
	/// <see cref="ModRootDetector.DetectFrom"/>, computed once in
	/// <see cref="PromptMapOptionsForPendingMap"/> right after
	/// <see cref="_pendingWadPath"/> is finalized, so it covers every entry
	/// point that routes through there (a plain file pick, a folder scan,
	/// <see cref="OpenSpecificMap"/>) without each needing its own copy of
	/// the detection logic. Either the enclosing pk3-style mod folder (a
	/// real GZDoom/ZDoom convention of one small per-map WAD per map, no
	/// UDB precedent for treating the whole folder as one resource at all)
	/// or, for a true standalone WAD, the exact same path as
	/// <see cref="_pendingWadPath"/> itself - "per-WAD" and "per-mod" are
	/// the same thing there. Null only for a brand-new, not-yet-saved map
	/// (<see cref="OnNewMapNameEntered"/>), which has no WAD path to detect
	/// a root from yet.
	/// </summary>
	private string _pendingModRootPath;

	// The map actually loaded and displayed right now - distinct from the
	// "_pending" load-in-progress state above, which only lives for the
	// duration of one open/confirm flow. Set only once a load truly
	// completes, so "Map Options..." can always revisit the real current
	// map regardless of what's mid-flight (or aborted) since.
	private WadFile _currentWad;
	private string _currentWadPath;
	private string _currentModRootPath;
	private string _currentMapName;
	private MapData _currentMapData;
	private string _currentNamespace;
	private IReadOnlyList<UdmfBlock> _currentUnknownBlocks;
	private GameConfigurationKind _currentGameConfigurationKind;
	private IReadOnlyList<string> _currentResourcePaths = Array.Empty<string>();
	private IReadOnlyList<NamedResource> _currentNamedResources = Array.Empty<NamedResource>();

	// The chosen Save As destination while the "this file already exists"
	// confirmation is showing - set by OnSaveFileSelected, consumed (and
	// cleared) by OnOverwriteConfirmed.
	private string _pendingSavePath;

	// The chosen Save Into destination/target-file lumps while the "this
	// file already contains a map with this name" confirmation is showing -
	// set by OnSaveIntoFileSelected, consumed (and cleared) by
	// OnMapCollisionConfirmed.
	private string _pendingSaveIntoPath;
	private IReadOnlyList<WadLump> _pendingSaveIntoOriginalLumps;

	// Set by SaveMapThen (see its own remarks) right before it falls
	// through to SaveMapAs for a never-saved-yet map, so the deferred
	// write that eventually happens - via OnSaveFileSelected/
	// OnOverwriteConfirmed, same as an ordinary Save As - still picks
	// the Testing node-builder profile. Consumed (and cleared) at
	// whichever of those two actually calls WriteMapToFile.
	private bool _pendingSaveForTesting;

	public override void _Ready()
	{
		_fileDialog = GetNode<FileDialog>("FileDialog");
		_saveFileDialog = GetNode<FileDialog>("SaveFileDialog");
		_saveIntoFileDialog = GetNode<FileDialog>("SaveIntoFileDialog");
		_errorDialog = GetNode<AcceptDialog>("ErrorDialog");
		_overwriteConfirmDialog = GetNode<ConfirmationDialog>("OverwriteConfirmDialog");
		_mapCollisionConfirmDialog = GetNode<ConfirmationDialog>("MapCollisionConfirmDialog");
		// Godot only allows one *exclusive* child window per parent window
		// at a time - these can legitimately need to show while the Map
		// Options dialog (itself exclusive) is already open.
		_errorDialog.Exclusive = false;
		_overwriteConfirmDialog.Exclusive = false;
		_mapCollisionConfirmDialog.Exclusive = false;
		// _fileDialog's own file_mode is OpenAny (set in MapDocument.tscn) -
		// lets it also target a PK3-style resource folder directly, scanned
		// for its own maps/*.wad files by OnDirSelected, rather than
		// requiring the user to dig into it manually to find the one .wad
		// file inside.
		_fileDialog.FileSelected += OnFileSelected;
		_fileDialog.DirSelected += OnDirSelected;
		_saveFileDialog.FileSelected += OnSaveFileSelected;
		_saveIntoFileDialog.FileSelected += OnSaveIntoFileSelected;
		_overwriteConfirmDialog.Confirmed += OnOverwriteConfirmed;
		_mapCollisionConfirmDialog.Confirmed += OnMapCollisionConfirmed;

		_mapSelectDialog = GD.Load<PackedScene>("res://Scenes/UI/MapSelectDialog.tscn").Instantiate<MapSelectDialog>();
		_mapSelectDialog.MapActivated += index =>
		{
			_mapSelectDialog.Hide();
			PromptMapOptionsForPendingMap(index);
		};
		AddChild(_mapSelectDialog);

		_mapOptionsDialog = GD.Load<PackedScene>("res://Scenes/UI/MapOptionsDialog.tscn").Instantiate<MapOptionsDialog>();
		_mapOptionsDialog.Confirmed += OnMapOptionsConfirmed;
		AddChild(_mapOptionsDialog);

		_newMapDialog = GD.Load<PackedScene>("res://Scenes/UI/NewMapDialog.tscn").Instantiate<NewMapDialog>();
		_newMapDialog.MapNameEntered += OnNewMapNameEntered;
		AddChild(_newMapDialog);
	}

	/// <summary>The currently loaded map's own name/game configuration/resource paths - null/empty when nothing's loaded, same "no map yet" case <see cref="SaveMap"/> itself already guards against.</summary>
	public string CurrentMapName => _currentMapName;

	public GameConfigurationKind CurrentGameConfigurationKind => _currentGameConfigurationKind;

	public IReadOnlyList<string> CurrentResourcePaths => _currentResourcePaths;

	/// <summary>
	/// The exact <see cref="IResourceContainer"/> instance backing the
	/// currently loaded map's own WAD - null for a brand-new, not-yet-saved
	/// map. The same object reference this map's own entry in
	/// <see cref="MapLoaded"/>'s <c>namedResources</c> list used (when it
	/// has one at all - see <see cref="OnMapOptionsConfirmed"/>'s own
	/// dedup), so a caller can identify "this resource IS the open map's
	/// own file" by reference equality rather than by path string matching.
	/// </summary>
	public IResourceContainer CurrentMapContainer => _currentWad;

	/// <summary>
	/// The currently loaded map's own real backing file path - set even
	/// when <see cref="CurrentMapContainer"/>'s own WAD has no top-level
	/// entry of its own in the last-reported resource list (the folder-
	/// dedup case - see <see cref="OnMapOptionsConfirmed"/>), which is
	/// exactly the case a caller needs this for: finding the right nested
	/// leaf by its own real path when there's no top-level entry to match
	/// by reference instead.
	/// </summary>
	public string CurrentWadPath => _currentWadPath;

	/// <summary>
	/// The exact resource list the resource browser last showed for this
	/// map - empty until a map genuinely loads. Lets a caller re-show "what
	/// this specific map tab's own browser content looked like" without
	/// needing to catch the next <see cref="MapLoaded"/>/<see cref="MapResourcesChanged"/>
	/// firing (e.g. <c>AppShell</c> re-running <c>ResourceBrowserPanel.Refresh</c>
	/// on every tab switch, not just on an actual load/resource change).
	/// </summary>
	public IReadOnlyList<NamedResource> CurrentNamedResources => _currentNamedResources;

	/// <summary>
	/// The current map's own real saveable bytes, built with the exact same
	/// construction <see cref="WriteMapToFile"/> uses for a genuine save -
	/// Test Map's own temp-WAD content, without touching disk or any of
	/// this class's own "current file" state the way an actual save does.
	/// </summary>
	public byte[] BuildCurrentMapBytes()
	{
		var document = new UdmfDocument(_currentMapData, _currentNamespace, _currentUnknownBlocks, Array.Empty<string>());
		return MapFileSaver.SaveUdmfMap(_currentWad?.Lumps, document, _currentMapName);
	}

	public void ShowOpenFileDialog() => _fileDialog.PopupCentered();

	/// <summary>
	/// Opens a specific, already-known map by name from a specific WAD path -
	/// skips the file picker and (if the WAD has more than one map) the map
	/// picker, since both are already decided, but still shows the normal
	/// interactive Map Options confirmation (game config + resources)
	/// before actually loading - the same safety net every other "open a
	/// map" entry point already has, deliberately not a silent auto-
	/// confirm like <see cref="LoadFromCommandLine"/>'s own dev-only
	/// shortcut. Used by the resource browser's own "Open" action on a
	/// <c>MapGroup</c> tree node, where the file and map name are already
	/// known exactly from the tree itself.
	/// </summary>
	public void OpenSpecificMap(string wadPath, string mapName)
	{
		try
		{
			var wad = WadFile.Read(wadPath);

			var maps = new List<MapEntry>();
			foreach (var name in wad.FindUdmfMapNames()) maps.Add(new MapEntry(name, IsUdmf: true));
			foreach (var name in wad.FindClassicMapNames()) maps.Add(new MapEntry(name, IsUdmf: false));

			var match = maps.FirstOrDefault(m => string.Equals(m.Name, mapName, StringComparison.OrdinalIgnoreCase));
			if (match.Name == null)
			{
				ShowError($"Map '{mapName}' not found in '{Path.GetFileName(wadPath)}'.");
				return;
			}

			_pendingWad = wad;
			_pendingWadPath = wadPath;
			_pendingMaps = new List<MapEntry> { match };
			_pendingFileName = Path.GetFileName(wadPath);
			_isRevisitingCurrentMap = false;

			PromptMapOptionsForPendingMap(0);
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
		}
	}

	private void OnFileSelected(string path)
	{
		try
		{
			var wad = WadFile.Read(path);

			var maps = new List<MapEntry>();
			foreach (var name in wad.FindUdmfMapNames()) maps.Add(new MapEntry(name, IsUdmf: true));
			foreach (var name in wad.FindClassicMapNames()) maps.Add(new MapEntry(name, IsUdmf: false));

			if (maps.Count == 0)
			{
				ShowError($"No supported maps found in '{Path.GetFileName(path)}'.");
				return;
			}

			_pendingWad = wad;
			_pendingWadPath = path;
			_pendingMaps = maps;
			_pendingFileName = Path.GetFileName(path);
			_isRevisitingCurrentMap = false;

			if (maps.Count == 1)
			{
				PromptMapOptionsForPendingMap(0);
				return;
			}

			_mapSelectDialog.SetMaps(maps.Select(m => m.Name).ToList());
			_mapSelectDialog.PopupCentered();
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
		}
	}

	/// <summary>
	/// The folder-targeting counterpart of <see cref="OnFileSelected"/> -
	/// picking a PK3-style resource folder directly (rather than a .wad/.pk3
	/// file) scans its own <c>maps/</c> subfolder for the real GZDoom/ZDoom
	/// convention of one little WAD per map (e.g. <c>maps/MAP01.wad</c>,
	/// holding just that map's own geometry - everything else a map needs
	/// still lives loosely in the folder's own namespace folders, following
	/// the same convention <see cref="DirectoryResource"/> already reads).
	/// Each found map can come from a different such WAD, so unlike
	/// <see cref="OnFileSelected"/> nothing is actually read into
	/// <see cref="_pendingWad"/> here - only once <see cref="PromptMapOptionsForPendingMap"/>
	/// knows which single map was actually chosen.
	/// </summary>
	private void OnDirSelected(string dir)
	{
		try
		{
			var mapsFolder = FindMapsSubfolder(dir);
			var maps = new List<MapEntry>();

			if (mapsFolder != null)
			{
				foreach (var wadPath in Directory.EnumerateFiles(mapsFolder, "*.wad", SearchOption.TopDirectoryOnly))
				{
					WadFile wad;
					try
					{
						wad = WadFile.Read(wadPath);
					}
					catch
					{
						// Not every *.wad sitting in maps/ need actually be a
						// readable WAD (a stray, unrelated, or corrupt file) -
						// skip it rather than aborting the whole folder scan.
						continue;
					}

					foreach (var name in wad.FindUdmfMapNames()) maps.Add(new MapEntry(name, IsUdmf: true, wadPath));
					foreach (var name in wad.FindClassicMapNames()) maps.Add(new MapEntry(name, IsUdmf: false, wadPath));
				}
			}

			if (maps.Count == 0)
			{
				ShowError($"No supported maps found in '{Path.GetFileName(dir)}' (looked for maps{Path.DirectorySeparatorChar}*.wad).");
				return;
			}

			_pendingMaps = maps;
			_isRevisitingCurrentMap = false;

			if (maps.Count == 1)
			{
				PromptMapOptionsForPendingMap(0);
				return;
			}

			_mapSelectDialog.SetMaps(maps.Select(m => m.Name).ToList());
			_mapSelectDialog.PopupCentered();
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
		}
	}

	/// <summary>Case-insensitive match for the real GZDoom/ZDoom PK3 "maps" folder name - <see cref="DirectoryResource"/>'s own file index is already fully case-insensitive for the exact same reason.</summary>
	private static string FindMapsSubfolder(string dir)
	{
		foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
		{
			if (string.Equals(Path.GetFileName(sub), "maps", StringComparison.OrdinalIgnoreCase)) return sub;
		}

		return null;
	}

	/// <summary>
	/// Parses the chosen map now (rather than waiting for the Map Options
	/// prompt to be confirmed) so <see cref="GameConfigurationDetector"/>
	/// can use its real Things as a signal, not just the map's name.
	/// </summary>
	private void PromptMapOptionsForPendingMap(int index)
	{
		if (_pendingMaps == null || index < 0 || index >= _pendingMaps.Count) return;
		var map = _pendingMaps[index];

		try
		{
			// A folder-sourced entry (see OnDirSelected) names its own real
			// per-map WAD rather than reusing an already-loaded _pendingWad -
			// only the one map actually picked ever gets read this way.
			if (map.SourceWadPath != null)
			{
				_pendingWad = WadFile.Read(map.SourceWadPath);
				_pendingWadPath = map.SourceWadPath;
				_pendingFileName = Path.GetFileName(map.SourceWadPath);
			}

			if (map.IsUdmf)
			{
				var document = UdmfReader.Read(_pendingWad.ReadMapTextMap(map.Name));
				_pendingMapData = document.Map;
				_pendingNamespace = document.Namespace;
				_pendingUnknownBlocks = document.UnknownBlocks;
			}
			else
			{
				var (mapData, _) = ClassicMapReader.Read(_pendingWad, map.Name);
				_pendingMapData = mapData;
				_pendingNamespace = null;
				_pendingUnknownBlocks = null;
			}

			_pendingMapName = map.Name;
			// Computed here, not by each caller individually - covers a
			// plain file pick, a folder scan, and OpenSpecificMap in one
			// place, since all three already funnel through this method.
			_pendingModRootPath = ModRootDetector.DetectFrom(_pendingWadPath);

			// A folder-rooted mod's own resources/game config are already
			// confirmed once they're saved at all - every other map from the
			// same mod shares them by definition (see MapSettings.GetFolderResources'
			// own remarks), so re-asking for every single one is pure
			// friction, not a real decision each time. A true standalone
			// WAD's own per-map-name resources stay interactively confirmed
			// every time, unchanged - different maps *can* legitimately want
			// different resources there.
			if (HasSavedFolderSettings(_pendingModRootPath))
			{
				PopulateMapOptionsDialogDefaults(_pendingMapData, _pendingModRootPath, map.Name, _pendingFileName);
				OnMapOptionsConfirmed();
				return;
			}

			ShowMapOptionsDialog(_pendingMapData, _pendingModRootPath, map.Name, _pendingFileName);
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
		}
	}

	/// <summary>Whether <paramref name="modRootPath"/> is a folder mod root that already has both a game configuration and at least one resource saved - the two things the Map Options dialog would otherwise ask for, so there's nothing left to confirm.</summary>
	private static bool HasSavedFolderSettings(string modRootPath)
	{
		if (modRootPath == null || !Directory.Exists(modRootPath)) return false;

		var mapSettings = MapSettingsFile.Load(modRootPath);
		return mapSettings.GetGameConfiguration() != null && mapSettings.GetFolderResources().Count > 0;
	}

	/// <summary>
	/// Re-opens the Map Options dialog for the map that's actually loaded
	/// right now, not a freshly picked file - lets a user add/change
	/// resources (e.g. finally point at the IWAD) without closing and
	/// reopening the map. Confirming this fires <see cref="MapResourcesChanged"/>
	/// instead of <see cref="MapLoaded"/>, so <c>MapView</c> can update
	/// rendering in place rather than reloading the map from scratch.
	/// </summary>
	public void ShowMapOptionsForCurrentMap()
	{
		if (_currentMapData == null)
		{
			ShowError("No map is currently loaded.");
			return;
		}

		_pendingWad = _currentWad;
		_pendingWadPath = _currentWadPath;
		_pendingMapData = _currentMapData;
		_pendingMapName = _currentMapName;
		// Reuses the already-known current mod root rather than resetting
		// to null - a revisit of a folder-rooted map needs to keep reading/
		// writing that same shared .dbs, not fall back to a per-WAD one.
		_pendingModRootPath = _currentModRootPath;
		_isRevisitingCurrentMap = true;

		ShowMapOptionsDialog(_currentMapData, _currentModRootPath, _currentMapName, Path.GetFileName(_currentWadPath));
	}

	/// <summary>
	/// Starts the New Map flow: prompts for a map-slot name first (this
	/// project's own scope choice, rather than silently defaulting to
	/// "MAP01"), then reuses the same Map Options (game config + resources)
	/// dialog Open Map already shows, with no backing WAD/file at all - a
	/// brand-new map exists only in memory until the first Save.
	/// </summary>
	public void ShowNewMapDialog() => _newMapDialog.PopupWithDefault("MAP01");

	private void OnNewMapNameEntered(string mapName)
	{
		_pendingWad = null;
		_pendingWadPath = null;
		_pendingFileName = null;
		_pendingMapData = new MapData();
		_pendingMapName = mapName;
		_pendingNamespace = null;
		_pendingUnknownBlocks = null;
		_pendingModRootPath = null;
		_isRevisitingCurrentMap = false;

		ShowMapOptionsDialog(_pendingMapData, null, mapName, null);
	}

	private void ShowMapOptionsDialog(MapData mapData, string modRootPath, string mapName, string fileNameHint)
	{
		PopulateMapOptionsDialogDefaults(mapData, modRootPath, mapName, fileNameHint);
		_mapOptionsDialog.PopupCentered();
	}

	/// <summary>
	/// <paramref name="modRootPath"/> is the real mod root this map's
	/// settings are scoped to (<see cref="ModRootDetector"/>) - a folder
	/// for the real GZDoom/ZDoom per-map-WAD convention, or the WAD itself
	/// for a true standalone WAD, where that's the same thing anyway. When
	/// it's a folder, its own resources are shared flat across every map
	/// from it (<see cref="MapSettings.GetFolderResources"/> - no
	/// legitimate case for one map in a folder wanting different resources
	/// than another), and it's always ensured to be in the pre-filled
	/// resource list itself, not just the place the map's own data
	/// happened to be read from - which is what lets
	/// <see cref="OnMapOptionsConfirmed"/>'s own
	/// <see cref="IResourceContainer.ContainsFile"/> dedup check actually
	/// have something to find. For a true standalone WAD, resources stay
	/// scoped per map name (<see cref="MapSettings.GetResources"/>) - real,
	/// confirmed UDB behavior, unrelated to the folder case.
	/// </summary>
	private void PopulateMapOptionsDialogDefaults(MapData mapData, string modRootPath, string mapName, string fileNameHint)
	{
		var mapSettings = MapSettingsFile.Load(modRootPath);
		var suggestion = mapSettings.GetGameConfiguration()
			?? GameConfigurationDetector.Detect(mapData, mapName, fileNameHint);
		_mapOptionsDialog.SetGameConfiguration(suggestion);

		var isFolderRoot = modRootPath != null && Directory.Exists(modRootPath);
		var savedResources = isFolderRoot ? mapSettings.GetFolderResources() : mapSettings.GetResources(mapName);
		var resourcePaths = (savedResources.Count > 0
			? savedResources
			: AppSettingsFile.Load().GetDefaultResources(suggestion)).ToList();

		if (isFolderRoot && !resourcePaths.Contains(modRootPath, StringComparer.OrdinalIgnoreCase))
		{
			resourcePaths.Add(modRootPath);
		}

		_mapOptionsDialog.SetResourcePaths(resourcePaths);
	}

	/// <summary>
	/// Loads a specific WAD/map immediately, skipping every interactive
	/// dialog (file picker, map picker, and the Map Options confirmation) -
	/// backs the dev-only <c>--file</c>/<c>--map</c> command-line args (see
	/// <see cref="CommandLineOptions"/>).
	/// Pre-fills the Map Options dialog exactly as <see cref="ShowMapOptionsDialog"/>
	/// would, then confirms it immediately as if the user had clicked OK -
	/// still reads/writes the same <c>.dbs</c>/app-settings persistence, so
	/// a subsequent manual "Map Options..." for this map sees the same
	/// state either path would have left it in.
	/// </summary>
	public void LoadFromCommandLine(string wadPath, string mapName)
	{
		try
		{
			var wad = WadFile.Read(wadPath);

			var maps = new List<MapEntry>();
			foreach (var name in wad.FindUdmfMapNames()) maps.Add(new MapEntry(name, IsUdmf: true));
			foreach (var name in wad.FindClassicMapNames()) maps.Add(new MapEntry(name, IsUdmf: false));

			var match = maps.FirstOrDefault(m => string.Equals(m.Name, mapName, StringComparison.OrdinalIgnoreCase));
			if (match.Name == null)
			{
				ShowError($"Map '{mapName}' not found in '{Path.GetFileName(wadPath)}'.");
				return;
			}

			MapData mapData;
			if (match.IsUdmf)
			{
				var document = UdmfReader.Read(wad.ReadMapTextMap(match.Name));
				mapData = document.Map;
				_pendingNamespace = document.Namespace;
				_pendingUnknownBlocks = document.UnknownBlocks;
			}
			else
			{
				var (data, _) = ClassicMapReader.Read(wad, match.Name);
				mapData = data;
				_pendingNamespace = null;
				_pendingUnknownBlocks = null;
			}

			_pendingWad = wad;
			_pendingWadPath = wadPath;
			_pendingFileName = Path.GetFileName(wadPath);
			_pendingMapData = mapData;
			_pendingMapName = match.Name;
			// Doesn't route through PromptMapOptionsForPendingMap (this is a
			// separate, fully non-interactive entry point) - computed here
			// directly instead.
			_pendingModRootPath = ModRootDetector.DetectFrom(wadPath);
			_isRevisitingCurrentMap = false;

			PopulateMapOptionsDialogDefaults(mapData, _pendingModRootPath, match.Name, _pendingFileName);
			OnMapOptionsConfirmed();
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
		}
	}

	private void OnMapOptionsConfirmed()
	{
		if (_pendingMapData == null) return;

		var kind = _mapOptionsDialog.GetGameConfiguration();
		var resourcePaths = _mapOptionsDialog.GetResourcePaths();
		var resourceContainers = _mapOptionsDialog.GetResourceContainers().ToList();

		// A brand-new map (New Map) has no backing WAD of its own yet to
		// append as a resource container - nothing to add in that case.
		// Also skipped when the map's own WAD physically lives inside one of
		// the *other* chosen resources already (the real GZDoom/ZDoom
		// maps/MAP01.wad-inside-a-resource-folder convention, same thing
		// OnDirSelected's own folder-open flow produces by construction) -
		// otherwise it would show up twice: once as that resource's own
		// nested file, once again as its own separate, fully-expanded entry.
		var wadAlreadyCoveredByAnotherResource = _pendingWad != null && _pendingWadPath != null
			&& resourceContainers.Any(c => c.ContainsFile(_pendingWadPath));
		var includePendingWadAsResource = _pendingWad != null && !wadAlreadyCoveredByAnotherResource;

		if (includePendingWadAsResource) resourceContainers.Add(_pendingWad);
		var resources = new ResourceSet(resourceContainers);

		// The trailing pending-WAD entry (if present) is always a fresh
		// WadFile.Read object, even for the exact same unmodified file - a
		// reference-based identity would almost never hit the cache. A
		// path+last-write-time+length token stays equal across repeated
		// opens of the same file but correctly changes on an edit/save;
		// every other entry is already a stable, shared instance (see
		// ResourceContainerCache) and is used as its own identity.
		var identity = new List<object>(resourceContainers);
		if (includePendingWadAsResource)
		{
			identity[^1] = (_pendingWadPath, File.GetLastWriteTimeUtc(_pendingWadPath), new FileInfo(_pendingWadPath).Length);
		}

		var textures = TextureSetCache.Load(resources, identity);
		// Layers in whatever the map's own resources' ZSCRIPT/DECORATE/
		// MAPINFO define on top of the static, `.cfg`-driven game
		// configuration - so a mod's own custom actors show up as
		// placeable Things instead of only the static roster.
		var gameConfiguration = ResourceActorScanner.Scan(GameConfigurations.Get(kind), resources);

		// Paired up in the same order the containers were appended above -
		// the map's own file (no saved path entry of its own) always last,
		// skipped entirely when there's no file yet or it's already covered.
		var resourcePathsForNamedResources = includePendingWadAsResource ? resourcePaths.Append(_pendingWadPath) : resourcePaths;
		var namedResources = resourcePathsForNamedResources
			.Zip(resourceContainers, (path, container) => new NamedResource(Path.GetFileName(path), container, path))
			.ToList();

		if (_isRevisitingCurrentMap)
		{
			MapResourcesChanged?.Invoke(textures, gameConfiguration, namedResources);
		}
		else
		{
			// Updated before MapLoaded fires, not after - CurrentMapContainer/
			// CurrentMapName need to already reflect the map that was just
			// loaded by the time any subscriber (e.g. ResourceBrowserPanel,
			// highlighting whichever tree item is the open map) reacts to it.
			_currentWad = _pendingWad;
			_currentWadPath = _pendingWadPath;
			_currentModRootPath = _pendingModRootPath;
			_currentMapName = _pendingMapName;
			_currentMapData = _pendingMapData;
			_currentNamespace = _pendingNamespace ?? DefaultNamespaceFor(kind);
			_currentUnknownBlocks = _pendingUnknownBlocks ?? Array.Empty<UdmfBlock>();
			MapLoaded?.Invoke(_pendingMapData, textures, gameConfiguration, namedResources);
		}

		// Applies to both branches - "Map Options..." on the already-loaded
		// map can change its game configuration/resources too, not just a
		// freshly opened one.
		_currentGameConfigurationKind = kind;
		_currentResourcePaths = resourcePaths;
		_currentNamedResources = namedResources;

		// A brand-new map has no mod root to key .dbs settings off of yet -
		// that persistence only starts to make sense once it's been saved
		// somewhere for the first time. Directory.Exists is the folder-vs-
		// WAD discriminator - true only for a detected mod folder, false
		// for a plain standalone WAD path (where "per-WAD" and "per-mod"
		// are the same thing anyway).
		if (_pendingModRootPath != null)
		{
			var mapSettings = MapSettingsFile.Load(_pendingModRootPath);
			var updatedSettings = Directory.Exists(_pendingModRootPath)
				? mapSettings.WithFolderSettings(kind, resourcePaths)
				: mapSettings.WithMapSettings(_pendingMapName, kind, resourcePaths);
			MapSettingsFile.Save(_pendingModRootPath, updatedSettings);
		}

		var appSettings = AppSettingsFile.Load().WithDefaultResources(kind, resourcePaths);
		AppSettingsFile.Save(appSettings);

		_pendingMapData = null;
	}

	/// <summary>
	/// The real UDMF namespace string to declare for a map that has none of
	/// its own yet (a brand-new map, or one upgraded from classic binary
	/// format on save) - "zdoom" for this project's one UDMF-native
	/// configuration, "doom" (the vanilla UDMF namespace, also
	/// <see cref="UdmfReader"/>'s own missing-namespace default) otherwise.
	/// </summary>
	private static string DefaultNamespaceFor(GameConfigurationKind kind) =>
		kind == GameConfigurationKind.GZDoomDoom2UDMF ? "zdoom" : "doom";

	/// <summary>
	/// Saves the current map - reuses <see cref="_currentWadPath"/> if this
	/// map has one already, otherwise redirects to <see cref="SaveMapAs"/>.
	/// </summary>
	public void SaveMap()
	{
		if (_currentMapData == null)
		{
			ShowError("No map is currently loaded.");
			return;
		}

		if (_currentWadPath == null)
		{
			SaveMapAs();
			return;
		}

		WriteMapToFile(_currentWadPath, _currentWad?.Lumps);
	}

	/// <summary>
	/// Same save as <see cref="SaveMap"/> (including its own "no path
	/// yet -> <see cref="SaveMapAs"/>" fallback), but calls
	/// <paramref name="onSaved"/> once the write actually completes -
	/// needed because that fallback is asynchronous (it just opens a
	/// dialog and returns; the real write happens later, if/when the
	/// user picks a file). Used by <see cref="TestMapLauncher"/> to
	/// force a save - and, via that, a script recompile
	/// (<see cref="ScriptsCompiled"/>) - before testing, matching UDB's
	/// own Test Map behavior.
	///
	/// Known, accepted limitation: if the map has never been saved and
	/// the user then cancels the resulting Save As prompt,
	/// <paramref name="onSaved"/> is never called for *this* request -
	/// but the subscription behind it isn't explicitly torn down either
	/// (no cancel signal to hang that off, across two separate
	/// confirmation dialogs), so it fires on the *next* save instead,
	/// whenever that happens. Same goes for <see cref="_pendingSaveForTesting"/>:
	/// a cancelled attempt leaves it set, so an unrelated later Save As
	/// would use the Testing node-builder profile once rather than the
	/// Normal one. Harmless either way (worst case: a caller's deferred
	/// work runs once, unexpectedly, on a later unrelated save, or nodes
	/// get built a little faster/rougher than intended that one time) -
	/// not worth the extra plumbing for this narrow a case.
	/// </summary>
	public void SaveMapThen(Action onSaved)
	{
		if (_currentMapData == null)
		{
			ShowError("No map is currently loaded.");
			return;
		}

		if (_currentWadPath != null)
		{
			WriteMapToFile(_currentWadPath, _currentWad?.Lumps, forTesting: true);
			onSaved();
			return;
		}

		void Handler()
		{
			MapSaved -= Handler;
			onSaved();
		}

		MapSaved += Handler;
		_pendingSaveForTesting = true;
		SaveMapAs();
	}

	public void SaveMapAs()
	{
		if (_currentMapData == null)
		{
			ShowError("No map is currently loaded.");
			return;
		}

		_saveFileDialog.PopupCentered();
	}

	/// <summary>
	/// "Save As" semantics: the rebuilt destination's non-map lumps
	/// (PNAMES/TEXTURE1-2, patches, flats, anything else bundled in the
	/// PWAD) always come from the *source* - the file the currently-open
	/// map is already associated with (<see cref="_currentWad"/>).
	/// Whatever already sits at the chosen destination path is irrelevant
	/// and gets fully discarded (after this project's own single-<c>.bak</c>
	/// backup, a simpler stand-in for a full backup-rotation scheme) -
	/// never read, never merged into. Contrast with <see cref="SaveMapInto"/>,
	/// the distinct, separate action for the opposite behavior (appending
	/// into another WAD's own other maps/resources).
	/// </summary>
	private void OnSaveFileSelected(string path)
	{
		if (File.Exists(path))
		{
			_pendingSavePath = path;
			_overwriteConfirmDialog.DialogText = $"'{Path.GetFileName(path)}' already exists. Overwrite it?";
			_overwriteConfirmDialog.PopupCentered();
			return;
		}

		WriteMapToFile(path, _currentWad?.Lumps, _pendingSaveForTesting);
		_pendingSaveForTesting = false;
	}

	private void OnOverwriteConfirmed()
	{
		WriteMapToFile(_pendingSavePath, _currentWad?.Lumps, _pendingSaveForTesting);
		_pendingSavePath = null;
		_pendingSaveForTesting = false;
	}

	/// <summary>
	/// Saves the current map into a (usually different, possibly brand-new)
	/// WAD without touching that WAD's own other maps/resources - the
	/// mirror image of <see cref="SaveMapAs"/>: here the rebuilt
	/// destination's non-map lumps come from the *target* file's own
	/// pre-existing content (if any), preserved and merged into rather
	/// than discarded - so saving into a WAD that already has other maps
	/// (or its own shared PNAMES/TEXTURE1-2/patches/flats) leaves all of
	/// that alone, only touching this map's own lump group. Like
	/// <see cref="SaveMapAs"/>, this still switches the currently-open
	/// map's own file association to the target afterward (not left
	/// pointing at the original source file) - deliberately, not an
	/// oversight, so a subsequent plain Save writes back to the same place
	/// this "Into" save just wrote.
	/// </summary>
	public void SaveMapInto()
	{
		if (_currentMapData == null)
		{
			ShowError("No map is currently loaded.");
			return;
		}

		_saveIntoFileDialog.PopupCentered();
	}

	/// <summary>
	/// Warns only on a real same-map-name collision within the target
	/// ("Target file already contains map "X" - Do you want to replace
	/// it?") - a target with no maps at all, or with other
	/// differently-named maps, is always safe to append into silently, no
	/// prompt at all.
	/// </summary>
	private void OnSaveIntoFileSelected(string path)
	{
		IReadOnlyList<WadLump> originalLumps = null;

		if (File.Exists(path))
		{
			WadFile existing;
			try
			{
				existing = WadFile.Read(path);
			}
			catch (Exception ex)
			{
				ShowError(ex.Message);
				return;
			}

			var alreadyHasThisMap = existing.FindUdmfMapNames().Concat(existing.FindClassicMapNames())
				.Any(name => string.Equals(name, _currentMapName, StringComparison.OrdinalIgnoreCase));

			if (alreadyHasThisMap)
			{
				_pendingSaveIntoPath = path;
				_pendingSaveIntoOriginalLumps = existing.Lumps;
				_mapCollisionConfirmDialog.DialogText =
					$"Target file already contains map \"{_currentMapName}\"\nDo you want to replace it?";
				_mapCollisionConfirmDialog.PopupCentered();
				return;
			}

			originalLumps = existing.Lumps;
		}

		WriteMapToFile(path, originalLumps);
	}

	private void OnMapCollisionConfirmed()
	{
		WriteMapToFile(_pendingSaveIntoPath, _pendingSaveIntoOriginalLumps);
		_pendingSaveIntoPath = null;
		_pendingSaveIntoOriginalLumps = null;
	}

	/// <summary>
	/// The actual on-disk write shared by Save, Save As, and Save Into: builds the
	/// UDMF text for the current map, splices it into
	/// <paramref name="originalLumps"/> (or starts a fresh file if null),
	/// backs up any file it's about to overwrite, then writes the result -
	/// only ever a full-rebuild of the target WAD (see
	/// <see cref="MapFileSaver"/>/<see cref="WadWriter"/>), deliberately
	/// not an in-place lump patch, to avoid the kind of subtle WAD
	/// corruption a partial patch can introduce.
	///
	/// <paramref name="forTesting"/> picks which real node-builder
	/// profile <see cref="NodeBuilderRunner"/> uses - matching UDB's own
	/// real Save-vs-Test node-builder distinction (confirmed from its
	/// source): a faster, rougher one (zero-reject) for a Test Map
	/// launch, a more thorough one for every other save.
	/// </summary>
	private void WriteMapToFile(string path, IReadOnlyList<WadLump> originalLumps, bool forTesting = false)
	{
		try
		{
			var document = new UdmfDocument(_currentMapData, _currentNamespace, _currentUnknownBlocks, Array.Empty<string>());
			var udmfText = UdmfWriter.Write(document);
			var lumps = MapFileSaver.BuildLumpsForSave(originalLumps, _currentMapName, udmfText);
			lumps = CompileScriptsIfPresent(path, lumps);
			lumps = NodeBuilderRunner.Build(lumps, _currentMapName, forTesting);
			var bytes = WadWriter.Write(lumps);

			if (File.Exists(path)) File.Move(path, path + ".bak", overwrite: true);
			File.WriteAllBytes(path, bytes);

			_currentWad = WadFile.Read(path);
			_currentWadPath = path;

			MapSaved?.Invoke();
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
		}
	}

	/// <summary>
	/// Recompiles this map's own <c>SCRIPTS</c> lump into <c>BEHAVIOR</c>,
	/// if it has one - the overwhelmingly common case (no <c>SCRIPTS</c>
	/// lump at all) returns <paramref name="lumps"/> completely unchanged,
	/// zero behavior change from before this existed. A failed compile
	/// (including "no compiler configured/bundled for this OS yet", which
	/// is deliberately silent - see <see cref="ScriptCompilerRunner"/>)
	/// keeps whatever <c>BEHAVIOR</c> bytes were already there; the map's
	/// own geometry still saves either way.
	/// </summary>
	private IReadOnlyList<WadLump> CompileScriptsIfPresent(string path, IReadOnlyList<WadLump> lumps)
	{
		var markerIndex = WadFile.FindMarkerIndex(lumps, _currentMapName);
		var scriptsIndex = markerIndex < 0 ? -1 : WadFile.FindScriptsLumpIndex(lumps, markerIndex);
		if (scriptsIndex < 0) return lumps;

		ScriptsLumpSaving?.Invoke(path, scriptsIndex);

		// The event above may have just flushed an open script tab straight
		// to disk (ScriptDocument.Save's own SaveLump, a full independent
		// WAD rewrite) - re-read just that one lump's bytes fresh rather
		// than trusting lumps[scriptsIndex], which was snapshotted before
		// that flush could have happened.
		var scriptSource = lumps[scriptsIndex].Data;
		if (File.Exists(path))
		{
			var onDisk = WadFile.Read(path);
			var onDiskMarker = WadFile.FindMarkerIndex(onDisk.Lumps, _currentMapName);
			var onDiskScriptsIndex = onDiskMarker < 0 ? -1 : WadFile.FindScriptsLumpIndex(onDisk.Lumps, onDiskMarker);
			if (onDiskScriptsIndex >= 0) scriptSource = onDisk.Lumps[onDiskScriptsIndex].Data;
		}

		var outcome = ScriptCompilerRunner.Compile(path, scriptSource, CurrentResourcePaths);
		if (!outcome.IsConfigured) return lumps;

		if (outcome.BehaviorBytes != null)
		{
			ScriptsCompiled?.Invoke(path, scriptsIndex, Array.Empty<ScriptCompileError>());
			return WadFile.WithSetBehaviorLump(lumps, markerIndex, outcome.BehaviorBytes).Lumps;
		}

		ShowError($"Error while compiling scripts: {outcome.Errors[0].Message}");
		ScriptsCompiled?.Invoke(path, scriptsIndex, outcome.Errors);
		return lumps;
	}

	private void ShowError(string message)
	{
		_errorDialog.DialogText = message;
		_errorDialog.PopupCentered();
	}
}
