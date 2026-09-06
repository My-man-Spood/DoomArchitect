using System.Numerics;

namespace DoomArchitect.Core.Lighting;

/// <summary>
/// Converts a sector's raw light level (0-255) into a rendered brightness
/// value - a close port of UDB's own <c>Renderer.CalculateBrightness</c>
/// (<c>Source/Core/Rendering/Renderer.cs</c>), including its two real
/// quirks:
///
/// - The "Doom light levels" curve: below 192, brightness drops off
///   faster than linear (<c>level' = 192 - (192-level)*1.5</c>), emulating
///   the banding of vanilla Doom's 32-entry COLORMAP lookup table rather
///   than a literal linear dimmer. Every vanilla game config has this on
///   by default (UDB's own <c>doomlightlevels</c> setting) - hardcoded on
///   here too, since there's no game-configuration system yet to make it
///   selectable (see TODO.md).
/// - "Fake contrast": a wall's own light level gets nudged +-16 purely
///   based on whether it runs exactly north-south or east-west on the map
///   - a vanilla Doom engine trick for depth perception, nothing to do
///   with any actual light source or direction. A diagonal wall gets no
///   adjustment at all, and this is skipped entirely once the sector's
///   own light level is already 253 or higher (UDB's own threshold).
///   Floors and ceilings never get this - only walls do.
/// </summary>
public static class SectorBrightness
{
    private const int FakeContrastThreshold = 253;
    private const int HorizontalWallShade = -16;
    private const int VerticalWallShade = 16;

    public static int Calculate(int lightLevel)
    {
        // No game-configuration system yet to make this selectable - every
        // vanilla game config has it on by default, so it's hardcoded on.
        const bool useDoomLightLevels = true;

        if (useDoomLightLevels && lightLevel < 192)
        {
            lightLevel = (int)(192.0 - (192 - lightLevel) * 1.5);
        }

        return Clamp(lightLevel);
    }

    /// <summary>
    /// Like <see cref="Calculate"/>, but for a wall: applies fake contrast
    /// first, based on <paramref name="wallDirection"/> - the wall's own
    /// direction vector along the map plane. Which end is "start" vs
    /// "end" doesn't matter: reversing a vector never changes whether its
    /// X or Y component is exactly zero, and both of a linedef's sides
    /// get the exact same shade direction in UDB regardless of which one
    /// is "front".
    ///
    /// UDB determines "runs north-south" vs "runs east-west" via an exact
    /// switch on the wall's angle in degrees (0/90/180/270). This checks
    /// the direction vector's components for exact zero instead, which is
    /// mathematically the same condition (a purely horizontal or vertical
    /// line has one axis-delta of exactly zero) reached a different way -
    /// flagged here as a same-behavior restructuring, not a UDB deviation.
    /// </summary>
    public static int CalculateForWall(int lightLevel, Vector2 wallDirection)
    {
        if (lightLevel < FakeContrastThreshold)
        {
            if (wallDirection.Y == 0)
            {
                lightLevel = Clamp(lightLevel + HorizontalWallShade);
            }
            else if (wallDirection.X == 0)
            {
                lightLevel = Clamp(lightLevel + VerticalWallShade);
            }
        }

        return Calculate(lightLevel);
    }

    private static int Clamp(int level) => Math.Clamp(level, 0, 255);
}
