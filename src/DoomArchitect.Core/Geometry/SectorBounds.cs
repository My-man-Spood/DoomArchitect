using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// A sector's own axis-aligned bounding box, derived from
/// <see cref="SectorTracer.Trace"/>'s traced vertices (every loop, outer
/// and holes alike - a hole can never extend past its own outer loop, so
/// including it doesn't change the result, and skipping the outer/hole
/// distinction keeps this simple). Used as this project's own sector
/// "anchor point" for screen-space overlays (tag labels, tag-arrow
/// endpoints) - UDB's own real label-point algorithm (a precomputed
/// pole-of-inaccessibility per sector) isn't ported here; this is exactly
/// UDB's own real fallback for when it lacks one
/// (<c>s.BBox.X + s.BBox.Width / 2, ...</c>), not an arbitrary shortcut.
/// </summary>
public readonly record struct SectorBounds(Vector2 Min, Vector2 Max)
{
    public Vector2 Center => (Min + Max) / 2f;

    public static SectorBounds Compute(Sector sector)
    {
        Vector2? min = null;
        Vector2? max = null;

        foreach (var loop in SectorTracer.Trace(sector))
        {
            foreach (var vertex in loop.Vertices)
            {
                var position = vertex.Position;
                min = min == null ? position : Vector2.Min(min.Value, position);
                max = max == null ? position : Vector2.Max(max.Value, position);
            }
        }

        return new SectorBounds(min ?? Vector2.Zero, max ?? Vector2.Zero);
    }
}
