using System.Linq;
using System.Numerics;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Draw mode's own real command: builds one brand-new, fully self-
/// contained sector from a closed loop of points (Draw Lines Phase 1 -
/// see TODO.md/the plan for the deferred Phase 2, which stitches into
/// already-existing geometry instead). One atomic command for the whole
/// loop, matching UDB's own real "one undo step per draw session"
/// (<c>UndoManager.CreateUndo("Line draw")</c> brackets its entire
/// <c>Tools.DrawLines</c> call as a single snapshot) - not a
/// <see cref="CommandGroup"/> of smaller per-element commands, since later
/// elements in such a group would need to reference vertices an earlier
/// command in the same group hasn't created yet at construction time.
///
/// Front/back sidedef assignment follows <see cref="PolygonWinding.IsClockwise"/>
/// exactly - <see cref="Geometry.SectorTracer"/>'s own documented
/// convention (a front sidedef is walked Start-to-End, a back sidedef
/// End-to-Start, either way the sector ends up on the walker's right) and
/// the existing <c>MapDataTestExtensions.CreateClosedSector</c> test
/// helper's own real precedent both agree: a clockwise loop's edges get
/// <c>front = the new sector</c>; a counter-clockwise loop's edges get
/// <c>back = the new sector</c> (front stays null - a one-sided wall,
/// exactly like a real self-contained new room's outer boundary).
///
/// Redo-safe by construction: re-running <see cref="Do"/> after an
/// <see cref="Undo"/> creates fresh <see cref="Vertex"/>/<see cref="Linedef"/>/
/// <see cref="Sector"/> instances rather than reusing the removed ones -
/// safe here specifically because nothing outside this command ever holds
/// a reference to them (unlike e.g. a move command, which must act on the
/// exact same pre-existing element every time).
///
/// Default floor/ceiling/wall textures, heights, and brightness all match
/// UDB's own real values, verified directly against its source
/// (<c>Tools.MakeSector</c>'s no-adjacent-sector fallback branch,
/// <c>ProgramConfiguration</c>'s real default-settings resolution, and
/// <c>Game_Doom.cfg</c>'s own real <c>defaultfloortexture</c>/
/// <c>defaultceilingtexture</c>/<c>defaultwalltexture</c> values) - not
/// <see cref="Sector"/>'s own plain class defaults (<c>"-"</c>/<c>"-"</c>/
/// <c>160</c>), which were never meant to represent this. UDB's own real
/// defaults are per-*game* (Doom/Heretic/Hexen/Strife each have their
/// own), but every game configuration this project currently bundles
/// (Doom/Doom2/GZDoomDoom2UDMF) is Doom-family, so the Doom values apply
/// uniformly with no game-configuration parameter needed yet - revisit if
/// a non-Doom-family configuration (Heretic/Hexen/Strife) is ever added.
///
/// <see cref="DefaultWallTexture"/> is the one deliberate exception:
/// `Game_Doom.cfg`'s own literal is <c>"STARTAN"</c> (confirmed directly
/// in UDB's source), but no texture by that exact name actually exists in
/// the real IWAD - only <c>"STARTAN2"</c> does (also this project's own
/// existing real-texture-name convention throughout its test fixtures).
/// In real UDB this raw literal is rarely ever user-visible: its own
/// resolution (<c>ProgramConfiguration.FindDefaultDrawSettings</c>) scans
/// the currently loaded resources for an already-used, genuinely-existing
/// texture first, only falling back to the config literal as a last
/// resort. This command has no equivalent "smart scan loaded resources"
/// step (a real Phase 2/3-sized feature of its own, not built), so its
/// own default is exposed directly and unconditionally on every draw -
/// using the real, existing <c>"STARTAN2"</c> here instead of blindly
/// reproducing UDB's rarely-seen fallback literal is the correct choice,
/// not a divergence from UDB's actual intent.
///
/// The wall texture only ever applies to a "required" sidedef part -
/// every sidedef this command creates is a one-sided wall's Middle, which
/// is always required (there's nothing else to see), matching exactly
/// the one real case UDB's own <c>Sidedef.MiddleRequired()</c>/
/// <c>ApplyDefaultsToSidedef</c> would actually write a default texture
/// into for a sector drawn in empty space.
/// </summary>
public sealed class CreateSectorLoopCommand : ICommand
{
    public const double DefaultFloorHeight = 0;
    public const double DefaultCeilingHeight = 128;
    public const int DefaultBrightness = 192;
    public const string DefaultFloorTexture = "FLOOR0_1";
    public const string DefaultCeilingTexture = "CEIL1_1";
    public const string DefaultWallTexture = "STARTAN2";

    private readonly MapData map;
    private readonly IReadOnlyList<Vector2> loopPositions;
    private readonly double floorHeight;
    private readonly double ceilingHeight;
    private readonly string floorTexture;
    private readonly string ceilingTexture;
    private readonly string wallTexture;

    public CreateSectorLoopCommand(
        MapData map, IReadOnlyList<Vector2> loopPositions,
        double floorHeight = DefaultFloorHeight, double ceilingHeight = DefaultCeilingHeight,
        string floorTexture = DefaultFloorTexture, string ceilingTexture = DefaultCeilingTexture,
        string wallTexture = DefaultWallTexture)
    {
        this.map = map;
        this.loopPositions = loopPositions;
        this.floorHeight = floorHeight;
        this.ceilingHeight = ceilingHeight;
        this.floorTexture = floorTexture;
        this.ceilingTexture = ceilingTexture;
        this.wallTexture = wallTexture;
    }

    private Sector? sector;
    private List<Vertex>? vertices;
    private List<Linedef>? linedefs;

    public void Do()
    {
        sector = map.CreateSector(floorHeight, ceilingHeight);
        sector.FloorTexture = floorTexture;
        sector.CeilingTexture = ceilingTexture;
        sector.Brightness = DefaultBrightness;

        vertices = loopPositions.Select(map.CreateVertex).ToList();

        var clockwise = PolygonWinding.IsClockwise(loopPositions);
        linedefs = new List<Linedef>(vertices.Count);
        for (var i = 0; i < vertices.Count; i++)
        {
            var start = vertices[i];
            var end = vertices[(i + 1) % vertices.Count];
            var linedef = clockwise
                ? map.CreateLinedef(start, end, front: sector, back: null)
                : map.CreateLinedef(start, end, front: null, back: sector);
            (linedef.Front ?? linedef.Back)!.MiddleTexture = wallTexture;
            linedefs.Add(linedef);
        }
    }

    public void Undo()
    {
        foreach (var linedef in linedefs!) map.RemoveLinedef(linedef);
        foreach (var vertex in vertices!) map.RemoveVertex(vertex);
        map.RemoveSector(sector!);
    }
}
