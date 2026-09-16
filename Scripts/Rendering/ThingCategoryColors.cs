using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// Resolves a <c>ThingTypeInfo.ColorIndex</c> (the real <c>color</c> `.cfg`
/// field, 0-19) into an actual color for tinting the 2D Thing marker icon -
/// UDB's own real shipped-default thing-color palette, not this project's
/// own invention (an earlier version of this file used its own original
/// colors; switched to UDB's real ones instead - more familiar to anyone
/// coming from UDB, and there was no real reason to diverge here). Verified
/// directly against UDB's own source: <c>Source/Core/Map/Thing.cs</c>
/// resolves a thing's tint as
/// <c>General.Colors.Colors[ti.Color + ColorCollection.THING_COLORS_OFFSET]</c>
/// (falling back to index 0 - <c>THINGCOLOR00</c> - when
/// <see cref="ThingTypeInfo.ColorIndex"/> is out of range, matching this
/// class's own <see cref="Get"/> fallback), and
/// <c>Source/Core/Rendering/ColorCollection.cs</c> assigns each
/// <c>THINGCOLOR00</c>-<c>THINGCOLOR19</c> constant its real shipped-default
/// <see cref="System.Drawing.Color"/> (a user's own live UDB installation
/// can further customize these via its own settings, but the values below
/// are UDB's own real *defaults*, the same ones a fresh UDB install
/// actually ships).
/// </summary>
public static class ThingCategoryColors
{
    private static readonly Color[] Palette =
    {
        new(0.41f, 0.41f, 0.41f), // 0: DimGray
        new(0.25f, 0.41f, 0.88f), // 1: RoyalBlue
        new(0.13f, 0.55f, 0.13f), // 2: ForestGreen
        new(0.13f, 0.70f, 0.67f), // 3: LightSeaGreen
        new(0.70f, 0.13f, 0.13f), // 4: Firebrick
        new(0.58f, 0f, 0.83f),    // 5: DarkViolet
        new(0.72f, 0.53f, 0.04f), // 6: DarkGoldenrod
        new(0.75f, 0.75f, 0.75f), // 7: Silver
        new(0.50f, 0.50f, 0.50f), // 8: Gray
        new(0f, 0.75f, 1f),       // 9: DeepSkyBlue
        new(0.20f, 0.80f, 0.20f), // 10: LimeGreen
        new(0.69f, 0.93f, 0.93f), // 11: PaleTurquoise
        new(1f, 0.39f, 0.28f),    // 12: Tomato
        new(0.93f, 0.51f, 0.93f), // 13: Violet
        new(1f, 1f, 0f),          // 14: Yellow
        new(0.96f, 0.96f, 0.96f), // 15: WhiteSmoke
        new(1f, 0.71f, 0.76f),    // 16: LightPink
        new(1f, 0.55f, 0f),       // 17: DarkOrange
        new(0.74f, 0.72f, 0.42f), // 18: DarkKhaki
        new(0.85f, 0.65f, 0.13f), // 19: Goldenrod
    };

    public static Color Get(int colorIndex) =>
        colorIndex >= 0 && colorIndex < Palette.Length ? Palette[colorIndex] : Palette[0];
}
