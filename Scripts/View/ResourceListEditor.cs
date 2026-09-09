using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DoomArchitect.Core.IO;
using Godot;

/// <summary>
/// A reusable "ordered list of resource WADs" editor - an <see cref="ItemList"/>
/// plus Add.../Remove-selected buttons and a nested "add" <see cref="FileDialog"/>,
/// wrapping the actual loaded <see cref="WadFile"/>s so callers never touch
/// raw paths without also having the parsed WAD ready to use. The same
/// widget is embedded in both <c>MapOptionsDialog</c> (a map's own
/// resources) and <c>PreferencesDialog</c> (a game configuration's app-
/// wide default resources) - mirrors UDB's own real <c>ResourceListEditor</c>
/// control, which is reused for exactly the same two purposes (found
/// while researching the multi-resource feature this builds on).
/// </summary>
public partial class ResourceListEditor : VBoxContainer
{
	private readonly record struct ResourceEntry(string Path, WadFile Wad);

	[Export] public string HintText { get; set; } = "Additional resource WADs - lower items override higher ones.";

	private Label _hintLabel;
	private ItemList _list;
	private FileDialog _addFileDialog;
	private AcceptDialog _errorDialog;

	private readonly List<ResourceEntry> _resources = new();

	public override void _Ready()
	{
		_hintLabel = GetNode<Label>("HintLabel");
		_list = GetNode<ItemList>("ItemList");
		var addButton = GetNode<Button>("ButtonsRow/AddButton");
		var removeButton = GetNode<Button>("ButtonsRow/RemoveButton");
		_addFileDialog = GetNode<FileDialog>("AddFileDialog");
		_errorDialog = GetNode<AcceptDialog>("ErrorDialog");

		_hintLabel.Text = HintText;
		addButton.Pressed += () => _addFileDialog.PopupCentered();
		removeButton.Pressed += OnRemovePressed;
		_addFileDialog.FileSelected += OnFileSelected;
	}

	/// <summary>Replaces the whole list, loading each path as a <see cref="WadFile"/> - one that fails to load (e.g. moved on disk) is skipped and reported, not fatal to the rest.</summary>
	public void SetResourcePaths(IReadOnlyList<string> paths)
	{
		_resources.Clear();
		var failed = new List<string>();

		foreach (var path in paths)
		{
			try
			{
				_resources.Add(new ResourceEntry(path, WadFile.Read(path)));
			}
			catch (Exception)
			{
				failed.Add(path);
			}
		}

		Refresh();

		if (failed.Count > 0)
		{
			ShowError($"Couldn't load {failed.Count} remembered resource(s) - they may have moved:\n{string.Join('\n', failed)}");
		}
	}

	public IReadOnlyList<string> GetResourcePaths() => _resources.Select(r => r.Path).ToList();

	public IReadOnlyList<WadFile> GetResourceWads() => _resources.Select(r => r.Wad).ToList();

	private void OnFileSelected(string path)
	{
		try
		{
			_resources.Add(new ResourceEntry(path, WadFile.Read(path)));
			Refresh();
		}
		catch (Exception ex)
		{
			ShowError(ex.Message);
		}
	}

	private void OnRemovePressed()
	{
		var selected = _list.GetSelectedItems();
		if (selected.Length == 0) return;

		_resources.RemoveAt(selected[0]);
		Refresh();
	}

	private void Refresh()
	{
		_list.Clear();
		foreach (var resource in _resources) _list.AddItem(Path.GetFileName(resource.Path));
	}

	private void ShowError(string message)
	{
		_errorDialog.DialogText = message;
		_errorDialog.PopupCentered();
	}
}
