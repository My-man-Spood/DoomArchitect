using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Everything about how a Linedef behaves in the 2D view. A linedef has no
/// position of its own, so its own <see cref="ElementOverlayHandler{Linedef,Vertex}"/>
/// drags the distinct set of vertices belonging to every currently
/// selected linedef, not the linedefs themselves - <see cref="MoveVertexCommand"/>,
/// the same command Vertex mode itself uses.
/// </summary>
public sealed class LinedefOverlayHandler
{
	private const float LinedefWidth = 2f;
	private const float LinedefPickRadius = 6f;
	private const float FrontIndicatorLength = 4f;
	private const float FrontIndicatorAlpha = 0.7f;

	private static readonly Color OneSidedColor = new(0.9f, 0.9f, 0.9f);
	private static readonly Color TwoSidedColor = new(0.55f, 0.55f, 0.6f);

	private readonly MapOverlay _owner;
	private readonly MapOverlayCamera _camera;
	private readonly ElementOverlayHandler<Linedef, Vertex> _input;

	public LinedefOverlayHandler(MapOverlay owner, MapOverlayCamera camera, MarqueeSelector marquee)
	{
		_owner = owner;
		_camera = camera;
		_input = new ElementOverlayHandler<Linedef, Vertex>(
			camera, marquee, () => _owner.UndoStack, _owner.SnapIfEnabled,
			FindNear, l => l.IsSelected,
			l => _owner.Map.SelectOnly(l), l => _owner.Map.ToggleSelect(l), () => _owner.Map.ClearSelectedLinedefs(),
			() => _owner.Map.GetSelectedLinedefs().SelectMany(l => new[] { l.Start, l.End }).Distinct(),
			v => v.Position, (v, p) => _owner.Map.MoveVertex(v, p),
			(v, oldPos, newPos) => new MoveVertexCommand(_owner.Map, v, oldPos, newPos),
			(min, max, mode) => _owner.Map.MarqueeSelectLinedefs(min, max, mode, _owner.MarqueeSelectTouching),
			onEdit: l => _owner.RaiseEditLinedefsRequested(_owner.Map.GetSelectedLinedefs().ToList()),
			onEmptyRightClick: screenPosition => _owner.StartDrawingAt(screenPosition));
	}

	public void HandleInput(InputEvent @event) => _input.HandleInput(@event);

	private Linedef FindNear(Vector2 screenPosition)
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

	public void Draw(CanvasItem target)
	{
		var alpha = _owner.Mode == EditMode.Linedefs ? 1f : MapOverlayColors.InactiveModeAlpha;
		foreach (var linedef in _owner.Map.Linedefs)
		{
			var isHighlighted = _owner.Mode == EditMode.Linedefs && linedef == _input.Hovered;
			var baseColor = isHighlighted ? MapOverlayColors.Hover
				: linedef.IsSelected ? MapOverlayColors.Selected
				: linedef.Back == null ? OneSidedColor : TwoSidedColor;
			var color = new Color(baseColor, alpha);
			DrawWorldLine(target, linedef.Start.Position, linedef.End.Position, color, LinedefWidth);

			if (linedef.Front != null)
			{
				DrawFrontIndicator(target, linedef, color);
			}
		}
	}

	/// <summary>
	/// A tiny tick from the linedef's midpoint towards its front side - the
	/// same "front sidedef is on the right walking Start-&gt;End" rule
	/// <see cref="Core.Geometry.SectorTracer"/> relies on, made visible.
	/// </summary>
	private void DrawFrontIndicator(CanvasItem target, Linedef linedef, Color color)
	{
		var start = linedef.Start.Position;
		var end = linedef.End.Position;
		var direction = MapVector2.Normalize(end - start);
		var rightNormal = new MapVector2(direction.Y, -direction.X);
		var midpoint = (start + end) / 2f;

		DrawWorldLine(target, midpoint, midpoint + rightNormal * FrontIndicatorLength, new Color(color, color.A * FrontIndicatorAlpha), LinedefWidth);
	}

	private void DrawWorldLine(CanvasItem target, MapVector2 from, MapVector2 to, Color color, float width = 1f) =>
		target.DrawLine(_camera.Project(from), _camera.Project(to), color, width);
}
