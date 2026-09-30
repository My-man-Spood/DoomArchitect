using System.Linq;
using DoomArchitect.Core.Geometry;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Sectors mode's Delete action - UDB's real <c>SectorsMode.DeleteItem</c>
/// (<c>ClassicModes/SectorsMode.cs</c>, minus its own optional "also delete
/// things inside the sector" step, which is gated behind a UDB preference
/// this project has no settings surface for yet - see TODO/TODO.md), ported one
/// sector at a time exactly as UDB's own sequential loop does (not a single
/// batched pass over every sector's geometry at once): for each selected
/// sector, in order,
/// <list type="number">
/// <item>detach every one of its own sidedefs from their linedef (setting
/// that side to null) and from the sector itself, then remove the sector
/// (<see cref="GeometryStitcher.DetachSectorSidedefs"/>, shared with
/// <c>DissolveLinedefsCommand</c>'s own equally-real
/// <c>MergeInvalidSectors</c> repair step);</item>
/// <item>any of those linedefs left with *both* sides now null (it only
/// ever bordered this sector, on both sides, or bordered nothing else) is
/// fully removed too - no vertex cleanup, matching
/// <see cref="DeleteLinedefsCommand"/>'s own equally blunt behavior;</item>
/// <item>any left with only a Back side gets flipped
/// (<see cref="GeometryStitcher.FlipBackwardLinedefs"/> - the format
/// convention that Front must exist whenever Back does);</item>
/// <item>whatever survives (now guaranteed one-sided, Front) gets a
/// simplified version of UDB's own texture cleanup: a two-sided wall's
/// Upper/Lower become meaningless once one-sided, so whichever of them
/// isn't blank gets copied into Middle if Middle is itself blank (so the
/// wall doesn't render as missing/black), then Upper/Lower are cleared.
/// UDB's own real <c>RemoveUnneededTextures</c> additionally skips this
/// whole step whenever the line/either sector carries a tag or the line
/// an action special (its own way of not clobbering a scripted texture
/// swap) - not ported, since this project has no typed action/tag model
/// to check against yet (only the raw <see cref="UniFields"/> bag) - see
/// TODO/TODO.md.</item>
/// </list>
/// Processing sectors one at a time, immediately fixing up their own
/// former linedefs before moving to the next selected sector, is what
/// makes two adjacent selected sectors sharing a wall resolve correctly
/// for free: the first sector's removal leaves the shared wall one-sided
/// (flipped so its survivor is Front); the second sector's own removal
/// then detaches that same survivor and finds it newly fully orphaned.
/// </summary>
public sealed class DeleteSectorsCommand : ICommand
{
    private readonly MapData map;
    private readonly IReadOnlyList<Sector> sectors;
    private readonly List<Action> undoActions = new();

    public DeleteSectorsCommand(MapData map, IReadOnlyList<Sector> sectors)
    {
        this.map = map;
        this.sectors = sectors;
    }

    public void Do()
    {
        undoActions.Clear();

        foreach (var sector in sectors)
        {
            if (!map.Sectors.Contains(sector)) continue;

            var formerLinedefs = GeometryStitcher.DetachSectorSidedefs(map, sector, undoActions);

            var surviving = new List<Linedef>();
            foreach (var linedef in formerLinedefs)
            {
                if (linedef.Front == null && linedef.Back == null)
                {
                    map.RemoveLinedef(linedef);
                    undoActions.Add(() => map.RestoreLinedef(linedef));
                }
                else
                {
                    surviving.Add(linedef);
                }
            }

            // Self-filtering - only actually flips a line left with just a
            // Back side; a survivor whose Front already belonged to a
            // different, untouched sector needs no flip at all.
            GeometryStitcher.FlipBackwardLinedefs(surviving, undoActions);

            foreach (var linedef in surviving) FixupOneSidedTextures(linedef.Front!);
        }
    }

    public void Undo()
    {
        for (var i = undoActions.Count - 1; i >= 0; i--) undoActions[i]();
    }

    private void FixupOneSidedTextures(Sidedef front)
    {
        var originalUpper = front.UpperTexture;
        var originalMiddle = front.MiddleTexture;
        var originalLower = front.LowerTexture;

        var newMiddle = originalMiddle;
        if (newMiddle == "-")
        {
            if (originalUpper != "-") newMiddle = originalUpper;
            else if (originalLower != "-") newMiddle = originalLower;
        }

        if (newMiddle == originalMiddle && originalUpper == "-" && originalLower == "-") return;

        front.MiddleTexture = newMiddle;
        front.UpperTexture = "-";
        front.LowerTexture = "-";

        undoActions.Add(() =>
        {
            front.UpperTexture = originalUpper;
            front.MiddleTexture = originalMiddle;
            front.LowerTexture = originalLower;
        });
    }
}
