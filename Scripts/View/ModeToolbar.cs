using Godot;

/// <summary>
/// Three radio-style buttons mirroring the 1/2/3 edit-mode keybinds -
/// either input drives the same <see cref="EditMode"/> on
/// <see cref="Overlay"/>. Lives on its own <c>UI</c> canvas layer, drawn
/// on top of and kept deliberately separate from <c>MapOverlay</c>'s
/// gizmo layer underneath - general UI shouldn't be a child of the
/// map-editing surface it controls. Its visibility is therefore driven
/// explicitly by <c>MapView</c> alongside the overlay's, rather than
/// inherited for free the way a child control's would be.
/// </summary>
public partial class ModeToolbar : HBoxContainer
{
	public MapOverlay Overlay { get; set; }

	private const string ButtonsPath = "EditingModeButtonGroup/MarginContainer/HBoxContainer";

	private Button _vertexButton;
	private Button _linedefButton;
	private Button _sectorButton;

	public override void _Ready()
	{
		_vertexButton = GetNode<Button>($"{ButtonsPath}/VertexModeButton3");
		_linedefButton = GetNode<Button>($"{ButtonsPath}/LinedefModeButton2");
		_sectorButton = GetNode<Button>($"{ButtonsPath}/SectorModeButton");

		_vertexButton.Toggled += pressed => OnToggled(pressed, EditMode.Vertices);
		_linedefButton.Toggled += pressed => OnToggled(pressed, EditMode.Linedefs);
		_sectorButton.Toggled += pressed => OnToggled(pressed, EditMode.Sectors);
	}

	public override void _Process(double delta)
	{
		if (Overlay == null) return;

		_vertexButton.SetPressedNoSignal(Overlay.Mode == EditMode.Vertices);
		_linedefButton.SetPressedNoSignal(Overlay.Mode == EditMode.Linedefs);
		_sectorButton.SetPressedNoSignal(Overlay.Mode == EditMode.Sectors);
	}

	private void OnToggled(bool pressed, EditMode mode)
	{
		if (pressed) Overlay.Mode = mode;
	}
}
