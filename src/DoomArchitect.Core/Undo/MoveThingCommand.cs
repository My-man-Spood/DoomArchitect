using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

public sealed class MoveThingCommand : ICommand
{
    private readonly MapData map;
    private readonly Thing thing;
    private readonly Vector2 from;
    private readonly Vector2 to;

    public MoveThingCommand(MapData map, Thing thing, Vector2 from, Vector2 to)
    {
        this.map = map;
        this.thing = thing;
        this.from = from;
        this.to = to;
    }

    public void Do() => map.MoveThing(thing, to);

    public void Undo() => map.MoveThing(thing, from);
}
