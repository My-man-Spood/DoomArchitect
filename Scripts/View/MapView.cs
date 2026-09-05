using System.Collections.Generic;
using DoomArchitect.Core.Map;
using DoomArchitect.Rendering;
using Godot;
using MapVector2 = System.Numerics.Vector2;

// Proof-of-concept for the "one live 3D scene, two cameras" idea: the
// top-down orthographic camera and the perspective camera look at the
// exact same scene, so switching views is just a camera swap.
public partial class MapView : Node3D
{
	// Godot render layers are 1-indexed bit positions; the ceiling mesh
	// lives on layer 2 so the top-down camera's cull mask can exclude it
	// while the perspective camera (default cull mask, all layers) still
	// sees it.
	private const uint CeilingRenderLayer = 2;

	private readonly Dictionary<Sector, (MeshInstance3D Floor, MeshInstance3D Ceiling)> _sectorMeshes = new();

	private Camera3D _topDownCamera;
	private Camera3D _perspectiveCamera;
	private MapOverlay _overlay;
	private MapData _map;
	private bool _in3D;

	public override void _Ready()
	{
		_topDownCamera = GetNode<Camera3D>("TopDownCamera");
		_perspectiveCamera = GetNode<Camera3D>("PerspectiveCamera");
		_overlay = GetNode<MapOverlay>("Overlay/MapOverlay");

		_map = new MapData();
		var sector = BuildSampleSector(_map);
		CreateSectorMeshInstances(sector);

		_overlay.Map = _map;
		_overlay.Camera = _topDownCamera;
	}

	public override void _Process(double delta)
	{
		foreach (var sector in _map.GetDirtySectors())
		{
			var mesh = SectorMeshBuilder.Build(sector);
			var instances = _sectorMeshes[sector];
			instances.Floor.Mesh = mesh.Floor;
			instances.Ceiling.Mesh = mesh.Ceiling;
			_map.ClearDirty(sector);
		}
	}

	private void CreateSectorMeshInstances(Sector sector)
	{
		var mesh = SectorMeshBuilder.Build(sector);
		var floor = new MeshInstance3D { Mesh = mesh.Floor };
		var ceiling = new MeshInstance3D { Mesh = mesh.Ceiling, Layers = CeilingRenderLayer };
		AddChild(floor);
		AddChild(ceiling);
		_sectorMeshes[sector] = (floor, ceiling);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab })
		{
			_in3D = !_in3D;
			_topDownCamera.Current = !_in3D;
			_perspectiveCamera.Current = _in3D;
			_overlay.Visible = !_in3D;
			Input.MouseMode = _in3D ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
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
