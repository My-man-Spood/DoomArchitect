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
/// </summary>
internal static class DoubleSidedMesh
{
    public static void AddTriangle(
        SurfaceTool surfaceTool, Vector3 a, Vector3 b, Vector3 c, Vector2 uvA, Vector2 uvB, Vector2 uvC)
    {
        surfaceTool.SetUV(uvA);
        surfaceTool.AddVertex(a);
        surfaceTool.SetUV(uvB);
        surfaceTool.AddVertex(b);
        surfaceTool.SetUV(uvC);
        surfaceTool.AddVertex(c);

        surfaceTool.SetUV(uvA);
        surfaceTool.AddVertex(a);
        surfaceTool.SetUV(uvC);
        surfaceTool.AddVertex(c);
        surfaceTool.SetUV(uvB);
        surfaceTool.AddVertex(b);
    }
}
