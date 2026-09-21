using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Input;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Textures;
using DoomArchitect.Core.Undo;
using DoomArchitect.Input;
using DoomArchitect.Interop;
using DoomArchitect.Rendering;
using Godot;
using MapVector2 = System.Numerics.Vector2;

// Proof-of-concept for the "one live 3D scene, two cameras" idea: the
// top-down orthographic camera and the perspective camera look at the
// exact same scene, so switching views is just a camera swap.
public partial class MapView : Node3D
{
	// Godot render layers are 1-indexed bit positions; ceilings and walls
	// live on layer 2 so the top-down camera's cull mask can exclude them
	// (2D view shows the floor plan, not a jumble of wall/ceiling shading)
	// while the perspective camera (default cull mask, all layers) still
	// sees everything.
	private const uint ThreeDOnlyRenderLayer = 2;

	private readonly Dictionary<Sector, (MeshInstance3D Floor, MeshInstance3D Ceiling)> _sectorMeshes = new();
	private readonly Dictionary<Linedef, MeshInstance3D> _wallMeshes = new();
	private readonly Dictionary<Thing, MeshInstance3D> _thingMeshes = new();
	private readonly Dictionary<int, (ArrayMesh Mesh, StandardMaterial3D Material)> _thingTypeMeshes = new();
	private ArrayMesh _fallbackThingMesh;
	private StandardMaterial3D _fallbackThingMaterial;
	private IGameConfiguration _gameConfiguration = GameConfigurations.Get(GameConfigurationKind.Doom);

	private Camera3D _topDownCamera;
	private Camera3D _perspectiveCamera;
	private MapOverlay _overlay;
	private ModeToolbar _modeToolbar;
	private GridToolbar _gridToolbar;
	private StatusBar _statusBar;
	private MapData _map;
	private TextureCache _textureCache;
	private TextureSet _textureSet;
	private readonly TextureIconCache _textureIconCache = new();
	private const int TextureIconDecodeBudgetPerFrame = 8;
	private readonly SpriteIconCache _spriteIconCache = new();
	private const int SpriteIconDecodeBudgetPerFrame = 8;
	private IReadOnlyList<NamedResource> _namedResources = Array.Empty<NamedResource>();
	private UndoStack _undoStack = new();
	private bool _in3D;

	// Matches the sample room's own baked-in starting transform (floor 0,
	// camera Y 48 in Scenes/Main.tscn) - reused here rather than a new,
	// independently-chosen number, so a fresh sample-room session (no
	// mouse-over-a-sector placement possible yet) still lands at exactly
	// the same height it always has.
	private const double EyeHeightAboveFloor = 48;

	// "What am I looking at" 3D targeting - see MapRaycaster's own remarks
	// for why this is hand-rolled Core geometry rather than Godot physics.
	private const double PickIntervalSeconds = 0.08; // matches UDB's own 80ms PICK_INTERVAL
	private readonly IMapSpatialIndex _spatialIndex = new UniformGridSpatialIndex();
	private IMapTargetFinder _targetFinder;
	private TargetHighlight _targetHighlight;
	private Crosshair _crosshair;
	private MapTarget? _currentTarget;
	private double _timeSinceLastPick;

	/// <summary>
	/// 3D visual-mode selection - genuinely separate from the classic 2D
	/// selection (<c>Sector.IsSelected</c>/<c>Linedef.IsSelected</c>),
	/// matching UDB's own real model (its visual-mode wrapper objects
	/// carry their own local <c>selected</c> flag, distinct from
	/// <c>MapElement.Selected</c>). Bridged with the classic selection
	/// only at the moment of entering/leaving 3D mode (see the
	/// <c>toggle_2d_3d</c> handling in <see cref="_UnhandledInput"/>), not
	/// shared live the way an earlier version of this feature did.
	/// </summary>
	private readonly HashSet<Sector> _selectedSectors3D = new();
	private readonly HashSet<Linedef> _selectedLinedefs3D = new();
	private readonly HashSet<Thing> _selectedThings3D = new();

	public override void _Ready()
	{
		KeyBindings.Bootstrap();

		_topDownCamera = GetNode<Camera3D>("TopDownCamera");
		_perspectiveCamera = GetNode<Camera3D>("PerspectiveCamera");
		_overlay = GetNode<MapOverlay>("Overlay/MapOverlay");
		_modeToolbar = GetNode<ModeToolbar>("UI/TopBar/ToolbarMargin/TopToolbar");
		_modeToolbar.Overlay = _overlay;
		_gridToolbar = GetNode<GridToolbar>("UI/TopBar/ToolbarMargin/TopToolbar/GridToolbar");
		_gridToolbar.Overlay = _overlay;
		_statusBar = GetNode<StatusBar>("UI/StatusBar");
		_statusBar.Overlay = _overlay;

		var openMapMenu = GetNode<OpenMapMenu>("UI/OpenMapMenu");
		openMapMenu.MapLoaded += LoadMap;
		openMapMenu.MapResourcesChanged += RefreshResources;
		GetNode<MainMenuBar>("UI/TopBar/MenuBarPanel/MenuBar").Initialize(openMapMenu, _overlay);

		// No WAD is open yet - every texture/flat lookup just resolves to
		// the shared placeholder until a real map is loaded.
		_textureSet = TextureSet.CreateEmpty();
		_textureCache = new TextureCache(_textureSet);
		_textureIconCache.SeedAll(_textureSet);
		_spriteIconCache.SeedAll(_textureSet, _gameConfiguration.GetThingTypes().Select(t => t.SpriteName));

		// Reads _textureCache/_map fresh on every call rather than a
		// captured value, so this keeps working correctly across LoadMap
		// swapping both fields out from under it later.
		_targetFinder = new MapRaycaster(_spatialIndex, name => _textureCache.GetWallTextureSize(name).Y, ResolveThingPickBounds);
		_targetHighlight = new TargetHighlight
		{
			MiddleTextureHeightLookup = name => _textureCache.GetWallTextureSize(name).Y,
			ThingPickBoundsLookup = ResolveThingPickBounds,
		};
		AddChild(_targetHighlight);
		// Parented under the same screen-space overlay layer MapOverlay
		// already renders correctly through, rather than directly under
		// this Node3D - that layer is the proven place for 2D content to
		// draw on top of the 3D scene.
		_crosshair = new Crosshair { Visible = false };
		GetNode<Node>("Overlay").AddChild(_crosshair);

		_fallbackThingMesh = ThingMeshBuilder.BuildFallback();
		_fallbackThingMaterial = ThingMeshBuilder.BuildFallbackMaterial();

		_map = new MapData();
		var sector = BuildSampleSector(_map);
		CreateSectorMeshInstances(sector);
		foreach (var linedef in _map.Linedefs)
		{
			CreateWallMeshInstance(linedef);
		}

		foreach (var thing in _map.Things)
		{
			CreateThingMeshInstance(thing);
		}

		_overlay.Map = _map;
		_overlay.Camera = _topDownCamera;
		_overlay.UndoStack = _undoStack;
		_overlay.GameConfiguration = _gameConfiguration;
		_overlay.TextureSet = _textureSet;
		_overlay.TextureIconCache = _textureIconCache;
		_overlay.SpriteIconCache = _spriteIconCache;
		_overlay.NamedResources = _namedResources;

		if (CommandLineOptions.TryGetFileAndMap(out var cliFilePath, out var cliMapName))
		{
			openMapMenu.LoadFromCommandLine(cliFilePath, cliMapName);
		}
	}

	public override void _Process(double delta)
	{
		_textureIconCache.ProcessBudget(TextureIconDecodeBudgetPerFrame);
		_spriteIconCache.ProcessBudget(SpriteIconDecodeBudgetPerFrame);
		SyncMeshInstancesWithMap();

		foreach (var sector in _map.GetDirtySectors())
		{
			var mesh = SectorMeshBuilder.Build(sector);
			var instances = _sectorMeshes[sector];
			instances.Floor.Mesh = mesh.Floor;
			instances.Ceiling.Mesh = mesh.Ceiling;
			ApplyFlatMaterial(instances.Floor, sector.FloorTexture);
			ApplyFlatMaterial(instances.Ceiling, sector.CeilingTexture);

			// A wall's shape can depend on both of a linedef's sectors (a
			// two-sided step), so any linedef touching this sector needs
			// rebuilding too - including ones whose *other* side is what
			// changed, since this sector is dirty either way. Touching a
			// linedef shared by two dirty sectors in the same frame just
			// rebuilds it twice with an identical result - harmless.
			foreach (var sidedef in sector.Sidedefs)
			{
				RebuildWallMesh(sidedef.Linedef);
			}

			_map.ClearDirty(sector);
		}

		foreach (var thing in _map.GetDirtyThings())
		{
			var info = _gameConfiguration.GetThingType(thing.Type);
			_thingMeshes[thing].Position = thing.Position.ToWorld((float)ResolveThingWorldZ(thing, info));
			_map.ClearDirty(thing);
		}

		if (_in3D)
		{
			_timeSinceLastPick += delta;
			if (_timeSinceLastPick >= PickIntervalSeconds)
			{
				_timeSinceLastPick = 0;
				UpdateTarget();
			}
		}
	}

	/// <summary>
	/// Moves the perspective camera to wherever the mouse is hovering in
	/// the 2D view right as Tab is pressed, rather than leaving it at
	/// whatever fixed transform it last had (the sample room's own baked-
	/// in starting position the very first time - see <see cref="EyeHeightAboveFloor"/>'s
	/// own remarks - and afterwards just wherever it happened to be left
	/// after free-flying around) - almost never where the user actually
	/// wants to look once a real, unrelated map is loaded. Casts the same
	/// camera-ray-to-ground-plane unprojection <see cref="MapOverlayCamera.Unproject"/>
	/// itself uses (through <see cref="_topDownCamera"/> directly rather
	/// than through <see cref="MapOverlay"/>, which doesn't expose this as
	/// a public operation) to find the map position under the cursor, then
	/// only actually moves the camera if that position falls inside a real
	/// sector - leaves it exactly where it was otherwise (empty space
	/// outside the map, or the mouse simply not over the map view at all)
	/// rather than guessing. Horizontal position only changes to the
	/// cursor's own map position; height is that sector's own floor plus
	/// <see cref="EyeHeightAboveFloor"/>, not the camera's previous
	/// height, since reusing the old height could leave the camera
	/// embedded in the floor or ceiling of a sector at a very different
	/// elevation. Rotation is left untouched entirely.
	/// </summary>
	private void PlacePerspectiveCameraAtMouse()
	{
		var screenPosition = GetViewport().GetMousePosition();
		var origin = _topDownCamera.ProjectRayOrigin(screenPosition);
		var direction = _topDownCamera.ProjectRayNormal(screenPosition);
		var distanceToPlane = -origin.Y / direction.Y;
		var mapPosition = (origin + direction * distanceToPlane).ToDoom();

		var sector = SectorHitTest.FindContaining(_map.Sectors, mapPosition);
		if (sector == null) return;

		_perspectiveCamera.Position = mapPosition.ToWorld((float)(sector.FloorHeight + EyeHeightAboveFloor));
	}

	/// <summary>
	/// Re-picks whatever the perspective camera is currently looking at,
	/// throttled to <see cref="PickIntervalSeconds"/> rather than every
	/// frame (see MapRaycaster/UniformGridSpatialIndex's own remarks on
	/// why - directly adopted from UDB's own proven throttle). Rebuilds
	/// the highlight every tick regardless of whether the hover target's
	/// identity changed, since a selection-only change (from a click, with
	/// no hover-target change) needs to be reflected too - still cheap at
	/// this cadence for realistic selection sizes (see TargetHighlight's
	/// own remarks).
	/// </summary>
	private void UpdateTarget()
	{
		_spatialIndex.Rebuild(_map);

		var origin = _perspectiveCamera.GlobalPosition.ToDoom3D();
		var direction = (-_perspectiveCamera.GlobalTransform.Basis.Z).ToDoom3D();
		_currentTarget = _targetFinder.FindTarget(origin, direction);

		_targetHighlight.UpdateHighlights(_currentTarget, _selectedSectors3D, _selectedLinedefs3D, _selectedThings3D, new MapVector2(origin.X, origin.Y));
	}

	/// <summary>
	/// 3D visual-mode click-to-select: acts on whichever target
	/// <see cref="UpdateTarget"/> most recently found (kept fresh every
	/// <see cref="PickIntervalSeconds"/>), at Sector/Linedef/Thing
	/// granularity - clicking any part of a wall selects its whole
	/// Linedef, any part of a floor/ceiling selects its whole Sector, a
	/// Thing selects itself. A plain click always toggles (adds if
	/// unselected, removes if selected) - UDB's own real visual-mode click
	/// (<c>BaseVisualGeometrySector.OnSelectEnd</c>/
	/// <c>BaseVisualGeometrySidedef.OnSelectEnd</c>/
	/// <c>BaseVisualThing.OnSelectEnd</c>) does the identical toggle, with
	/// no modifier needed. Touches only the local 3D selection
	/// (<see cref="_selectedSectors3D"/>/<see cref="_selectedLinedefs3D"/>/
	/// <see cref="_selectedThings3D"/>), not the classic 2D selection -
	/// they're bridged only on entering/leaving 3D mode, matching UDB's
	/// real separate-selection model. Unlike 2D, there's no "current mode"
	/// restricting which type can be selected - clicking a floor then a
	/// wall then a Thing naturally builds a mixed selection, matching
	/// UDB's real visual-mode behavior.
	/// </summary>
	private void HandleThreeDSelectClick()
	{
		if (_currentTarget is { } target)
		{
			if (target.Kind == TargetSurfaceKind.Wall)
			{
				var linedef = target.WallSegment!.Value.Side.Linedef;
				if (!_selectedLinedefs3D.Remove(linedef)) _selectedLinedefs3D.Add(linedef);
			}
			else if (target.Kind == TargetSurfaceKind.Thing)
			{
				var thing = target.Thing!;
				if (!_selectedThings3D.Remove(thing)) _selectedThings3D.Add(thing);
			}
			else
			{
				if (!_selectedSectors3D.Remove(target.Sector!)) _selectedSectors3D.Add(target.Sector!);
			}
		}
		else
		{
			_selectedSectors3D.Clear();
			_selectedLinedefs3D.Clear();
			_selectedThings3D.Clear();
		}
	}

	/// <summary>
	/// UDB's own real visual-mode right-click ("visualedit", bound to the
	/// right mouse button by default, exactly like classic mode's own
	/// "classicedit") - opens the properties dialog for whichever element
	/// *type* is currently targeted (<c>BaseVisualGeometrySector.OnEditEnd</c>'s
	/// own real <c>ShowEditSectors</c> for a floor/ceiling,
	/// <c>BaseVisualGeometrySidedef.OnEditEnd</c>'s own real
	/// <c>ShowEditLinedefs</c> for a wall), for the *whole* current 3D-mode
	/// selection of that type if any - matching UDB's real
	/// <c>GetSelectedObjects</c> fallback exactly: only when nothing of
	/// that type is selected does it fall back to acting on just the
	/// targeted element. Which dialog opens is decided purely by what's
	/// under the crosshair, not by which selection happens to be
	/// non-empty - aiming at a wall always means Linedef properties, even
	/// with sectors also selected elsewhere, and vice versa.
	///
	/// Releases the mouse capture 3D mode holds (matching
	/// <see cref="FreeFlyCamera"/>'s own already-established Escape/
	/// left-click capture toggle) *before* popping the dialog - a popup
	/// opened while the OS cursor is still captured/hidden would be
	/// unreachable to actually click. Left-clicking back into the 3D view
	/// afterward re-captures it again, the same existing gesture already
	/// used to resume after Escape.
	/// </summary>
	private void HandleThreeDEditClick()
	{
		if (_currentTarget is not { } target) return;

		Input.MouseMode = Input.MouseModeEnum.Visible;

		if (target.Kind == TargetSurfaceKind.Wall)
		{
			var linedefs = _selectedLinedefs3D.Count > 0
				? _selectedLinedefs3D.ToList()
				: new List<Linedef> { target.WallSegment!.Value.Side.Linedef };
			_overlay.RaiseEditLinedefsRequested(linedefs);
		}
		else if (target.Kind == TargetSurfaceKind.Thing)
		{
			var things = _selectedThings3D.Count > 0 ? _selectedThings3D.ToList() : new List<Thing> { target.Thing! };
			_overlay.RaiseEditThingsRequested(things);
		}
		else
		{
			var sectors = _selectedSectors3D.Count > 0 ? _selectedSectors3D.ToList() : new List<Sector> { target.Sector! };
			_overlay.RaiseEditSectorsRequested(sectors);
		}
	}

	/// <summary>
	/// UDB's own real visual-mode plain-mouse-wheel default binding
	/// (<c>raisesector8</c>/<c>lowersector8</c>, verified directly against
	/// its default keybind config - the numeric action-key values there
	/// decode into "plain wheel" for the unmodified 8-unit raise/lower,
	/// "wheel+Shift" for a 1-unit fine adjustment, "wheel+Ctrl" for
	/// brightness instead, the latter two not ported here since the user's
	/// own request was specifically the plain-wheel case): raises or
	/// lowers whichever *specific* surface - floor or ceiling, exactly
	/// matching <see cref="TargetSurfaceKind"/> - is currently targeted,
	/// by <paramref name="amount"/> map units, undoably. A wall target is
	/// deliberately left untouched, matching the user's own explicit
	/// scope ("if we're pointing at a ceiling or a floor").
	///
	/// Unlike the edit-click above, this deliberately does *not* extend to
	/// the whole 3D-mode selection - <see cref="_selectedSectors3D"/> only
	/// ever tracks *which sectors* are selected, not separately *which
	/// surface* of each (UDB's own real per-surface
	/// <c>BaseVisualGeometrySector</c> objects do track that distinction,
	/// letting a multi-select scroll raise several floors *and* ceilings
	/// together correctly) - always acting on just the live target avoids
	/// that ambiguity entirely rather than guessing. Each wheel notch is
	/// also its own separate undo step, unlike UDB's own real
	/// <c>UndoGroup</c>-based coalescing of a rapid scroll gesture into
	/// one - this project's own <see cref="UndoStack"/> has no such
	/// merging mechanism yet, a deliberately small, flagged simplification
	/// rather than a new general-purpose one built just for this.
	/// </summary>
	private void AdjustTargetHeight(double amount)
	{
		if (_currentTarget is not { } target) return;
		if (target.Kind is TargetSurfaceKind.Wall or TargetSurfaceKind.Thing) return;

		var sector = target.Sector!;
		ICommand command = target.Kind == TargetSurfaceKind.Floor
			? new SetPropertyCommand<Sector, double>(sector, (s, v) => s.FloorHeight = v, sector.FloorHeight, sector.FloorHeight + amount, s => _map.MarkDirty(s))
			: new SetPropertyCommand<Sector, double>(sector, (s, v) => s.CeilingHeight = v, sector.CeilingHeight, sector.CeilingHeight + amount, s => _map.MarkDirty(s));

		_undoStack.Execute(command);
	}

	/// <summary>
	/// UDB's own real texture-offset-nudge keybinds
	/// (<c>movetextureleft</c>/<c>right</c>/<c>up</c>/<c>down</c>, plain/
	/// <c>*8</c>/<c>*gs</c> variants, verified against its own default
	/// keybind config): plain arrow = 1 pixel, Alt+arrow = 8 pixels,
	/// Ctrl+arrow = the current grid size (<see cref="MapOverlay.GridSize"/> -
	/// not a fixed step, matches UDB's own real behavior for the step sizes
	/// themselves). The 8-pixel modifier is Alt here, not UDB's own real
	/// Shift - Shift is this project's own pre-existing
	/// <c>FreeFlyCamera</c> fly-down control (polled every frame,
	/// independent of whatever else is happening with the key), and a real
	/// collision surfaced the same day this landed: holding Shift+Arrow to
	/// nudge also silently drifted the camera downward. Reassigning
	/// fly-down to a fresh key was tried first and reverted at the user's
	/// own request - Shift stays fly-down, unchanged from before this
	/// feature existed, and the nudge modifier moved instead. Always
	/// writes the targeted wall's own *per-part* UDMF offset field
	/// (<c>offsetx_&lt;part&gt;</c>/<c>offsety_&lt;part&gt;</c>, resolved via
	/// <see cref="WallSegment.PartKind"/> - see <see cref="LinedefWallBuilder.PartSuffix"/>'s
	/// own remarks), never the shared <see cref="Sidedef.OffsetX"/>/
	/// <see cref="Sidedef.OffsetY"/> - matches every one of UDB's own real
	/// <c>VisualUpper</c>/<c>VisualLower</c>/<c>VisualMiddleSingle</c>/
	/// <c>VisualMiddleDouble.MoveTextureOffset</c> overrides, which all do
	/// the identical thing. Same "act on just the live target, never the
	/// whole selection" scope as <see cref="AdjustTargetHeight"/> above,
	/// for the identical reason (this project's selection model tracks
	/// *which linedefs* are selected, not separately *which wall part* of
	/// each).
	///
	/// Explicitly bypasses <c>SetFieldCommand</c>'s lack of a dirty-marking
	/// callback (unlike <see cref="SetPropertyCommand{T,TValue}"/>, which
	/// has one) by marking the sector dirty directly right after executing -
	/// this needs to actually redraw live as the key is held, unlike some
	/// of this codebase's other <c>SetFieldCommand</c> call sites (e.g.
	/// <c>LinedefEditDialog</c>'s own per-part offset/scale fields, which
	/// don't mark dirty at all - a real, separate, pre-existing gap, not
	/// something to silently inherit here).
	/// </summary>
	/// <summary>Which nudge action fired - a semantic direction rather than a literal <see cref="Key"/>, since <c>texture_nudge_left</c>/etc. are independently rebindable and might not even be bound to arrow keys anymore.</summary>
	private enum NudgeDirection { Left, Right, Up, Down }

	private void HandleTextureNudge(NudgeDirection direction, bool alt, bool ctrl)
	{
		if (_currentTarget is not { Kind: TargetSurfaceKind.Wall } target) return;

		var segment = target.WallSegment!.Value;
		var side = segment.Side;
		var suffix = LinedefWallBuilder.PartSuffix(segment.PartKind);

		double delta = alt ? 8 : ctrl ? _overlay.GridSize : 1;
		double dx = direction switch { NudgeDirection.Left => -delta, NudgeDirection.Right => delta, _ => 0 };
		// Up needs +delta, Down needs -delta to read the way a mapper
		// expects - confirmed live. Never camera-relative: a wall is
		// always vertical and this project's camera never rolls, so
		// world-up is unambiguous regardless of which way you're facing,
		// unlike X below.
		double dy = direction switch { NudgeDirection.Up => delta, NudgeDirection.Down => -delta, _ => 0 };
		if (dx == 0 && dy == 0) return;

		// X, unlike Y, *is* camera-relative: "Left"/"Right" should always
		// shift the texture left/right as seen on screen right now, not in
		// the wall's own fixed Start->End coordinate space - a fixed sign
		// (tried first) is only ever right for walls that happen to run
		// the same way relative to the camera as whatever wall it was
		// tuned against, and wrong for others (a wall in a different room,
		// or the same wall viewed from the opposite end of it). Flip the
		// base UDB-literal sign (`Right` = <c>+delta</c> in the wall's own
		// direction) whenever that direction actually points toward the
		// camera's own screen-left instead of screen-right.
		if (dx != 0)
		{
			var wallDirection = segment.End.Position - segment.Start.Position;
			var cameraRight = _perspectiveCamera.GlobalTransform.Basis.X.ToDoom();
			// Confirmed 100% inverted from correct once actually tried
			// live, consistently rather than intermittently - a plain
			// polarity flip of the whole check, not a deeper bug in which
			// cases get flipped.
			if (wallDirection != MapVector2.Zero && cameraRight != MapVector2.Zero && MapVector2.Dot(wallDirection, cameraRight) > 0)
			{
				dx = -dx;
			}
		}

		var textureSize = _textureCache.GetWallTextureSize(segment.Texture);
		var commands = new List<ICommand>();

		if (dx != 0)
		{
			var oldX = side.Fields.GetFloat($"offsetx_{suffix}", 0.0);
			var newX = TextureOffsetMath.Nudge(oldX, dx, textureSize.X);
			commands.Add(new SetFieldCommand(side.Fields, $"offsetx_{suffix}", newX == 0 ? null : new UniValue(UniversalType.Float, newX)));
		}

		if (dy != 0)
		{
			var oldY = side.Fields.GetFloat($"offsety_{suffix}", 0.0);
			var newY = TextureOffsetMath.Nudge(oldY, dy, textureSize.Y);
			commands.Add(new SetFieldCommand(side.Fields, $"offsety_{suffix}", newY == 0 ? null : new UniValue(UniversalType.Float, newY)));
		}

		_undoStack.Execute(commands.Count == 1 ? commands[0] : new CommandGroup(commands));
		_map.MarkDirty(side.Sector);
	}

	/// <summary>
	/// UDB's own real texture auto-align (<c>visualautoalign</c>/<c>x</c>/
	/// <c>y</c>, see <see cref="TextureAutoAligner"/>'s own remarks for the
	/// real algorithm this ports) - starts from the currently targeted wall
	/// part and flood-fills outward, so this deliberately reads
	/// <see cref="_currentTarget"/> fresh rather than the 3D-mode selection,
	/// same reasoning as <see cref="HandleTextureNudge"/> above.
	/// </summary>
	private void HandleTextureAutoAlign(bool alignX, bool alignY)
	{
		if (_currentTarget is not { Kind: TargetSurfaceKind.Wall } target) return;

		var segment = target.WallSegment!.Value;
		var results = TextureAutoAligner.Align(
			segment.Side, segment.PartKind, alignX, alignY,
			name => _textureCache.GetWallTextureSize(name).X, name => _textureCache.GetWallTextureSize(name).Y);
		if (results.Count == 0) return;

		var suffix = LinedefWallBuilder.PartSuffix(segment.PartKind);
		var commands = new List<ICommand>();
		var dirtySectors = new HashSet<Sector>();

		foreach (var result in results)
		{
			if (result.OffsetX != null)
			{
				commands.Add(new SetFieldCommand(result.Side.Fields, $"offsetx_{suffix}", result.OffsetX == 0 ? null : new UniValue(UniversalType.Float, result.OffsetX.Value)));
			}

			if (result.OffsetY != null)
			{
				commands.Add(new SetFieldCommand(result.Side.Fields, $"offsety_{suffix}", result.OffsetY == 0 ? null : new UniValue(UniversalType.Float, result.OffsetY.Value)));
			}

			dirtySectors.Add(result.Side.Sector);
		}

		if (commands.Count == 0) return;

		_undoStack.Execute(new CommandGroup(commands));
		foreach (var sector in dirtySectors) _map.MarkDirty(sector);
	}

	/// <summary>
	/// Swaps in a freshly loaded map: tears down every existing sector's
	/// meshes and rebuilds from scratch, resets undo history (the old
	/// stack's commands still close over the discarded MapData, so they'd
	/// be pointless - see TODO.md), and refits the top-down camera since a
	/// real loaded map is very unlikely to sit in the same 256x256 area
	/// the sample room did.
	/// </summary>
	private void LoadMap(MapData newMap, TextureSet textures, IGameConfiguration gameConfiguration, IReadOnlyList<NamedResource> namedResources)
	{
		_textureCache = new TextureCache(textures);
		_textureSet = textures;
		_textureIconCache.SeedAll(textures);
		_namedResources = namedResources;
		_gameConfiguration = gameConfiguration;
		_spriteIconCache.SeedAll(textures, _gameConfiguration.GetThingTypes().Select(t => t.SpriteName));
		_map = newMap;

		RebuildAllMeshes();

		_undoStack = new UndoStack();
		_overlay.UndoStack = _undoStack;

		FitTopDownCameraToMap(newMap);
	}

	/// <summary>
	/// A user revisited Map Options for the map that's already loaded
	/// (e.g. finally pointed at the IWAD) rather than loading a different
	/// one - rebuilds every mesh against the new textures/game
	/// configuration exactly like <see cref="LoadMap"/> does, but
	/// deliberately does *not* touch undo history or the camera, since the
	/// map itself (<see cref="_map"/>) hasn't actually changed.
	/// </summary>
	public void RefreshResources(TextureSet textures, IGameConfiguration gameConfiguration, IReadOnlyList<NamedResource> namedResources)
	{
		_textureCache = new TextureCache(textures);
		_textureSet = textures;
		_textureIconCache.SeedAll(textures);
		_namedResources = namedResources;
		_gameConfiguration = gameConfiguration;
		_spriteIconCache.SeedAll(textures, _gameConfiguration.GetThingTypes().Select(t => t.SpriteName));

		RebuildAllMeshes();
	}

	/// <summary>
	/// Keeps <see cref="_sectorMeshes"/>/<see cref="_wallMeshes"/>/
	/// <see cref="_thingMeshes"/> in sync with whatever <see cref="_map"/>
	/// currently actually contains - a gap that never mattered before
	/// Draw mode (Sector/Linedef) and <see cref="CreateThingCommand"/>
	/// (Thing), since until now every element of a live map was already
	/// known about from the initial <see cref="LoadMap"/>/
	/// <see cref="RebuildAllMeshes"/> pass; nothing ever added or removed
	/// one at runtime. <see cref="DrawLoopCommand"/>'s/<see cref="CreateThingCommand"/>'s
	/// own <c>Do</c>/<c>Undo</c> now does both, so this frame-by-frame
	/// catch-up is what actually gives a freshly drawn sector or freshly
	/// placed Thing its mesh (real bug: the very first version of Draw
	/// mode crashed with a <see cref="KeyNotFoundException"/> here, since
	/// nothing ever created the new sector's dictionary entry at all) and
	/// cleans up a since-undone one's mesh instances instead of leaving
	/// them orphaned in the scene tree. A cheap count comparison first, so
	/// the O(n) diff below only ever runs on the rare frame right after a
	/// structural change, not every frame.
	/// </summary>
	private void SyncMeshInstancesWithMap()
	{
		if (_map.Sectors.Count != _sectorMeshes.Count) SyncSectorMeshes();
		if (_map.Linedefs.Count != _wallMeshes.Count) SyncWallMeshes();
		if (_map.Things.Count != _thingMeshes.Count) SyncThingMeshes();
	}

	private void SyncSectorMeshes()
	{
		var live = new HashSet<Sector>(_map.Sectors);
		var removed = _sectorMeshes.Keys.Where(s => !live.Contains(s)).ToList();

		foreach (var sector in removed)
		{
			var (floor, ceiling) = _sectorMeshes[sector];
			floor.QueueFree();
			ceiling.QueueFree();
			_sectorMeshes.Remove(sector);
		}

		if (removed.Count > 0) ClearStaleThreeDReferences(removed, Array.Empty<Linedef>(), Array.Empty<Thing>());

		foreach (var sector in _map.Sectors)
		{
			if (!_sectorMeshes.ContainsKey(sector)) CreateSectorMeshInstances(sector);
		}
	}

	private void SyncWallMeshes()
	{
		var live = new HashSet<Linedef>(_map.Linedefs);
		var removed = _wallMeshes.Keys.Where(l => !live.Contains(l)).ToList();

		foreach (var linedef in removed)
		{
			_wallMeshes[linedef].QueueFree();
			_wallMeshes.Remove(linedef);
		}

		if (removed.Count > 0) ClearStaleThreeDReferences(Array.Empty<Sector>(), removed, Array.Empty<Thing>());

		foreach (var linedef in _map.Linedefs)
		{
			if (!_wallMeshes.ContainsKey(linedef)) CreateWallMeshInstance(linedef);
		}
	}

	private void SyncThingMeshes()
	{
		var live = new HashSet<Thing>(_map.Things);
		var removed = _thingMeshes.Keys.Where(t => !live.Contains(t)).ToList();

		foreach (var thing in removed)
		{
			_thingMeshes[thing].QueueFree();
			_thingMeshes.Remove(thing);
		}

		if (removed.Count > 0) ClearStaleThreeDReferences(Array.Empty<Sector>(), Array.Empty<Linedef>(), removed);

		foreach (var thing in _map.Things)
		{
			if (!_thingMeshes.ContainsKey(thing)) CreateThingMeshInstance(thing);
		}
	}

	/// <summary>
	/// A removed Sector/Linedef/Thing can still be referenced by the
	/// 3D-mode target/selection (independent of the classic 2D selection -
	/// see their own remarks) - drops just those specific stale references
	/// rather than a blanket clear, so undoing a drawn sector doesn't also
	/// wipe an unrelated in-progress 3D selection.
	/// </summary>
	private void ClearStaleThreeDReferences(
		IReadOnlyCollection<Sector> removedSectors, IReadOnlyCollection<Linedef> removedLinedefs, IReadOnlyCollection<Thing> removedThings)
	{
		if (_currentTarget is { } target)
		{
			var targetLinedef = target.WallSegment?.Side.Linedef;
			var isStale = (target.Sector != null && removedSectors.Contains(target.Sector))
				|| (targetLinedef != null && removedLinedefs.Contains(targetLinedef))
				|| (target.Thing != null && removedThings.Contains(target.Thing));
			if (isStale)
			{
				_currentTarget = null;
				_targetHighlight.UpdateHighlights(null, _selectedSectors3D, _selectedLinedefs3D, _selectedThings3D, MapVector2.Zero);
			}
		}

		_selectedSectors3D.ExceptWith(removedSectors);
		_selectedLinedefs3D.ExceptWith(removedLinedefs);
		_selectedThings3D.ExceptWith(removedThings);
	}

	/// <summary>Tears down and rebuilds every sector/wall/thing mesh against the current <see cref="_map"/>/<see cref="_textureCache"/>/<see cref="_gameConfiguration"/> - the part <see cref="LoadMap"/> and <see cref="RefreshResources"/> share.</summary>
	private void RebuildAllMeshes()
	{
		foreach (var (floor, ceiling) in _sectorMeshes.Values)
		{
			floor.QueueFree();
			ceiling.QueueFree();
		}

		_sectorMeshes.Clear();

		foreach (var wall in _wallMeshes.Values)
		{
			wall.QueueFree();
		}

		_wallMeshes.Clear();

		foreach (var thing in _thingMeshes.Values)
		{
			thing.QueueFree();
		}

		_thingMeshes.Clear();
		_thingTypeMeshes.Clear();

		// The old target - and the local 3D selection, which holds its own
		// Sector/Linedef/Thing references independent of the classic 2D
		// selection - may reference meshes being discarded here; never
		// carry any of them across a rebuild.
		_currentTarget = null;
		_selectedSectors3D.Clear();
		_selectedLinedefs3D.Clear();
		_selectedThings3D.Clear();
		_targetHighlight.UpdateHighlights(null, _selectedSectors3D, _selectedLinedefs3D, _selectedThings3D, MapVector2.Zero);

		foreach (var sector in _map.Sectors)
		{
			CreateSectorMeshInstances(sector);
		}

		foreach (var linedef in _map.Linedefs)
		{
			CreateWallMeshInstance(linedef);
		}

		foreach (var thing in _map.Things)
		{
			CreateThingMeshInstance(thing);
		}

		_overlay.Map = _map;
		_overlay.GameConfiguration = _gameConfiguration;
		_overlay.TextureSet = _textureSet;
		_overlay.TextureIconCache = _textureIconCache;
		_overlay.SpriteIconCache = _spriteIconCache;
		_overlay.NamedResources = _namedResources;
	}

	private void FitTopDownCameraToMap(MapData map)
	{
		if (map.Vertices.Count == 0) return;

		var min = map.Vertices[0].Position;
		var max = min;
		foreach (var vertex in map.Vertices)
		{
			min = MapVector2.Min(min, vertex.Position);
			max = MapVector2.Max(max, vertex.Position);
		}

		var center = (min + max) / 2f;
		var size = Mathf.Max(max.X - min.X, max.Y - min.Y) * 1.2f;

		// Goes through ToWorld rather than constructing the position by
		// hand, so this can't independently drift from the shared Doom ->
		// Godot mapping the way it once did (see ToWorld's own remarks).
		_topDownCamera.Position = center.ToWorld(_topDownCamera.Position.Y);
		_topDownCamera.Size = Mathf.Max(size, 64f);
	}

	private void CreateSectorMeshInstances(Sector sector)
	{
		var mesh = SectorMeshBuilder.Build(sector);
		var floor = new MeshInstance3D { Mesh = mesh.Floor };
		var ceiling = new MeshInstance3D { Mesh = mesh.Ceiling, Layers = ThreeDOnlyRenderLayer };
		ApplyFlatMaterial(floor, sector.FloorTexture);
		ApplyFlatMaterial(ceiling, sector.CeilingTexture);
		AddChild(floor);
		AddChild(ceiling);
		_sectorMeshes[sector] = (floor, ceiling);
	}

	/// <summary>
	/// "-" is the map-format sentinel for "no texture" - not a genuinely
	/// missing/unresolvable name, so it deliberately skips TextureCache
	/// entirely (leaving Godot's own default material) instead of showing
	/// the placeholder that's reserved for an actually-unresolvable name.
	/// A sector with no closed boundary yet (e.g. mid-edit, before its
	/// linedefs form a full loop) makes SectorMeshBuilder produce a mesh
	/// with zero surfaces - nothing to put a material on, so this skips
	/// rather than letting Godot throw on an out-of-bounds surface index.
	/// </summary>
	private void ApplyFlatMaterial(MeshInstance3D instance, string textureName)
	{
		if (textureName == "-") return;
		if (instance.Mesh == null || instance.Mesh.GetSurfaceCount() == 0) return;
		instance.SetSurfaceOverrideMaterial(0, _textureCache.GetFlatMaterial(textureName));
	}

	private void CreateWallMeshInstance(Linedef linedef)
	{
		var instance = new MeshInstance3D { Layers = ThreeDOnlyRenderLayer };
		AddChild(instance);
		_wallMeshes[linedef] = instance;
		RebuildWallMesh(linedef);
	}

	private void RebuildWallMesh(Linedef linedef)
	{
		var instance = _wallMeshes[linedef];
		var result = WallMeshBuilder.Build(linedef, _textureCache);
		instance.Mesh = result.Mesh;

		for (var i = 0; i < result.SurfaceTextures.Count; i++)
		{
			// Same "-" skip as ApplyFlatMaterial - see its remarks.
			if (result.SurfaceTextures[i] == "-") continue;
			var material = result.SurfaceIsMasked[i]
				? _textureCache.GetMaskedWallMaterial(result.SurfaceTextures[i])
				: _textureCache.GetWallMaterial(result.SurfaceTextures[i]);
			instance.SetSurfaceOverrideMaterial(i, material);
		}
	}

	/// <summary>
	/// Creates a Thing's mesh instance once at load time; subsequent moves
	/// (Things edit mode) resync its position via the dirty-things loop in
	/// <see cref="_Process"/> instead of recreating it - see
	/// <see cref="ResolveThingWorldZ"/>, shared by both paths.
	/// </summary>
	private void CreateThingMeshInstance(Thing thing)
	{
		var (mesh, material, _) = ResolveThingMeshAndMaterial(thing.Type);
		var worldZ = ResolveThingWorldZ(thing, _gameConfiguration.GetThingType(thing.Type));

		var instance = new MeshInstance3D
		{
			Mesh = mesh,
			Position = thing.Position.ToWorld((float)worldZ),
			Layers = ThreeDOnlyRenderLayer,
		};
		instance.SetSurfaceOverrideMaterial(0, material);

		AddChild(instance);
		_thingMeshes[thing] = instance;
	}

	/// <summary>
	/// A hanging Thing (e.g. a ceiling-attached decoration) measures
	/// <see cref="Thing.Height"/> down from its containing sector's
	/// ceiling instead of up from its floor - shared by
	/// <see cref="CreateThingMeshInstance"/>, the dirty-things resync in
	/// <see cref="_Process"/>, and <see cref="ResolveThingPickBounds"/>'s
	/// own fallback, so a moved Thing's height stays correct even if it
	/// crosses into a different sector.
	///
	/// Also clamps against the *opposite* surface using the type's own
	/// real collision height (<paramref name="info"/> - matches UDB's own
	/// real <c>BaseVisualThing</c> Z-position resolution exactly, verified
	/// directly against source): a floor-standing Thing whose own
	/// mapper-set <see cref="Thing.Height"/> offset (or a tall type in a
	/// low room) would push it above <c>ceiling - info.Height</c> gets
	/// pulled back down to it instead (never below its own floor, for a
	/// sector too short to fit it at all); a ceiling-hanging Thing is the
	/// mirror image, clamped against the floor. Missing before this fix -
	/// a real, previously-caught bug: an unclamped Thing could sit
	/// partially or fully inside solid floor/ceiling geometry, both
	/// looking wrong *and* becoming unhoverable in 3D (its own pick box,
	/// buried behind the very floor/ceiling surface a ray would hit
	/// first, can never win against it). UDB's own <c>AbsoluteZ</c>/
	/// <c>nointeraction</c>/special-DoomEdNum-9500-9501 branches aren't
	/// modeled - no per-type <c>AbsoluteZ</c> flag or special-actor
	/// concept exists here yet, so every Thing always gets the same real
	/// clamped floor/ceiling-relative treatment.
	/// </summary>
	private double ResolveThingWorldZ(Thing thing, ThingTypeInfo info)
	{
		var containingSector = SectorHitTest.FindContaining(_map.Sectors, thing.Position);
		var floor = containingSector?.FloorHeight ?? 0;
		var ceiling = containingSector?.CeilingHeight ?? 0;
		var collisionHeight = info?.Height ?? ThingMeshBuilder.FallbackHeight;

		if (info?.Hangs ?? false)
		{
			var z = ceiling - collisionHeight;
			if (thing.Height > 0) z -= thing.Height;
			if (z < floor) z = Math.Min(floor, ceiling - collisionHeight);
			return z;
		}
		else
		{
			var z = floor;
			if (thing.Height > 0) z += thing.Height;
			var maxZ = ceiling - collisionHeight;
			if (z > maxZ) z = Math.Max(floor, maxZ);
			return z;
		}
	}

	/// <summary>
	/// The real pick box <see cref="MapRaycaster"/>/<see cref="TargetHighlight"/>
	/// need for a Thing (see <see cref="ThingPickBounds"/>'s own remarks) -
	/// its type's own real radius/height (the same fallback dimensions
	/// <see cref="ThingMeshBuilder"/> uses for its rendered mesh when the
	/// type's unrecognized, for consistency between what's drawn and
	/// what's clickable) plus the same floor-standing/ceiling-hanging
	/// world Z its rendered billboard already uses - read directly off
	/// that already-positioned mesh instance rather than recomputed via
	/// <see cref="ResolveThingWorldZ"/> (and the <see cref="SectorHitTest.FindContaining"/>
	/// it needs) from scratch. That recompute is genuinely expensive -
	/// <see cref="SectorHitTest.FindContaining"/>'s own remarks document it
	/// as a brute-force scan meant to run "once per Thing at load/rebuild
	/// time, never per-frame" - and this method runs once per *candidate*
	/// Thing on every ~80ms pick tick (<see cref="MapRaycaster.FindTarget"/>),
	/// which is exactly the per-frame-ish hot path that contract warns
	/// against: a real, caught-immediately performance regression (a
	/// visibly laggy 3D view with more than a handful of Things around)
	/// from the first version of this method, which called both directly.
	/// <see cref="_thingMeshes"/>'s own position is already correct at
	/// this point for exactly the same reason it's safe for rendering -
	/// the dirty-things sync loop in <see cref="_Process"/> keeps it so
	/// the moment a Thing actually needs it recomputed, not fresh on every
	/// read. <see cref="ThingPickBounds.Sector"/> is left <c>null</c> -
	/// nothing in this project currently reads a Thing target's own
	/// containing sector, so there's no reason to pay for it at all here.
	/// </summary>
	private ThingPickBounds ResolveThingPickBounds(Thing thing)
	{
		var info = _gameConfiguration.GetThingType(thing.Type);
		var radius = info?.Radius ?? ThingMeshBuilder.FallbackRadius;
		var height = info?.Height ?? ThingMeshBuilder.FallbackHeight;

		var worldZ = _thingMeshes.TryGetValue(thing, out var instance)
			? instance.Position.Y
			: ResolveThingWorldZ(thing, info);

		return new ThingPickBounds(radius, height, worldZ, Sector: null);
	}

	/// <summary>
	/// Resolves a Thing type's real per-type mesh/material, built once per
	/// distinct DoomEd number and cached (every instance of the same type
	/// looks identical). Falls back to the generic placeholder when the
	/// type is unrecognized or its real sprite isn't present in the
	/// currently loaded WAD - see <see cref="ThingMeshBuilder"/>'s own
	/// remarks for why that's an expected, common case, not a bug.
	/// </summary>
	private (ArrayMesh Mesh, StandardMaterial3D Material, bool Hangs) ResolveThingMeshAndMaterial(int doomEdNum)
	{
		var info = _gameConfiguration.GetThingType(doomEdNum);
		if (info == null) return (_fallbackThingMesh, _fallbackThingMaterial, false);

		if (_thingTypeMeshes.TryGetValue(doomEdNum, out var cached))
		{
			return (cached.Mesh, cached.Material, info.Hangs);
		}

		var sprite = _textureCache.TryGetSpriteEntry(info.SpriteName);
		if (sprite == null) return (_fallbackThingMesh, _fallbackThingMaterial, info.Hangs);

		// Sized from the sprite's own real pixel dimensions/offsets, not
		// info.Radius/info.Height - those are gameplay collision values,
		// not the sprite art's actual proportions (see ThingMeshBuilder's
		// remarks); using them to size the quad stretched or squished any
		// sprite whose aspect ratio didn't happen to match "2*radius : height".
		var mesh = ThingMeshBuilder.BuildSprite(sprite.Value.Size.X, sprite.Value.Size.Y, sprite.Value.Offset.X, sprite.Value.Offset.Y);
		_thingTypeMeshes[doomEdNum] = (mesh, sprite.Value.Material);
		return (mesh, sprite.Value.Material, info.Hangs);
	}

	/// <summary>
	/// Every keyboard action here is a real, independently rebindable
	/// <see cref="KeyBindingRegistry"/> entry (see TODO.md's "Keybinding
	/// management" writeup) - <see cref="KeyBindings.Bootstrap"/> registers
	/// each one's default binding (or a saved user override) with Godot's
	/// own <see cref="InputMap"/> before this method can ever run.
	/// Deliberately matched with <c>exactMatch: false</c> (Godot's own
	/// default) everywhere, not <c>true</c>: every site here already only
	/// checks the *specific* modifier(s) it cares about and tolerates any
	/// other modifier being incidentally also held, exactly like the raw
	/// <c>key.CtrlPressed</c>/<c>key.ShiftPressed</c> checks this replaced
	/// did - <c>exactMatch: true</c> would be a real, if subtle, behavior
	/// change (rejecting e.g. Ctrl+Alt+Z), not a faithful migration of it.
	/// </summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (_in3D && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
		{
			HandleThreeDSelectClick();
			return;
		}

		if (_in3D && @event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
		{
			HandleThreeDEditClick();
			return;
		}

		if (_in3D && @event is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true })
		{
			AdjustTargetHeight(8);
			return;
		}

		if (_in3D && @event is InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true })
		{
			AdjustTargetHeight(-8);
			return;
		}

		// Texture-offset nudging (UDB's own real movetextureleft/right/up/down*
		// actions all set `repeat = true` - held-down-arrow-keeps-nudging is
		// the real behavior, not a single-shot press) - handled here, before
		// the `Echo: false` filter every other keyboard action goes through
		// below, same reason the mouse-wheel height adjustment above isn't
		// gated by it either.
		if (_in3D && @event is InputEventKey { Pressed: true } nudgeKey)
		{
			// allowEcho: true - these are the one place in this method that
			// deliberately reads a held-down key repeatedly (UDB's own real
			// `repeat = true` on movetextureleft/right/up/down*), unlike
			// IsActionPressed's own default of rejecting OS key-repeat.
			NudgeDirection? direction = nudgeKey switch
			{
				_ when nudgeKey.IsActionPressed("texture_nudge_left", allowEcho: true) => NudgeDirection.Left,
				_ when nudgeKey.IsActionPressed("texture_nudge_right", allowEcho: true) => NudgeDirection.Right,
				_ when nudgeKey.IsActionPressed("texture_nudge_up", allowEcho: true) => NudgeDirection.Up,
				_ when nudgeKey.IsActionPressed("texture_nudge_down", allowEcho: true) => NudgeDirection.Down,
				_ => null,
			};

			if (direction != null)
			{
				HandleTextureNudge(
					direction.Value,
					Input.IsActionPressed("texture_nudge_amount_x8_modifier"),
					Input.IsActionPressed("texture_nudge_amount_grid_modifier"));
				return;
			}
		}

		if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

		if (key.IsActionPressed("undo"))
		{
			_undoStack.Undo();
			return;
		}

		if (key.IsActionPressed("redo"))
		{
			_undoStack.Redo();
			return;
		}

		if (_in3D && key.IsActionPressed("texture_auto_align"))
		{
			// UDB's own real default keybinds, verified against its
			// Actions.cfg/UDBuilder.default.cfg: plain A = X only,
			// Shift+A = Y only, Ctrl+A = both (the one most mappers
			// actually reach for) - none of the three are `repeat`d,
			// unlike the arrow-key nudges above.
			var axisSwap = Input.IsActionPressed("texture_auto_align_axis_swap_modifier");
			var both = Input.IsActionPressed("texture_auto_align_both_modifier");
			HandleTextureAutoAlign(alignX: !axisSwap, alignY: axisSwap || both);
			return;
		}

		if (key.IsActionPressed("toggle_2d_3d"))
		{
			// Computed *before* any Current flag changes below, while
			// _topDownCamera is still definitely the viewport's own
			// active camera - Camera3D's ray-projection methods appear
			// to depend on a camera actually being the current one for
			// an up-to-date projection matrix (the likely real cause
			// behind this being unreliable/imprecise when it ran after
			// the Current swap instead: a stale matrix from whatever
			// frame the top-down camera was last actually active,
			// rather than genuinely wrong math).
			if (!_in3D) PlacePerspectiveCameraAtMouse();

			_in3D = !_in3D;
			_topDownCamera.Current = !_in3D;
			_perspectiveCamera.Current = _in3D;
			_overlay.Visible = !_in3D;
			_modeToolbar.Visible = !_in3D;
			_statusBar.Visible = !_in3D;
			_crosshair.Visible = _in3D;
			Input.MouseMode = _in3D ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
			if (_in3D)
			{
				// Entering 3D - seed the local 3D selection from
				// whatever's currently selected in 2D (UDB's real
				// sync-on-entry bridge between the two selections).
				_selectedSectors3D.Clear();
				_selectedSectors3D.UnionWith(_map.GetSelectedSectors());
				_selectedLinedefs3D.Clear();
				_selectedLinedefs3D.UnionWith(_map.GetSelectedLinedefs());
				_selectedThings3D.Clear();
				_selectedThings3D.UnionWith(_map.GetSelectedThings());
			}
			else
			{
				// Leaving 3D - write the local 3D selection back out
				// (the matching sync-on-exit bridge), then don't leave
				// a stale highlight showing and force a fresh pick
				// next time 3D mode is entered.
				_map.ClearSelectedSectors();
				_map.ClearSelectedLinedefs();
				_map.ClearSelectedThings();
				foreach (var sector in _selectedSectors3D) _map.ToggleSelect(sector);
				foreach (var linedef in _selectedLinedefs3D) _map.ToggleSelect(linedef);
				foreach (var thing in _selectedThings3D) _map.ToggleSelect(thing);

				_currentTarget = null;
				_targetHighlight.UpdateHighlights(null, _selectedSectors3D, _selectedLinedefs3D, _selectedThings3D, MapVector2.Zero);
			}

			return;
		}

		if (key.IsActionPressed("mode_vertices")) { _overlay.Mode = EditMode.Vertices; return; }
		if (key.IsActionPressed("mode_linedefs")) { _overlay.Mode = EditMode.Linedefs; return; }
		if (key.IsActionPressed("mode_sectors")) { _overlay.Mode = EditMode.Sectors; return; }
		if (key.IsActionPressed("mode_things")) { _overlay.Mode = EditMode.Things; return; }
		if (key.IsActionPressed("mode_draw")) { _overlay.Mode = EditMode.Draw; return; }
		if (key.IsActionPressed("toggle_snap")) { _overlay.SnapEnabled = !_overlay.SnapEnabled; return; }
		if (key.IsActionPressed("toggle_dynamic_grid")) { _overlay.DynamicGridSizeEnabled = !_overlay.DynamicGridSizeEnabled; return; }
		if (key.IsActionPressed("grid_size_decrease")) { _overlay.DecreaseGridSize(); return; }
		if (key.IsActionPressed("grid_size_increase")) { _overlay.IncreaseGridSize(); return; }
	}

	// A 256x256 room with a 64x64 pillar hole in the middle - enough to
	// exercise the hole-cutting path, not just a plain box.
	private static Sector BuildSampleSector(MapData map)
	{
		var sector = map.CreateSector(floorHeight: 0, ceilingHeight: 128);

		var v0 = map.CreateVertex(new MapVector2(0, 0));
		var v1 = map.CreateVertex(new MapVector2(0, 256));
		var v2 = map.CreateVertex(new MapVector2(256, 256));
		var v3 = map.CreateVertex(new MapVector2(256, 0));

		map.CreateLinedef(v0, v1, front: sector, back: null);
		map.CreateLinedef(v1, v2, front: sector, back: null);
		map.CreateLinedef(v2, v3, front: sector, back: null);
		map.CreateLinedef(v3, v0, front: sector, back: null);

		var h0 = map.CreateVertex(new MapVector2(96, 96));
		var h1 = map.CreateVertex(new MapVector2(160, 96));
		var h2 = map.CreateVertex(new MapVector2(160, 160));
		var h3 = map.CreateVertex(new MapVector2(96, 160));

		map.CreateLinedef(h0, h1, front: sector, back: null);
		map.CreateLinedef(h1, h2, front: sector, back: null);
		map.CreateLinedef(h2, h3, front: sector, back: null);
		map.CreateLinedef(h3, h0, front: sector, back: null);

		return sector;
	}
}
