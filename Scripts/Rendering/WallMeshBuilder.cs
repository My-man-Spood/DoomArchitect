using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Lighting;
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
/// pure math, no Godot) into an actual Godot mesh - each quad single-
/// sided, wound so its outward face (and generated normal) points into
/// whichever <see cref="Sidedef.Sector"/> that quad's own <see cref="WallSegment.Side"/>
/// belongs to, matching UDB's own real single-sided-per-face wall
/// rendering. Not double-sided the way <c>SectorMeshBuilder</c>'s
/// floor/ceiling meshes deliberately still are: a two-sided linedef's
/// masked middle can carry two genuinely *different* textures at the
/// exact same 3D position (front's own vs back's own) - <see cref="DoubleSidedMesh"/>
/// would draw both, double-sided, coincident, and which one actually
/// wins each pixel becomes undefined/z-fighting; a real one-sided
/// texture (<see cref="Sidedef.IsFront"/> either way, nothing coincident
/// to conflict with) still renders correctly single-sided since
/// <c>TextureCache.CreateMaterial</c> never disables the engine's own
/// default back-face culling - it only ever looked "double-sided" before
/// because of the old manual double-triangle trick, not because
/// anything relied on genuinely seeing a wall's texture from its own
/// wrong side.
///
/// UVs are top-pegged (V=0 at the segment's own top edge, offset by the
/// originating sidedef's OffsetX/OffsetY) - see LinedefWallBuilder's
/// remarks on why real linedef-flag pegging isn't modeled yet.
///
/// Each quad also gets a single flat brightness color (see
/// <c>Core.Lighting.SectorBrightness</c>) baked onto all of its vertices,
/// including the classic "fake contrast" wall-orientation shading -
/// computed from the wall's own sector, since a two-sided linedef's two
/// sides can belong to sectors with different light levels entirely.
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

        // Fake contrast only depends on the wall's own direction, not
        // which end is "start" vs "end" - see SectorBrightness's remarks.
        var wallDirection = segment.End.Position - segment.Start.Position;
        var brightness = SectorBrightness.CalculateForWall(segment.Side.Sector.Brightness, wallDirection);
        var color = brightness.ToBrightnessColor();

        // Winding flips between front and back: walking Start->End puts
        // the front sidedef's own sector on the walker's right (the same
        // convention established by SectorTracer/LinedefSide/BoundaryTracer's
        // own SidePoint, and visibly confirmed correct by
        // LinedefOverlayHandler.DrawFrontIndicator's own already-shipped
        // 2D front tick). This is the winding order empirically confirmed
        // (after an initial, backwards first attempt - Godot's actual
        // front-face/culling convention turned out not to match a naive
        // right-hand-rule-normal derivation against VectorConversions.ToWorld's
        // axis mapping) to make each side's own texture visible only from
        // inside that side's own <see cref="Sector"/>, not its neighbor's.
        if (segment.Side.IsFront)
        {
            AddTriangle(surfaceTool, startBottom, startTop, endTop, uvStartBottom, uvStartTop, uvEndTop, color);
            AddTriangle(surfaceTool, startBottom, endTop, endBottom, uvStartBottom, uvEndTop, uvEndBottom, color);
        }
        else
        {
            AddTriangle(surfaceTool, startBottom, endTop, startTop, uvStartBottom, uvEndTop, uvStartTop, color);
            AddTriangle(surfaceTool, startBottom, endBottom, endTop, uvStartBottom, uvEndBottom, uvEndTop, color);
        }
    }

    private static void AddTriangle(
        SurfaceTool surfaceTool, Vector3 a, Vector3 b, Vector3 c, Vector2 uvA, Vector2 uvB, Vector2 uvC, Color color)
    {
        surfaceTool.SetColor(color);
        surfaceTool.SetUV(uvA);
        surfaceTool.AddVertex(a);
        surfaceTool.SetColor(color);
        surfaceTool.SetUV(uvB);
        surfaceTool.AddVertex(b);
        surfaceTool.SetColor(color);
        surfaceTool.SetUV(uvC);
        surfaceTool.AddVertex(c);
    }
}
