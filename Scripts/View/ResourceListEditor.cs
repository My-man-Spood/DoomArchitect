using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.ZDoom;
using DoomArchitect.Settings;
using Godot;

/// <summary>
/// A reusable "ordered list of resources" editor - an <see cref="ItemList"/>
/// plus Add.../Remove-selected buttons and a nested "add" <see cref="FileDialog"/>
/// (<c>FileModeEnum.OpenAny</c>, so a WAD/PK3 file or a loose folder can be
/// picked in the same dialog), wrapping the actual loaded
/// <see cref="IResourceContainer"/>s - see <see cref="ResourceContainerFactory"/> -
/// so callers never touch raw paths without also having the parsed resource
/// ready to use. The same
/// widget is embedded in both <c>MapOptionsDialog</c> (a map's own
/// resources) and <c>PreferencesDialog</c> (a game configuration's app-
/// wide default resources).
/// </summary>
public partial class ResourceListEditor : VBoxContainer
{
	private readonly record struct ResourceEntry(string Path, IResourceContainer Container);

	[Export] public string HintText { get; set; } = "Additional resource WADs/PK3s - lower items override higher ones.";

	private Label _hintLabel;
	private ItemList _list;
	private FileDialog _addFileDialog;
	private AcceptDialog _errorDialog;
	private Label _warningsLabel;

	private readonly List<ResourceEntry> _resources = new();
	private IGameConfiguration _gameConfiguration;

	/// <summary>
	/// Which game configuration's own <see cref="IGameConfiguration.GetRequiredArchives"/>
	/// the warning below checks against - null skips the check entirely
	/// (e.g. before a caller has resolved one yet). Setting this re-checks
	/// immediately, so switching game configurations with resources already
	/// listed updates the warning right away.
	/// </summary>
	public IGameConfiguration GameConfiguration
	{
		get => _gameConfiguration;
		set
		{
			_gameConfiguration = value;
			UpdateWarnings();
		}
	}

	public override void _Ready()
	{
		_hintLabel = GetNode<Label>("HintLabel");
		_list = GetNode<ItemList>("ItemList");
		var addButton = GetNode<Button>("ButtonsRow/AddButton");
		var removeButton = GetNode<Button>("ButtonsRow/RemoveButton");
		_addFileDialog = GetNode<FileDialog>("AddFileDialog");
		_errorDialog = GetNode<AcceptDialog>("ErrorDialog");
		_warningsLabel = GetNode<Label>("WarningsLabel");

		_hintLabel.Text = HintText;
		addButton.Pressed += () => _addFileDialog.PopupCentered();
		removeButton.Pressed += OnRemovePressed;
		_addFileDialog.FileSelected += OnFileSelected;
		_addFileDialog.DirSelected += OnFileSelected;
	}

	/// <summary>Replaces the whole list, loading each path via <see cref="ResourceContainerCache"/> (a WAD or a PK3, reused if another tab already opened the same path) - one that fails to load (e.g. moved on disk) is skipped and reported, not fatal to the rest.</summary>
	public void SetResourcePaths(IReadOnlyList<string> paths)
	{
		_resources.Clear();
		var failed = new List<string>();

		foreach (var path in paths)
		{
			try
			{
				_resources.Add(new ResourceEntry(path, ResourceContainerCache.Open(path)));
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

	public IReadOnlyList<IResourceContainer> GetResourceContainers() => _resources.Select(r => r.Container).ToList();

	private void OnFileSelected(string path)
	{
		try
		{
			_resources.Add(new ResourceEntry(path, ResourceContainerCache.Open(path)));
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
		UpdateWarnings();
	}

	/// <summary>
	/// One line per <see cref="IGameConfiguration.GetRequiredArchives"/>
	/// entry that nothing in the current list actually satisfies (by real
	/// content, via <see cref="RequiredArchiveDetector"/> - not by file
	/// name) - matches UDB's own real "a resource archive is required...
	/// but not present" warning.
	/// </summary>
	private void UpdateWarnings()
	{
		if (_warningsLabel == null) return; // not _Ready yet - GameConfiguration's setter can run before then

		var lines = new List<string>();
		if (_gameConfiguration != null)
		{
			foreach (var archive in _gameConfiguration.GetRequiredArchives())
			{
				var found = _resources.Any(r => RequiredArchiveDetector.Matches(archive, r.Container));
				if (!found) lines.Add($"A resource archive is required for this game configuration, but not present: \"{archive.FileName}\".");
			}
		}

		_warningsLabel.Text = string.Join("\n", lines);
		_warningsLabel.Visible = lines.Count > 0;
	}

	private void ShowError(string message)
	{
		_errorDialog.DialogText = message;
		_errorDialog.PopupCentered();
	}
}
