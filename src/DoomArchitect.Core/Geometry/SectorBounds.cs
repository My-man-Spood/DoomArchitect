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
/// endpoints). UDB itself computes a proper pole-of-inaccessibility per
/// sector and only falls back to bbox-center
/// (<c>s.BBox.X + s.BBox.Width / 2, ...</c>) when it lacks one; this
/// project always uses that fallback formula rather than the full
/// algorithm - not an arbitrary shortcut, since it's the same formula UDB
/// itself falls back to.
/// </summary>
public readonly record struct SectorBounds(Vector2 Min, Vector2 Max)
{
    public Vector2 Center => (Min + Max) / 2f;

    /// <summary>UDB's own real bounding-box-area comparison (<c>Dissolve</c>'s "keep the bigger of the two sectors" rule) - not the sector's actual floor area, just its bbox's.</summary>
    public float Area => (Max.X - Min.X) * (Max.Y - Min.Y);

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
