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
/// </summary>
public partial class OpenMapMenu : PanelContainer
{
	public event Action<MapData, TextureSet, IGameConfiguration> MapLoaded;
	public event Action<TextureSet, IGameConfiguration> MapResourcesChanged;

	private readonly record struct MapEntry(string Name, bool IsUdmf);

	private FileDialog _fileDialog;
	private AcceptDialog _errorDialog;
	private MapSelectDialog _mapSelectDialog;
	private MapOptionsDialog _mapOptionsDialog;

	private WadFile _pendingWad;
	private string _pendingWadPath;
	private IReadOnlyList<MapEntry> _pendingMaps;
	private string _pendingFileName;
	private MapData _pendingMapData;
	private string _pendingMapName;
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

	public override void _Ready()
	{
		_fileDialog = GetNode<FileDialog>("FileDialog");
		_errorDialog = GetNode<AcceptDialog>("ErrorDialog");
		// Godot only allows one *exclusive* child window per parent window
		// at a time - this can legitimately need to show while the Map
		// Options dialog (itself exclusive) is already open.
		_errorDialog.Exclusive = false;
		_fileDialog.FileSelected += OnFileSelected;

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
			}
			else
			{
				var (mapData, _) = ClassicMapReader.Read(_pendingWad, map.Name);
				_pendingMapData = mapData;
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

	private void ShowMapOptionsDialog(MapData mapData, string wadPath, string mapName, string fileNameHint)
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

		_mapOptionsDialog.PopupCentered();
	}

	private void OnMapOptionsConfirmed()
	{
		if (_pendingMapData == null) return;

		var kind = _mapOptionsDialog.GetGameConfiguration();
		var resourcePaths = _mapOptionsDialog.GetResourcePaths();
		var resourceWads = _mapOptionsDialog.GetResourceWads().Append(_pendingWad).ToList();
		var textures = TextureSet.Load(new WadResourceSet(resourceWads));
		var gameConfiguration = GameConfigurations.Get(kind);

		if (_isRevisitingCurrentMap)
		{
			MapResourcesChanged?.Invoke(textures, gameConfiguration);
		}
		else
		{
			MapLoaded?.Invoke(_pendingMapData, textures, gameConfiguration);
			_currentWad = _pendingWad;
			_currentWadPath = _pendingWadPath;
			_currentMapName = _pendingMapName;
			_currentMapData = _pendingMapData;
		}

		var mapSettings = MapSettingsFile.Load(_pendingWadPath).WithMapSettings(_pendingMapName, kind, resourcePaths);
		MapSettingsFile.Save(_pendingWadPath, mapSettings);

		var appSettings = AppSettingsFile.Load().WithDefaultResources(kind, resourcePaths);
		AppSettingsFile.Save(appSettings);

		_pendingMapData = null;
	}

	private void ShowError(string message)
	{
		_errorDialog.DialogText = message;
		_errorDialog.PopupCentered();
	}
}
