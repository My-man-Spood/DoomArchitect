using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Interop;
using Godot;
using MapVector2 = System.Numerics.Vector2;

namespace DoomArchitect.Rendering;

/// <summary>
/// A single, dynamically-rebuilt highlight overlay for whatever the 3D
/// view is currently targeting (see <c>MapView</c>'s targeting loop) -
/// built as its own separate mesh rather than tinting the real rendering
/// mesh's material the way UDB does, since a linedef's wall parts sharing
/// a texture can be merged into one shared surface
/// (<see cref="WallMeshBuilder"/>) with no way to highlight just one part
/// of it at the material level. Reuses the exact same Core.Geometry
/// triangulation already used for the real geometry, offset slightly to
/// avoid z-fighting. Only rebuilt when the target actually changes, never
/// every frame - a cheap, small mesh either way (one sector or one wall
/// segment's worth of triangles).
/// </summary>
public partial class TargetHighlight : MeshInstance3D
{
    private const float FloorCeilingOffset = 0.5f;
    private const float WallOffset = 0.5f;

    private static readonly Color HighlightColor = new(1f, 0.5f, 0f, 0.03f);

    public override void _Ready()
    {
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = HighlightColor,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            // The real wall/floor/ceiling meshes are double-sided via two
            // opposite-wound triangles each (see DoubleSidedMesh) so their
            // normals stay correct for lighting - this highlight has no
            // lighting to get right (Unshaded), so simply disabling
            // backface culling is enough to stay visible from both sides.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Visible = false;
    }

    public void ShowFloor(Sector sector) => ShowFlat(sector, sector.FloorHeight, FloorCeilingOffset);

    public void ShowCeiling(Sector sector) => ShowFlat(sector, sector.CeilingHeight, -FloorCeilingOffset);

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
    public void ShowWall(WallSegment segment, MapVector2 viewerPosition)
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

        Mesh = surfaceTool.Commit();
        Visible = true;
    }

    public void HideHighlight()
    {
        Visible = false;
    }

    private void ShowFlat(Sector sector, double height, float offset)
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
        Mesh = surfaceTool.Commit();
        Visible = true;
    }
}
