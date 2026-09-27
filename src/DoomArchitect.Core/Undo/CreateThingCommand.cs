using System.Numerics;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Undo;

/// <summary>
/// Creates a new Thing at a given position with default settings
/// (angle, flags) applied - the same action as a right-click-on-empty-
/// space placement.
/// </summary>
public sealed class CreateThingCommand : ICommand
{
    /// <summary>
    /// Player 1 Start - the default thing type, before any session ever
    /// customizes it. Only ever used as this class's own fallback (a
    /// caller that genuinely has no better value, e.g. a Core test) - the
    /// App layer's real caller (<c>ThingOverlayHandler</c>) instead
    /// passes whatever <c>MapOverlay.LastUsedThingType</c> currently
    /// holds, a session value kept updated with whatever type was last
    /// actually placed.
    /// </summary>
    public const int DefaultType = 1;

    public const int DefaultAngle = 0;

    /// <summary>Easy | Medium | Hard (bits 1, 2, 4) - UDB's classic-format <c>defaultthingflags</c> (<c>Doom_misc.cfg</c>: <c>{ 1; 2; 4; }</c>). Not Ambush (8) or Multiplayer-only (16).</summary>
    public const ushort DefaultRawFlags = 0b0111;

    private readonly MapData map;
    private readonly Vector2 position;
    private readonly int type;
    private Thing? thing;

    public CreateThingCommand(MapData map, Vector2 position, int type = DefaultType)
    {
        this.map = map;
        this.position = position;
        this.type = type;
    }

    /// <summary>The thing this command creates - only meaningful after <see cref="Do"/> has run.</summary>
    public Thing CreatedThing => thing!;

    public void Do()
    {
        thing = map.CreateThing(position, type);
        thing.Angle = DefaultAngle;
        thing.RawFlags = DefaultRawFlags;
    }

    public void Undo() => map.RemoveThing(thing!);
}
