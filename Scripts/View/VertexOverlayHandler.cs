using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Everything about how a Vertex behaves in the 2D view - hit-testing,
/// select/marquee/drag input (via the shared
/// <see cref="ElementOverlayHandler{Vertex,Vertex}"/> engine, since a
/// vertex is its own draggable - it has a position of its own, unlike
/// Linedef/Sector), and drawing. Vertex mode has no double-click dialog to
/// open (UDB has no vertex property dialog), so
/// <see cref="ElementOverlayHandler{TSelectable,TDraggable}"/> is
/// constructed with a <c>null</c> double-click delegate here.
/// </summary>
public sealed class VertexOverlayHandler
{
	private const float VertexSize = 6f;
	private const float VertexPickRadius = 10f;

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
			onDoubleClick: null);
	}

	public void HandleInput(InputEvent @event) => _input.HandleInput(@event);

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
