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
	public const float DefaultGridSize = 32f;
	public const float MinGridSize = 1f;
	public const float MaxGridSize = 1024f;

	private const float VertexSize = 6f;
	private const float LinedefWidth = 2f;
	private const float FrontIndicatorLength = 4f;
	private const float FrontIndicatorAlpha = 0.7f;
	private const float VertexPickRadius = 10f;
	private const float LinedefPickRadius = 6f;
	private const float InactiveModeAlpha = 0.35f;

	/// <summary>UDB's own threshold in <c>Renderer2D.RenderGrid</c> for when a grid tier is too dense to read.</summary>
	private const float MinGridCellPixels = 6f;
	private const int MaxGridDoublings = 20;
	private const float Grid64Size = 64f;

	private const float ZoomFactor = 0.9f;
	private const float MinCameraSize = 20f;
	private const float MaxCameraSize = 2000f;

	private static readonly Color GridColor = new(0.5f, 0.5f, 0.55f, 0.22f);
	private static readonly Color Grid64Color = new(0.65f, 0.65f, 0.85f, 0.32f);
	private static readonly Color OneSidedColor = new(0.9f, 0.9f, 0.9f);
	private static readonly Color TwoSidedColor = new(0.55f, 0.55f, 0.6f);
	private static readonly Color UnselectedVertexColor = new(0.35f, 0.65f, 1f);
	private static readonly Color HoverColor = new(1f, 0.55f, 0.1f);
	private static readonly Color SectorHighlightColor = new(1f, 0.55f, 0.1f, 0.25f);

	public MapData Map { get; set; }
	public Camera3D Camera { get; set; }
	public EditMode Mode { get; set; } = EditMode.Vertices;
	public float GridSize { get; set; } = DefaultGridSize;

	/// <summary>
	/// The persistent on/off state (matches UDB's toolbar checkbox, which
	/// this app has no equivalent of yet - see <see cref="EffectiveSnap"/>
	/// for the momentary Shift-key override UDB also applies on top).
	/// </summary>
	public bool SnapEnabled { get; set; } = true;

	/// <summary>
	/// Mirrors UDB's own "DynamicGridSize" setting (default on there too):
	/// while enabled, zooming recomputes <see cref="GridSize"/> via
	/// <see cref="Core.Geometry.DynamicGridSize"/> instead of leaving it
	/// fixed. Manually changing grid size (<c>[</c>/<c>]</c>) turns this
	/// off, matching UDB's <c>DisableDynamicGridResize</c>.
	/// </summary>
	public bool DynamicGridSizeEnabled { get; set; } = true;

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

	/// <summary>
	/// Ported from UDB's own <c>ShiftState ^ SnapToGrid</c> pattern (used
	/// identically across every one of its classic edit modes): holding
	/// Shift inverts whatever the persistent toggle is currently set to.
	/// </summary>
	private bool EffectiveSnap => SnapEnabled ^ Input.IsKeyPressed(Key.Shift);

	private MapVector2 SnapIfEnabled(MapVector2 position) =>
		EffectiveSnap ? GridSnapper.Snap(position, GridSize) : position;

	public override void _Process(double delta)
	{
		if (Visible) QueueRedraw();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible || Map == null || Camera == null) return;

		if (@event is InputEventMouseButton { Pressed: true } wheel)
		{
			if (wheel.ButtonIndex == MouseButton.WheelUp) ZoomAt(wheel.Position, ZoomFactor);
			else if (wheel.ButtonIndex == MouseButton.WheelDown) ZoomAt(wheel.Position, 1f / ZoomFactor);
		}

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
				Map.MoveVertex(_draggedVertex, SnapIfEnabled(Unproject(motion.Position)));
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
					_dragOrigin = SnapIfEnabled(Unproject(press.Position));
					_dragStartLineStart = _draggedLinedef.Start.Position;
					_dragStartLineEnd = _draggedLinedef.End.Position;
				}
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
				_draggedLinedef = null;
				break;
			case InputEventMouseMotion motion when _draggedLinedef != null:
				var lineDelta = SnapIfEnabled(Unproject(motion.Position)) - _dragOrigin;
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
					_dragOrigin = SnapIfEnabled(Unproject(press.Position));
					_dragStartSectorVertices = SectorVertices(_draggedSector).ToDictionary(v => v, v => v.Position);
				}
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
				_draggedSector = null;
				_dragStartSectorVertices = null;
				break;
			case InputEventMouseMotion motion when _draggedSector != null:
				var sectorDelta = SnapIfEnabled(Unproject(motion.Position)) - _dragOrigin;
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

	/// <summary>
	/// Changes the ortho camera's <see cref="Camera3D.Size"/> (smaller =
	/// zoomed in) while keeping the map-space point under the cursor fixed
	/// on screen, the way UDB's own scroll-to-zoom does - otherwise
	/// zooming would recenter on the map origin instead of the cursor.
	/// </summary>
	private void ZoomAt(Vector2 screenPosition, float factor)
	{
		var before = Unproject(screenPosition);
		Camera.Size = Mathf.Clamp(Camera.Size * factor, MinCameraSize, MaxCameraSize);
		var after = Unproject(screenPosition);
		Camera.Position += (before - after).ToWorld(0f);

		if (DynamicGridSizeEnabled) ApplyDynamicGridSize();
	}

	/// <summary>Ported from UDB's <c>ClassicMode.MatchGridSizeToDisplayScale</c>, called on every zoom change.</summary>
	private void ApplyDynamicGridSize()
	{
		var (min, max) = ViewportBounds();
		var minVisibleExtent = Mathf.Min(max.X - min.X, max.Y - min.Y);
		var target = DynamicGridSize.ForVisibleExtent(minVisibleExtent);
		GridSize = Mathf.Clamp(target, MinGridSize, MaxGridSize);
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

	/// <summary>
	/// Ported from UDB's <c>RenderBackgroundGrid</c>/<c>RenderGrid</c>:
	/// the configured grid draws in the normal color, plus - whenever
	/// that configured size is 64 or finer - a second tier always fixed
	/// at exactly 64 units (Doom's standard alignment unit) in a distinct
	/// color, so that reference stays visible however fine you've zoomed
	/// the working grid. Not ported: UDB's separate "DynamicGridSize"
	/// setting that auto-adjusts the persisted grid size itself as you
	/// zoom - this only adapts what's drawn, never the configured/snap size.
	/// </summary>
	private void DrawGrid()
	{
		DrawGridTier(GridSize, GridColor);
		if (GridSize <= Grid64Size) DrawGridTier(Grid64Size, Grid64Color);
	}

	/// <summary>
	/// Doubles <paramref name="baseSize"/> until each cell is at least
	/// <see cref="MinGridCellPixels"/> wide on screen, exactly like UDB's
	/// own "increase rendered grid size if needed" fallback in
	/// <c>RenderGrid</c> - otherwise a fine grid zoomed far out renders as
	/// a dense, illegible mesh of lines.
	/// </summary>
	private void DrawGridTier(float baseSize, Color color)
	{
		var size = baseSize;
		for (var i = 0; i < MaxGridDoublings && CellPixelSize(size) <= MinGridCellPixels; i++)
		{
			size *= 2f;
		}

		var (min, max) = ViewportBounds();
		var startX = SnapDown(min.X, size);
		var endX = SnapUp(max.X, size);
		var startY = SnapDown(min.Y, size);
		var endY = SnapUp(max.Y, size);

		for (var x = startX; x <= endX; x += size)
		{
			DrawWorldLine(new MapVector2(x, startY), new MapVector2(x, endY), color);
		}

		for (var y = startY; y <= endY; y += size)
		{
			DrawWorldLine(new MapVector2(startX, y), new MapVector2(endX, y), color);
		}
	}

	private float CellPixelSize(float size) =>
		Project(new MapVector2(size, 0)).DistanceTo(Project(MapVector2.Zero));

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
		var snapState = EffectiveSnap ? "on" : "off";
		var dynamicState = DynamicGridSizeEnabled ? "on" : "off";
		DrawString(font, new Vector2(12, 12 + fontSize),
			$"Mode: {Mode}  (1 Vertices · 2 Linedefs · 3 Sectors)  Grid: {GridSize} ([ larger, ] smaller)  " +
			$"Snap: {snapState} (G to toggle, hold Shift to invert)  Dynamic: {dynamicState} (D to toggle)",
			HorizontalAlignment.Left, -1, fontSize, Colors.White);
	}

	/// <summary>
	/// The map-space rectangle the camera currently sees, found by
	/// unprojecting the viewport's own corners rather than reasoning about
	/// Godot's orthographic-projection math directly - works the same
	/// regardless of projection type or aspect ratio, and reuses the exact
	/// same ray/plane intersection every other pick in this file already
	/// goes through.
	/// </summary>
	private (MapVector2 Min, MapVector2 Max) ViewportBounds()
	{
		var size = GetViewportRect().Size;
		var corners = new[]
		{
			Unproject(Vector2.Zero),
			Unproject(new Vector2(size.X, 0)),
			Unproject(new Vector2(0, size.Y)),
			Unproject(size),
		};

		var min = corners[0];
		var max = corners[0];
		foreach (var corner in corners)
		{
			min = MapVector2.Min(min, corner);
			max = MapVector2.Max(max, corner);
		}

		return (min, max);
	}

	private void DrawWorldLine(MapVector2 from, MapVector2 to, Color color, float width = 1f) =>
		DrawLine(Project(from), Project(to), color, width);

	private Vector2 Project(MapVector2 doomPosition) => Camera.UnprojectPosition(doomPosition.ToWorld(0f));

	private static float SnapDown(float value, float step) => Mathf.Floor(value / step) * step;

	private static float SnapUp(float value, float step) => Mathf.Ceil(value / step) * step;
}
