using DoomArchitect.Core.Configuration;
using DoomArchitect.Settings;
using Godot;

/// <summary>
/// Edits DoomArchitect's global, app-wide default resources per game
/// configuration - matches UDB's own real "Game Configurations" dialog
/// UX: a list of configurations on one side, and a single shared details
/// area on the other that swaps to show whichever one is currently
/// selected, rather than duplicating the same fields once per
/// configuration. Switching the selection captures whatever's currently
/// in the details area into an in-memory working copy first (so it isn't
/// lost), and only writes to disk when the dialog is actually confirmed -
/// picking a different configuration to look at isn't itself a commit.
/// </summary>
public partial class PreferencesDialog : AcceptDialog
{
	private static readonly GameConfigurationKind[] Kinds = { GameConfigurationKind.Doom, GameConfigurationKind.Doom2 };

	private ItemList _configList;
	private ResourceListEditor _resourceListEditor;

	private AppSettings _workingSettings;
	private int _selectedIndex;

	public override void _Ready()
	{
		_configList = GetNode<ItemList>("Container/ConfigList");
		_resourceListEditor = GetNode<ResourceListEditor>("Container/ResourceListEditor");
		_resourceListEditor.HintText = "Default resources for this game (e.g. its IWAD) - pre-fills new maps for it.";

		foreach (var kind in Kinds) _configList.AddItem(kind.ToString());
		_configList.ItemSelected += OnConfigSelected;

		Confirmed += OnConfirmed;
	}

	/// <summary>Loads the current settings fresh every time, rather than once at startup - another session's/dialog's save since then should be reflected.</summary>
	public void Open()
	{
		_workingSettings = AppSettingsFile.Load();
		_selectedIndex = 0;
		_configList.Select(0);
		_resourceListEditor.SetResourcePaths(_workingSettings.GetDefaultResources(Kinds[0]));
		PopupCentered();
	}

	private void OnConfigSelected(long index)
	{
		CaptureCurrentSelection();
		_selectedIndex = (int)index;
		_resourceListEditor.SetResourcePaths(_workingSettings.GetDefaultResources(Kinds[_selectedIndex]));
	}

	private void OnConfirmed()
	{
		CaptureCurrentSelection();
		AppSettingsFile.Save(_workingSettings);
	}

	private void CaptureCurrentSelection() =>
		_workingSettings = _workingSettings.WithDefaultResources(Kinds[_selectedIndex], _resourceListEditor.GetResourcePaths());
}
