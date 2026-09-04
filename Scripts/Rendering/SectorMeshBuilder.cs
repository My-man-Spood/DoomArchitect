using System.Collections.Generic;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Interop;
using Godot;
using MapVector2 = System.Numerics.Vector2;

namespace DoomArchitect.Rendering;

/// <summary>
/// Turns a sector's triangulated shape (Core.Geometry - all pure math, no
/// Godot involved) into an actual Godot mesh: a floor at FloorHeight and a
/// ceiling at CeilingHeight, each genuinely double-sided - two real
/// triangles per face, one wound each way, rather than one triangle plus
/// a double-sided material. A single triangle's normal only shades
/// correctly from the side it's meant to face; viewed from the other side
/// under normal lighting it renders essentially black regardless of
/// culling, so two real triangles (each with its own correct normal) is
/// simpler than getting a renderer's backface-lighting behavior right.
/// </summary>
public static class SectorMeshBuilder
{
    public static ArrayMesh Build(Sector sector)
    {
        var polygons = PolygonCutter.Cut(PolygonNesting.BuildTree(SectorTracer.Trace(sector)));

        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        foreach (var polygon in polygons)
        {
            var triangles = EarClipper.Clip(polygon);
            AddDoubleSidedFace(surfaceTool, triangles, (float)sector.FloorHeight);
            AddDoubleSidedFace(surfaceTool, triangles, (float)sector.CeilingHeight);
        }

        surfaceTool.GenerateNormals();
        return surfaceTool.Commit();
    }

    private static void AddDoubleSidedFace(
        SurfaceTool surfaceTool, IReadOnlyList<(MapVector2 A, MapVector2 B, MapVector2 C)> triangles, float height)
    {
        foreach (var (a, b, c) in triangles)
        {
            var worldA = ToWorld(a, height);
            var worldB = ToWorld(b, height);
            var worldC = ToWorld(c, height);

            surfaceTool.AddVertex(worldA);
            surfaceTool.AddVertex(worldB);
            surfaceTool.AddVertex(worldC);

            surfaceTool.AddVertex(worldA);
            surfaceTool.AddVertex(worldC);
            surfaceTool.AddVertex(worldB);
        }
    }

    /// <summary>
    /// First-pass mapping: Doom's map-plane X/Y become Godot's ground-plane
    /// X/Z, height becomes Godot's Y (up). Confirmed correct (not mirrored)
    /// against an actual rendered top-down view.
    /// </summary>
    private static Godot.Vector3 ToWorld(MapVector2 position, float height) =>
        new(position.X, height, position.Y);
}
