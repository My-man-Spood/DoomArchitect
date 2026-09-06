using System;
using System.Collections.Generic;
using System.IO;
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
/// scoped to a single WAD for now - see the texture pipeline plan). A WAD
/// with only one map loads it immediately; a WAD with several (like a
/// real IWAD) pops up a small picker built entirely in code, no scene
/// wiring needed. Deliberately unaware of <c>MapView</c> so this stays a
/// plain "pick a file, hand back a MapData" widget, matching the rest of
/// this project's separation between the map-editing surface and general
/// UI.
/// </summary>
public partial class OpenMapMenu : PanelContainer
{
	public event Action<MapData, TextureSet> MapLoaded;

	private readonly record struct MapEntry(string Name, bool IsUdmf);

	private FileDialog _fileDialog;
	private AcceptDialog _errorDialog;
	private AcceptDialog _mapSelectDialog;
	private ItemList _mapSelectList;

	private WadFile _pendingWad;
	private IReadOnlyList<MapEntry> _pendingMaps;

	public override void _Ready()
	{
		var openButton = GetNode<Button>("MarginContainer/OpenButton");
		_fileDialog = GetNode<FileDialog>("FileDialog");
		_errorDialog = GetNode<AcceptDialog>("ErrorDialog");

		openButton.Pressed += () => _fileDialog.PopupCentered();
		_fileDialog.FileSelected += OnFileSelected;

		BuildMapSelectDialog();
	}

	private void BuildMapSelectDialog()
	{
		_mapSelectList = new ItemList { CustomMinimumSize = new Vector2(300, 200) };
		_mapSelectList.ItemActivated += index =>
		{
			_mapSelectDialog.Hide();
			LoadPendingMap((int)index);
		};

		_mapSelectDialog = new AcceptDialog { Title = "Select a map" };
		_mapSelectDialog.AddChild(_mapSelectList);
		_mapSelectDialog.Confirmed += OnMapSelectionConfirmed;
		AddChild(_mapSelectDialog);
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

			if (maps.Count == 1)
			{
				LoadMap(wad, maps[0]);
				return;
			}

			_pendingWad = wad;
			_pendingMaps = maps;
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
		if (selected.Length > 0) LoadPendingMap(selected[0]);
	}

	private void LoadPendingMap(int index)
	{
		if (_pendingMaps == null || index < 0 || index >= _pendingMaps.Count) return;
		LoadMap(_pendingWad, _pendingMaps[index]);
	}

	private void LoadMap(WadFile wad, MapEntry map)
	{
		try
		{
			if (map.IsUdmf)
			{
				var document = UdmfReader.Read(wad.ReadMapTextMap(map.Name));
				MapLoaded?.Invoke(document.Map, TextureSet.Load(wad));
			}
			else
			{
				var (mapData, _) = ClassicMapReader.Read(wad, map.Name);
				MapLoaded?.Invoke(mapData, TextureSet.Load(wad));
			}
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
