using System;
using System.IO;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Map;
using Godot;

/// <summary>
/// A button that opens a file browser filtered to .wad files, loads the
/// first UDMF-format map it finds inside, and reports the result via
/// <see cref="MapLoaded"/> - deliberately unaware of <c>MapView</c> so
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
			var mapNames = wad.FindUdmfMapNames();
			if (mapNames.Count == 0)
			{
				ShowError($"No UDMF maps found in '{Path.GetFileName(path)}'.\n\nClassic binary-format maps (like the original id Software WADs) aren't supported yet.");
				return;
			}

			var document = UdmfReader.Read(wad.ReadMapTextMap(mapNames[0]));
			MapLoaded?.Invoke(document.Map);
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
