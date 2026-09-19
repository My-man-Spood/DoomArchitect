using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Everything about how a Sector behaves in the 2D view. Like Linedef, a
/// sector has no position of its own - its own
/// <see cref="ElementOverlayHandler{Sector,Vertex}"/> drags the distinct
/// set of vertices belonging to every currently selected sector's own
/// traced boundary loops.
/// </summary>
public sealed class SectorOverlayHandler
{
	private static readonly Color HighlightColor = new(1f, 0.55f, 0.1f, 0.25f);
	private static readonly Color SelectedHighlightColor = new(0.9f, 0.15f, 0.15f, 0.18f);

	private readonly MapOverlay _owner;
	private readonly MapOverlayCamera _camera;
	private readonly ElementOverlayHandler<Sector, Vertex> _input;

	public SectorOverlayHandler(MapOverlay owner, MapOverlayCamera camera, MarqueeSelector marquee)
	{
		_owner = owner;
		_camera = camera;
		_input = new ElementOverlayHandler<Sector, Vertex>(
			camera, marquee, () => _owner.UndoStack, _owner.SnapIfEnabled,
			FindNear, s => s.IsSelected,
			s => _owner.Map.SelectOnly(s), s => _owner.Map.ToggleSelect(s), () => _owner.Map.ClearSelectedSectors(),
			() => _owner.Map.GetSelectedSectors().SelectMany(SectorVertices).Distinct(),
			v => v.Position, (v, p) => _owner.Map.MoveVertex(v, p),
			(v, oldPos, newPos) => new MoveVertexCommand(_owner.Map, v, oldPos, newPos),
			(min, max, mode) => _owner.Map.MarqueeSelectSectors(min, max, mode, _owner.MarqueeSelectTouching),
			onEdit: s => _owner.RaiseEditSectorsRequested(_owner.Map.GetSelectedSectors().ToList()),
			onEmptyRightClick: screenPosition => _owner.StartDrawingAt(screenPosition));
	}

	public void HandleInput(InputEvent @event) => _input.HandleInput(@event);

	private Sector FindNear(Vector2 screenPosition)
	{
		var point = _camera.Unproject(screenPosition);
		return _owner.Map.Sectors.FirstOrDefault(sector => SectorHitTest.Contains(sector, point));
	}

	private static IEnumerable<Vertex> SectorVertices(Sector sector) =>
		SectorTracer.Trace(sector).SelectMany(loop => loop.Vertices).Distinct();

	/// <summary>
	/// Fills the hovered/dragged sector's actual floor area (holes
	/// excluded) using the same trace -&gt; nest -&gt; cut -&gt; ear-clip
	/// pipeline <c>SectorMeshBuilder</c> uses for the 3D mesh - there's no
	/// separate 2D-only triangulation to keep in sync.
	/// </summary>
	public void Draw(CanvasItem target)
	{
		if (_owner.Mode != EditMode.Sectors) return;

		foreach (var sector in _owner.Map.Sectors)
		{
			if (sector != _input.Hovered && !sector.IsSelected) continue;

			var color = sector == _input.Hovered ? HighlightColor : SelectedHighlightColor;
			var polygons = PolygonCutter.Cut(PolygonNesting.BuildTree(SectorTracer.Trace(sector)));
			foreach (var polygon in polygons)
			{
				foreach (var (a, b, c) in EarClipper.Clip(polygon))
				{
					var pa = _camera.Project(a);
					var pb = _camera.Project(b);
					var pc = _camera.Project(c);
					if (IsDegenerateTriangle(pa, pb, pc)) continue;
					target.DrawColoredPolygon(new[] { pa, pb, pc }, color);
				}
			}
		}
	}

	/// <summary>
	/// A real ear-clipped triangle from valid map geometry should never be
	/// degenerate, but projecting to screen space can still collapse one
	/// to zero area (or produce a non-finite point) for a sector whose
	/// vertices happen to coincide at that instant - e.g. mid-drag, before
	/// a vertex has moved away from one it started stacked on. Godot's own
	/// <see cref="CanvasItem.DrawColoredPolygon"/> hard-crashes on a
	/// zero-area input ("Invalid polygon data, triangulation failed")
	/// rather than silently skipping it, so this has to be caught before
	/// the call, not after.
	/// </summary>
	private static bool IsDegenerateTriangle(Vector2 a, Vector2 b, Vector2 c)
	{
		if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c)) return true;

		var area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
		return Mathf.Abs(area) < 0.01f;
	}

	private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}
