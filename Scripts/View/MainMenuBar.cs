using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Map;
using DoomArchitect.Settings;
using Godot;

/// <summary>
/// The conventional top menu bar (File / Edit / Map / Preferences) - a
/// native Godot 4.4+ <see cref="MenuBar"/>, whose children are the
/// <see cref="PopupMenu"/>s shown as its top-level entries (each child's
/// node name is its label). Lives in <c>Main.tscn</c> as app-level chrome
/// rather than inside any one map document's own scene - actions here
/// ("Save Map", "Open Script...") should stay reachable no matter which
/// tab is currently showing, which a menu bar living inside a map
/// document's own tab couldn't do (a real, reported bug: it disappeared
/// along with the rest of that tab's content whenever a Script tab was
/// active). Since more than one Map tab can exist at once,
/// <see cref="BuildMenus"/> (the one-time menu-structure/event-handler
/// construction - called once by <c>AppShell</c> at startup) is kept
/// separate from <see cref="SetActiveMap"/> (re-callable, just re-points
/// which map's own <see cref="OpenMapMenu"/>/<c>MapOverlay</c> every
/// action here actually targets - called by <c>AppShell</c> every time a
/// Map tab becomes the active one, not just the first).
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

	/// <summary>
	/// Every <see cref="OpenMapMenu"/> <see cref="SetActiveMap"/> has ever
	/// been pointed at - its own per-instance event wiring (<see cref="OpenMapMenu.MapSaved"/>,
	/// the <c>MapOverlay</c> edit-request events below) only needs doing
	/// once per map tab, not once per *switch back to* an already-visited
	/// one, or those events would fire their own handlers more than once.
	/// </summary>
	private readonly HashSet<OpenMapMenu> _wiredMapMenus = new();

	// Which File-menu action the discard-unsaved-changes prompt should
	// actually perform once confirmed - set right before showing it.
	private Action _pendingDiscardAction;

	/// <summary>
	/// Set by <c>AppShell</c> right after instancing this map's own tab -
	/// "File &gt; Open Script..." has to reach the shared tab strip, which
	/// lives a level above this menu bar's own content (see
	/// <c>AppShell</c>'s own remarks on why that's not wired the other way
	/// around). Null-safe: a no-op if nothing's listening yet.
	/// </summary>
	public Action OpenScriptRequested { get; set; }

	/// <summary>Fired with the new value whenever "Immersive 3D View" is toggled from the Preferences menu - <c>AppShell</c> owns what that actually does to its own layout (see its own remarks), this menu only owns persisting the setting and reflecting its checked state.</summary>
	public Action<bool> Immersive3DViewToggled { get; set; }

	/// <summary>
	/// Fired when "New Map..."/"Open Map..." is pressed while no map tab is
	/// active (<see cref="_openMapMenu"/> is null) - there's no existing
	/// <see cref="OpenMapMenu"/> instance to act on yet, since one only
	/// ever exists inside a <c>MapView</c>'s own scene. <c>AppShell</c>
	/// creates a fresh, blank Map tab and immediately shows that tab's own
	/// Open Map file dialog (<paramref name="showOpenDialog"/> true) or New
	/// Map name prompt (false) on it.
	/// </summary>
	public Action<bool> CreateMapTabRequested { get; set; }

	/// <summary>
	/// Builds every menu's own items and <c>IdPressed</c> dispatch exactly
	/// once, independent of any specific map tab - every handler below
	/// reads <see cref="_openMapMenu"/>/<see cref="_overlay"/> as fields at
	/// the moment it actually runs (not whatever they were when this method
	/// ran), so building this structure before either field is ever set is
	/// fine, the same way it already worked when both were assigned here
	/// too. Call <see cref="SetActiveMap"/> separately once a map tab
	/// exists to actually point these actions at it.
	/// </summary>
	public void BuildMenus()
	{
		var fileMenu = GetNode<PopupMenu>("File");
		fileMenu.AddItem("New Map...", 0);
		fileMenu.AddItem("Open Map...", 1);
		fileMenu.AddItem("Save Map", 2);
		fileMenu.AddItem("Save Map As...", 3);
		fileMenu.AddItem("Save Map Into...", 4);
		fileMenu.AddSeparator();
		fileMenu.AddItem("Open Script...", 5);
		fileMenu.IdPressed += id =>
		{
			// Reachable now that zero active map tabs is a real, supported
			// state (every tab, including the original one, is closable),
			// not just a transient moment during startup - "New Map..."/
			// "Open Map..." have no existing OpenMapMenu instance to act on
			// in that state (one lives inside each MapView's own scene,
			// nowhere else), so they go through AppShell to create a fresh
			// Map tab first. Save/Save As/Save Into fundamentally need an
			// already-loaded map's data to act on - no tab means nothing to
			// save, so those (and "Map Options..." in the Map menu below)
			// stay graceful no-ops; visually disabling them while inactive
			// is a reasonable follow-up, not required for this pass.
			if (_openMapMenu == null)
			{
				if (id == 0 || id == 1) CreateMapTabRequested?.Invoke(id == 1);
				else if (id == 5) OpenScriptRequested?.Invoke();
				return;
			}

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
				case 5:
					OpenScriptRequested?.Invoke();
					break;
			}
		};

		var editMenu = GetNode<PopupMenu>("Edit");
		editMenu.AddItem("Edit Selection...", 0);
		editMenu.IdPressed += id =>
		{
			if (id == 0 && _overlay != null) OpenEditSelectionDialog();
		};

		var mapMenu = GetNode<PopupMenu>("Map");
		mapMenu.AddItem("Map Options...", 0);
		mapMenu.IdPressed += id =>
		{
			if (id == 0) _openMapMenu?.ShowMapOptionsForCurrentMap();
		};

		var preferencesMenu = GetNode<PopupMenu>("Preferences");
		// "Preferences..." (not "Resources...") since PreferencesDialog now
		// covers more than just game-configuration resources - it gained a
		// Keybinds tab (see TODO/TODO.md's "Keybinding management" entry).
		preferencesMenu.AddItem("Preferences...", 0);
		preferencesMenu.AddCheckItem("Immersive 3D View", 1);
		preferencesMenu.SetItemChecked(1, AppSettingsFile.Load().GetImmersive3DView());
		preferencesMenu.IdPressed += id =>
		{
			if (id == 0) OpenPreferences();
			else if (id == 1) ToggleImmersive3DView(preferencesMenu);
		};

		InitializeSelectionBoxMenu(preferencesMenu);
	}

	/// <summary>
	/// Points every action this menu bar owns at a specific map tab -
	/// called by <c>AppShell</c> every time a Map tab becomes the active
	/// one (not just the first), so "Save Map"/"Map Options..."/etc.
	/// always act on whichever map the user is actually looking at. Each
	/// <see cref="OpenMapMenu"/>'s own per-instance event wiring
	/// (<see cref="OpenMapMenu.MapSaved"/>, the three <c>MapOverlay</c>
	/// edit-request events) only happens the first time this is ever
	/// called for it - re-subscribing on every switch back to an already-
	/// visited tab would fire those handlers more than once per real event.
	/// </summary>
	public void SetActiveMap(OpenMapMenu openMapMenu, MapOverlay overlay)
	{
		_openMapMenu = openMapMenu;
		_overlay = overlay;

		// null is a legitimate argument here (AppShell clears both fields
		// when the active tab isn't a map at all, or none is active) -
		// HashSet<T>.Add(null) itself would happily succeed and report
		// "newly added", so this has to be checked explicitly rather than
		// relying on that call alone to guard the wiring below.
		if (openMapMenu != null && _wiredMapMenus.Add(openMapMenu))
		{
			openMapMenu.MapSaved += () => overlay.UndoStack?.MarkSaved();
			overlay.EditSectorsRequested += OpenSectorEditDialogFor;
			overlay.EditLinedefsRequested += OpenLinedefEditDialogFor;
			overlay.EditThingsRequested += OpenThingEditDialogFor;
		}
	}

	/// <summary>
	/// Persists immediately on click, same as every other immediate-effect
	/// Preferences menu item here (unlike <see cref="InitializeSelectionBoxMenu"/>'s
	/// own "Selection Box" item, which is deliberately session-only) -
	/// <see cref="Immersive3DViewToggled"/> is how <c>AppShell</c> finds
	/// out, the same settable-callback shape <see cref="OpenScriptRequested"/>
	/// already uses.
	/// </summary>
	private void ToggleImmersive3DView(PopupMenu preferencesMenu)
	{
		var settings = AppSettingsFile.Load();
		var newValue = !settings.GetImmersive3DView();
		AppSettingsFile.Save(settings.WithImmersive3DView(newValue));
		preferencesMenu.SetItemChecked(1, newValue);
		Immersive3DViewToggled?.Invoke(newValue);
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
			_overlay.SpriteIconCache, onTypeChanged: type => _overlay.LastUsedThingType = type);
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
	/// unsaved changes; otherwise asks first. <see cref="MapOverlay.UndoStack"/>
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
	/// "Select Inside"/"Select Touching" - moved here into a Preferences
	/// submenu instead of a per-mode toolbar button, since this project's
	/// menu bar is where settings-like toggles already live. Session-only -
	/// see <see cref="MapOverlay.MarqueeSelectTouching"/>.
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
			if (_overlay != null) _overlay.MarqueeSelectTouching = id == 1;
			selectionBoxMenu.SetItemChecked(0, id == 0);
			selectionBoxMenu.SetItemChecked(1, id == 1);
		};
	}

	/// <summary>Launches the current map at the last skill/monsters choice - shared by the toolbar's Test Map button and the F9 keybind.</summary>
	public void TestMap()
	{
		var error = TestMapLauncher.Launch(_openMapMenu, _overlay.LastTestSkill, _overlay.LastTestNoMonsters);
		if (error != null) ShowError(error);
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
