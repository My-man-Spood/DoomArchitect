using Godot;

// Proof-of-concept for the "one live 3D scene, two cameras" idea: the
// top-down orthographic camera and the perspective camera look at the
// exact same scene, so switching views is just a camera swap.
public partial class MapView : Node3D
{
	private Camera3D _topDownCamera;
	private Camera3D _perspectiveCamera;
	private bool _in3D;

	public override void _Ready()
	{
		_topDownCamera = GetNode<Camera3D>("TopDownCamera");
		_perspectiveCamera = GetNode<Camera3D>("PerspectiveCamera");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab })
		{
			_in3D = !_in3D;
			_topDownCamera.Current = !_in3D;
			_perspectiveCamera.Current = _in3D;
		}
	}
}
