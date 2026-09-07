using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// A single vertical billboard quad representing a Thing - every Thing
/// currently looks identical (no per-type sprite/size data exists yet;
/// that's the Game configuration system, next), so this mesh and its
/// material are built once and shared across every Thing's mesh instance
/// rather than rebuilt per-instance.
/// </summary>
public static class ThingMeshBuilder
{
    // Matches UDB's own generic "unknown thing" fallback dimensions
    // exactly (ThingTypeInfo's parameterless-index constructor) - real
    // per-type sizes arrive with the Game configuration system.
    public const float Radius = 10f;
    public const float Height = 20f;

    public static ArrayMesh Build()
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        var bottomLeft = new Vector3(-Radius, 0, 0);
        var bottomRight = new Vector3(Radius, 0, 0);
        var topLeft = new Vector3(-Radius, Height, 0);
        var topRight = new Vector3(Radius, Height, 0);

        surfaceTool.SetUV(new Vector2(0, 1));
        surfaceTool.AddVertex(bottomLeft);
        surfaceTool.SetUV(new Vector2(0, 0));
        surfaceTool.AddVertex(topLeft);
        surfaceTool.SetUV(new Vector2(1, 0));
        surfaceTool.AddVertex(topRight);
        surfaceTool.SetUV(new Vector2(0, 1));
        surfaceTool.AddVertex(bottomLeft);
        surfaceTool.SetUV(new Vector2(1, 0));
        surfaceTool.AddVertex(topRight);
        surfaceTool.SetUV(new Vector2(1, 1));
        surfaceTool.AddVertex(bottomRight);

        surfaceTool.GenerateNormals();
        return surfaceTool.Commit();
    }

    /// <summary>
    /// <c>BillboardMode = FixedY</c> rotates the quad to face the camera
    /// around the vertical axis only - matches UDB's own default Thing
    /// billboard behavior (full billboarding is reserved for actors whose
    /// type info sets a "force full billboard" flag, which needs real
    /// per-type data this codebase doesn't have yet). Using Godot's own
    /// built-in billboard mode rather than hand-rolling UDB's per-frame
    /// camera-relative rotation matrix - a native feature built for
    /// exactly this classic-sprite-in-3D-world case.
    /// </summary>
    public static StandardMaterial3D BuildMaterial() => new()
    {
        AlbedoTexture = GD.Load<Texture2D>("res://Assets/Icons/icon_thing.svg"),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
