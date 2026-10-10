using System.Collections.Generic;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Lighting;
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
    public static SectorMesh Build(Sector sector, TextureCache textures)
    {
        var polygons = PolygonCutter.Cut(PolygonNesting.BuildTree(SectorTracer.Trace(sector)));
        var triangleLists = new List<IReadOnlyList<(MapVector2 A, MapVector2 B, MapVector2 C)>>();
        foreach (var polygon in polygons)
        {
            triangleLists.Add(EarClipper.Clip(polygon));
        }

        // Floors and ceilings never get fake contrast (that's a wall-only
        // trick) and this codebase has no separate floor/ceiling light
        // level yet (a ZDoom UDMF extension) - both just use the plain
        // curve on the sector's own brightness.
        var color = SectorBrightness.Calculate(sector.Brightness).ToBrightnessColor();

        // A flat tiles at its own real decoded pixel size, 1 texel per
        // map unit (same rule WallMeshBuilder already applies to wall
        // textures via GetWallTextureSize) - not a fixed assumed size.
        // Real GZDoom does this too: a 128x128 flat on a 64x64 sector
        // shows only its own top-left quarter, not the whole image
        // squeezed to fit. A hardcoded 64 (the classic Doom flat size,
        // and the only one that was ever wrong to assume) used to stand
        // in here instead, silently stretching any non-64x64 flat to
        // fill whatever sector it was on.
        var floorSize = textures.GetFlatTextureSize(sector.FloorTexture);
        var ceilingSize = textures.GetFlatTextureSize(sector.CeilingTexture);

        var floor = BuildFace(triangleLists, (float)sector.FloorHeight, color, floorSize);
        var ceiling = BuildFace(triangleLists, (float)sector.CeilingHeight, color, ceilingSize);
        return new SectorMesh(floor, ceiling);
    }

    private static ArrayMesh BuildFace(
        List<IReadOnlyList<(MapVector2 A, MapVector2 B, MapVector2 C)>> triangleLists, float height, Color color,
        Vector2I textureSize)
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        foreach (var triangles in triangleLists)
        {
            AddDoubleSidedFace(surfaceTool, triangles, height, color, textureSize);
        }

        surfaceTool.GenerateNormals();
        return surfaceTool.Commit();
    }

    private static void AddDoubleSidedFace(
        SurfaceTool surfaceTool, IReadOnlyList<(MapVector2 A, MapVector2 B, MapVector2 C)> triangles, float height,
        Color color, Vector2I textureSize)
    {
        foreach (var (a, b, c) in triangles)
        {
            DoubleSidedMesh.AddTriangle(
                surfaceTool, a.ToWorld(height), b.ToWorld(height), c.ToWorld(height),
                ToFlatUv(a, textureSize), ToFlatUv(b, textureSize), ToFlatUv(c, textureSize), color);
        }
    }

    private static Vector2 ToFlatUv(MapVector2 position, Vector2I textureSize) =>
        new(position.X / textureSize.X, position.Y / textureSize.Y);
}
