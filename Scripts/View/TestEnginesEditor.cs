using System.Collections.Generic;
using System.IO;
using DoomArchitect.Core.Configuration;
using Godot;

/// <summary>
/// Test Map's own engine configuration - a game configuration on the left
/// (mirroring <see cref="PreferencesDialog"/>'s own Game Configurations
/// tab), and on the right a single engine selector (dropdown + Add/Remove)
/// above the details for whichever engine that dropdown currently shows.
/// Picking an engine from the dropdown *is* making it the one Test Map
/// uses for that game configuration - there's no separate "active" concept
/// on top of it. Godot's <see cref="OptionButton"/> can't be renamed inline
/// the way an editable combo box can, so the Name field below it is the one
/// implementation difference; everything else - "New Engine" going straight
/// to a file browse rather than adding a blank placeholder, the path field
/// being browse-only - is the real, lighter shape, not a list-based one.
/// Works entirely against an in-memory working copy per game configuration
/// (<see cref="Load"/>/<see cref="Apply"/>) - <see cref="PreferencesDialog"/>
/// owns actually saving it, matching <see cref="KeybindsEditor"/>'s own
/// identical shape.
/// </summary>
public partial class TestEnginesEditor : HBoxContainer
{
	private static readonly GameConfigurationKind[] Kinds = { GameConfigurationKind.Doom, GameConfigurationKind.Doom2, GameConfigurationKind.GZDoomDoom2UDMF };

	private ItemList _gameConfigList;
	private OptionButton _engineSelector;
	private Button _addButton;
	private Button _removeButton;
	private LineEdit _nameEdit;
	private LineEdit _pathEdit;
	private Button _browseButton;
	private CheckBox _useCustomParametersCheck;
	private LineEdit _customParametersEdit;
	private FileDialog _browseFileDialog;

	// Add goes straight to a file browse rather than a blank placeholder
	// row, so the dialog needs to know whether a picked file creates a new
	// engine or just updates the currently selected one's path.
	private bool _browsingForNewEngine;

	private readonly Dictionary<GameConfigurationKind, List<TestEngine>> _workingEngines = new();
	private readonly Dictionary<GameConfigurationKind, int> _workingActiveIndex = new();
	private int _selectedGameConfigIndex;

	private GameConfigurationKind SelectedKind => Kinds[_selectedGameConfigIndex];
	private List<TestEngine> SelectedEngines => _workingEngines[SelectedKind];
	private int SelectedEngineIndex => _workingActiveIndex[SelectedKind];

	public override void _Ready()
	{
		_gameConfigList = GetNode<ItemList>("GameConfigList");
		_engineSelector = GetNode<OptionButton>("DetailsPanel/EngineRow/EngineSelector");
		_addButton = GetNode<Button>("DetailsPanel/EngineRow/AddButton");
		_removeButton = GetNode<Button>("DetailsPanel/EngineRow/RemoveButton");
		_nameEdit = GetNode<LineEdit>("DetailsPanel/NameRow/NameEdit");
		_pathEdit = GetNode<LineEdit>("DetailsPanel/PathRow/PathEdit");
		_browseButton = GetNode<Button>("DetailsPanel/PathRow/BrowseButton");
		_useCustomParametersCheck = GetNode<CheckBox>("DetailsPanel/UseCustomParametersCheck");
		_customParametersEdit = GetNode<LineEdit>("DetailsPanel/CustomParametersEdit");
		_browseFileDialog = GetNode<FileDialog>("BrowseFileDialog");

		foreach (var kind in Kinds) _gameConfigList.AddItem(kind.ToString());

		_gameConfigList.ItemSelected += OnGameConfigSelected;
		_engineSelector.ItemSelected += OnEngineSelected;
		_addButton.Pressed += OnAddPressed;
		_removeButton.Pressed += OnRemovePressed;
		_browseButton.Pressed += () => { _browsingForNewEngine = false; _browseFileDialog.PopupCentered(); };
		_browseFileDialog.FileSelected += OnFileSelected;
		_nameEdit.TextChanged += OnNameEdited;
		_useCustomParametersCheck.Toggled += useCustom => _customParametersEdit.Editable = useCustom;

		ClearDetails();
	}

	/// <summary>Resets every game configuration's own working copy to what's actually saved - call every time the dialog opens, same reasoning as <see cref="PreferencesDialog.Open"/>'s own "load fresh every time" comment.</summary>
	public void Load(AppSettings settings)
	{
		_workingEngines.Clear();
		_workingActiveIndex.Clear();
		foreach (var kind in Kinds)
		{
			_workingEngines[kind] = new List<TestEngine>(settings.GetTestEngines(kind));
			_workingActiveIndex[kind] = settings.GetActiveTestEngineIndex(kind);
		}

		_selectedGameConfigIndex = 0;
		_gameConfigList.Select(0);
		RefreshEngineSelector();
	}

	/// <summary>Writes every game configuration's own working copy into <paramref name="settings"/>, capturing whatever's currently in the details pane first.</summary>
	public AppSettings Apply(AppSettings settings)
	{
		CaptureSelectedEngineEdits();

		foreach (var kind in Kinds)
		{
			settings = settings.WithTestEngines(kind, _workingEngines[kind], _workingActiveIndex[kind]);
		}

		return settings;
	}

	private void OnGameConfigSelected(long index)
	{
		CaptureSelectedEngineEdits();
		_selectedGameConfigIndex = (int)index;
		RefreshEngineSelector();
	}

	private void OnEngineSelected(long index)
	{
		CaptureSelectedEngineEdits();
		_workingActiveIndex[SelectedKind] = (int)index;
		ShowDetails(SelectedEngines[(int)index]);
	}

	private void OnAddPressed()
	{
		_browsingForNewEngine = true;
		_browseFileDialog.PopupCentered();
	}

	private void OnFileSelected(string path)
	{
		if (_browsingForNewEngine)
		{
			CaptureSelectedEngineEdits();
			var name = Path.GetFileNameWithoutExtension(path);
			SelectedEngines.Add(new TestEngine(name, path, UseCustomParameters: false, CustomParameters: ""));
			_workingActiveIndex[SelectedKind] = SelectedEngines.Count - 1;
			RefreshEngineSelector();
		}
		else
		{
			_pathEdit.Text = path;
		}
	}

	private void OnRemovePressed()
	{
		var index = SelectedEngineIndex;
		if (index < 0) return;

		SelectedEngines.RemoveAt(index);
		if (index >= SelectedEngines.Count) index = SelectedEngines.Count - 1;
		_workingActiveIndex[SelectedKind] = index;
		RefreshEngineSelector();
	}

	private void OnNameEdited(string name)
	{
		if (SelectedEngineIndex < 0) return;

		_engineSelector.SetItemText(SelectedEngineIndex, name);
	}

	/// <summary>Writes whatever's currently in the details pane back into the working list - called before any selection change, mirroring <see cref="PreferencesDialog.CaptureCurrentSelection"/>.</summary>
	private void CaptureSelectedEngineEdits()
	{
		var index = SelectedEngineIndex;
		if (index < 0 || index >= SelectedEngines.Count) return;

		SelectedEngines[index] = new TestEngine(
			_nameEdit.Text, _pathEdit.Text, _useCustomParametersCheck.ButtonPressed, _customParametersEdit.Text);
	}

	private void RefreshEngineSelector()
	{
		_engineSelector.Clear();
		foreach (var engine in SelectedEngines) _engineSelector.AddItem(engine.Name);

		var activeIndex = _workingActiveIndex[SelectedKind];
		if (activeIndex < 0 || activeIndex >= SelectedEngines.Count)
		{
			_workingActiveIndex[SelectedKind] = SelectedEngines.Count == 0 ? -1 : 0;
			activeIndex = _workingActiveIndex[SelectedKind];
		}

		if (activeIndex >= 0)
		{
			_engineSelector.Select(activeIndex);
			ShowDetails(SelectedEngines[activeIndex]);
		}
		else
		{
			ClearDetails();
		}

		_removeButton.Disabled = SelectedEngines.Count <= 1;
	}

	private void ShowDetails(TestEngine engine)
	{
		_nameEdit.Editable = true;
		_pathEdit.Editable = false;
		_browseButton.Disabled = false;
		_useCustomParametersCheck.Disabled = false;

		_nameEdit.Text = engine.Name;
		_pathEdit.Text = engine.ExecutablePath;
		_useCustomParametersCheck.ButtonPressed = engine.UseCustomParameters;
		_customParametersEdit.Editable = engine.UseCustomParameters;
		_customParametersEdit.Text = engine.CustomParameters;
	}

	private void ClearDetails()
	{
		_nameEdit.Text = "";
		_nameEdit.Editable = false;
		_pathEdit.Text = "";
		_pathEdit.Editable = false;
		_browseButton.Disabled = true;
		_useCustomParametersCheck.ButtonPressed = false;
		_useCustomParametersCheck.Disabled = true;
		_customParametersEdit.Text = "";
		_customParametersEdit.Editable = false;
		_removeButton.Disabled = true;
	}
}
