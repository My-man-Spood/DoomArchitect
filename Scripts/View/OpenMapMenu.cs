using System;
using System.Collections.Generic;
using System.IO;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Textures;
using Godot;

/// <summary>
/// A button that opens a file browser filtered to .wad files, looking for
/// every map inside it - both UDMF and the classic binary format, so both
/// modern-editor maps and the original id Software WADs work - and
/// reporting the result via <see cref="MapLoaded"/>, alongside a
/// <see cref="TextureSet"/> resolved from that same WAD (textures are
/// scoped to a single WAD for now - see the texture pipeline plan) and the
/// <see cref="IGameConfiguration"/> the user confirmed for it. A WAD with
/// only one map skips straight to the game-configuration prompt; a WAD
/// with several (like a real IWAD) first pops a small map picker, built
/// entirely in code, no scene wiring needed. The game-configuration prompt
/// itself always appears regardless of map count - matching UDB's own
/// explicit "Configurations" dialog rather than silently auto-picking -
/// pre-selected with <see cref="GameConfigurationDetector"/>'s best guess
/// but always requiring confirmation. Deliberately unaware of
/// <c>MapView</c> so this stays a plain "pick a file, hand back a
/// MapData" widget, matching the rest of this project's separation
/// between the map-editing surface and general UI.
/// </summary>
public partial class OpenMapMenu : PanelContainer
{
	public event Action<MapData, TextureSet, IGameConfiguration> MapLoaded;

	private readonly record struct MapEntry(string Name, bool IsUdmf);

	private FileDialog _fileDialog;
	private AcceptDialog _errorDialog;
	private AcceptDialog _mapSelectDialog;
	private ItemList _mapSelectList;
	private AcceptDialog _gameConfigDialog;
	private OptionButton _gameConfigOption;

	private WadFile _pendingWad;
	private IReadOnlyList<MapEntry> _pendingMaps;
	private string _pendingFileName;

	private MapData _pendingMapData;
	private TextureSet _pendingTextures;

	public override void _Ready()
	{
		var openButton = GetNode<Button>("MarginContainer/OpenButton");
		_fileDialog = GetNode<FileDialog>("FileDialog");
		_errorDialog = GetNode<AcceptDialog>("ErrorDialog");

		openButton.Pressed += () => _fileDialog.PopupCentered();
		_fileDialog.FileSelected += OnFileSelected;

		BuildMapSelectDialog();
		BuildGameConfigDialog();
	}

	private void BuildMapSelectDialog()
	{
		_mapSelectList = new ItemList { CustomMinimumSize = new Vector2(300, 200) };
		_mapSelectList.ItemActivated += index =>
		{
			_mapSelectDialog.Hide();
			PromptGameConfigurationForPendingMap((int)index);
		};

		_mapSelectDialog = new AcceptDialog { Title = "Select a map" };
		_mapSelectDialog.AddChild(_mapSelectList);
		_mapSelectDialog.Confirmed += OnMapSelectionConfirmed;
		AddChild(_mapSelectDialog);
	}

	private void BuildGameConfigDialog()
	{
		var container = new VBoxContainer();
		container.AddChild(new Label { Text = "Which game does this map belong to?" });

		_gameConfigOption = new OptionButton();
		_gameConfigOption.AddItem("Doom");
		_gameConfigOption.AddItem("Doom2");
		container.AddChild(_gameConfigOption);

		_gameConfigDialog = new AcceptDialog { Title = "Game configuration" };
		_gameConfigDialog.AddChild(container);
		_gameConfigDialog.Confirmed += OnGameConfigurationConfirmed;
		AddChild(_gameConfigDialog);
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
			_pendingMaps = maps;
			_pendingFileName = Path.GetFileName(path);

			if (maps.Count == 1)
			{
				PromptGameConfigurationForPendingMap(0);
				return;
			}

			_mapSelectList.Clear();
			foreach (var map in maps) _mapSelectList.AddItem(map.Name);
			_mapSelectDialog.PopupCentered();
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
		}
	}

	private void OnMapSelectionConfirmed()
	{
		var selected = _mapSelectList.GetSelectedItems();
		if (selected.Length > 0) PromptGameConfigurationForPendingMap(selected[0]);
	}

	/// <summary>
	/// Parses the chosen map now (rather than waiting for the game-
	/// configuration prompt to be confirmed) so <see cref="GameConfigurationDetector"/>
	/// can use its real Things as a signal, not just the map's name.
	/// </summary>
	private void PromptGameConfigurationForPendingMap(int index)
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

			_pendingTextures = TextureSet.Load(_pendingWad);

			var suggestion = GameConfigurationDetector.Detect(_pendingMapData, map.Name, _pendingFileName);
			_gameConfigOption.Selected = suggestion == GameConfigurationKind.Doom2 ? 1 : 0;
			_gameConfigDialog.PopupCentered();
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
		}
	}

	private void OnGameConfigurationConfirmed()
	{
		if (_pendingMapData == null || _pendingTextures == null) return;

		var kind = _gameConfigOption.Selected == 1 ? GameConfigurationKind.Doom2 : GameConfigurationKind.Doom;
		MapLoaded?.Invoke(_pendingMapData, _pendingTextures, GameConfigurations.Get(kind));

		_pendingMapData = null;
		_pendingTextures = null;
	}

	private void ShowError(string message)
	{
		_errorDialog.DialogText = message;
		_errorDialog.PopupCentered();
	}
}
