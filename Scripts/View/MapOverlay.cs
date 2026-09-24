using System.Collections.Generic;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Textures;
using DoomArchitect.Core.Undo;
using DoomArchitect.Interop;
using DoomArchitect.Rendering;
using Godot;
using MapVector2 = System.Numerics.Vector2;

/// <summary>
/// Screen-space gizmo layer for the 2D (top-down) view: grid, vertices,
/// linedefs, sectors, and things, plus the per-mode hover/select/drag
/// interactions for whichever of those <see cref="Mode"/> currently
/// targets. Left-click only ever selects (toggles the clicked element,
/// never moves anything); right-click-drag is the only thing that moves
/// geometry, matching UDB's own real button split exactly - pressing on
/// an unselected element replaces the selection with just that one before
/// dragging it, pressing on an already-selected element drags the entire
/// current selection together. Deliberately separate from the 3D floor/
/// ceiling meshes - projected fresh from map-space via
/// <see cref="Camera3D.UnprojectPosition"/> every frame instead of baked
/// into world geometry, so it stays crisp at any zoom.
///
/// This class is now a thin orchestrator over several composed pieces
/// (plain C# classes, not partials - see TODO.md's own "MapOverlay.cs is
/// a growing god-object" concern, noted back when this file was ~500
/// lines and still true once it had grown past 1100): <see cref="MapOverlayCamera"/>
/// (projection math), <see cref="MapOverlayGrid"/> (the background grid),
/// <see cref="MarqueeSelector"/> (the shared left-button marquee state
/// machine every mode drives), and one handler per element type
/// (<see cref="VertexOverlayHandler"/>/<see cref="LinedefOverlayHandler"/>/
/// <see cref="SectorOverlayHandler"/>/<see cref="ThingOverlayHandler"/>),
/// each owning that element type's own hit-testing, select/drag input
/// (via the shared generic <see cref="ElementOverlayHandler{TSelectable,TDraggable}"/>
/// engine - reading all four modes' own original input-handling methods
/// side by side confirmed they were an identical skeleton, differing only
/// in a handful of delegate-shaped specifics), and drawing. Split by
/// element type rather than by input-vs-drawing since that's the axis
/// this file's own growth has actually followed (the Thing-rendering work
/// alone never touched Vertex/Linedef/Sector code, and future per-element
/// work like "drawing mode"/"adding things" will cut the same way).
/// </summary>
public partial class MapOverlay : Control
{
	private const float ZoomFactor = 0.9f;

	// Constructed as field initializers, not in _Ready(), deliberately:
	// MapView's own _Ready() assigns straight into this.Camera (and
	// MainMenuBar/GridToolbar/ModeToolbar read other properties) with no
	// guarantee Godot has already called this node's own _Ready() by
	// then - the two aren't in a parent-child relationship this project
	// can rely on Godot's bottom-up ready ordering for. Field
	// initializers run as part of this instance's own constructor, before
	// any external code can reach it at all, so every property setter
	// below is always safe to call from the very first frame.
	private readonly MapOverlayCamera _camera;
	private readonly MapOverlayGrid _grid;
	private readonly MarqueeSelector _marquee;
	private readonly VertexOverlayHandler _vertexHandler;
	private readonly LinedefOverlayHandler _linedefHandler;
	private readonly SectorOverlayHandler _sectorHandler;
	private readonly ThingOverlayHandler _thingHandler;
	private readonly DrawOverlayHandler _drawHandler;
	private readonly TagIndicatorOverlayHandler _tagIndicatorHandler;

	public MapOverlay()
	{
		_camera = new MapOverlayCamera(this);
		_grid = new MapOverlayGrid(_camera);
		_marquee = new MarqueeSelector(_camera);
		_vertexHandler = new VertexOverlayHandler(this, _camera, _marquee);
		_linedefHandler = new LinedefOverlayHandler(this, _camera, _marquee);
		_sectorHandler = new SectorOverlayHandler(this, _camera, _marquee);
		_thingHandler = new ThingOverlayHandler(this, _camera, _marquee);
		_drawHandler = new DrawOverlayHandler(this, _camera);
		_tagIndicatorHandler = new TagIndicatorOverlayHandler(this, _camera, _linedefHandler, _sectorHandler);
	}

	/// <summary>UDB's own real <c>GZShowEventLines</c>/<c>ViewSelectionEffects</c> toggle, combined - see <see cref="TagIndicatorOverlayHandler"/>'s own remarks. Exposed here so the toolbar button and the Godot-InputMap-backed keybind action both have one place to read/write.</summary>
	public bool TagIndicatorsEnabled
	{
		get => _tagIndicatorHandler.Enabled;
		set => _tagIndicatorHandler.Enabled = value;
	}

	public MapData Map { get; set; }
	public UndoStack UndoStack { get; set; }
	public IGameConfiguration GameConfiguration { get; set; }
	public TextureSet TextureSet { get; set; }
	public TextureIconCache TextureIconCache { get; set; }
	public SpriteIconCache SpriteIconCache { get; set; }

	public Camera3D Camera
	{
		get => _camera.Camera;
		set => _camera.Camera = value;
	}

	/// <summary>Every currently loaded resource, named for display - the texture browser's per-resource tree.</summary>
	public IReadOnlyList<NamedResource> NamedResources { get; set; } = System.Array.Empty<NamedResource>();

	/// <summary>
	/// Raised on either of the two gestures that open a properties dialog
	/// for the current mode - a right-click that releases without ever
	/// turning into a drag (UDB's own real behavior, verified directly
	/// against its source: <c>OnEditEnd</c> only opens the dialog when
	/// <c>OnDragStart</c> never fired) or a left-double-click (this
	/// project's own added convenience, not a real UDB gesture, but a
	/// harmless and common one). See <see cref="ElementOverlayHandler{TSelectable,TDraggable}"/>'s
	/// own remarks on why both share one delegate.
	/// </summary>
	public event System.Action<IReadOnlyList<Sector>> EditSectorsRequested;
	public event System.Action<IReadOnlyList<Linedef>> EditLinedefsRequested;
	public event System.Action<IReadOnlyList<Thing>> EditThingsRequested;

	/// <summary>
	/// C# events can only be invoked from their declaring type, so each
	/// per-element handler (a separate top-level class, not nested) calls
	/// these rather than raising the event directly - the handler's own
	/// double-click delegate is wired to one of these at construction.
	/// </summary>
	internal void RaiseEditSectorsRequested(IReadOnlyList<Sector> sectors) => EditSectorsRequested?.Invoke(sectors);

	internal void RaiseEditLinedefsRequested(IReadOnlyList<Linedef> linedefs) => EditLinedefsRequested?.Invoke(linedefs);

	internal void RaiseEditThingsRequested(IReadOnlyList<Thing> things) => EditThingsRequested?.Invoke(things);

	private EditMode _mode = EditMode.Vertices;

	/// <summary>
	/// Whatever mode was active right before Draw mode was last entered -
	/// UDB's own real <c>PreviousStableMode</c>, remembered here the same
	/// way (captured the instant <see cref="Mode"/> is set to
	/// <see cref="EditMode.Draw"/>, regardless of *how* it got there - the
	/// W key or <see cref="StartDrawingAt"/>'s own right-click-empty-space
	/// entry both funnel through this one setter). <see cref="ReturnFromDraw"/>
	/// is what actually switches back to it.
	/// </summary>
	private EditMode _modeBeforeDraw = EditMode.Vertices;

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

			// A half-drawn loop belongs to Draw mode alone - leaving it
			// without discarding would otherwise keep rendering (and stay
			// closeable) after switching to something else entirely.
			if (_mode == EditMode.Draw) _drawHandler.CancelDraw();

			if (value == EditMode.Draw) _modeBeforeDraw = _mode;

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

	/// <summary>
	/// UDB's own real Draw-mode exit: both finishing (commit) and
	/// cancelling return to whichever mode was active right before Draw
	/// mode was entered (<see cref="_modeBeforeDraw"/>), not a fixed mode
	/// and not staying in Draw - called by <see cref="DrawOverlayHandler"/>
	/// itself on both its own finish and cancel paths.
	/// </summary>
	internal void ReturnFromDraw()
	{
		if (_mode == EditMode.Draw) Mode = _modeBeforeDraw;
	}

	/// <summary>
	/// UDB's own real <c>General.Settings.DefaultThingType</c> - the type
	/// a freshly right-click-inserted Thing gets (<see cref="ThingOverlayHandler"/>'s
	/// own real <c>InsertThingAt</c>), updated in turn whenever a type is
	/// actually applied through the thing-edit dialog
	/// (<c>ThingEditDialog</c>'s own <c>onTypeChanged</c> callback, wired
	/// in <c>MainMenuBar</c>) - a plain in-memory session value, same as
	/// UDB's own real one before it's ever saved to disk; naturally
	/// outlives a single map load/unload since this <see cref="MapOverlay"/>
	/// instance itself does, matching UDB's own real cross-map
	/// persistence within one running session close enough without this
	/// project having any settings-file persistence to hook into at all
	/// (see TODO.md's "Keybinding management" entry on that same gap).
	/// </summary>
	internal int LastUsedThingType { get; set; } = CreateThingCommand.DefaultType;

	public float GridSize
	{
		get => _grid.GridSize;
		set => _grid.GridSize = value;
	}

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

	/// <summary>
	/// The persistent on/off state (matches UDB's toolbar checkbox, which
	/// this app has no equivalent of yet - see <see cref="EffectiveSnap"/>
	/// for the momentary Shift-key override UDB also applies on top).
	/// </summary>
	public bool SnapEnabled { get; set; } = true;

	/// <summary>
	/// UDB's own real "continuous drawing" (a Draw Lines options-panel
	/// checkbox, <c>drawlinesmode.continuousdrawing</c>) - when on,
	/// finishing or cancelling a drawn polyline stays in
	/// <see cref="EditMode.Draw"/> and clears the in-progress points for a
	/// fresh one, instead of <see cref="ReturnFromDraw"/>'s own normal
	/// return to whatever mode was active before. Read directly by
	/// <see cref="DrawOverlayHandler"/>'s own finish/cancel paths, not this
	/// property itself - <see cref="Mode"/>'s setter still runs its usual
	/// <see cref="_drawHandler"/>.CancelDraw()/mode-switch machinery
	/// unconditionally on every *other* trigger (e.g. clicking a toolbar
	/// mode button while continuous drawing is on genuinely does leave
	/// Draw mode, matching UDB - this toggle only changes what a drawn
	/// polyline's own finish/cancel do, not every way to leave the mode).
	/// Session-only, like every other toolbar toggle here - no
	/// settings-persistence layer exists yet (see TODO.md).
	/// </summary>
	public bool ContinuousDrawing { get; set; }

	public bool DynamicGridSizeEnabled
	{
		get => _grid.DynamicGridSizeEnabled;
		set => _grid.DynamicGridSizeEnabled = value;
	}

	public void IncreaseGridSize() => _grid.IncreaseGridSize();

	public void DecreaseGridSize() => _grid.DecreaseGridSize();

	/// <summary>
	/// Ported from UDB's own <c>ShiftState ^ SnapToGrid</c> pattern (used
	/// identically across every one of its classic edit modes): holding
	/// the real, independently rebindable <c>grid_snap_invert_modifier</c>
	/// action (default Shift) inverts whatever the persistent toggle is
	/// currently set to.
	/// </summary>
	public bool EffectiveSnap => SnapEnabled ^ Input.IsActionPressed("grid_snap_invert_modifier");

	/// <summary>Internal (not private) so each per-element handler can pass it as a delegate - see <see cref="ElementOverlayHandler{TSelectable,TDraggable}"/>'s own constructor.</summary>
	internal MapVector2 SnapIfEnabled(MapVector2 position) =>
		EffectiveSnap ? GridSnapper.Snap(position, GridSize) : position;

	/// <summary>
	/// UDB's own real "AutoDrawOnEdit": right-clicking empty space in
	/// Vertices/Linedefs/Sectors mode (nothing under the cursor for that
	/// mode's own <see cref="ElementOverlayHandler{TSelectable,TDraggable}"/>
	/// to select/edit) starts Draw mode with the first point already
	/// placed right there, rather than doing nothing - wired as the
	/// <c>onEmptyRightClick</c> delegate on each of those three handlers'
	/// own <see cref="ElementOverlayHandler{TSelectable,TDraggable}"/>.
	/// Not wired for Things mode - UDB's own real equivalent there is
	/// "insert a new Thing", a distinct, larger, not-yet-built feature of
	/// its own (see TODO.md's "Adding things" entry), not this one.
	/// </summary>
	internal void StartDrawingAt(Vector2 screenPosition)
	{
		Mode = EditMode.Draw;
		_drawHandler.BeginAt(screenPosition);
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
		if (Input.IsActionPressed("pan_view_modifier"))
		{
			if (@event is InputEventMouseMotion motion) _camera.PanView(motion);
			return;
		}

		switch (Mode)
		{
			case EditMode.Vertices:
				_vertexHandler.HandleInput(@event);
				break;
			case EditMode.Linedefs:
				_linedefHandler.HandleInput(@event);
				break;
			case EditMode.Sectors:
				_sectorHandler.HandleInput(@event);
				break;
			case EditMode.Things:
				_thingHandler.HandleInput(@event);
				break;
			case EditMode.Draw:
				_drawHandler.HandleInput(@event);
				break;
		}
	}

	private void ZoomAt(Vector2 screenPosition, float factor)
	{
		_camera.ZoomAt(screenPosition, factor);
		if (DynamicGridSizeEnabled) _grid.ApplyDynamicGridSize();
	}

	public override void _Draw()
	{
		if (Map == null || Camera == null) return;

		_grid.Draw(this);
		_sectorHandler.Draw(this);
		_linedefHandler.Draw(this);
		_tagIndicatorHandler.Draw(this);
		_vertexHandler.Draw(this);
		_thingHandler.Draw(this);
		_drawHandler.Draw(this);
		_marquee.Draw(this);
	}
}
