using Godot;

namespace DoomArchitect.Rendering;

/// <summary>
/// Resolves a <c>ThingTypeInfo.ColorIndex</c> (a small palette index, same
/// idea as UDB's real <c>color</c> field) into an actual color for tinting
/// the 2D Thing marker icon. These are DoomArchitect's own original color
/// choices, not UDB's actual bundled editor color scheme - that palette is
/// itself a UI/design choice UDB's own `.cfg`-adjacent settings data
/// defines, not a vanilla-Doom fact, so it's authored fresh here rather
/// than extracted from UDB's source the way factual game data was.
/// </summary>
public static class ThingCategoryColors
{
    // One color per category, not per exact key color - a per-key blue/
    // yellow/red tint (an earlier version of this) is actively confusing
    // at a glance, since other categories also use blue-ish/yellow-ish
    // tones: a blue marker would leave a mapper unsure whether they're
    // looking at a key specifically or just some other blue-tinted
    // pickup. One shared "keys" color keeps the palette meaning "which
    // category is this" consistently, the same as every other entry here.
    // Indices match this project's own thingtypes category grouping,
    // which mirrors UDB's real Doom_things.cfg category names exactly
    // (players/teleports/monsters/weapons/ammunition/health/powerups/
    // keys/obstacles - cross-checked directly, not guessed).
    private static readonly Color[] Palette =
    {
        new(1f, 1f, 1f),           // 0: neutral/default - keeps the icon's own unmodified color
        new(0.4f, 0.6f, 1f),       // 1: player
        new(0.3f, 0.8f, 0.75f),    // 2: teleport
        new(0.9f, 0.25f, 0.25f),   // 3: monster
        new(1f, 0.6f, 0.2f),       // 4: weapon
        new(0.65f, 0.55f, 0.3f),   // 5: ammunition
        new(0.35f, 0.8f, 0.35f),   // 6: health/armor
        new(0.65f, 0.4f, 0.85f),   // 7: powerup
        new(0.95f, 0.8f, 0.2f),    // 8: keys
        new(0.6f, 0.5f, 0.4f),     // 9: obstacle
    };

    public static Color Get(int colorIndex) =>
        colorIndex >= 0 && colorIndex < Palette.Length ? Palette[colorIndex] : Colors.White;
}
