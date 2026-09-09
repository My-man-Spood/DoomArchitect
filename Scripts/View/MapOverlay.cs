using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using DoomArchitect.Interop;
using DoomArchitect.Rendering;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Screen-space gizmo layer for the 2D (top-down) view: grid, vertices,
/// linedefs, and a sector fill highlight, plus the per-mode hover/select/
/// drag interactions for whichever of those <see cref="Mode"/> currently
/// targets. Left-click only ever selects (toggles the clicked element,
/// never moves anything); right-click-drag is the only thing that moves
/// geometry, matching UDB's own real button split exactly - pressing on
/// an unselected element replaces the selection with just that one before
/// dragging it, pressing on an already-selected element drags the entire
/// current selection together. Deliberately separate from the 3D floor/
/// ceiling meshes - projected fresh from map-space via
/// <see cref="Camera3D.UnprojectPosition"/> every frame instead of baked
/// into world geometry, so it stays crisp at any zoom.
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

	/// <summary>Persistent multi-selection tint - always drawn in place of the base color, but hover always wins over it (see <see cref="DrawVertices"/>/<see cref="DrawLinedefs"/>/<see cref="DrawThings"/>).</summary>
	private static readonly Color SelectedColor = new(0.9f, 0.15f, 0.15f);
	private static readonly Color SectorSelectedHighlightColor = new(0.9f, 0.15f, 0.15f, 0.18f);

	/// <summary>One color per <see cref="MarqueeSelectionMode"/>, matching which combine mode the current marquee drag would apply on release (see <see cref="GetMarqueeSelectionMode"/>) - this project's own color choices, not a port of UDB's actual theme values.</summary>
	private static readonly Color MarqueeSelectColor = new(0.9f, 0.9f, 0.9f);
	private static readonly Color MarqueeAddColor = new(0.3f, 0.9f, 0.3f);
	private static readonly Color MarqueeSubtractColor = new(0.9f, 0.3f, 0.3f);
	private static readonly Color MarqueeIntersectColor = new(0.6f, 0.4f, 0.9f);

	public MapData Map { get; set; }
	public Camera3D Camera { get; set; }
	public UndoStack UndoStack { get; set; }
	public IGameConfiguration GameConfiguration { get; set; }

	private EditMode _mode = EditMode.Vertices;

	/// <summary>
	/// Switching to Vertices/Linedefs/Sectors re-derives that type's
	/// selection from whatever's currently selected across all three,
	/// exactly matching UDB's real <c>MapSet.ConvertSelection</c> (called
	/// by every classic mode's <c>OnEngage</c>) - not a clear, not a
	/// no-op. Switching to <see cref="EditMode.Things"/> runs no
	/// conversion at all; Thing selection is independent of this entirely,
	/// matching UDB.
	/// </summary>
	public EditMode Mode
	{
		get => _mode;
		set
		{
			if (_mode == value) return;
			_mode = value;

			switch (value)
			{
				case EditMode.Vertices:
					Map?.ConvertGeometrySelection(GeometrySelectionType.Vertices);
					break;
				case EditMode.Linedefs:
					Map?.ConvertGeometrySelection(GeometrySelectionType.Linedefs);
					break;
				case EditMode.Sectors:
					Map?.ConvertGeometrySelection(GeometrySelectionType.Sectors);
					break;
			}
		}
	}

	public float GridSize { get; set; } = DefaultGridSize;

	/// <summary>
	/// Whether a Linedefs/Sectors-mode marquee selects anything merely
	/// touching the rectangle (default UDB's real "select inside" mode
	/// only selects fully-enclosed elements) - matches UDB's own real
	/// <c>MarqueSelectTouching</c> toggle exactly: session-only, never
	/// persisted, defaults off. Meaningless for Vertices/Things (a single
	/// point has no touching-vs-enclosed distinction), so their marquee
	/// methods never read this.
	/// </summary>
	public bool MarqueeSelectTouching { get; set; }

	private Texture2D _thingIcon;
	private Texture2D _thingIconNoDirection;

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

	/// <summary>
	/// Matches UDB's own <c>[</c>/<c>]</c> grid-size keys: doubles within
	/// the 1..1024 bound, and turns off <see cref="DynamicGridSizeEnabled"/>
	/// first, matching UDB's <c>DisableDynamicGridResize</c> - manual and
	/// automatic sizing shouldn't fight each other. Shared by the keybind
	/// and the grid toolbar's +/- buttons so both go through one policy.
	/// </summary>
	public void IncreaseGridSize()
	{
		DynamicGridSizeEnabled = false;
		if (GridSize <= MaxGridSize / 2) GridSize *= 2f;
	}

	public void DecreaseGridSize()
	{
		DynamicGridSizeEnabled = false;
		if (GridSize >= MinGridSize * 2) GridSize /= 2f;
	}

	private Vertex _hoveredVertex;
	private Linedef _hoveredLinedef;
	private Sector _hoveredSector;
	private Thing _hoveredThing;

	/// <summary>
	/// Every field below is null when no right-button drag is in progress
	/// for that mode, else a snapshot of each dragged element's start
	/// position - the whole selection when the pressed element was
	/// already selected, just that one element otherwise (see each
	/// <c>Handle*Input</c>'s right-button press case). Shared
	/// <see cref="_dragOrigin"/> is the snapped mouse position at press
	/// time, common to all four.
	/// </summary>
	private MapVector2 _dragOrigin;
	private Dictionary<Vertex, MapVector2> _dragStartVertices;
	private Dictionary<Vertex, MapVector2> _dragStartLinedefVertices;
	private Dictionary<Vertex, MapVector2> _dragStartSectorVertices;
	private Dictionary<Thing, MapVector2> _dragStartThings;

	private const float MarqueeStartThresholdPixels = 2f; // matches UDB's own MouseSelectionThreshold default
	private const float MarqueeMinSize = 0.1f; // matches UDB's own selectionvolume guard

	/// <summary>
	/// Left-button marquee/box-select tracking - a separate concept from
	/// the right-button move-drag fields above, shared across all four
	/// modes since there's only ever one marquee in flight regardless of
	/// which mode is active. <see cref="_selecting"/> only becomes true
	/// once the drag has moved more than <see cref="MarqueeStartThresholdPixels"/>
	/// from the press point - below that, releasing the button is a plain
	/// click (see each <c>Handle*Input</c>'s left-button cases).
	/// </summary>
	private Vector2 _selectPressScreen;
	private MapVector2 _selectStartMap;
	private MapVector2 _selectEndMap;
	private bool _selecting;

	/// <summary>Ported from UDB's real <c>BaseClassicMode.GetMultiSelectionMode</c>.</summary>
	private static MarqueeSelectionMode GetMarqueeSelectionMode()
	{
		var ctrl = Input.IsKeyPressed(Key.Ctrl);
		var shift = Input.IsKeyPressed(Key.Shift);
		if (ctrl && shift) return MarqueeSelectionMode.Intersect;
		if (ctrl) return MarqueeSelectionMode.Subtract;
		if (shift) return MarqueeSelectionMode.Add;
		return MarqueeSelectionMode.Select;
	}

	/// <summary>Left-button press: record where a marquee would start from, without yet deciding whether this becomes a click or a drag.</summary>
	private void BeginMarqueeOrClick(Vector2 screenPosition)
	{
		_selectPressScreen = screenPosition;
		_selectStartMap = Unproject(screenPosition);
		_selectEndMap = _selectStartMap;
		_selecting = false;
	}

	/// <summary>
	/// Call on every left-button-held motion event. Crosses into
	/// "selecting" once past <see cref="MarqueeStartThresholdPixels"/> and
	/// keeps the live rectangle updated from then on. Returns whether a
	/// marquee is (now) in progress, so the caller knows not to also
	/// update hover this frame - matches the existing "hover freezes
	/// during a drag" convention already used for right-button move-drags.
	/// </summary>
	private bool UpdateMarquee(Vector2 screenPosition)
	{
		if (!_selecting && _selectPressScreen.DistanceTo(screenPosition) > MarqueeStartThresholdPixels)
		{
			_selecting = true;
		}

		if (!_selecting) return false;

		_selectEndMap = Unproject(screenPosition);
		return true;
	}

	/// <summary>Left-button release while a marquee was in progress: applies the combine mode over the final rectangle, unless it's too small to have been a real drag (matches UDB's own <c>selectionvolume</c> guard).</summary>
	private void EndMarquee(System.Action<MapVector2, MapVector2> applySelection)
	{
		var min = MapVector2.Min(_selectStartMap, _selectEndMap);
		var max = MapVector2.Max(_selectStartMap, _selectEndMap);
		if (max.X - min.X > MarqueeMinSize && max.Y - min.Y > MarqueeMinSize)
		{
			applySelection(min, max);
		}

		_selecting = false;
	}

	/// <summary>
	/// Ported from UDB's own <c>ShiftState ^ SnapToGrid</c> pattern (used
	/// identically across every one of its classic edit modes): holding
	/// Shift inverts whatever the persistent toggle is currently set to.
	/// </summary>
	public bool EffectiveSnap => SnapEnabled ^ Input.IsKeyPressed(Key.Shift);

	private MapVector2 SnapIfEnabled(MapVector2 position) =>
		EffectiveSnap ? GridSnapper.Snap(position, GridSize) : position;

	public override void _Ready()
	{
		_thingIcon = GD.Load<Texture2D>("res://Assets/Icons/icon_thing.svg");
		_thingIconNoDirection = GD.Load<Texture2D>("res://Assets/Icons/icon_thing_nodir.svg");
	}

	public override void _Process(double delta)
	{
		if (Visible) QueueRedraw();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Visible || Map == null || Camera == null || UndoStack == null) return;

		if (@event is InputEventMouseButton { Pressed: true } wheel)
		{
			if (wheel.ButtonIndex == MouseButton.WheelUp) ZoomAt(wheel.Position, ZoomFactor);
			else if (wheel.ButtonIndex == MouseButton.WheelDown) ZoomAt(wheel.Position, 1f / ZoomFactor);
		}

		// Space-held pans the view (UDB's own real "pan_view" action - held
		// key + mouse move, no button needed at all) and suppresses every
		// other mouse interaction for as long as it's held, matching UDB's
		// own per-mode "if(panning) return;" guard at the top of its
		// OnMouseMove - here centralized once instead of once per mode,
		// since this project doesn't have separate mode classes to guard
		// individually. Deliberately a stricter suppression than UDB's own
		// (which only guards hover/marquee/drag-threshold logic, not the
		// button-press handlers themselves) - simpler, and avoids any
		// chance of also starting a select/drag/marquee gesture while
		// panning, which is straightforwardly better than replicating
		// UDB's own partial guard.
		if (Input.IsKeyPressed(Key.Space))
		{
			if (@event is InputEventMouseMotion motion) PanView(motion);
			return;
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
			case EditMode.Things:
				HandleThingInput(@event);
				break;
		}
	}

	private void HandleVertexInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
				BeginMarqueeOrClick(press.Position);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
				if (_selecting)
				{
					EndMarquee((min, max) => Map.MarqueeSelectVertices(min, max, GetMarqueeSelectionMode()));
				}
				else if (_hoveredVertex != null) Map.ToggleSelect(_hoveredVertex);
				else Map.ClearSelectedVertices();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } press:
				var target = FindVertexNear(press.Position);
				_hoveredVertex = target;
				if (target != null)
				{
					if (!target.IsSelected) Map.SelectOnly(target);
					_dragOrigin = SnapIfEnabled(Unproject(press.Position));
					_dragStartVertices = Map.GetSelectedVertices().ToDictionary(v => v, v => v.Position);
				}
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
				if (_dragStartVertices != null)
				{
					if (_dragStartVertices.Any(kvp => kvp.Key.Position != kvp.Value))
					{
						var commands = _dragStartVertices
							.Select(kvp => (ICommand)new MoveVertexCommand(Map, kvp.Key, kvp.Value, kvp.Key.Position))
							.ToList();
						UndoStack.Record(new CommandGroup(commands));
					}

					_dragStartVertices = null;
				}
				break;
			case InputEventMouseMotion motion when _dragStartVertices != null:
				var delta = SnapIfEnabled(Unproject(motion.Position)) - _dragOrigin;
				foreach (var (vertex, startPosition) in _dragStartVertices)
				{
					Map.MoveVertex(vertex, startPosition + delta);
				}
				break;
			case InputEventMouseMotion motion when motion.ButtonMask.HasFlag(MouseButtonMask.Left):
				if (UpdateMarquee(motion.Position)) break;
				_hoveredVertex = FindVertexNear(motion.Position);
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
				BeginMarqueeOrClick(press.Position);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
				if (_selecting)
				{
					EndMarquee((min, max) => Map.MarqueeSelectLinedefs(min, max, GetMarqueeSelectionMode(), MarqueeSelectTouching));
				}
				else if (_hoveredLinedef != null) Map.ToggleSelect(_hoveredLinedef);
				else Map.ClearSelectedLinedefs();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } press:
				var target = FindLinedefNear(press.Position);
				_hoveredLinedef = target;
				if (target != null)
				{
					if (!target.IsSelected) Map.SelectOnly(target);
					_dragOrigin = SnapIfEnabled(Unproject(press.Position));
					_dragStartLinedefVertices = Map.GetSelectedLinedefs()
						.SelectMany(l => new[] { l.Start, l.End })
						.Distinct()
						.ToDictionary(v => v, v => v.Position);
				}
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
				if (_dragStartLinedefVertices != null)
				{
					if (_dragStartLinedefVertices.Any(kvp => kvp.Key.Position != kvp.Value))
					{
						var commands = _dragStartLinedefVertices
							.Select(kvp => (ICommand)new MoveVertexCommand(Map, kvp.Key, kvp.Value, kvp.Key.Position))
							.ToList();
						UndoStack.Record(new CommandGroup(commands));
					}

					_dragStartLinedefVertices = null;
				}
				break;
			case InputEventMouseMotion motion when _dragStartLinedefVertices != null:
				var delta = SnapIfEnabled(Unproject(motion.Position)) - _dragOrigin;
				foreach (var (vertex, startPosition) in _dragStartLinedefVertices)
				{
					Map.MoveVertex(vertex, startPosition + delta);
				}
				break;
			case InputEventMouseMotion motion when motion.ButtonMask.HasFlag(MouseButtonMask.Left):
				if (UpdateMarquee(motion.Position)) break;
				_hoveredLinedef = FindLinedefNear(motion.Position);
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
				BeginMarqueeOrClick(press.Position);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
				if (_selecting)
				{
					EndMarquee((min, max) => Map.MarqueeSelectSectors(min, max, GetMarqueeSelectionMode(), MarqueeSelectTouching));
				}
				else if (_hoveredSector != null) Map.ToggleSelect(_hoveredSector);
				else Map.ClearSelectedSectors();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } press:
				var target = FindSectorAt(press.Position);
				_hoveredSector = target;
				if (target != null)
				{
					if (!target.IsSelected) Map.SelectOnly(target);
					_dragOrigin = SnapIfEnabled(Unproject(press.Position));
					_dragStartSectorVertices = Map.GetSelectedSectors()
						.SelectMany(SectorVertices)
						.Distinct()
						.ToDictionary(v => v, v => v.Position);
				}
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
				if (_dragStartSectorVertices != null)
				{
					if (_dragStartSectorVertices.Any(kvp => kvp.Key.Position != kvp.Value))
					{
						var commands = _dragStartSectorVertices
							.Select(kvp => (ICommand)new MoveVertexCommand(Map, kvp.Key, kvp.Value, kvp.Key.Position))
							.ToList();
						UndoStack.Record(new CommandGroup(commands));
					}

					_dragStartSectorVertices = null;
				}
				break;
			case InputEventMouseMotion motion when _dragStartSectorVertices != null:
				var delta = SnapIfEnabled(Unproject(motion.Position)) - _dragOrigin;
				foreach (var (vertex, startPosition) in _dragStartSectorVertices)
				{
					Map.MoveVertex(vertex, startPosition + delta);
				}
				break;
			case InputEventMouseMotion motion when motion.ButtonMask.HasFlag(MouseButtonMask.Left):
				if (UpdateMarquee(motion.Position)) break;
				_hoveredSector = FindSectorAt(motion.Position);
				break;
			case InputEventMouseMotion motion:
				_hoveredSector = FindSectorAt(motion.Position);
				break;
		}
	}

	private void HandleThingInput(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
				BeginMarqueeOrClick(press.Position);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
				if (_selecting)
				{
					EndMarquee((min, max) => Map.MarqueeSelectThings(min, max, GetMarqueeSelectionMode()));
				}
				else if (_hoveredThing != null) Map.ToggleSelect(_hoveredThing);
				else Map.ClearSelectedThings();
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } press:
				var target = FindThingNear(press.Position);
				_hoveredThing = target;
				if (target != null)
				{
					if (!target.IsSelected) Map.SelectOnly(target);
					_dragOrigin = SnapIfEnabled(Unproject(press.Position));
					_dragStartThings = Map.GetSelectedThings().ToDictionary(t => t, t => t.Position);
				}
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
				if (_dragStartThings != null)
				{
					if (_dragStartThings.Any(kvp => kvp.Key.Position != kvp.Value))
					{
						var commands = _dragStartThings
							.Select(kvp => (ICommand)new MoveThingCommand(Map, kvp.Key, kvp.Value, kvp.Key.Position))
							.ToList();
						UndoStack.Record(new CommandGroup(commands));
					}

					_dragStartThings = null;
				}
				break;
			case InputEventMouseMotion motion when _dragStartThings != null:
				var delta = SnapIfEnabled(Unproject(motion.Position)) - _dragOrigin;
				foreach (var (thing, startPosition) in _dragStartThings)
				{
					Map.MoveThing(thing, startPosition + delta);
				}
				break;
			case InputEventMouseMotion motion when motion.ButtonMask.HasFlag(MouseButtonMask.Left):
				if (UpdateMarquee(motion.Position)) break;
				_hoveredThing = FindThingNear(motion.Position);
				break;
			case InputEventMouseMotion motion:
				_hoveredThing = FindThingNear(motion.Position);
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

	/// <summary>
	/// Picks against each Thing's own real on-screen radius rather than a
	/// small fixed pick radius like <see cref="FindVertexNear"/> - a
	/// Thing's footprint varies hugely by type (a Spider Mastermind vs. a
	/// key), so "click what you see" is truer here than a uniform hit
	/// circle would be for the big ones.
	/// </summary>
	private Thing FindThingNear(Vector2 screenPosition)
	{
		Thing closest = null;
		var closestDistance = float.MaxValue;
		foreach (var thing in Map.Things)
		{
			var radius = GameConfiguration?.GetThingType(thing.Type)?.Radius ?? ThingMeshBuilder.FallbackRadius;
			var screenRadius = WorldSizeToScreenPixels(radius);
			var distance = Project(thing.Position).DistanceTo(screenPosition);
			if (distance <= screenRadius && distance < closestDistance)
			{
				closest = thing;
				closestDistance = distance;
			}
		}

		return closest;
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

	/// <summary>
	/// Grab-and-drag view panning while Space is held - a direct port of
	/// UDB's own real <c>ClassicMode.OnUpdateViewPanning</c>/
	/// <c>ScrollBy(lastmappos - mousemappos)</c>: the map point that was
	/// under the cursor before this motion event ends up under the cursor
	/// again after it, at whatever the current zoom's screen-to-map ratio
	/// is - no separate pan speed to tune. Reuses the exact same
	/// before/after-unproject-then-shift-camera trick <see cref="ZoomAt"/>
	/// already established for keeping a point fixed under the cursor,
	/// just for a translation instead of a zoom change.
	/// </summary>
	private void PanView(InputEventMouseMotion motion)
	{
		var before = Unproject(motion.Position - motion.Relative);
		var after = Unproject(motion.Position);
		Camera.Position += (before - after).ToWorld(0f);
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
		DrawThings();
		DrawMarquee();
	}

	/// <summary>
	/// The live marquee rectangle while a left-button drag is in progress
	/// - an unfilled outline, matching UDB's own real
	/// <c>ClassicMode.RenderMultiSelection</c> (a border-only rectangle,
	/// not a filled one). Color reflects whichever combine mode would
	/// apply if released right now, so the modifier-key feedback is live.
	/// </summary>
	private void DrawMarquee()
	{
		if (!_selecting) return;

		var color = GetMarqueeSelectionMode() switch
		{
			MarqueeSelectionMode.Add => MarqueeAddColor,
			MarqueeSelectionMode.Subtract => MarqueeSubtractColor,
			MarqueeSelectionMode.Intersect => MarqueeIntersectColor,
			_ => MarqueeSelectColor,
		};

		var a = Project(_selectStartMap);
		var b = Project(_selectEndMap);
		var min = new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y));
		var max = new Vector2(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y));
		DrawRect(new Rect2(min, max - min), color, false, 2f);
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
		for (var i = 0; i < MaxGridDoublings && WorldSizeToScreenPixels(size) <= MinGridCellPixels; i++)
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

	private float WorldSizeToScreenPixels(float size) =>
		Project(new MapVector2(size, 0)).DistanceTo(Project(MapVector2.Zero));

	/// <summary>
	/// Fills the hovered/dragged sector's actual floor area (holes
	/// excluded) using the same trace -&gt; nest -&gt; cut -&gt; ear-clip
	/// pipeline <c>SectorMeshBuilder</c> uses for the 3D mesh - there's no
	/// separate 2D-only triangulation to keep in sync.
	/// </summary>
	private void DrawSectorHighlight()
	{
		if (Mode != EditMode.Sectors) return;

		foreach (var sector in Map.Sectors)
		{
			if (sector != _hoveredSector && !sector.IsSelected) continue;

			var color = sector == _hoveredSector ? SectorHighlightColor : SectorSelectedHighlightColor;
			var polygons = PolygonCutter.Cut(PolygonNesting.BuildTree(SectorTracer.Trace(sector)));
			foreach (var polygon in polygons)
			{
				foreach (var (a, b, c) in EarClipper.Clip(polygon))
				{
					var pa = Project(a);
					var pb = Project(b);
					var pc = Project(c);
					if (IsDegenerateTriangle(pa, pb, pc)) continue;
					DrawColoredPolygon(new[] { pa, pb, pc }, color);
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
	/// <see cref="DrawColoredPolygon"/> hard-crashes on a zero-area input
	/// ("Invalid polygon data, triangulation failed") rather than silently
	/// skipping it, so this has to be caught before the call, not after.
	/// </summary>
	private static bool IsDegenerateTriangle(Vector2 a, Vector2 b, Vector2 c)
	{
		if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c)) return true;

		var area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
		return Mathf.Abs(area) < 0.01f;
	}

	private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);

	private void DrawLinedefs()
	{
		var alpha = Mode == EditMode.Linedefs ? 1f : InactiveModeAlpha;
		foreach (var linedef in Map.Linedefs)
		{
			var isHighlighted = Mode == EditMode.Linedefs && linedef == _hoveredLinedef;
			var baseColor = isHighlighted ? HoverColor
				: linedef.IsSelected ? SelectedColor
				: linedef.Back == null ? OneSidedColor : TwoSidedColor;
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
			var baseColor = vertex == _hoveredVertex ? HoverColor
				: vertex.IsSelected ? SelectedColor
				: UnselectedVertexColor;
			DrawRect(new Rect2(center - half, new Vector2(VertexSize, VertexSize)), new Color(baseColor, alpha));
		}
	}

	/// <summary>
	/// Sized in world space (scaling with zoom) rather than the fixed
	/// screen-pixel size <see cref="DrawVertices"/> uses - a thing's
	/// radius is a real map-unit footprint, worth showing at its actual
	/// relative scale. Rotated to the thing's own <see cref="Thing.Angle"/>
	/// via <see cref="DrawSetTransform"/> rather than the front-indicator-
	/// tick approach linedefs use, since the icon's direction is baked
	/// into its own art (a notch cut out of the circle) rather than drawn
	/// as a separate line.
	/// </summary>
	private void DrawThings()
	{
		if (_thingIcon == null) return;

		var alpha = Mode == EditMode.Things ? 1f : InactiveModeAlpha;

		foreach (var thing in Map.Things)
		{
			var info = GameConfiguration?.GetThingType(thing.Type);
			var radius = info?.Radius ?? ThingMeshBuilder.FallbackRadius;
			var screenRadius = WorldSizeToScreenPixels(radius);
			var diameter = screenRadius * 2f;

			// A type that doesn't actually rotate in gameplay (most
			// pickups/decorations) gets the plain ring icon instead of
			// the directional notch one - showing a facing indicator for
			// something with no meaningful facing is actively misleading,
			// not just unnecessary detail. Unrecognized types keep the
			// directional icon, matching this project's existing "assume
			// nothing" default from before per-type data existed.
			var icon = info is { ShowsDirection: false } ? _thingIconNoDirection : _thingIcon;
			// Hovered/dragged swaps the category tint for HoverColor
			// outright, the same treatment vertices/linedefs already use,
			// rather than a new visual language just for Things.
			var isHighlighted = Mode == EditMode.Things && thing == _hoveredThing;
			var tint = isHighlighted ? HoverColor
				: thing.IsSelected ? SelectedColor
				: ThingCategoryColors.Get(info?.ColorIndex ?? 0);

			var center = Project(thing.Position);
			var rotation = -Mathf.DegToRad(thing.Angle);

			DrawSetTransform(center, rotation, Vector2.One);
			DrawTextureRect(icon, new Rect2(-screenRadius, -screenRadius, diameter, diameter), false, new Color(tint, alpha));
			DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
		}
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
