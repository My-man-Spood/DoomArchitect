using Godot;

/// <summary>
/// The conventional top menu bar (File / Map / Preferences) - a native
/// Godot 4.4+ <see cref="MenuBar"/>, whose children are the
/// <see cref="PopupMenu"/>s shown as its top-level entries (each child's
/// node name is its label). <see cref="Initialize"/> is called by
/// <c>MapView</c> once it has resolved <see cref="OpenMapMenu"/> itself,
/// keeping node-path lookups centralized there rather than duplicated
/// here.
/// </summary>
public partial class MainMenuBar : MenuBar
{
	private OpenMapMenu _openMapMenu;
	private PreferencesDialog _preferencesDialog;

	public void Initialize(OpenMapMenu openMapMenu)
	{
		_openMapMenu = openMapMenu;

		var fileMenu = GetNode<PopupMenu>("File");
		fileMenu.AddItem("Open Map...", 0);
		fileMenu.IdPressed += id =>
		{
			if (id == 0) _openMapMenu.ShowOpenFileDialog();
		};

		var mapMenu = GetNode<PopupMenu>("Map");
		mapMenu.AddItem("Map Options...", 0);
		mapMenu.IdPressed += id =>
		{
			if (id == 0) _openMapMenu.ShowMapOptionsForCurrentMap();
		};

		var preferencesMenu = GetNode<PopupMenu>("Preferences");
		preferencesMenu.AddItem("Resources...", 0);
		preferencesMenu.IdPressed += id =>
		{
			if (id == 0) OpenPreferences();
		};
	}

	private void OpenPreferences()
	{
		_preferencesDialog ??= CreatePreferencesDialog();
		_preferencesDialog.Open();
	}

	private PreferencesDialog CreatePreferencesDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/PreferencesDialog.tscn").Instantiate<PreferencesDialog>();
		AddChild(dialog);
		return dialog;
	}
}
