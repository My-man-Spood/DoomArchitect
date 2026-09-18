using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Geometry;

/// <summary>
/// One directed side of a linedef - a linedef plus which side, matching
/// UDB's own real type of the same name. <see cref="Front"/> true means
/// this side is walked Start-to-End; false means End-to-Start - either
/// way, matching <see cref="SectorTracer"/>'s own documented convention,
/// whatever this side ends up bordering is on the walker's right.
///
/// Deliberately not tied to an actual <see cref="Sidedef"/> the way
/// <see cref="SectorTracer"/>'s own tracing is - <see cref="BoundaryTracer"/>
/// walks the map's raw vertex/linedef topology (old and newly-drawn
/// linedefs alike, whether or not either side has a real Sidedef/Sector
/// yet), so this only ever needs a <see cref="Linedef"/> reference and a
/// direction, nothing more.
/// </summary>
public readonly record struct LinedefSide(Linedef Linedef, bool Front);
