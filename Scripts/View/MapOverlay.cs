using DoomArchitect.Core.Map;
using DoomArchitect.Interop;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Screen-space gizmo layer for the 2D (top-down) view: grid, vertices,
/// and linedefs color-coded one-sided/two-sided. Deliberately separate
/// from the 3D floor/ceiling meshes - projected fresh from map-space via
/// <see cref="Camera3D.UnprojectPosition"/> every frame instead of baked
/// into world geometry, so it stays crisp at any zoom.
/// </summary>
public partial class MapOverlay : Control
{
	private const float GridSpacing = 64f;
	private const float GridMargin = GridSpacing * 2f;
	private const float VertexSize = 6f;
	private const float LinedefWidth = 2f;
	private const float FrontIndicatorLength = 4f;
	private const float FrontIndicatorAlpha = 0.7f;
	private const float VertexPickRadius = 10f;

	private static readonly Color GridColor = new(0.3f, 0.3f, 0.3f);
	private static readonly Color OneSidedColor = new(0.9f, 0.9f, 0.9f);
	private static readonly Color TwoSidedColor = new(0.55f, 0.55f, 0.6f);
	private static readonly Color UnselectedVertexColor = new(0.35f, 0.65f, 1f);
	private static readonly Color HoverVertexColor = new(1f, 0.55f, 0.1f);

	public MapData Map { get; set; }
	public Camera3D Camera { get; set; }

	private Vertex _draggedVertex;
	private Vertex _hoveredVertex;

	public override void _Process(double delta)
	{
		if (Visible) QueueRedraw();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible || Map == null || Camera == null) return;

		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
				_draggedVertex = FindVertexNear(press.Position);
				_hoveredVertex = _draggedVertex;
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
				_draggedVertex = null;
				break;
			case InputEventMouseMotion motion when _draggedVertex != null:
				Map.MoveVertex(_draggedVertex, Unproject(motion.Position));
				break;
			case InputEventMouseMotion motion:
				_hoveredVertex = FindVertexNear(motion.Position);
				break;
		}
	}

	private Vertex FindVertexNear(Vector2 screenPosition)
	{
		Vertex closest = null;
		var closestDistance = VertexPickRadius;
		foreach (var vertex in Map.Vertices)
		{
			var distance = Project(vertex.Position).DistanceTo(screenPosition);
			if (distance <= closestDistance)
			{
				closest = vertex;
				closestDistance = distance;
			}
		}

		return closest;
	}

	/// <summary>Casts a ray from the camera through a screen point down to the map's ground plane (Y = 0).</summary>
	private MapVector2 Unproject(Vector2 screenPosition)
	{
		var origin = Camera.ProjectRayOrigin(screenPosition);
		var direction = Camera.ProjectRayNormal(screenPosition);
		var distanceToPlane = -origin.Y / direction.Y;
		return (origin + direction * distanceToPlane).ToDoom();
	}

	public override void _Draw()
	{
		if (Map == null || Camera == null) return;

		DrawGrid();
		DrawLinedefs();
		DrawVertices();
	}

	private void DrawGrid()
	{
		var (min, max) = ComputeBounds();
		var startX = SnapDown(min.X, GridSpacing);
		var endX = SnapUp(max.X, GridSpacing);
		var startY = SnapDown(min.Y, GridSpacing);
		var endY = SnapUp(max.Y, GridSpacing);

		for (var x = startX; x <= endX; x += GridSpacing)
		{
			DrawWorldLine(new MapVector2(x, startY), new MapVector2(x, endY), GridColor);
		}

		for (var y = startY; y <= endY; y += GridSpacing)
		{
			DrawWorldLine(new MapVector2(startX, y), new MapVector2(endX, y), GridColor);
		}
	}

	private void DrawLinedefs()
	{
		foreach (var linedef in Map.Linedefs)
		{
			var color = linedef.Back == null ? OneSidedColor : TwoSidedColor;
			DrawWorldLine(linedef.Start.Position, linedef.End.Position, color, LinedefWidth);

			if (linedef.Front != null)
			{
				DrawFrontIndicator(linedef, color);
			}
		}
	}

	/// <summary>
	/// A tiny tick from the linedef's midpoint towards its front side - the
	/// same "front sidedef is on the right walking Start-&gt;End" rule
	/// <see cref="Core.Geometry.SectorTracer"/> relies on, made visible.
	/// </summary>
	private void DrawFrontIndicator(Linedef linedef, Color color)
	{
		var start = linedef.Start.Position;
		var end = linedef.End.Position;
		var direction = MapVector2.Normalize(end - start);
		var rightNormal = new MapVector2(direction.Y, -direction.X);
		var midpoint = (start + end) / 2f;

		DrawWorldLine(midpoint, midpoint + rightNormal * FrontIndicatorLength, new Color(color, FrontIndicatorAlpha), LinedefWidth);
	}

	private void DrawVertices()
	{
		var half = new Vector2(VertexSize, VertexSize) / 2f;
		foreach (var vertex in Map.Vertices)
		{
			var center = Project(vertex.Position);
			var color = vertex == _hoveredVertex ? HoverVertexColor : UnselectedVertexColor;
			DrawRect(new Rect2(center - half, new Vector2(VertexSize, VertexSize)), color);
		}
	}

	private (MapVector2 Min, MapVector2 Max) ComputeBounds()
	{
		if (Map.Vertices.Count == 0) return (MapVector2.Zero, MapVector2.Zero);

		var min = Map.Vertices[0].Position;
		var max = min;
		foreach (var vertex in Map.Vertices)
		{
			min = MapVector2.Min(min, vertex.Position);
			max = MapVector2.Max(max, vertex.Position);
		}

		var margin = new MapVector2(GridMargin, GridMargin);
		return (min - margin, max + margin);
	}

	private void DrawWorldLine(MapVector2 from, MapVector2 to, Color color, float width = 1f) =>
		DrawLine(Project(from), Project(to), color, width);

	private Vector2 Project(MapVector2 doomPosition) => Camera.UnprojectPosition(doomPosition.ToWorld(0f));

	private static float SnapDown(float value, float step) => Mathf.Floor(value / step) * step;

	private static float SnapUp(float value, float step) => Mathf.Ceil(value / step) * step;
}
