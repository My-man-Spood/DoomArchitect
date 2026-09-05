using System;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Ported from Ultimate Doom Builder's
/// <c>ClassicMode.MatchGridSizeToDisplayScale</c>: picks a grid size that
/// keeps the grid legible at any zoom level by targeting roughly a fixed
/// number of cells across the smaller visible screen dimension. The
/// result is always a power of two (including fractional ones, e.g. 0.5,
/// 0.125) - a direct port of UDB's integer round-up-to-power-of-two bit
/// trick, kept bit-for-bit rather than reimplemented as a logarithm, so
/// it produces exactly the same sizes UDB does at the same zoom levels.
/// </summary>
public static class DynamicGridSize
{
    public static float ForVisibleExtent(float minVisibleMapUnits)
    {
        var target = (int)MathF.Ceiling(minVisibleMapUnits / 4f);

        target--;
        target |= target >> 1;
        target |= target >> 2;
        target |= target >> 4;
        target |= target >> 8;
        target |= target >> 16;
        target++;

        return target / 8f;
    }
}
