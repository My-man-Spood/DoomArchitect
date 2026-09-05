using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Interop;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Screen-space gizmo layer for the 2D (top-down) view: grid, vertices,
/// linedefs, and a sector fill highlight, plus the per-mode hover/drag
/// interactions for whichever of those <see cref="Mode"/> currently
/// targets. Deliberately separate from the 3D floor/ceiling meshes -
/// projected fresh from map-space via <see cref="Camera3D.UnprojectPosition"/>
/// every frame instead of baked into world geometry, so it stays crisp at
/// any zoom.
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
	private const float LinedefPickRadius = 6f;
	private const float InactiveModeAlpha = 0.35f;

	private static readonly Color GridColor = new(0.3f, 0.3f, 0.3f);
	private static readonly Color OneSidedColor = new(0.9f, 0.9f, 0.9f);
	private static readonly Color TwoSidedColor = new(0.55f, 0.55f, 0.6f);
	private static readonly Color UnselectedVertexColor = new(0.35f, 0.65f, 1f);
	private static readonly Color HoverColor = new(1f, 0.55f, 0.1f);
	private static readonly Color SectorHighlightColor = new(1f, 0.55f, 0.1f, 0.25f);

	public MapData Map { get; set; }
	public Camera3D Camera { get; set; }
	public EditMode Mode { get; set; } = EditMode.Vertices;

	private Vertex _draggedVertex;
	private Vertex _hoveredVertex;

	private Linedef _draggedLinedef;
	private Linedef _hoveredLinedef;

	private Sector _draggedSector;
	private Sector _hoveredSector;

	private MapVector2 _dragOrigin;
	private MapVector2 _dragStartLineStart;
	private MapVector2 _dragStartLineEnd;
	private Dictionary<Vertex, MapVector2> _dragStartSectorVertices;

	public override void _Process(double delta)
	{
		if (Visible) QueueRedraw();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible || Map == null || Camera == null) return;

		switch (Mode)
		{
			case EditMode.Vertices:
				HandleVertexInput(@event);
				break;
			case EditMode.Linedefs:
				HandleLinedefInput(@event);
				break;
			case EditMode.Sectors:
				HandleSectorInput(@event);
				break;
		}
	}

	private void HandleVertexInput(InputEvent @event)
	{
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

	private void HandleLinedefInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
				_draggedLinedef = FindLinedefNear(press.Position);
				_hoveredLinedef = _draggedLinedef;
				if (_draggedLinedef != null)
				{
					_dragOrigin = Unproject(press.Position);
					_dragStartLineStart = _draggedLinedef.Start.Position;
					_dragStartLineEnd = _draggedLinedef.End.Position;
				}
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
				_draggedLinedef = null;
				break;
			case InputEventMouseMotion motion when _draggedLinedef != null:
				var lineDelta = Unproject(motion.Position) - _dragOrigin;
				Map.MoveVertex(_draggedLinedef.Start, _dragStartLineStart + lineDelta);
				Map.MoveVertex(_draggedLinedef.End, _dragStartLineEnd + lineDelta);
				break;
			case InputEventMouseMotion motion:
				_hoveredLinedef = FindLinedefNear(motion.Position);
				break;
		}
	}

	private void HandleSectorInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
				_draggedSector = FindSectorAt(press.Position);
				_hoveredSector = _draggedSector;
				if (_draggedSector != null)
				{
					_dragOrigin = Unproject(press.Position);
					_dragStartSectorVertices = SectorVertices(_draggedSector).ToDictionary(v => v, v => v.Position);
				}
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
				_draggedSector = null;
				_dragStartSectorVertices = null;
				break;
			case InputEventMouseMotion motion when _draggedSector != null:
				var sectorDelta = Unproject(motion.Position) - _dragOrigin;
				foreach (var (vertex, startPosition) in _dragStartSectorVertices)
				{
					Map.MoveVertex(vertex, startPosition + sectorDelta);
				}
				break;
			case InputEventMouseMotion motion:
				_hoveredSector = FindSectorAt(motion.Position);
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

	private Linedef FindLinedefNear(Vector2 screenPosition)
	{
		Linedef closest = null;
		var closestDistance = LinedefPickRadius;
		foreach (var linedef in Map.Linedefs)
		{
			var distance = DistanceToSegment(
				screenPosition, Project(linedef.Start.Position), Project(linedef.End.Position));
			if (distance <= closestDistance)
			{
				closest = linedef;
				closestDistance = distance;
			}
		}

		return closest;
	}

	private Sector FindSectorAt(Vector2 screenPosition)
	{
		var point = Unproject(screenPosition);
		return Map.Sectors.FirstOrDefault(sector => SectorHitTest.Contains(sector, point));
	}

	private static IEnumerable<Vertex> SectorVertices(Sector sector) =>
		SectorTracer.Trace(sector).SelectMany(loop => loop.Vertices).Distinct();

	private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
	{
		var ab = b - a;
		var t = ab.LengthSquared() > 0f ? Mathf.Clamp((point - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
		return point.DistanceTo(a + ab * t);
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
		DrawSectorHighlight();
		DrawLinedefs();
		DrawVertices();
		DrawModeLabel();
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

	/// <summary>
	/// Fills the hovered/dragged sector's actual floor area (holes
	/// excluded) using the same trace -&gt; nest -&gt; cut -&gt; ear-clip
	/// pipeline <c>SectorMeshBuilder</c> uses for the 3D mesh - there's no
	/// separate 2D-only triangulation to keep in sync.
	/// </summary>
	private void DrawSectorHighlight()
	{
		if (Mode != EditMode.Sectors || _hoveredSector == null) return;

		var polygons = PolygonCutter.Cut(PolygonNesting.BuildTree(SectorTracer.Trace(_hoveredSector)));
		foreach (var polygon in polygons)
		{
			foreach (var (a, b, c) in EarClipper.Clip(polygon))
			{
				DrawColoredPolygon(new[] { Project(a), Project(b), Project(c) }, SectorHighlightColor);
			}
		}
	}

	private void DrawLinedefs()
	{
		var alpha = Mode == EditMode.Linedefs ? 1f : InactiveModeAlpha;
		foreach (var linedef in Map.Linedefs)
		{
			var isHighlighted = Mode == EditMode.Linedefs && linedef == _hoveredLinedef;
			var baseColor = isHighlighted ? HoverColor : linedef.Back == null ? OneSidedColor : TwoSidedColor;
			var color = new Color(baseColor, alpha);
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

		DrawWorldLine(midpoint, midpoint + rightNormal * FrontIndicatorLength, new Color(color, color.A * FrontIndicatorAlpha), LinedefWidth);
	}

	private void DrawVertices()
	{
		var half = new Vector2(VertexSize, VertexSize) / 2f;
		var alpha = Mode == EditMode.Vertices ? 1f : InactiveModeAlpha;
		foreach (var vertex in Map.Vertices)
		{
			var center = Project(vertex.Position);
			var baseColor = vertex == _hoveredVertex ? HoverColor : UnselectedVertexColor;
			DrawRect(new Rect2(center - half, new Vector2(VertexSize, VertexSize)), new Color(baseColor, alpha));
		}
	}

	private void DrawModeLabel()
	{
		var font = GetThemeDefaultFont();
		var fontSize = GetThemeDefaultFontSize();
		DrawString(font, new Vector2(12, 12 + fontSize), $"Mode: {Mode}  (1 Vertices · 2 Linedefs · 3 Sectors)",
			HorizontalAlignment.Left, -1, fontSize, Colors.White);
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
