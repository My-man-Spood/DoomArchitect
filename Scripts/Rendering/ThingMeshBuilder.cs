using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// A single vertical billboard quad representing a Thing, sized and
/// textured to its resolved <c>ThingTypeInfo</c> when one is available -
/// callers building a whole map cache one mesh/material pair per distinct
/// DoomEd number (see <c>MapView</c>) rather than per Thing, since every
/// instance of the same type looks identical. Falls back to the generic
/// radius-10/height-20 quad with the user's own hand-made
/// <c>icon_thing.svg</c> for unrecognized types or when the real sprite
/// lump isn't present in the currently loaded WAD - exactly the behavior
/// this codebase had before the game-configuration system existed.
/// </summary>
public static class ThingMeshBuilder
{
    // Matches UDB's own generic "unknown thing" fallback dimensions
    // exactly (ThingTypeInfo's parameterless-index constructor).
    public const float FallbackRadius = 10f;
    public const float FallbackHeight = 20f;

    private static ArrayMesh BuildQuad(float left, float right, float bottom, float top)
    {
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        var bottomLeft = new Vector3(left, bottom, 0);
        var bottomRight = new Vector3(right, bottom, 0);
        var topLeft = new Vector3(left, top, 0);
        var topRight = new Vector3(right, top, 0);

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

    public static ArrayMesh BuildFallback() => BuildQuad(-FallbackRadius, FallbackRadius, 0, FallbackHeight);

    /// <summary>
    /// Sizes and anchors the quad from the sprite's own real pixel
    /// dimensions/offsets (1 pixel = 1 map unit, vanilla's own convention -
    /// no per-thing DECORATE "scale" data exists yet to modify that),
    /// matching real Doom sprite placement: <paramref name="offsetY"/>
    /// pixels down from the image's top edge is the actor's own anchor
    /// point (typically where its feet meet the floor), and
    /// <paramref name="offsetX"/> pixels in from the left edge is its
    /// horizontal center - both usually near half the image's own
    /// dimensions, but not always exactly, so using them beats assuming a
    /// sprite is perfectly centered/floor-flush.
    /// </summary>
    public static ArrayMesh BuildSprite(int pixelWidth, int pixelHeight, int offsetX, int offsetY) =>
        BuildQuad(-offsetX, pixelWidth - offsetX, offsetY - pixelHeight, offsetY);

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
    public static StandardMaterial3D BuildMaterial(Texture2D texture) => new()
    {
        AlbedoTexture = texture,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public static StandardMaterial3D BuildFallbackMaterial() =>
        BuildMaterial(GD.Load<Texture2D>("res://Assets/Icons/icon_thing.svg"));
}
