using System;
using Godot;

/// <summary>
/// Reusable rotating compass dial, ported directly from UDB's real
/// <c>AngleControlEx</c> (verified against its own <c>OnPaint</c>/mouse-
/// handler logic, not guessed): a circle with tick marks every 45 degrees
/// and a needle pointing at the current <see cref="Angle"/>. Left-click or
/// drag snaps to the nearest 45 degrees; right-click or drag is free
/// rotation - UDB's own real split (its <c>MouseDown</c>/<c>MouseMove</c>
/// handlers only round to the nearest 45 when the left button is the one
/// held). Angle 0 points east and increases counter-clockwise, matching
/// Doom's own real angle convention and UDB's own real <c>DegreesToXY</c>
/// (standard trig X/Y with Y negated for screen space, reproduced here
/// verbatim - Godot's own Y-down screen space needs the identical
/// negation).
///
/// A null <see cref="Angle"/> (this project's own "blank/mixed-across-
/// selection" convention, the same role UDB's own real <c>NO_ANGLE</c>
/// sentinel plays) simply omits the needle - the dial itself still draws
/// normally.
///
/// This control has no built-in "AngleOffset" concept - UDB's own real one
/// exists on <c>AngleControlEx</c> but is never actually set away from its
/// zero default anywhere in <c>ThingEditFormUDMF</c>; Pitch/Roll's own
/// "0 points up" look instead comes from that dialog's own code manually
/// adding/subtracting 90 when talking to the control (confirmed directly
/// in its own <c>pitch_WhenTextChanged</c>/<c>pitchControl_AngleChanged</c>
/// pair) - so the same +/-90 adjustment belongs in whichever host dialog
/// wants it, mirroring UDB's own real split of responsibility rather than
/// baking an unused feature into this control.
/// </summary>
public partial class AngleDialControl : Control
{
	private const float TickIntervalDegrees = 45f;
	private const float OutlineInset = 2f;
	private const float TickOuterInset = 6f;
	private const float TickInnerFraction = 0.2f;

	private static readonly Color FillColor = new(1, 1, 1, 0.06f);
	private static readonly Color OutlineColor = new(1, 1, 1, 0.78431374f);
	private static readonly Color TickColor = new(1, 1, 1, 0.45f);
	private static readonly Color NeedleColor = Colors.White;
	private static readonly Color DisabledMultiplier = new(1, 1, 1, 0.4f);

	public event Action<int> AngleChanged;

	public int? Angle
	{
		get => _angle;
		set
		{
			_angle = value.HasValue ? ClampDegrees(value.Value) : null;
			QueueRedraw();
		}
	}

	public bool Editable
	{
		get => _editable;
		set { _editable = value; QueueRedraw(); }
	}

	private int? _angle;
	private bool _editable = true;

	private static int ClampDegrees(int degrees) => ((degrees % 360) + 360) % 360;

	private float Radius => Mathf.Min(Size.X, Size.Y) / 2f - OutlineInset;

	private Vector2 Origin => Size / 2f;

	private static Vector2 DegreesToPoint(float degrees, float radius, Vector2 origin)
	{
		var radians = Mathf.DegToRad(degrees);
		return new Vector2(Mathf.Cos(radians) * radius + origin.X, -Mathf.Sin(radians) * radius + origin.Y);
	}

	private static int PointToDegrees(Vector2 point, Vector2 origin)
	{
		var diff = point - origin;
		return ClampDegrees(Mathf.RoundToInt(Mathf.RadToDeg(Mathf.Atan2(-diff.Y, diff.X))));
	}

	public override void _Draw()
	{
		var origin = Origin;
		var radius = Radius;
		if (radius <= 0f) return;

		var dim = _editable ? 1f : DisabledMultiplier.A;
		DrawCircle(origin, radius, FillColor with { A = FillColor.A * dim });
		DrawArc(origin, radius, 0f, Mathf.Tau, 48, OutlineColor with { A = OutlineColor.A * dim }, 2f, true);

		for (var degrees = 0f; degrees < 360f; degrees += TickIntervalDegrees)
		{
			var outer = DegreesToPoint(degrees, radius - TickOuterInset, origin);
			var inner = DegreesToPoint(degrees, radius * (1f - TickInnerFraction), origin);
			DrawLine(inner, outer, TickColor with { A = TickColor.A * dim }, 1f);
		}

		if (_angle.HasValue)
		{
			var needleEnd = DegreesToPoint(_angle.Value, radius - 4f, origin);
			DrawLine(origin, needleEnd, NeedleColor with { A = NeedleColor.A * dim }, 2f);
			DrawCircle(origin, 2f, NeedleColor with { A = NeedleColor.A * dim });
		}
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (!_editable) return;

		if (@event is InputEventMouseButton { Pressed: true } buttonEvent &&
			buttonEvent.ButtonIndex is MouseButton.Left or MouseButton.Right)
		{
			SetAngleFromMouse(buttonEvent.Position, snap: buttonEvent.ButtonIndex == MouseButton.Left);
			AcceptEvent();
			return;
		}

		if (@event is InputEventMouseMotion motionEvent &&
			(motionEvent.ButtonMask.HasFlag(MouseButtonMask.Left) || motionEvent.ButtonMask.HasFlag(MouseButtonMask.Right)))
		{
			SetAngleFromMouse(motionEvent.Position, snap: motionEvent.ButtonMask.HasFlag(MouseButtonMask.Left));
			AcceptEvent();
		}
	}

	private void SetAngleFromMouse(Vector2 position, bool snap)
	{
		var degrees = PointToDegrees(position, Origin);
		if (snap) degrees = ClampDegrees(Mathf.RoundToInt(degrees / TickIntervalDegrees) * (int)TickIntervalDegrees);

		if (degrees == _angle) return;

		_angle = degrees;
		QueueRedraw();
		AngleChanged?.Invoke(degrees);
	}
}
