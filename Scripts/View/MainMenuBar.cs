using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Map;
using Godot;

/// <summary>
/// The conventional top menu bar (File / Edit / Map / Preferences) - a
/// native Godot 4.4+ <see cref="MenuBar"/>, whose children are the
/// <see cref="PopupMenu"/>s shown as its top-level entries (each child's
/// node name is its label). <see cref="Initialize"/> is called by
/// <c>MapView</c> once it has resolved <see cref="OpenMapMenu"/> itself,
/// keeping node-path lookups centralized there rather than duplicated
/// here.
/// </summary>
public partial class MainMenuBar : MenuBar
{
	private OpenMapMenu _openMapMenu;
	private MapOverlay _overlay;
	private PreferencesDialog _preferencesDialog;
	private SectorEditDialog _sectorEditDialog;
	private AcceptDialog _errorDialog;

	public void Initialize(OpenMapMenu openMapMenu, MapOverlay overlay)
	{
		_openMapMenu = openMapMenu;
		_overlay = overlay;

		var fileMenu = GetNode<PopupMenu>("File");
		fileMenu.AddItem("Open Map...", 0);
		fileMenu.IdPressed += id =>
		{
			if (id == 0) _openMapMenu.ShowOpenFileDialog();
		};

		var editMenu = GetNode<PopupMenu>("Edit");
		editMenu.AddItem("Edit Selection...", 0);
		editMenu.IdPressed += id =>
		{
			if (id == 0) OpenEditSelectionDialog();
		};
		_overlay.EditSectorsRequested += OpenSectorEditDialogFor;

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

		InitializeSelectionBoxMenu(preferencesMenu);
	}

	/// <summary>
	/// Generic-sounding on purpose ("Edit Selection", not "Edit Sector") -
	/// Linedef and Thing property dialogs slot into this same menu item
	/// next, per <c>TODO.md</c>'s own ordering; only the Sectors-mode
	/// branch exists so far.
	/// </summary>
	private void OpenEditSelectionDialog()
	{
		if (_overlay.Mode != EditMode.Sectors)
		{
			ShowError("Edit Selection currently only supports Sectors mode.");
			return;
		}

		var selected = _overlay.Map?.GetSelectedSectors().ToList() ?? new List<Sector>();
		if (selected.Count == 0)
		{
			ShowError("Select one or more sectors first.");
			return;
		}

		OpenSectorEditDialogFor(selected);
	}

	private void OpenSectorEditDialogFor(IReadOnlyList<Sector> sectors)
	{
		if (sectors.Count == 0) return;

		_sectorEditDialog ??= CreateSectorEditDialog();
		_sectorEditDialog.SetSectors(
			sectors, _overlay.Map, _overlay.GameConfiguration, _overlay.UndoStack, () => _overlay.QueueRedraw(),
			_overlay.TextureSet, _overlay.NamedResources, _overlay.TextureIconCache);
		_sectorEditDialog.PopupCentered();
	}

	private SectorEditDialog CreateSectorEditDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/SectorEditDialog.tscn").Instantiate<SectorEditDialog>();
		AddChild(dialog);
		return dialog;
	}

	private void ShowError(string message)
	{
		_errorDialog ??= CreateErrorDialog();
		_errorDialog.DialogText = message;
		_errorDialog.PopupCentered();
	}

	private AcceptDialog CreateErrorDialog()
	{
		var dialog = new AcceptDialog { Title = "Error" };
		AddChild(dialog);
		return dialog;
	}

	/// <summary>
	/// "Select Inside"/"Select Touching" - real UDB terminology (its own
	/// toolbar button is literally labeled "Select Touching", with the
	/// off state referred to as "select inside" in its own tooltip/status
	/// text), moved here into a Preferences submenu instead of UDB's real
	/// per-mode toolbar button placement since this project's menu bar is
	/// where settings-like toggles already live. Session-only, matching
	/// UDB's own real behavior - see <see cref="MapOverlay.MarqueeSelectTouching"/>.
	/// Godot doesn't auto-enforce mutual exclusion between radio-checkable
	/// items, even adjacent ones, so both checkmarks are set explicitly on
	/// every press.
	/// </summary>
	private void InitializeSelectionBoxMenu(PopupMenu preferencesMenu)
	{
		var selectionBoxMenu = GetNode<PopupMenu>("Preferences/SelectionBox");
		preferencesMenu.AddSubmenuNodeItem("Selection Box", selectionBoxMenu);

		selectionBoxMenu.AddRadioCheckItem("Select Inside", 0);
		selectionBoxMenu.AddRadioCheckItem("Select Touching", 1);
		selectionBoxMenu.SetItemChecked(0, true);

		selectionBoxMenu.IdPressed += id =>
		{
			_overlay.MarqueeSelectTouching = id == 1;
			selectionBoxMenu.SetItemChecked(0, id == 0);
			selectionBoxMenu.SetItemChecked(1, id == 1);
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
