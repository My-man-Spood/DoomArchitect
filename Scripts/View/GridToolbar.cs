using Godot;

/// <summary>
/// Grid controls mirroring the keybinds: <c>G</c> (snap toggle) and
/// <c>[</c>/<c>]</c> (grid size), plus the tag-indicator toggle (<c>I</c>,
/// UDB's own real default) - not strictly a "grid" control, but a general
/// 2D-view display option with nowhere more specific to live yet; a whole
/// new toolbar panel for this one button would be more machinery than the
/// feature needs. Same separation-of-concerns reasoning as
/// <see cref="ModeToolbar"/> - lives on its own <c>UI</c>-layer panel,
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
	private Button _tagIndicatorsToggleButton;

	public override void _Ready()
	{
		_snapToggleButton = GetNode<Button>($"{ButtonsPath}/SnapToggleButton");
		_decreaseButton = GetNode<Button>($"{ButtonsPath}/DecreaseGridButton");
		_increaseButton = GetNode<Button>($"{ButtonsPath}/IncreaseGridButton");
		_gridSizeLabel = GetNode<Label>($"{ButtonsPath}/GridSizeLabel");
		_tagIndicatorsToggleButton = GetNode<Button>($"{ButtonsPath}/TagIndicatorsToggleButton");

		_snapToggleButton.Toggled += OnSnapToggled;
		_decreaseButton.Pressed += () => Overlay?.DecreaseGridSize();
		_increaseButton.Pressed += () => Overlay?.IncreaseGridSize();
		_tagIndicatorsToggleButton.Toggled += OnTagIndicatorsToggled;
	}

	public override void _Process(double delta)
	{
		if (Overlay == null) return;

		_snapToggleButton.SetPressedNoSignal(Overlay.SnapEnabled);
		_gridSizeLabel.Text = Overlay.GridSize.ToString();
		_tagIndicatorsToggleButton.SetPressedNoSignal(Overlay.TagIndicatorsEnabled);
	}

	private void OnSnapToggled(bool pressed)
	{
		if (Overlay != null) Overlay.SnapEnabled = pressed;
	}

	private void OnTagIndicatorsToggled(bool pressed)
	{
		if (Overlay != null) Overlay.TagIndicatorsEnabled = pressed;
	}
}
