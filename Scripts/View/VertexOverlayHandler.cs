using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Everything about how a Vertex behaves in the 2D view - hit-testing,
/// select/marquee/drag input (via the shared
/// <see cref="ElementOverlayHandler{Vertex,Vertex}"/> engine, since a
/// vertex is its own draggable - it has a position of its own, unlike
/// Linedef/Sector), and drawing. Vertex mode has no properties dialog to
/// open yet, so <see cref="ElementOverlayHandler{TSelectable,TDraggable}"/>
/// is constructed with a <c>null</c> <c>onEdit</c> delegate here - both of
/// its own real triggers (a right-click that releases without dragging,
/// and this project's own added left-double-click convenience) correctly
/// end up no-ops as a result. UDB's own real right-click *does* open a
/// vertex properties dialog (<c>VerticesMode.OnEditEnd</c>'s
/// <c>ShowEditVertices</c>), a genuine gap this project doesn't have yet
/// (no <c>VertexEditDialog</c> exists at all - see TODO.md), not something
/// intentionally skipped.
///
/// Right-click gets one more layer ahead of the shared engine, matching
/// UDB's own real three-way <c>VerticesMode.OnEditBegin</c> priority
/// exactly: a highlighted vertex still drags/edits via the engine as
/// normal; failing that, a nearby linedef splits immediately
/// (<see cref="SplitLinedefCommand"/>) rather than falling through to
/// empty space; only truly empty space starts Draw mode (the engine's own
/// <c>onEmptyRightClick</c>).
/// </summary>
public sealed class VertexOverlayHandler
{
	private const float VertexSize = 6f;
	private const float VertexPickRadius = 10f;
	private const float LinedefPickRadius = 6f; // matches LinedefOverlayHandler's own LinedefPickRadius

	private static readonly Color UnselectedColor = new(0.35f, 0.65f, 1f);

	private readonly MapOverlay _owner;
	private readonly MapOverlayCamera _camera;
	private readonly ElementOverlayHandler<Vertex, Vertex> _input;

	public VertexOverlayHandler(MapOverlay owner, MapOverlayCamera camera, MarqueeSelector marquee)
	{
		_owner = owner;
		_camera = camera;
		_input = new ElementOverlayHandler<Vertex, Vertex>(
			camera, marquee, () => _owner.UndoStack, _owner.SnapIfEnabled,
			FindNear, v => v.IsSelected,
			v => _owner.Map.SelectOnly(v), v => _owner.Map.ToggleSelect(v), () => _owner.Map.ClearSelectedVertices(),
			() => _owner.Map.GetSelectedVertices(), v => v.Position, (v, p) => _owner.Map.MoveVertex(v, p),
			(v, oldPos, newPos) => new MoveVertexCommand(_owner.Map, v, oldPos, newPos),
			(min, max, mode) => _owner.Map.MarqueeSelectVertices(min, max, mode),
			onEdit: null,
			onEmptyRightClick: screenPosition => _owner.StartDrawingAt(screenPosition));
	}

	public void HandleInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } press
			&& FindNear(press.Position) == null)
		{
			var linedef = FindNearLinedef(press.Position);
			if (linedef != null)
			{
				var position = ProjectOntoLinedef(linedef, _camera.Unproject(press.Position));
				_owner.UndoStack.Execute(new SplitLinedefCommand(_owner.Map, linedef, position));
				return;
			}
		}

		_input.HandleInput(@event);
	}

	private Vertex FindNear(Vector2 screenPosition)
	{
		Vertex closest = null;
		var closestDistance = VertexPickRadius;
		foreach (var vertex in _owner.Map.Vertices)
		{
			var distance = _camera.Project(vertex.Position).DistanceTo(screenPosition);
			if (distance <= closestDistance)
			{
				closest = vertex;
				closestDistance = distance;
			}
		}

		return closest;
	}

	private Linedef FindNearLinedef(Vector2 screenPosition)
	{
		Linedef closest = null;
		var closestDistance = LinedefPickRadius;
		foreach (var linedef in _owner.Map.Linedefs)
		{
			var distance = DistanceToSegment(
				screenPosition, _camera.Project(linedef.Start.Position), _camera.Project(linedef.End.Position));
			if (distance <= closestDistance)
			{
				closest = linedef;
				closestDistance = distance;
			}
		}

		return closest;
	}

	private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
	{
		var ab = b - a;
		var t = ab.LengthSquared() > 0f ? Mathf.Clamp((point - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
		return point.DistanceTo(a + ab * t);
	}

	private static MapVector2 ProjectOntoLinedef(Linedef linedef, MapVector2 point)
	{
		var a = linedef.Start.Position;
		var b = linedef.End.Position;
		var ab = b - a;
		var lengthSquared = ab.LengthSquared();
		if (lengthSquared <= 0f) return a;

		var t = MapVector2.Dot(point - a, ab) / lengthSquared;
		t = System.Math.Clamp(t, 0f, 1f);
		return a + ab * t;
	}

	public void Draw(CanvasItem target)
	{
		var half = new Vector2(VertexSize, VertexSize) / 2f;
		var alpha = _owner.Mode == EditMode.Vertices ? 1f : MapOverlayColors.InactiveModeAlpha;
		foreach (var vertex in _owner.Map.Vertices)
		{
			var center = _camera.Project(vertex.Position);
			var baseColor = vertex == _input.Hovered ? MapOverlayColors.Hover
				: vertex.IsSelected ? MapOverlayColors.Selected
				: UnselectedColor;
			target.DrawRect(new Rect2(center - half, new Vector2(VertexSize, VertexSize)), new Color(baseColor, alpha));
		}
	}
}
