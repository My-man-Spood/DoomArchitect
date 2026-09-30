using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Things mode's Delete action - UDB's real <c>ThingsMode.DeleteItem</c>
/// (<c>ClassicModes/ThingsMode.cs</c>) - just removes each selected thing,
/// full stop, exactly like <see cref="DeleteLinedefsCommand"/>'s own
/// equally simple shape: a Thing has no adjacency of its own to cascade
/// through.
///
/// Not ported: UDB's own <c>BaseClassicMode.DeleteThings</c> "path
/// reconnecting" step - deleting an `InterpolationPoint`/`PathFollower`-
/// style thing mid-chain retargets the chain's own tag/arg links so the
/// path doesn't just break at the gap. This project has no typed
/// Thing `Args`/`Tag` model yet (only the raw <see cref="UniFields"/>
/// bag <see cref="Thing.Fields"/> already carries for every format-
/// specific field) - see TODO/editing-delete-actions.md.
/// </summary>
public sealed class DeleteThingsCommand : ICommand
{
    private readonly MapData map;
    private readonly IReadOnlyList<Thing> things;

    public DeleteThingsCommand(MapData map, IReadOnlyList<Thing> things)
    {
        this.map = map;
        this.things = things;
    }

    public void Do()
    {
        foreach (var thing in things) map.RemoveThing(thing);
    }

    public void Undo()
    {
        foreach (var thing in things) map.RestoreThing(thing);
    }
}
