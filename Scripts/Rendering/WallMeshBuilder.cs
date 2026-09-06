using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;
using DoomArchitect.Interop;
using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// A linedef's wall mesh, plus which texture name each of its
/// <see cref="Mesh"/> surfaces uses - a linedef's upper/lower/middle
/// segments can each need a different texture, and a single
/// <see cref="ArrayMesh"/> surface only carries one material, so each
/// distinct texture gets its own surface. Applying the actual materials
/// is left to the caller (MapView already owns mesh-instance lifecycle).
/// </summary>
public readonly record struct WallMeshResult(ArrayMesh Mesh, IReadOnlyList<string> SurfaceTextures);

/// <summary>
/// Turns a linedef's wall segments (Core.Geometry.LinedefWallBuilder -
/// pure math, no Godot) into an actual Godot mesh, each quad genuinely
/// double-sided via <see cref="DoubleSidedMesh"/> - a wall isn't tracked
/// as "front-facing" only, it should look correct from whichever side
/// you're actually standing on.
///
/// UVs are top-pegged (V=0 at the segment's own top edge, offset by the
/// originating sidedef's OffsetX/OffsetY) - see LinedefWallBuilder's
/// remarks on why real linedef-flag pegging isn't modeled yet.
/// </summary>
public static class WallMeshBuilder
{
    // The map-format sentinel "-" ("no texture") must never reach
    // TextureSet's lookups (that's a genuinely-unresolvable-name concept,
    // logging a warning and returning the placeholder) - a "-" surface
    // gets no material at all (see MapView), so its UV scale is cosmetic
    // and this fixed size is just a harmless placeholder for the math.
    private static readonly Vector2I NoTextureSize = new(64, 64);

    public static WallMeshResult Build(Linedef linedef, TextureCache textures)
    {
        var segments = LinedefWallBuilder.Build(
            linedef, name => name == "-" ? NoTextureSize.Y : textures.GetWallTextureSize(name).Y);

        var mesh = new ArrayMesh();
        var surfaceTextures = new List<string>();

        foreach (var group in segments.GroupBy(s => s.Texture))
        {
            var surfaceTool = new SurfaceTool();
            surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

            foreach (var segment in group)
            {
                AddQuad(surfaceTool, segment, textures);
            }

            surfaceTool.GenerateNormals();
            surfaceTool.Commit(mesh);
            surfaceTextures.Add(group.Key);
        }

        return new WallMeshResult(mesh, surfaceTextures);
    }

    private static void AddQuad(SurfaceTool surfaceTool, WallSegment segment, TextureCache textures)
    {
        var bottom = (float)segment.Bottom;
        var top = (float)segment.Top;

        var startBottom = segment.Start.Position.ToWorld(bottom);
        var startTop = segment.Start.Position.ToWorld(top);
        var endBottom = segment.End.Position.ToWorld(bottom);
        var endTop = segment.End.Position.ToWorld(top);

        var textureSize = segment.Texture == "-" ? NoTextureSize : textures.GetWallTextureSize(segment.Texture);
        var textureWidth = Mathf.Max(textureSize.X, 1);
        var textureHeight = Mathf.Max(textureSize.Y, 1);
        var length = (segment.End.Position - segment.Start.Position).Length();

        var uStart = segment.Side.OffsetX / (float)textureWidth;
        var uEnd = ((float)length + segment.Side.OffsetX) / textureWidth;
        var vTop = segment.Side.OffsetY / (float)textureHeight;
        var vBottom = ((top - bottom) + segment.Side.OffsetY) / textureHeight;

        var uvStartBottom = new Vector2(uStart, vBottom);
        var uvStartTop = new Vector2(uStart, vTop);
        var uvEndBottom = new Vector2(uEnd, vBottom);
        var uvEndTop = new Vector2(uEnd, vTop);

        DoubleSidedMesh.AddTriangle(surfaceTool, startBottom, startTop, endTop, uvStartBottom, uvStartTop, uvEndTop);
        DoubleSidedMesh.AddTriangle(surfaceTool, startBottom, endTop, endBottom, uvStartBottom, uvEndTop, uvEndBottom);
    }
}
