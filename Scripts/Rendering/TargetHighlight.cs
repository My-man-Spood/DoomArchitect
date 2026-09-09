using System;
using System.Collections.Generic;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Interop;
using Godot;
using MapVector2 = System.Numerics.Vector2;

namespace DoomArchitect.Rendering;

/// <summary>
/// Every highlighted surface in the 3D view: the current hover/crosshair
/// target, plus every selected Sector/Linedef - rebuilt as a pool of
/// child <see cref="MeshInstance3D"/>s each time <see cref="UpdateHighlights"/>
/// is called (see its own remarks), rather than as one single mesh the
/// way this class originally worked when it only ever showed one target
/// at a time. Reuses the exact same Core.Geometry triangulation already
/// used for the real geometry, offset slightly to avoid z-fighting.
/// </summary>
public partial class TargetHighlight : Node3D
{
    private const float FloorCeilingOffset = 0.5f;
    private const float WallOffset = 0.5f;

    private static readonly Color HoverColor = new(1f, 0.5f, 0f, 0.03f);

    /// <summary>
    /// Same red hue as <c>MapOverlay.SelectedColor</c> (kept in sync by
    /// convention, not a shared constant - this is a translucent 3D
    /// surface fill covering large areas, tuned to a much lower alpha
    /// than the 2D view's opaque icon tint needs).
    /// </summary>
    private static readonly Color SelectedColor = new(0.9f, 0.15f, 0.15f, 0.05f);

    private readonly List<MeshInstance3D> _highlightInstances = new();
    private StandardMaterial3D _hoverMaterial;
    private StandardMaterial3D _selectedMaterial;

    /// <summary>Same lookup <c>MapView</c> already gives its <see cref="MapRaycaster"/> - needed to size a two-sided linedef's masked middle texture. Set once from <c>MapView._Ready</c>.</summary>
    public Func<string, double> MiddleTextureHeightLookup { get; set; }

    public override void _Ready()
    {
        _hoverMaterial = BuildMaterial(HoverColor);
        _selectedMaterial = BuildMaterial(SelectedColor);
    }

    private static StandardMaterial3D BuildMaterial(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        // The real wall/floor/ceiling meshes are double-sided via two
        // opposite-wound triangles each (see DoubleSidedMesh) so their
        // normals stay correct for lighting - this highlight has no
        // lighting to get right (Unshaded), so simply disabling
        // backface culling is enough to stay visible from both sides.
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>
    /// Rebuilds every highlighted surface from scratch on each call
    /// (rather than diffing against the previous call) - simplest correct
    /// approach, and cheap enough at the ~80ms pick cadence <c>MapView</c>
    /// already throttles this to for realistic selection sizes. Hover
    /// always wins visually over selection: an element that's both the
    /// hover target and selected is drawn once, in the hover material
    /// only - the same "hover always wins, no blended state" rule
    /// <c>MapOverlay</c>'s 2D view uses.
    /// </summary>
    public void UpdateHighlights(
        MapTarget? hoverTarget, IEnumerable<Sector> selectedSectors, IEnumerable<Linedef> selectedLinedefs, MapVector2 viewerPosition)
    {
        foreach (var instance in _highlightInstances) instance.QueueFree();
        _highlightInstances.Clear();

        var hoverSector = hoverTarget is { Kind: TargetSurfaceKind.Floor or TargetSurfaceKind.Ceiling }
            ? hoverTarget.Value.Sector
            : null;
        var hoverLinedef = hoverTarget is { Kind: TargetSurfaceKind.Wall }
            ? hoverTarget.Value.WallSegment!.Value.Side.Linedef
            : null;

        foreach (var sector in selectedSectors)
        {
            if (sector == hoverSector) continue;
            AddFlat(sector, sector.FloorHeight, FloorCeilingOffset, _selectedMaterial);
            AddFlat(sector, sector.CeilingHeight, -FloorCeilingOffset, _selectedMaterial);
        }

        foreach (var linedef in selectedLinedefs)
        {
            if (linedef == hoverLinedef) continue;
            foreach (var segment in LinedefWallBuilder.Build(linedef, MiddleTextureHeightLookup))
            {
                AddWall(segment, viewerPosition, _selectedMaterial);
            }
        }

        switch (hoverTarget)
        {
            case { Kind: TargetSurfaceKind.Floor } target:
                AddFlat(target.Sector, target.Sector.FloorHeight, FloorCeilingOffset, _hoverMaterial);
                break;
            case { Kind: TargetSurfaceKind.Ceiling } target:
                AddFlat(target.Sector, target.Sector.CeilingHeight, -FloorCeilingOffset, _hoverMaterial);
                break;
            case { Kind: TargetSurfaceKind.Wall } target:
                AddWall(target.WallSegment!.Value, viewerPosition, _hoverMaterial);
                break;
        }
    }

    private void AddFlat(Sector sector, double height, float offset, StandardMaterial3D material)
    {
        var polygons = PolygonCutter.Cut(PolygonNesting.BuildTree(SectorTracer.Trace(sector)));

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        foreach (var polygon in polygons)
        {
            foreach (var (a, b, c) in EarClipper.Clip(polygon))
            {
                surfaceTool.AddVertex(a.ToWorld((float)height + offset));
                surfaceTool.AddVertex(b.ToWorld((float)height + offset));
                surfaceTool.AddVertex(c.ToWorld((float)height + offset));
            }
        }

        surfaceTool.GenerateNormals();
        AddInstance(surfaceTool.Commit(), material);
    }

    /// <summary>
    /// <paramref name="viewerPosition"/> (map-space XY, e.g. the camera's
    /// own position) decides which way to nudge the highlight off the
    /// wall's exact plane: always toward whichever side is actually being
    /// looked from. A single fixed perpendicular direction would push the
    /// highlight behind the wall's own opaque geometry - invisible, since
    /// a translucent quad loses the depth test against an opaque surface
    /// in front of it - for every wall whose Start-End winding happens to
    /// point away from the viewer, which in practice is most/all of them
    /// at once (real maps wind their sector boundaries consistently, so a
    /// fixed choice isn't a coin flip per wall, it's the same wrong answer
    /// every time). Caught after highlighting silently failed for every
    /// wall in a real map.
    /// </summary>
    private void AddWall(WallSegment segment, MapVector2 viewerPosition, StandardMaterial3D material)
    {
        var direction = segment.End.Position - segment.Start.Position;
        var length = direction.Length();
        var normal = length > 0 ? new MapVector2(-direction.Y, direction.X) / length : MapVector2.Zero;

        var midpoint = (segment.Start.Position + segment.End.Position) / 2f;
        if (MapVector2.Dot(normal, viewerPosition - midpoint) < 0) normal = -normal;

        var offsetStart = segment.Start.Position + normal * WallOffset;
        var offsetEnd = segment.End.Position + normal * WallOffset;

        var startBottom = offsetStart.ToWorld((float)segment.Bottom);
        var startTop = offsetStart.ToWorld((float)segment.Top);
        var endBottom = offsetEnd.ToWorld((float)segment.Bottom);
        var endTop = offsetEnd.ToWorld((float)segment.Top);

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        surfaceTool.AddVertex(startBottom);
        surfaceTool.AddVertex(startTop);
        surfaceTool.AddVertex(endTop);
        surfaceTool.AddVertex(startBottom);
        surfaceTool.AddVertex(endTop);
        surfaceTool.AddVertex(endBottom);
        surfaceTool.GenerateNormals();

        AddInstance(surfaceTool.Commit(), material);
    }

    private void AddInstance(ArrayMesh mesh, StandardMaterial3D material)
    {
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
        AddChild(instance);
        _highlightInstances.Add(instance);
    }
}
