using System.Collections.Generic;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Interop;
using Godot;
using MapVector2 = System.Numerics.Vector2;

namespace DoomArchitect.Rendering;

/// <summary>
/// A sector's floor and ceiling as two separate meshes rather than one -
/// each needs its own <see cref="Godot.MeshInstance3D"/> so it can carry
/// its own render layer (the top-down camera excludes the ceiling layer
/// so "2D view" shows floors, not ceilings).
/// </summary>
public readonly record struct SectorMesh(ArrayMesh Floor, ArrayMesh Ceiling);

/// <summary>
/// Turns a sector's triangulated shape (Core.Geometry - all pure math, no
/// Godot involved) into actual Godot meshes: a floor at FloorHeight and a
/// ceiling at CeilingHeight, each genuinely double-sided via
/// <see cref="DoubleSidedMesh"/>.
/// </summary>
public static class SectorMeshBuilder
{
    public static SectorMesh Build(Sector sector)
    {
        var polygons = PolygonCutter.Cut(PolygonNesting.BuildTree(SectorTracer.Trace(sector)));
        var triangleLists = new List<IReadOnlyList<(MapVector2 A, MapVector2 B, MapVector2 C)>>();
        foreach (var polygon in polygons)
        {
            triangleLists.Add(EarClipper.Clip(polygon));
        }

        var floor = BuildFace(triangleLists, (float)sector.FloorHeight);
        var ceiling = BuildFace(triangleLists, (float)sector.CeilingHeight);
        return new SectorMesh(floor, ceiling);
    }

    private static ArrayMesh BuildFace(
        List<IReadOnlyList<(MapVector2 A, MapVector2 B, MapVector2 C)>> triangleLists, float height)
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        foreach (var triangles in triangleLists)
        {
            AddDoubleSidedFace(surfaceTool, triangles, height);
        }

        surfaceTool.GenerateNormals();
        return surfaceTool.Commit();
    }

    private static void AddDoubleSidedFace(
        SurfaceTool surfaceTool, IReadOnlyList<(MapVector2 A, MapVector2 B, MapVector2 C)> triangles, float height)
    {
        foreach (var (a, b, c) in triangles)
        {
            DoubleSidedMesh.AddTriangle(surfaceTool, a.ToWorld(height), b.ToWorld(height), c.ToWorld(height));
        }
    }
}
