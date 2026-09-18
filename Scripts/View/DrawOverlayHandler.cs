using System.Collections.Generic;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Draw Lines mode - see the project plan for the deferred Phase 3
/// (cardinal-direction snap, auto-close across existing geometry,
/// continuous drawing, and other polish). Left-click places new points;
/// clicking back near the first one closes and commits the loop, same as
/// right-click (<c>finishdraw</c> in UDB's own real default keybinds) -
/// UDB's finish action commits from wherever the cursor currently is,
/// with no "must be near the first point" requirement, but this project's
/// <see cref="DrawLoopCommand"/> always closes back to the first point
/// (it has no genuinely-open-polyline support yet, unlike UDB's own real
/// <c>Tools.DrawLines</c> - see TODO.md), so right-click here is really
/// "close the loop right now" rather than UDB's more general "commit
/// whatever's drawn, open or closed." Escape (UDB's own real
/// <c>cancelmode</c>) discards the in-progress loop entirely instead -
/// genuinely distinct from finish, not a synonym for it, matching UDB
/// exactly. Either way - finish or cancel - control returns to whichever
/// mode was active before Draw mode was entered
/// (<see cref="MapOverlay.ReturnFromDraw"/>, UDB's own real
/// <c>PreviousStableMode</c>), never leaving the user parked in Draw mode
/// itself. Doesn't touch the map at all until a loop actually commits;
/// nothing is undoable before that point because nothing has happened
/// yet.
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
	private const float CloseMapEpsilon = 0.5f; // a snapped click landing this close (map units) to the first point counts as closing too, even if the raw click wasn't within ClosePickRadius on screen
	private const float VertexPickRadius = 10f; // matches VertexOverlayHandler's own VertexPickRadius
	private const float LinedefPickRadius = 6f; // matches LinedefOverlayHandler's own LinedefPickRadius
	private const float PointMarkerSize = 6f;
	private const float LineWidth = 2f;
	private const float LabelOffsetPixels = 12f;
	private const int LabelFontSize = 13;
	private const float LabelPadding = 3f;

	private static readonly Color LabelBackgroundColor = new(0f, 0f, 0f, 0.6f);
	private static readonly Color LabelTextColor = Colors.White;

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
				FinishDraw();
				break;
			case InputEventMouseMotion motion:
				_hasCursor = true;
				_cursorScreen = motion.Position;
				_hoveredVertex = FindNearVertex(motion.Position);
				_hoveredLinedef = _hoveredVertex == null ? FindNearLinedef(motion.Position) : null;
				break;
			case InputEventKey { Pressed: true, Keycode: Key.Escape }:
				_owner.ReturnFromDraw();
				break;
			case InputEventKey { Pressed: true, Keycode: Key.Backspace } when _points.Count > 0:
				_points.RemoveAt(_points.Count - 1);
				break;
		}
	}

	private void OnLeftClick(Vector2 screenPosition)
	{
		if (_points.Count > 0 && _camera.Project(_points[0].Position).DistanceTo(screenPosition) <= ClosePickRadius)
		{
			FinishDraw();
			return;
		}

		var point = ResolveDrawPoint(screenPosition);

		// Snapping (grid/vertex/linedef) can converge this click onto the
		// exact same map position as the loop's own first point even when
		// the raw, unsnapped click isn't within screen-pixel range of
		// that point's own projected position - one grid cell can easily
		// span more screen pixels than ClosePickRadius at a moderate
		// zoom. Still means "close the loop", not "place a second point
		// exactly on top of the first" (which used to slip through here
		// and just sit there as a visually-duplicate, never-closing
		// point until manually cancelled).
		if (_points.Count > 0 && MapVector2.DistanceSquared(point.Position, _points[0].Position) < CloseMapEpsilon * CloseMapEpsilon)
		{
			FinishDraw();
			return;
		}

		_points.Add(point);
	}

	/// <summary>
	/// Commits the loop drawn so far - clicking back near the first point,
	/// or right-click (<see cref="HandleInput"/>'s own Right button case,
	/// matching UDB's own real <c>finishdraw</c> default). Fewer than 3
	/// points isn't a closed shape <see cref="DrawLoopCommand"/> can build
	/// anything from, so there's nothing meaningful to commit - discarded
	/// the same as a plain cancel rather than left as a stray point.
	/// Either way, returns to whichever mode was active before Draw mode
	/// was entered (<see cref="MapOverlay.ReturnFromDraw"/>, UDB's own
	/// real behavior - <c>PreviousStableMode</c> - for both its
	/// <c>OnAccept</c> and <c>OnCancel</c>), which is also what actually
	/// clears <see cref="_points"/> here (the <see cref="MapOverlay.Mode"/>
	/// setter's own existing "leaving Draw mode discards it" cleanup, not
	/// a separate clear of its own).
	/// </summary>
	private void FinishDraw()
	{
		if (_points.Count >= 3)
		{
			_owner.UndoStack.Execute(new DrawLoopCommand(_owner.Map, new List<DrawPoint>(_points)));
		}

		_owner.ReturnFromDraw();
	}

	/// <summary>
	/// UDB's own real "AutoDrawOnEdit": right-clicking empty space in
	/// another mode starts Draw mode with the first point already placed
	/// right there (<see cref="MapOverlay.StartDrawingAt"/>) rather than
	/// requiring a separate mode switch first. Seeds the rubber-band
	/// cursor state too, not just the point itself - <see cref="_hasCursor"/>/
	/// <see cref="_cursorScreen"/> otherwise stay at whatever they were
	/// last left at (motion events never reach this handler outside Draw
	/// mode at all), which without this produced a brief, wrong rubber-
	/// band line rendered from that stale leftover position on the very
	/// first frame, until the next real mouse-move event corrected it.
	/// </summary>
	public void BeginAt(Vector2 screenPosition)
	{
		_points.Clear();
		_points.Add(ResolveDrawPoint(screenPosition));
		_hasCursor = true;
		_cursorScreen = screenPosition;
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

	/// <summary>Discards any in-progress, uncommitted loop without touching the map - called both on Escape and when <see cref="MapOverlay.Mode"/> switches away from <see cref="EditMode.Draw"/>.</summary>
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
				DrawLengthLabel(target, _points[i - 1].Position, _points[i].Position);
			}
		}

		if (_hasCursor)
		{
			var cursorMapPosition = _camera.Unproject(_cursorScreen);
			target.DrawLine(_camera.Project(_points[^1].Position), _cursorScreen, MapOverlayColors.Hover, LineWidth);
			DrawLengthLabel(target, _points[^1].Position, cursorMapPosition);
		}
	}

	/// <summary>
	/// UDB's own real per-segment <c>LineLengthLabel</c> ("L:&lt;length&gt;;
	/// A:&lt;angle&gt;", shown for every already-placed segment and the
	/// current rubber-band one alike) - without it, a segment's own real
	/// length/direction is only guessable by eye against the grid, which
	/// is exactly what made this mode hard to use precisely. Positioned
	/// off to one side of the segment's own midpoint (perpendicular
	/// offset) so it never sits directly on top of the line it describes;
	/// the background rect behind the text is a DoomArchitect-specific
	/// legibility choice, not a UDB behavior (UDB draws it against its own
	/// editor theme's background color, which this project's overlay
	/// doesn't have an equivalent concept of).
	/// </summary>
	private void DrawLengthLabel(CanvasItem target, MapVector2 start, MapVector2 end)
	{
		var delta = end - start;
		var length = delta.Length();
		if (length <= 0.5f) return;

		var angleDegrees = Mathf.PosMod(Mathf.RadToDeg(Mathf.Atan2(delta.Y, delta.X)), 360f);
		var text = $"L:{length:0}  A:{angleDegrees:0}";

		// The perpendicular offset is derived straight from the already-
		// projected screen points, not from a map-space direction scaled
		// afterwards - sidesteps any question of whether a map-space
		// perpendicular even survives projection/zoom unchanged, and
		// keeps this a constant pixel offset regardless of zoom, matching
		// UDB's own real behavior (it divides by the renderer's own scale
		// for the identical reason).
		var screenStart = _camera.Project(start);
		var screenEnd = _camera.Project(end);
		var screenDelta = screenEnd - screenStart;
		if (screenDelta.LengthSquared() < 1f) return;

		var screenDirection = screenDelta.Normalized();
		var screenPerpendicular = new Vector2(-screenDirection.Y, screenDirection.X);
		var midpoint = (screenStart + screenEnd) / 2f + screenPerpendicular * LabelOffsetPixels;

		var font = ThemeDB.FallbackFont;
		var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, LabelFontSize);
		var baseline = midpoint - new Vector2(textSize.X / 2f, 0f);

		target.DrawRect(
			new Rect2(baseline - new Vector2(LabelPadding, textSize.Y + LabelPadding), textSize + new Vector2(LabelPadding, LabelPadding) * 2f),
			LabelBackgroundColor);
		target.DrawString(font, baseline, text, HorizontalAlignment.Left, -1, LabelFontSize, LabelTextColor);
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
