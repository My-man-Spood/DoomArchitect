using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// Shared by every mesh builder that needs a face lit correctly from
/// either side: two real triangles per face, one wound each way, rather
/// than one triangle plus a double-sided material. A single triangle's
/// baked normal only shades correctly from the side it's meant to face;
/// viewed from the other side under normal lighting it renders
/// essentially black regardless of culling, so two real triangles (each
/// with its own correct normal) is simpler than getting a renderer's
/// backface-lighting behavior right.
///
/// <paramref name="color"/> is a single flat shade applied to every
/// vertex of the face - Doom's own lighting has no gradient across a
/// surface, just one brightness value per sector (or per wall, with fake
/// contrast - see <c>Core.Lighting.SectorBrightness</c>), so there's
/// nothing to interpolate between corners here on purpose. It only takes
/// effect on a material with <c>VertexColorUseAsAlbedo</c> enabled (see
/// <c>TextureCache</c>) - the whole point of baking it onto the geometry
/// itself, rather than driving it from a real Godot light, is that it
/// stays correct regardless of which material happens to render this
/// mesh, and doesn't respond to anything in the scene the way a real
/// light/normal interaction would.
/// </summary>
internal static class DoubleSidedMesh
{
    public static void AddTriangle(
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

        surfaceTool.SetColor(color);
        surfaceTool.SetUV(uvA);
        surfaceTool.AddVertex(a);
        surfaceTool.SetColor(color);
        surfaceTool.SetUV(uvC);
        surfaceTool.AddVertex(c);
        surfaceTool.SetColor(color);
        surfaceTool.SetUV(uvB);
        surfaceTool.AddVertex(b);
    }
}
