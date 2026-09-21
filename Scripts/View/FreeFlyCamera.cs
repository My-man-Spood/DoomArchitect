using Godot;

// Noclip-style fly camera for the 3D visual mode: WASD moves along the
// exact look direction (including pitch), mouse looks around while
// captured. Only acts while this camera is Current, so it's inert
// whenever the top-down camera is active instead.
//
// Movement is driven by the real, independently rebindable camera_forward/
// backward/strafe_left/strafe_right/fly_up/fly_down actions (see
// TODO.md's "Keybinding management" writeup) - Escape-releases-the-mouse
// below deliberately stays a plain hardcoded Key.Escape check, not an
// action: it's a universal "get my cursor back" safety hatch, the kind of
// thing most editors/games keep non-rebindable on purpose, not an
// oversight.
public partial class FreeFlyCamera : Camera3D
{
	[Export] public float MoveSpeed = 200f;
	[Export] public float MouseSensitivity = 0.2f;

	private float _yaw;
	private float _pitch;

	public override void _Ready()
	{
		_yaw = RotationDegrees.Y;
		_pitch = RotationDegrees.X;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Current) return;

		if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
		}
		else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
		else if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_yaw -= motion.Relative.X * MouseSensitivity;
			_pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -89f, 89f);
			RotationDegrees = new Vector3(_pitch, _yaw, 0);
		}
	}

	public override void _Process(double delta)
	{
		if (!Current) return;

		var direction = Vector3.Zero;
		if (Input.IsActionPressed("camera_forward")) direction -= Transform.Basis.Z;
		if (Input.IsActionPressed("camera_backward")) direction += Transform.Basis.Z;
		if (Input.IsActionPressed("camera_strafe_left")) direction -= Transform.Basis.X;
		if (Input.IsActionPressed("camera_strafe_right")) direction += Transform.Basis.X;
		if (Input.IsActionPressed("camera_fly_up")) direction += Vector3.Up;
		if (Input.IsActionPressed("camera_fly_down")) direction -= Vector3.Up;

		if (direction != Vector3.Zero)
		{
			Position += direction.Normalized() * MoveSpeed * (float)delta;
		}
	}
}
