using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Interop;
using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// Turns a linedef's wall segments (Core.Geometry.LinedefWallBuilder -
/// pure math, no Godot) into an actual Godot mesh, each quad genuinely
/// double-sided via <see cref="DoubleSidedMesh"/> - a wall isn't tracked
/// as "front-facing" only, it should look correct from whichever side
/// you're actually standing on.
/// </summary>
public static class WallMeshBuilder
{
    public static ArrayMesh Build(Linedef linedef)
    {
        var segments = LinedefWallBuilder.Build(linedef);

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        foreach (var segment in segments)
        {
            AddQuad(surfaceTool, segment);
        }

        surfaceTool.GenerateNormals();
        return surfaceTool.Commit();
    }

    private static void AddQuad(SurfaceTool surfaceTool, WallSegment segment)
    {
        var bottom = (float)segment.Bottom;
        var top = (float)segment.Top;

        var startBottom = segment.Start.Position.ToWorld(bottom);
        var startTop = segment.Start.Position.ToWorld(top);
        var endBottom = segment.End.Position.ToWorld(bottom);
        var endTop = segment.End.Position.ToWorld(top);

        DoubleSidedMesh.AddTriangle(surfaceTool, startBottom, startTop, endTop);
        DoubleSidedMesh.AddTriangle(surfaceTool, startBottom, endTop, endBottom);
    }
}
