using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Input;
using DoomArchitect.Input;
using DoomArchitect.Settings;
using Godot;

/// <summary>
/// Edits DoomArchitect's global, app-wide settings - matches UDB's own
/// real Preferences dialog UX of a category-grouped set of pages: "Game
/// Configurations" (a list of configurations on one side, a single shared
/// details area on the other that swaps to show whichever one is
/// currently selected, rather than duplicating the same fields once per
/// configuration) and "Keybinds" (<see cref="KeybindsEditor"/>, UDB's own
/// real Controls page). Switching selection within the Game Configurations
/// tab captures whatever's currently in the details area into an in-memory
/// working copy first (so it isn't lost), and only writes to disk - and
/// live-applies any rebound keys - when the dialog is actually confirmed.
/// </summary>
public partial class PreferencesDialog : AcceptDialog
{
	private static readonly GameConfigurationKind[] Kinds = { GameConfigurationKind.Doom, GameConfigurationKind.Doom2, GameConfigurationKind.GZDoomDoom2UDMF };

	private ItemList _configList;
	private ResourceListEditor _resourceListEditor;
	private KeybindsEditor _keybindsEditor;

	private AppSettings _workingSettings;
	private int _selectedIndex;

	public override void _Ready()
	{
		var tabs = GetNode<TabContainer>("Tabs");
		tabs.SetTabTitle(0, "Game Configurations");
		tabs.SetTabTitle(1, "Keybinds");

		_configList = GetNode<ItemList>("Tabs/Container/ConfigList");
		_resourceListEditor = GetNode<ResourceListEditor>("Tabs/Container/ResourceListEditor");
		_resourceListEditor.HintText = "Default resources for this game (e.g. its IWAD) - pre-fills new maps for it.";
		_keybindsEditor = GetNode<KeybindsEditor>("Tabs/KeybindsEditor");

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
		_keybindsEditor.Load(_workingSettings.GetKeyBindingOverrides());
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

		// Every action gets reset first, then every entry the editor's own
		// working copy still has gets (re)written - simpler and just as
		// correct as diffing old-vs-new, and this project's whole registry
		// is small enough (36 actions) that the extra writes cost nothing.
		foreach (var definition in KeyBindingRegistry.All)
		{
			_workingSettings = _workingSettings.WithKeyBindingReset(definition.Name);
		}

		foreach (var (action, binding) in _keybindsEditor.GetOverrides())
		{
			_workingSettings = _workingSettings.WithKeyBindingOverride(action, binding);
		}

		AppSettingsFile.Save(_workingSettings);

		// Re-applies every action's default-or-override binding to the
		// live InputMap immediately - a rebind takes effect right away,
		// no restart needed.
		KeyBindings.Bootstrap();
	}

	private void CaptureCurrentSelection() =>
		_workingSettings = _workingSettings.WithDefaultResources(Kinds[_selectedIndex], _resourceListEditor.GetResourcePaths());
}
