using System;
using System.IO;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Map;
using Godot;

/// <summary>
/// A button that opens a file browser filtered to .wad files and loads
/// the first map it finds inside - trying UDMF first, falling back to
/// the classic binary format (so both modern-editor maps and the
/// original id Software WADs work), and reporting the result via
/// <see cref="MapLoaded"/>. Deliberately unaware of <c>MapView</c> so
/// this stays a plain "pick a file, hand back a MapData" widget, matching
/// the rest of this project's separation between the map-editing surface
/// and general UI.
/// </summary>
public partial class OpenMapMenu : PanelContainer
{
	public event Action<MapData> MapLoaded;

	private FileDialog _fileDialog;
	private AcceptDialog _errorDialog;

	public override void _Ready()
	{
		var openButton = GetNode<Button>("MarginContainer/OpenButton");
		_fileDialog = GetNode<FileDialog>("FileDialog");
		_errorDialog = GetNode<AcceptDialog>("ErrorDialog");

		openButton.Pressed += () => _fileDialog.PopupCentered();
		_fileDialog.FileSelected += OnFileSelected;
	}

	private void OnFileSelected(string path)
	{
		try
		{
			var wad = WadFile.Read(path);

			var udmfMapNames = wad.FindUdmfMapNames();
			if (udmfMapNames.Count > 0)
			{
				var document = UdmfReader.Read(wad.ReadMapTextMap(udmfMapNames[0]));
				MapLoaded?.Invoke(document.Map);
				return;
			}

			var classicMapNames = wad.FindClassicMapNames();
			if (classicMapNames.Count > 0)
			{
				var (map, _) = ClassicMapReader.Read(wad, classicMapNames[0]);
				MapLoaded?.Invoke(map);
				return;
			}

			ShowError($"No supported maps found in '{Path.GetFileName(path)}'.");
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
