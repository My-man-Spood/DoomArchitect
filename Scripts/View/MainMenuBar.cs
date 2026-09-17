using System;
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
	private LinedefEditDialog _linedefEditDialog;
	private ThingEditDialog _thingEditDialog;
	private AcceptDialog _errorDialog;
	private ConfirmationDialog _discardChangesDialog;

	// Which File-menu action the discard-unsaved-changes prompt should
	// actually perform once confirmed - set right before showing it.
	private Action _pendingDiscardAction;

	public void Initialize(OpenMapMenu openMapMenu, MapOverlay overlay)
	{
		_openMapMenu = openMapMenu;
		_overlay = overlay;
		_openMapMenu.MapSaved += () => _overlay.UndoStack?.MarkSaved();

		var fileMenu = GetNode<PopupMenu>("File");
		fileMenu.AddItem("New Map...", 0);
		fileMenu.AddItem("Open Map...", 1);
		fileMenu.AddItem("Save Map", 2);
		fileMenu.AddItem("Save Map As...", 3);
		fileMenu.AddItem("Save Map Into...", 4);
		fileMenu.IdPressed += id =>
		{
			switch (id)
			{
				case 0:
					RunWithDiscardConfirmationIfDirty(_openMapMenu.ShowNewMapDialog);
					break;
				case 1:
					RunWithDiscardConfirmationIfDirty(_openMapMenu.ShowOpenFileDialog);
					break;
				case 2:
					_openMapMenu.SaveMap();
					break;
				case 3:
					_openMapMenu.SaveMapAs();
					break;
				case 4:
					_openMapMenu.SaveMapInto();
					break;
			}
		};

		var editMenu = GetNode<PopupMenu>("Edit");
		editMenu.AddItem("Edit Selection...", 0);
		editMenu.IdPressed += id =>
		{
			if (id == 0) OpenEditSelectionDialog();
		};
		_overlay.EditSectorsRequested += OpenSectorEditDialogFor;
		_overlay.EditLinedefsRequested += OpenLinedefEditDialogFor;
		_overlay.EditThingsRequested += OpenThingEditDialogFor;

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
	/// Sectors, Linedefs, and now Things modes are all wired.
	/// </summary>
	private void OpenEditSelectionDialog()
	{
		switch (_overlay.Mode)
		{
			case EditMode.Sectors:
				var selectedSectors = _overlay.Map?.GetSelectedSectors().ToList() ?? new List<Sector>();
				if (selectedSectors.Count == 0)
				{
					ShowError("Select one or more sectors first.");
					return;
				}

				OpenSectorEditDialogFor(selectedSectors);
				break;
			case EditMode.Linedefs:
				var selectedLinedefs = _overlay.Map?.GetSelectedLinedefs().ToList() ?? new List<Linedef>();
				if (selectedLinedefs.Count == 0)
				{
					ShowError("Select one or more linedefs first.");
					return;
				}

				OpenLinedefEditDialogFor(selectedLinedefs);
				break;
			case EditMode.Things:
				var selectedThings = _overlay.Map?.GetSelectedThings().ToList() ?? new List<Thing>();
				if (selectedThings.Count == 0)
				{
					ShowError("Select one or more things first.");
					return;
				}

				OpenThingEditDialogFor(selectedThings);
				break;
			default:
				ShowError("Edit Selection currently only supports Sectors, Linedefs, and Things modes.");
				break;
		}
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

	private void OpenLinedefEditDialogFor(IReadOnlyList<Linedef> linedefs)
	{
		if (linedefs.Count == 0) return;

		_linedefEditDialog ??= CreateLinedefEditDialog();
		_linedefEditDialog.SetLinedefs(
			linedefs, _overlay.Map, _overlay.GameConfiguration, _overlay.UndoStack, () => _overlay.QueueRedraw(),
			_overlay.TextureSet, _overlay.NamedResources, _overlay.TextureIconCache);
		_linedefEditDialog.PopupCentered();
	}

	private LinedefEditDialog CreateLinedefEditDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/LinedefEditDialog.tscn").Instantiate<LinedefEditDialog>();
		AddChild(dialog);
		return dialog;
	}

	private void OpenThingEditDialogFor(IReadOnlyList<Thing> things)
	{
		if (things.Count == 0) return;

		_thingEditDialog ??= CreateThingEditDialog();
		_thingEditDialog.SetThings(
			things, _overlay.Map, _overlay.GameConfiguration, _overlay.UndoStack, () => _overlay.QueueRedraw(),
			_overlay.SpriteIconCache);
		_thingEditDialog.PopupCentered();
	}

	private ThingEditDialog CreateThingEditDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/ThingEditDialog.tscn").Instantiate<ThingEditDialog>();
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
	/// Runs <paramref name="action"/> immediately if the undo stack has no
	/// unsaved changes; otherwise asks first - matches UDB's own real
	/// <c>AskSaveMap</c> guard on New Map/Open Map. <see cref="MapOverlay.UndoStack"/>
	/// is null before the very first map ever loads, which reads as "not
	/// dirty" (nothing to lose yet).
	/// </summary>
	private void RunWithDiscardConfirmationIfDirty(Action action)
	{
		if (_overlay.UndoStack?.IsDirty != true)
		{
			action();
			return;
		}

		_discardChangesDialog ??= CreateDiscardChangesDialog();
		_pendingDiscardAction = action;
		_discardChangesDialog.PopupCentered();
	}

	private ConfirmationDialog CreateDiscardChangesDialog()
	{
		var dialog = new ConfirmationDialog
		{
			Title = "Discard Unsaved Changes?",
			DialogText = "This map has unsaved changes. Discard them?",
			Exclusive = false,
		};
		dialog.Confirmed += () => _pendingDiscardAction?.Invoke();
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
