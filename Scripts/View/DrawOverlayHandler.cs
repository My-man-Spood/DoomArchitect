using System.Collections.Generic;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Draw Lines mode - Phase 3 (cardinal-direction snap, auto-close across
/// existing geometry, continuous drawing, and the rest) is done; see
/// TODO.md for the full writeup. Left-click places new points; clicking
/// back near the first one closes and commits a ring, same as right-click
/// (<c>finishdraw</c> in UDB's own real default keybinds) - but right-click
/// (or too few points to close) commits whatever's drawn as a genuinely
/// open polyline instead (<see cref="DrawLoopCommand"/>'s own
/// <c>closeLoop</c> parameter, UDB's own real <c>Tools.DrawLines</c> open-
/// polyline support), matching UDB's own real "commit whatever's drawn,
/// open or closed" - not the "close the loop right now" special case this
/// used to be before open-polyline support existed. Escape (UDB's own real
/// <c>cancelmode</c>) discards the in-progress loop entirely instead -
/// genuinely distinct from finish, not a synonym for it, matching UDB
/// exactly (unless <see cref="MapOverlay.ContinuousDrawing"/> is on, which
/// changes what both finish and cancel do afterward - see
/// <see cref="FinishDraw"/>'s own remarks). Either way - finish or cancel -
/// control normally returns to whichever mode was active before Draw mode
/// was entered (<see cref="MapOverlay.ReturnFromDraw"/>, UDB's own real
/// <c>PreviousStableMode</c>). Doesn't touch the map at all until a loop
/// actually commits; nothing is undoable before that point because nothing
/// has happened yet.
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
	private const float CardinalLineEpsilon = 0.5f; // matches CloseMapEpsilon's own map-unit snapping tolerance
	private const float PointMarkerSize = 6f;
	private const float LineWidth = 2f;
	private const float DirectionTickLengthPixels = 10f; // UDB's own RenderLinedefDirectionIndicator, screen-space fixed length like DrawLengthLabel's own offset
	private const float LabelOffsetPixels = 12f;
	private const int LabelFontSize = 13;
	private const float LabelPadding = 3f;

	private static readonly Color LabelBackgroundColor = new(0f, 0f, 0f, 0.6f);
	private static readonly Color LabelTextColor = Colors.White;

	private readonly MapOverlay _owner;
	private readonly MapOverlayCamera _camera;

	private readonly List<DrawPoint> _points = new();
	private bool _hasCursor;
	private DrawPoint _previewPoint;
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
				FinishDraw(closeLoop: false);
				break;
			case InputEventMouseMotion motion:
				_hasCursor = true;
				// Resolved through the exact same snap/stitch/cardinal-lock
				// logic a click would use - previously this only read the
				// raw, unsnapped cursor position, so the live rubber-band
				// preview (and the hover highlight, via _hoveredVertex/
				// _hoveredLinedef below) never actually showed the
				// cardinal-direction lock (or grid/vertex/linedef snap)
				// while aiming, only after the click itself already landed
				// snapped - a real, reported bug ("i dont see any
				// snapping"), not just a cosmetic gap.
				_previewPoint = ResolveDrawPoint(motion.Position);
				_hoveredVertex = _previewPoint.ExistingVertex;
				_hoveredLinedef = _previewPoint.SplitLinedef;
				break;
			case InputEventKey { Pressed: true } key when key.IsActionPressed("draw_cancel"):
				// UDB's own real OnCancel guard: continuous drawing blocks
				// leaving Draw mode entirely via Escape - only the
				// in-progress shape itself is discarded, matching UDB's
				// `if(continuousdrawing) { return; }` before its own
				// mode-change call.
				if (_owner.ContinuousDrawing) CancelDraw();
				else _owner.ReturnFromDraw();
				break;
			case InputEventKey { Pressed: true } key when _points.Count > 0 && key.IsActionPressed("draw_remove_last_point"):
				_points.RemoveAt(_points.Count - 1);
				break;
		}
	}

	private void OnLeftClick(Vector2 screenPosition)
	{
		if (_points.Count > 0 && _camera.Project(_points[0].Position).DistanceTo(screenPosition) <= ClosePickRadius)
		{
			FinishDraw(closeLoop: true);
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
			FinishDraw(closeLoop: true);
			return;
		}

		_points.Add(point);
	}

	/// <summary>
	/// Commits the drawing so far - clicking back near the first point
	/// (<paramref name="closeLoop"/> true, wraps back to a closed ring), or
	/// right-click (<see cref="HandleInput"/>'s own Right button case,
	/// matching UDB's own real <c>finishdraw</c> default - <paramref name="closeLoop"/>
	/// false, a genuinely open polyline, UDB's own real <c>Tools.DrawLines</c>
	/// support for it - <see cref="DrawLoopCommand"/>'s own remarks on this
	/// parameter). A closed loop needs at least 3 points to be a real
	/// shape; an open polyline needs only 2 (a single segment) - fewer
	/// than that isn't anything <see cref="DrawLoopCommand"/> can build,
	/// discarded the same as a plain cancel rather than left as a stray
	/// point.
	///
	/// Normally returns to whichever mode was active before Draw mode was
	/// entered (<see cref="MapOverlay.ReturnFromDraw"/>, UDB's own real
	/// behavior - <c>PreviousStableMode</c> - for both its <c>OnAccept</c>
	/// and <c>OnCancel</c>), which is also what actually clears
	/// <see cref="_points"/> in that case (the <see cref="MapOverlay.Mode"/>
	/// setter's own existing "leaving Draw mode discards it" cleanup, not a
	/// separate clear of its own). With <see cref="MapOverlay.ContinuousDrawing"/>
	/// on, stays in Draw mode instead and clears <see cref="_points"/>
	/// directly - UDB's own real <c>OnAccept</c>:
	/// <c>points.Clear(); ... RedrawDisplay();</c> rather than changing mode.
	/// </summary>
	private void FinishDraw(bool closeLoop)
	{
		var canCommit = closeLoop ? _points.Count >= 3 : _points.Count >= 2;
		if (canCommit)
		{
			_owner.UndoStack.Execute(new DrawLoopCommand(_owner.Map, new List<DrawPoint>(_points), closeLoop));
		}

		if (_owner.ContinuousDrawing) CancelDraw();
		else _owner.ReturnFromDraw();
	}

	/// <summary>
	/// UDB's own real "AutoDrawOnEdit": right-clicking empty space in
	/// another mode starts Draw mode with the first point already placed
	/// right there (<see cref="MapOverlay.StartDrawingAt"/>) rather than
	/// requiring a separate mode switch first. Seeds the rubber-band
	/// cursor state too, not just the point itself - <see cref="_hasCursor"/>/
	/// <see cref="_previewPoint"/> otherwise stay at whatever they were
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
		_previewPoint = _points[0];
	}

	/// <summary>
	/// UDB's own real <c>Alt+Shift</c> cardinal/45-degree direction lock
	/// (<see cref="CardinalSnapper"/>) - a live poll of the real,
	/// independently rebindable <c>draw_cardinal_lock_modifier</c> action
	/// (default Alt, with Shift as an additional qualifier on that same
	/// bound event - see TODO.md's "Keybinding management" writeup for why
	/// a single action rather than two).
	/// </summary>
	private static bool CardinalSnapEnabled => Input.IsActionPressed("draw_cardinal_lock_modifier");

	/// <summary>
	/// Vertex/linedef stitch-snap takes priority over grid snap entirely
	/// (see this class's own remarks) - and, per UDB's own real rule, over
	/// the cardinal-direction lock too, but *only* when the candidate
	/// itself actually lies on the locked direction line; a stitch
	/// candidate off that line is rejected outright rather than silently
	/// breaking the lock, matching UDB's own real
	/// <c>ourline.GetSideOfLine(nv.Position) == 0</c> gate.
	/// </summary>
	private DrawPoint ResolveDrawPoint(Vector2 screenPosition)
	{
		var mapPosition = _camera.Unproject(screenPosition);
		MapVector2? cardinalLock = CardinalSnapEnabled && _points.Count > 0
			? CardinalSnapper.Snap(_points[^1].Position, mapPosition)
			: null;

		var vertex = FindNearVertex(screenPosition);
		if (vertex != null && (cardinalLock == null || IsOnLockedLine(_points[^1].Position, cardinalLock.Value, vertex.Position)))
		{
			return DrawPoint.AtExistingVertex(vertex);
		}

		var linedef = FindNearLinedef(screenPosition);
		if (linedef != null)
		{
			var projected = ProjectOntoLinedef(linedef, mapPosition);
			if (cardinalLock == null || IsOnLockedLine(_points[^1].Position, cardinalLock.Value, projected))
			{
				return DrawPoint.OnLinedef(linedef, projected);
			}
		}

		var finalPosition = cardinalLock ?? mapPosition;

		// Cardinal lock forces grid snap on too, matching UDB's own real
		// `snaptogrid = snaptocardinaldirection || ...` - applied directly
		// via GridSnapper here rather than through _owner.SnapIfEnabled,
		// since Shift is already structurally consumed by the Alt+Shift
		// cardinal chord and would otherwise flip EffectiveSnap's own
		// persistent-toggle inversion the wrong way (see this file's class
		// remarks for the one deliberately-unported refinement here).
		return DrawPoint.AtNewPosition(cardinalLock != null
			? GridSnapper.Snap(finalPosition, _owner.GridSize)
			: _owner.SnapIfEnabled(finalPosition));
	}

	private static bool IsOnLockedLine(MapVector2 from, MapVector2 lockedPoint, MapVector2 candidate)
	{
		var direction = lockedPoint - from;
		var lengthSquared = direction.LengthSquared();
		if (lengthSquared <= 0f) return true;

		var cross = (candidate.X - from.X) * direction.Y - (candidate.Y - from.Y) * direction.X;
		var perpendicularDistance = System.MathF.Abs(cross) / System.MathF.Sqrt(lengthSquared);
		return perpendicularDistance <= CardinalLineEpsilon;
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
				DrawSegment(target, _camera.Project(_points[i - 1].Position), center, StitchColor(_points[i]));
				DrawLengthLabel(target, _points[i - 1].Position, _points[i].Position);
			}
		}

		if (_hasCursor)
		{
			DrawSegment(target, _camera.Project(_points[^1].Position), _camera.Project(_previewPoint.Position), StitchColor(_previewPoint));
			DrawLengthLabel(target, _points[^1].Position, _previewPoint.Position);
		}
	}

	/// <summary>Whether this placed point snapped onto existing geometry - the same distinction the rubber-band's own live cursor state uses (<see cref="_hoveredVertex"/>/<see cref="_hoveredLinedef"/>).</summary>
	private static bool Stitches(DrawPoint point) => point.ExistingVertex != null || point.SplitLinedef != null;

	private static Color StitchColor(DrawPoint endPoint) => Stitches(endPoint) ? MapOverlayColors.Hover : MapOverlayColors.Selected;

	/// <summary>
	/// A placed segment or the live rubber-band, colored by whether its
	/// own end point stitches onto existing geometry - UDB's own real
	/// <c>DrawGeometryMode.Update</c> colors each segment identically
	/// (<c>stitchcolor</c>/<c>losecolor</c>) rather than distinguishing
	/// "placed" from "rubber-band" the way an earlier version of this
	/// method did (every placed segment plain red, the rubber-band always
	/// plain orange regardless of what it would actually snap onto).
	/// Solid, not dashed - see this file's own class remarks on the
	/// "dashed" premise in TODO.md having been wrong.
	/// </summary>
	private void DrawSegment(CanvasItem target, Vector2 screenStart, Vector2 screenEnd, Color color)
	{
		target.DrawLine(screenStart, screenEnd, color, LineWidth);
		DrawDirectionTick(target, screenStart, screenEnd, color);
	}

	/// <summary>UDB's own real <c>RenderLinedefDirectionIndicator</c>: a short tick off the segment's own midpoint, along its screen-space perpendicular - drawn toward the opposite side from <see cref="DrawLengthLabel"/>'s own offset so the two never overlap.</summary>
	private static void DrawDirectionTick(CanvasItem target, Vector2 screenStart, Vector2 screenEnd, Color color)
	{
		var screenDelta = screenEnd - screenStart;
		if (screenDelta.LengthSquared() < 1f) return;

		var direction = screenDelta.Normalized();
		var perpendicular = new Vector2(-direction.Y, direction.X);
		var midpoint = (screenStart + screenEnd) / 2f;
		target.DrawLine(midpoint, midpoint - perpendicular * DirectionTickLengthPixels, color, LineWidth);
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
