using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Textures;
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
/// Options prompt itself always appears regardless of map count -
/// matching UDB's own real combined dialog rather than silently auto-
/// picking - pre-filled from this WAD's own remembered <c>.dbs</c>
/// settings if present, else from the game configuration's app-wide
/// default resources, but always requiring confirmation. Confirming saves
/// both back, so opening the same map again remembers its resources, and
/// opening a different, previously-unopened map for the same game
/// configuration is pre-filled with the same default.
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

	private readonly record struct MapEntry(string Name, bool IsUdmf);

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

	// The map actually loaded and displayed right now - distinct from the
	// "_pending" load-in-progress state above, which only lives for the
	// duration of one open/confirm flow. Set only once a load truly
	// completes, so "Map Options..." can always revisit the real current
	// map regardless of what's mid-flight (or aborted) since.
	private WadFile _currentWad;
	private string _currentWadPath;
	private string _currentMapName;
	private MapData _currentMapData;
	private string _currentNamespace;
	private IReadOnlyList<UdmfBlock> _currentUnknownBlocks;

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
		_fileDialog.FileSelected += OnFileSelected;
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

	public void ShowOpenFileDialog() => _fileDialog.PopupCentered();

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
			ShowMapOptionsDialog(_pendingMapData, _pendingWadPath, map.Name, _pendingFileName);
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
		}
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
		_isRevisitingCurrentMap = true;

		ShowMapOptionsDialog(_currentMapData, _currentWadPath, _currentMapName, Path.GetFileName(_currentWadPath));
	}

	/// <summary>
	/// Starts the New Map flow: prompts for a map-slot name first (per this
	/// project's own scope choice - UDB itself silently defaults to
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
		_isRevisitingCurrentMap = false;

		ShowMapOptionsDialog(_pendingMapData, null, mapName, null);
	}

	private void ShowMapOptionsDialog(MapData mapData, string wadPath, string mapName, string fileNameHint)
	{
		PopulateMapOptionsDialogDefaults(mapData, wadPath, mapName, fileNameHint);
		_mapOptionsDialog.PopupCentered();
	}

	private void PopulateMapOptionsDialogDefaults(MapData mapData, string wadPath, string mapName, string fileNameHint)
	{
		var mapSettings = MapSettingsFile.Load(wadPath);
		var suggestion = mapSettings.GetGameConfiguration()
			?? GameConfigurationDetector.Detect(mapData, mapName, fileNameHint);
		_mapOptionsDialog.SetGameConfiguration(suggestion);

		var savedResources = mapSettings.GetResources(mapName);
		var resourcePaths = savedResources.Count > 0
			? savedResources
			: AppSettingsFile.Load().GetDefaultResources(suggestion);
		_mapOptionsDialog.SetResourcePaths(resourcePaths);
	}

	/// <summary>
	/// Loads a specific WAD/map immediately, skipping every interactive
	/// dialog (file picker, map picker, and the Map Options confirmation) -
	/// backs the dev-only <c>--file</c>/<c>--map</c> command-line args (see
	/// <see cref="CommandLineOptions"/>), not a real UDB-parity feature.
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
			_isRevisitingCurrentMap = false;

			PopulateMapOptionsDialogDefaults(mapData, wadPath, match.Name, _pendingFileName);
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
		if (_pendingWad != null) resourceContainers.Add(_pendingWad);
		var textures = TextureSet.Load(new ResourceSet(resourceContainers));
		var gameConfiguration = GameConfigurations.Get(kind);

		// Paired up in the same order the containers were appended above -
		// the map's own file (no saved path entry of its own) always last,
		// skipped entirely when there's no file yet.
		var resourcePathsForNamedResources = _pendingWad != null ? resourcePaths.Append(_pendingWadPath) : resourcePaths;
		var namedResources = resourcePathsForNamedResources
			.Zip(resourceContainers, (path, container) => new NamedResource(Path.GetFileName(path), container))
			.ToList();

		if (_isRevisitingCurrentMap)
		{
			MapResourcesChanged?.Invoke(textures, gameConfiguration, namedResources);
		}
		else
		{
			MapLoaded?.Invoke(_pendingMapData, textures, gameConfiguration, namedResources);
			_currentWad = _pendingWad;
			_currentWadPath = _pendingWadPath;
			_currentMapName = _pendingMapName;
			_currentMapData = _pendingMapData;
			_currentNamespace = _pendingNamespace ?? DefaultNamespaceFor(kind);
			_currentUnknownBlocks = _pendingUnknownBlocks ?? Array.Empty<UdmfBlock>();
		}

		// A brand-new map has no WAD path to key .dbs settings off of yet -
		// that persistence only starts to make sense once it's been saved
		// somewhere for the first time.
		if (_pendingWadPath != null)
		{
			var mapSettings = MapSettingsFile.Load(_pendingWadPath).WithMapSettings(_pendingMapName, kind, resourcePaths);
			MapSettingsFile.Save(_pendingWadPath, mapSettings);
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
	/// map has one already, otherwise redirects to <see cref="SaveMapAs"/>,
	/// matching UDB's own real Save/SaveAs split exactly.
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
	/// Matches UDB's own real "Save As" semantics exactly, verified
	/// directly against its source (<c>MapManager.SaveMap</c>,
	/// <c>SavePurpose.AsNewFile</c>): the rebuilt destination's non-map
	/// lumps (PNAMES/TEXTURE1-2, patches, flats, anything else bundled in
	/// the PWAD) always come from the *source* - the file the currently-
	/// open map is already associated with (<see cref="_currentWad"/>) -
	/// via a real <c>File.Copy(filepathname, newfilepathname, true)</c> in
	/// UDB's own code before it ever touches the target. Whatever already
	/// sits at the chosen destination path is irrelevant and gets fully
	/// discarded (after this project's own single-<c>.bak</c> backup, a
	/// simpler stand-in for UDB's real 3-level rotation) - never read,
	/// never merged into. Contrast with <see cref="SaveMapInto"/>, UDB's
	/// distinct, separate action for the opposite behavior (appending into
	/// another WAD's own other maps/resources).
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

		WriteMapToFile(path, _currentWad?.Lumps);
	}

	private void OnOverwriteConfirmed()
	{
		WriteMapToFile(_pendingSavePath, _currentWad?.Lumps);
		_pendingSavePath = null;
	}

	/// <summary>
	/// Saves the current map into a (usually different, possibly brand-new)
	/// WAD without touching that WAD's own other maps/resources - UDB's own
	/// real "Save Map Into" (<c>SavePurpose.IntoFile</c>), the mirror image
	/// of <see cref="SaveMapAs"/>: here the rebuilt destination's non-map
	/// lumps come from the *target* file's own pre-existing content (if
	/// any), preserved and merged into rather than discarded - so saving
	/// into a WAD that already has other maps (or its own shared
	/// PNAMES/TEXTURE1-2/patches/flats) leaves all of that alone, only
	/// touching this map's own lump group. Verified directly against UDB's
	/// source: like <see cref="SaveMapAs"/>, this still switches the
	/// currently-open map's own file association to the target afterward
	/// (not left pointing at the original source file) - UDB's real
	/// <c>filepathname</c> reassignment in <c>MapManager.SaveMap</c> isn't
	/// conditioned on <c>SavePurpose.IntoFile</c> at all, only on
	/// <c>Testing</c>/<c>Autosave</c>, so this matches that exactly rather
	/// than guessing a "nicer" behavior UDB doesn't actually have.
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
	/// Warns only on a real same-map-name collision within the target,
	/// matching UDB's own real prompt exactly ("Target file already
	/// contains map "X" - Do you want to replace it?") - a target with no
	/// maps at all, or with other differently-named maps, is always safe
	/// to append into silently, no prompt at all (matches UDB's own real
	/// <c>FindAndRemoveMap</c> short-circuit).
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
	/// only ever a full-rebuild of the target WAD, matching
	/// <see cref="MapFileSaver"/>/<see cref="WadWriter"/>'s own real
	/// approach (mirroring UDB's own, cited in their own source as a fix
	/// for GitHub issue #531).
	/// </summary>
	private void WriteMapToFile(string path, IReadOnlyList<WadLump> originalLumps)
	{
		try
		{
			var document = new UdmfDocument(_currentMapData, _currentNamespace, _currentUnknownBlocks, Array.Empty<string>());
			var bytes = MapFileSaver.SaveUdmfMap(originalLumps, document, _currentMapName);

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

	private void ShowError(string message)
	{
		_errorDialog.DialogText = message;
		_errorDialog.PopupCentered();
	}
}
