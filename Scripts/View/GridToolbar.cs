using Godot;

/// <summary>
/// Grid controls mirroring the keybinds: <c>G</c> (snap toggle) and
/// <c>[</c>/<c>]</c> (grid size). Same separation-of-concerns reasoning
/// as <see cref="ModeToolbar"/> - lives on its own <c>UI</c>-layer panel,
/// not inside <c>MapOverlay</c>'s gizmo layer.
/// </summary>
public partial class GridToolbar : PanelContainer
{
	private const string ButtonsPath = "MarginContainer/HBoxContainer";

	public MapOverlay Overlay { get; set; }

	private Button _snapToggleButton;
	private Button _decreaseButton;
	private Button _increaseButton;
	private Label _gridSizeLabel;

	public override void _Ready()
	{
		_snapToggleButton = GetNode<Button>($"{ButtonsPath}/SnapToggleButton");
		_decreaseButton = GetNode<Button>($"{ButtonsPath}/DecreaseGridButton");
		_increaseButton = GetNode<Button>($"{ButtonsPath}/IncreaseGridButton");
		_gridSizeLabel = GetNode<Label>($"{ButtonsPath}/GridSizeLabel");

		_snapToggleButton.Toggled += OnSnapToggled;
		_decreaseButton.Pressed += () => Overlay?.DecreaseGridSize();
		_increaseButton.Pressed += () => Overlay?.IncreaseGridSize();
	}

	public override void _Process(double delta)
	{
		if (Overlay == null) return;

		_snapToggleButton.SetPressedNoSignal(Overlay.SnapEnabled);
		_gridSizeLabel.Text = Overlay.GridSize.ToString();
	}

	private void OnSnapToggled(bool pressed)
	{
		if (Overlay != null) Overlay.SnapEnabled = pressed;
	}
}
