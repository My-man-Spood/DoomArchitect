using System.Collections.Generic;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Draw Lines mode - see the project plan for the deferred Phase 3
/// (cardinal-direction snap, auto-close across existing geometry,
/// continuous drawing, and other polish). Clicking places new points;
/// clicking back near the first one ends the gesture - closing and
/// committing the loop via <see cref="DrawLoopCommand"/> if at least 3
/// points were placed, or just cancelling (a degenerate loop) otherwise -
/// matching UDB's own real <c>DrawGeometryMode</c> exactly. Doesn't touch
/// the map at all until the loop actually closes; nothing is undoable
/// before that point because nothing has happened yet.
///
/// A placed point snaps onto an existing vertex or existing linedef
/// (projected exactly onto the line) before falling back to a plain new
/// grid-snapped position - the same screen-pixel-radius hit-testing
/// convention already established by <c>VertexOverlayHandler.FindNear</c>/
/// <c>LinedefOverlayHandler.FindNear</c>, reused rather than reinvented.
/// Vertex/linedef snapping takes priority over grid snap entirely
/// (matches UDB's own real layered snap priority - stitch snap over
/// plain grid snap), not combined with it.
///
/// Unlike Vertex/Linedef/Sector/Thing, this doesn't build on the shared
/// <see cref="ElementOverlayHandler{TSelectable,TDraggable}"/> engine -
/// that engine selects/drags *existing* elements, and there's no
/// meaningful "existing element under the cursor" here at all; this owns
/// its own small input state machine instead.
/// </summary>
public sealed class DrawOverlayHandler
{
	private const float ClosePickRadius = 10f; // matches VertexOverlayHandler's own VertexPickRadius
	private const float VertexPickRadius = 10f; // matches VertexOverlayHandler's own VertexPickRadius
	private const float LinedefPickRadius = 6f; // matches LinedefOverlayHandler's own LinedefPickRadius
	private const float PointMarkerSize = 6f;
	private const float LineWidth = 2f;

	private readonly MapOverlay _owner;
	private readonly MapOverlayCamera _camera;

	private readonly List<DrawPoint> _points = new();
	private bool _hasCursor;
	private Vector2 _cursorScreen;
	private Vertex _hoveredVertex;
	private Linedef _hoveredLinedef;

	public DrawOverlayHandler(MapOverlay owner, MapOverlayCamera camera)
	{
		_owner = owner;
		_camera = camera;
	}

	public void HandleInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } button:
				OnLeftClick(button.Position);
				break;
			case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
				CancelDraw();
				break;
			case InputEventMouseMotion motion:
				_hasCursor = true;
				_cursorScreen = motion.Position;
				_hoveredVertex = FindNearVertex(motion.Position);
				_hoveredLinedef = _hoveredVertex == null ? FindNearLinedef(motion.Position) : null;
				break;
			case InputEventKey { Pressed: true, Keycode: Key.Escape }:
				CancelDraw();
				break;
			case InputEventKey { Pressed: true, Keycode: Key.Backspace } when _points.Count > 0:
				_points.RemoveAt(_points.Count - 1);
				break;
		}
	}

	private void OnLeftClick(Vector2 screenPosition)
	{
		var closesLoop = _points.Count > 0
			&& _camera.Project(_points[0].Position).DistanceTo(screenPosition) <= ClosePickRadius;

		if (closesLoop)
		{
			if (_points.Count >= 3)
			{
				_owner.UndoStack.Execute(new DrawLoopCommand(_owner.Map, new List<DrawPoint>(_points)));
			}

			_points.Clear();
			return;
		}

		_points.Add(ResolveDrawPoint(screenPosition));
	}

	private DrawPoint ResolveDrawPoint(Vector2 screenPosition)
	{
		var vertex = FindNearVertex(screenPosition);
		if (vertex != null) return DrawPoint.AtExistingVertex(vertex);

		var linedef = FindNearLinedef(screenPosition);
		if (linedef != null)
		{
			var projected = ProjectOntoLinedef(linedef, _camera.Unproject(screenPosition));
			return DrawPoint.OnLinedef(linedef, projected);
		}

		return DrawPoint.AtNewPosition(_owner.SnapIfEnabled(_camera.Unproject(screenPosition)));
	}

	private Vertex FindNearVertex(Vector2 screenPosition)
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

	/// <summary>Discards any in-progress, uncommitted loop without touching the map - called both on Escape/right-click and when <see cref="MapOverlay.Mode"/> switches away from <see cref="EditMode.Draw"/>.</summary>
	public void CancelDraw() => _points.Clear();

	public void Draw(CanvasItem target)
	{
		if (_owner.Mode != EditMode.Draw) return;

		DrawHoverHighlight(target);

		if (_points.Count == 0) return;

		var half = new Vector2(PointMarkerSize, PointMarkerSize) / 2f;
		for (var i = 0; i < _points.Count; i++)
		{
			var center = _camera.Project(_points[i].Position);
			var color = i == 0 ? MapOverlayColors.Hover : MapOverlayColors.Selected;
			target.DrawRect(new Rect2(center - half, new Vector2(PointMarkerSize, PointMarkerSize)), color);

			if (i > 0)
			{
				target.DrawLine(_camera.Project(_points[i - 1].Position), center, MapOverlayColors.Selected, LineWidth);
			}
		}

		if (_hasCursor)
		{
			target.DrawLine(_camera.Project(_points[^1].Position), _cursorScreen, MapOverlayColors.Hover, LineWidth);
		}
	}

	/// <summary>Highlights whichever existing vertex/linedef the next click would snap onto - the same <see cref="MapOverlayColors.Hover"/> every other mode already uses for this exact purpose.</summary>
	private void DrawHoverHighlight(CanvasItem target)
	{
		if (_hoveredVertex != null)
		{
			var half = new Vector2(PointMarkerSize, PointMarkerSize) / 2f;
			var center = _camera.Project(_hoveredVertex.Position);
			target.DrawRect(new Rect2(center - half, new Vector2(PointMarkerSize, PointMarkerSize)), MapOverlayColors.Hover);
		}
		else if (_hoveredLinedef != null)
		{
			target.DrawLine(
				_camera.Project(_hoveredLinedef.Start.Position), _camera.Project(_hoveredLinedef.End.Position),
				MapOverlayColors.Hover, LineWidth);
		}
	}
}
