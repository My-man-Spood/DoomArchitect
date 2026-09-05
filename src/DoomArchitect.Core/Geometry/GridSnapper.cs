using System.Numerics;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// Ported from Ultimate Doom Builder's <c>GridSetup.SnappedToGrid</c>:
/// rounds each axis independently to the nearest multiple of the grid
/// size, using the runtime's default (round-half-to-even) rounding mode -
/// same as UDB, since it doesn't specify a <c>MidpointRounding</c> either.
/// UDB's version also supports a rotated/offset grid and clamps the
/// result to the map format's configured boundaries; neither exists in
/// this codebase yet (no grid rotation setting, no game-config map
/// boundaries), so both are left out rather than faked.
/// </summary>
public static class GridSnapper
{
    public static Vector2 Snap(Vector2 position, float gridSize) =>
        new(MathF.Round(position.X / gridSize) * gridSize, MathF.Round(position.Y / gridSize) * gridSize);
}
