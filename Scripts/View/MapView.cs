using System.Collections.Generic;
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

	public override void _Ready()
	{
		_topDownCamera = GetNode<Camera3D>("TopDownCamera");
		_perspectiveCamera = GetNode<Camera3D>("PerspectiveCamera");
		_overlay = GetNode<MapOverlay>("Overlay/MapOverlay");
		_modeToolbar = GetNode<ModeToolbar>("UI/MarginContainer/TopToolbar");
		_modeToolbar.Overlay = _overlay;
		_gridToolbar = GetNode<GridToolbar>("UI/MarginContainer/TopToolbar/GridToolbar");
		_gridToolbar.Overlay = _overlay;
		_statusBar = GetNode<StatusBar>("UI/StatusBar");
		_statusBar.Overlay = _overlay;
		GetNode<OpenMapMenu>("UI/MarginContainer/TopToolbar/OpenMapGroup").MapLoaded += LoadMap;

		// No WAD is open yet - every texture/flat lookup just resolves to
		// the shared placeholder until a real map is loaded.
		_textureCache = new TextureCache(TextureSet.CreateEmpty());

		// Reads _textureCache/_map fresh on every call rather than a
		// captured value, so this keeps working correctly across LoadMap
		// swapping both fields out from under it later.
		_targetFinder = new MapRaycaster(_spatialIndex, name => _textureCache.GetWallTextureSize(name).Y);
		_targetHighlight = new TargetHighlight();
		AddChild(_targetHighlight);
		// Parented under the same screen-space overlay layer MapOverlay
		// already renders correctly through, rather than directly under
		// this Node3D - that layer is the proven place for 2D content to
		// draw on top of the 3D scene.
		_crosshair = new Crosshair { Visible = false };
		GetNode<Node>("Overlay").AddChild(_crosshair);

		_map = new MapData();
		var sector = BuildSampleSector(_map);
		CreateSectorMeshInstances(sector);
		foreach (var linedef in _map.Linedefs)
		{
			CreateWallMeshInstance(linedef);
		}

		_overlay.Map = _map;
		_overlay.Camera = _topDownCamera;
		_overlay.UndoStack = _undoStack;
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
	/// why - directly adopted from UDB's own proven throttle). The
	/// highlight itself is only touched when the target's actual identity
	/// changes, not on every poll tick.
	/// </summary>
	private void UpdateTarget()
	{
		_spatialIndex.Rebuild(_map);

		var origin = _perspectiveCamera.GlobalPosition.ToDoom3D();
		var direction = (-_perspectiveCamera.GlobalTransform.Basis.Z).ToDoom3D();
		var target = _targetFinder.FindTarget(origin, direction);

		if (target == _currentTarget) return;

		_currentTarget = target;
		switch (target)
		{
			case null:
				_targetHighlight.HideHighlight();
				break;
			case { Kind: TargetSurfaceKind.Floor }:
				_targetHighlight.ShowFloor(target.Value.Sector);
				break;
			case { Kind: TargetSurfaceKind.Ceiling }:
				_targetHighlight.ShowCeiling(target.Value.Sector);
				break;
			case { Kind: TargetSurfaceKind.Wall }:
				_targetHighlight.ShowWall(target.Value.WallSegment!.Value, new MapVector2(origin.X, origin.Y));
				break;
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
	private void LoadMap(MapData newMap, TextureSet textures)
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

		_textureCache = new TextureCache(textures);

		// The old target may reference a Sector/WallSegment from the map
		// being discarded - never carry that across a load.
		_currentTarget = null;
		_targetHighlight.HideHighlight();

		_map = newMap;
		foreach (var sector in newMap.Sectors)
		{
			CreateSectorMeshInstances(sector);
		}

		foreach (var linedef in newMap.Linedefs)
		{
			CreateWallMeshInstance(linedef);
		}

		_undoStack = new UndoStack();
		_overlay.Map = _map;
		_overlay.UndoStack = _undoStack;

		FitTopDownCameraToMap(newMap);
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
	/// </summary>
	private void ApplyFlatMaterial(MeshInstance3D instance, string textureName)
	{
		if (textureName == "-") return;
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
	/// Ctrl+Z/Ctrl+Y match UDB's own default undo/redo keys exactly
	/// (verified against its default keybind config, not guessed). 1/2/3
	/// switch edit mode; the rest are grid/snap controls on
	/// <see cref="_overlay"/>. <c>[</c>/<c>]</c> (double/halve, 1..1024)
	/// and <c>G</c> (snap toggle) match UDB's own keys and bounds, except
	/// <c>G</c> and <c>D</c> themselves - UDB binds neither by default,
	/// since both are toolbar checkboxes there. Manually resizing the
	/// grid disables dynamic sizing, matching UDB's own
	/// <c>DisableDynamicGridResize</c>.
	/// </summary>
	public override void _UnhandledInput(InputEvent @event)
	{
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
				if (!_in3D)
				{
					// Leaving 3D - don't leave a stale highlight showing,
					// and force a fresh pick next time 3D mode is entered.
					_currentTarget = null;
					_targetHighlight.HideHighlight();
				}

				break;
			case Key.Key1:
				_overlay.Mode = EditMode.Vertices;
				break;
			case Key.Key2:
				_overlay.Mode = EditMode.Linedefs;
				break;
			case Key.Key3:
				_overlay.Mode = EditMode.Sectors;
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
