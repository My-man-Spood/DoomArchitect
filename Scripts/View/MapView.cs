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
	private UndoStack _undoStack = new();
	private bool _in3D;

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
	/// <c>Key.Tab</c> case in <see cref="_UnhandledInput"/>), not shared
	/// live the way an earlier version of this feature did.
	/// </summary>
	private readonly HashSet<Sector> _selectedSectors3D = new();
	private readonly HashSet<Linedef> _selectedLinedefs3D = new();

	public override void _Ready()
	{
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
		_textureCache = new TextureCache(TextureSet.CreateEmpty());

		// Reads _textureCache/_map fresh on every call rather than a
		// captured value, so this keeps working correctly across LoadMap
		// swapping both fields out from under it later.
		_targetFinder = new MapRaycaster(_spatialIndex, name => _textureCache.GetWallTextureSize(name).Y);
		_targetHighlight = new TargetHighlight { MiddleTextureHeightLookup = name => _textureCache.GetWallTextureSize(name).Y };
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
	}

	public override void _Process(double delta)
	{
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
			var hangs = _gameConfiguration.GetThingType(thing.Type)?.Hangs ?? false;
			_thingMeshes[thing].Position = thing.Position.ToWorld((float)ResolveThingWorldZ(thing, hangs));
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

		_targetHighlight.UpdateHighlights(_currentTarget, _selectedSectors3D, _selectedLinedefs3D, new MapVector2(origin.X, origin.Y));
	}

	/// <summary>
	/// 3D visual-mode click-to-select: acts on whichever target
	/// <see cref="UpdateTarget"/> most recently found (kept fresh every
	/// <see cref="PickIntervalSeconds"/>), at Sector/Linedef granularity -
	/// clicking any part of a wall selects its whole Linedef, any part of
	/// a floor/ceiling selects its whole Sector. A plain click always
	/// toggles (adds if unselected, removes if selected) - UDB's own real
	/// visual-mode click (<c>BaseVisualGeometrySector.OnSelectEnd</c>/
	/// <c>BaseVisualGeometrySidedef.OnSelectEnd</c>) does the identical
	/// toggle, with no modifier needed. Touches only the local 3D
	/// selection (<see cref="_selectedSectors3D"/>/
	/// <see cref="_selectedLinedefs3D"/>), not the classic 2D selection -
	/// they're bridged only on entering/leaving 3D mode, matching UDB's
	/// real separate-selection model. Unlike 2D, there's no "current mode"
	/// restricting which type can be selected - clicking a floor then a
	/// wall naturally builds a mixed Sector+Linedef selection, matching
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
			else
			{
				if (!_selectedSectors3D.Remove(target.Sector)) _selectedSectors3D.Add(target.Sector);
			}
		}
		else
		{
			_selectedSectors3D.Clear();
			_selectedLinedefs3D.Clear();
		}
	}

	/// <summary>
	/// Swaps in a freshly loaded map: tears down every existing sector's
	/// meshes and rebuilds from scratch, resets undo history (the old
	/// stack's commands still close over the discarded MapData, so they'd
	/// be pointless - see TODO.md), and refits the top-down camera since a
	/// real loaded map is very unlikely to sit in the same 256x256 area
	/// the sample room did.
	/// </summary>
	private void LoadMap(MapData newMap, TextureSet textures, IGameConfiguration gameConfiguration)
	{
		_textureCache = new TextureCache(textures);
		_gameConfiguration = gameConfiguration;
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
	public void RefreshResources(TextureSet textures, IGameConfiguration gameConfiguration)
	{
		_textureCache = new TextureCache(textures);
		_gameConfiguration = gameConfiguration;

		RebuildAllMeshes();
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
		// Sector/Linedef references independent of the classic 2D
		// selection - may reference meshes being discarded here; never
		// carry either across a rebuild.
		_currentTarget = null;
		_selectedSectors3D.Clear();
		_selectedLinedefs3D.Clear();
		_targetHighlight.UpdateHighlights(null, _selectedSectors3D, _selectedLinedefs3D, MapVector2.Zero);

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
			instance.SetSurfaceOverrideMaterial(i, _textureCache.GetWallMaterial(result.SurfaceTextures[i]));
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
		var (mesh, material, hangs) = ResolveThingMeshAndMaterial(thing.Type);
		var worldZ = ResolveThingWorldZ(thing, hangs);

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
	/// <see cref="CreateThingMeshInstance"/> and the dirty-things resync
	/// in <see cref="_Process"/>, so a moved Thing's height stays correct
	/// even if it crosses into a different sector.
	/// </summary>
	private double ResolveThingWorldZ(Thing thing, bool hangs)
	{
		var containingSector = SectorHitTest.FindContaining(_map.Sectors, thing.Position);
		return hangs
			? (containingSector?.CeilingHeight ?? 0) - thing.Height
			: (containingSector?.FloorHeight ?? 0) + thing.Height;
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
	/// Ctrl+Z/Ctrl+Y match UDB's own default undo/redo keys exactly
	/// (verified against its default keybind config, not guessed). V/L/S/T
	/// switch edit mode - matches UDB's own real defaults
	/// (<c>Assets/Common/UDBuilder.default.cfg</c>:
	/// <c>buildermodes_verticesmode/linedefsmode/sectorsmode/thingsmode =
	/// 86/76/83/84</c>, i.e. the raw key codes for V/L/S/T). An earlier
	/// version of this method used 1/2/3 for the first three modes - an
	/// uncorrected divergence from UDB's real defaults, found and fixed
	/// once Things mode needed a fourth key. The rest are grid/snap
	/// controls on <see cref="_overlay"/>. <c>[</c>/<c>]</c> (double/halve,
	/// 1..1024) and <c>G</c> (snap toggle) match UDB's own keys and bounds,
	/// except <c>G</c> and <c>D</c> themselves - UDB binds neither by
	/// default, since both are toolbar checkboxes there. Manually resizing
	/// the grid disables dynamic sizing, matching UDB's own
	/// <c>DisableDynamicGridResize</c>. None of this is user-rebindable
	/// yet - see TODO.md.
	/// </summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (_in3D && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
		{
			HandleThreeDSelectClick();
			return;
		}

		if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

		switch (key.Keycode)
		{
			case Key.Z when key.CtrlPressed:
				_undoStack.Undo();
				break;
			case Key.Y when key.CtrlPressed:
				_undoStack.Redo();
				break;
			case Key.Tab:
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
				}
				else
				{
					// Leaving 3D - write the local 3D selection back out
					// (the matching sync-on-exit bridge), then don't leave
					// a stale highlight showing and force a fresh pick
					// next time 3D mode is entered.
					_map.ClearSelectedSectors();
					_map.ClearSelectedLinedefs();
					foreach (var sector in _selectedSectors3D) _map.ToggleSelect(sector);
					foreach (var linedef in _selectedLinedefs3D) _map.ToggleSelect(linedef);

					_currentTarget = null;
					_targetHighlight.UpdateHighlights(null, _selectedSectors3D, _selectedLinedefs3D, MapVector2.Zero);
				}

				break;
			case Key.V:
				_overlay.Mode = EditMode.Vertices;
				break;
			case Key.L:
				_overlay.Mode = EditMode.Linedefs;
				break;
			case Key.S:
				_overlay.Mode = EditMode.Sectors;
				break;
			case Key.T:
				_overlay.Mode = EditMode.Things;
				break;
			case Key.G:
				_overlay.SnapEnabled = !_overlay.SnapEnabled;
				break;
			case Key.D:
				_overlay.DynamicGridSizeEnabled = !_overlay.DynamicGridSizeEnabled;
				break;
			case Key.Bracketleft:
				_overlay.IncreaseGridSize();
				break;
			case Key.Bracketright:
				_overlay.DecreaseGridSize();
				break;
		}
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
