using System.Collections.Generic;
using DoomArchitect.Core.Undo;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Draw Lines mode, Phase 1 (standalone new sectors) - see the project
/// plan for the deferred Phase 2 (stitching into existing geometry) and
/// Phase 3 (polish). Clicking places new points; clicking back near the
/// first one ends the gesture - closing and committing the loop as a
/// brand-new, fully self-contained sector via <see cref="CreateSectorLoopCommand"/>
/// if at least 3 points were placed, or just cancelling (a degenerate
/// loop) otherwise - matching UDB's own real <c>DrawGeometryMode</c>
/// exactly. Doesn't touch the map at all until the loop actually closes;
/// nothing is undoable before that point because nothing has happened yet.
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
	private const float PointMarkerSize = 6f;
	private const float LineWidth = 2f;

	private readonly MapOverlay _owner;
	private readonly MapOverlayCamera _camera;

	private readonly List<MapVector2> _points = new();
	private bool _hasCursor;
	private Vector2 _cursorScreen;

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
		var closesLoop = _points.Count > 0 && _camera.Project(_points[0]).DistanceTo(screenPosition) <= ClosePickRadius;

		if (closesLoop)
		{
			if (_points.Count >= 3)
			{
				_owner.UndoStack.Execute(new CreateSectorLoopCommand(_owner.Map, new List<MapVector2>(_points)));
			}

			_points.Clear();
			return;
		}

		_points.Add(_owner.SnapIfEnabled(_camera.Unproject(screenPosition)));
	}

	/// <summary>Discards any in-progress, uncommitted loop without touching the map - called both on Escape/right-click and when <see cref="MapOverlay.Mode"/> switches away from <see cref="EditMode.Draw"/>.</summary>
	public void CancelDraw() => _points.Clear();

	public void Draw(CanvasItem target)
	{
		if (_owner.Mode != EditMode.Draw || _points.Count == 0) return;

		var half = new Vector2(PointMarkerSize, PointMarkerSize) / 2f;
		for (var i = 0; i < _points.Count; i++)
		{
			var center = _camera.Project(_points[i]);
			var color = i == 0 ? MapOverlayColors.Hover : MapOverlayColors.Selected;
			target.DrawRect(new Rect2(center - half, new Vector2(PointMarkerSize, PointMarkerSize)), color);

			if (i > 0)
			{
				target.DrawLine(_camera.Project(_points[i - 1]), center, MapOverlayColors.Selected, LineWidth);
			}
		}

		if (_hasCursor)
		{
			target.DrawLine(_camera.Project(_points[^1]), _cursorScreen, MapOverlayColors.Hover, LineWidth);
		}
	}
}
