namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// One parsed state label's frame sequence (e.g. everything under
/// `Spawn:`) - ported from UDB's real <c>StateStructure</c>. The one real
/// adaptation: UDB resolves a cross-actor `goto OtherClass::State` through
/// its ambient <c>General.Map.Data.GetZDoomActor</c> singleton; this project
/// has no ambient lookup like that, so <see cref="GetSprite"/> takes an
/// optional lookup delegate instead. Phase 2 (parsing a single resource in
/// isolation) has no merged cross-resource actor set to resolve against
/// yet, so it's fine to omit - falls back to this state's own first sprite,
/// same as when the goto target genuinely isn't found. Phase 4 (the merge
/// step) is expected to supply a real lookup once the merged actor
/// dictionary exists - tracked in TODO/TODO.md, not a silent gap.
/// </summary>
public class StateStructure
{
    public sealed class FrameInfo
    {
        public string Sprite = string.Empty;
        public string? LightName;
        public bool Bright;
        public int Duration; // used by TrimLeft

        public bool IsEmpty() => Sprite.StartsWith("TNT1") || Duration == 0;
    }

    // `internal` in UDB's own source - `public` here for the same
    // no-InternalsVisibleTo reason as the rest of this port.
    public readonly List<FrameInfo> Sprites = new();
    public StateGoto? GotoState;

    public StateStructure() { }

    /// <summary>A one-frame state with just a sprite name - used when a class inherits a sprite it never declares a real state for.</summary>
    public StateStructure(string spriteName)
    {
        Sprites.Add(new FrameInfo { Sprite = spriteName });
    }

    public int SpritesCount => Sprites.Count;

    /// <summary>Removes leading TNT1 (empty) frames from the start of the sequence, unless every frame is empty.</summary>
    internal void TrimLeft()
    {
        var firstNonEmpty = -1;
        for (var i = 0; i < Sprites.Count; i++)
        {
            if (!Sprites[i].IsEmpty())
            {
                firstNonEmpty = i;
                break;
            }
        }

        if (firstNonEmpty > 0) Sprites.RemoveRange(0, firstNonEmpty);
    }

    /// <summary>Finds the first valid sprite, following a `goto` chain (via <paramref name="lookupActor"/>, when supplied) up to one level of cross-actor indirection at a time.</summary>
    public FrameInfo GetSprite(int index, Func<string, ActorStructure?>? lookupActor = null) =>
        GetSprite(index, new HashSet<StateStructure>(), lookupActor);

    private FrameInfo GetSprite(int index, HashSet<StateStructure> visited, Func<string, ActorStructure?>? lookupActor)
    {
        if (index < Sprites.Count) return Sprites[index];

        if (GotoState != null && lookupActor != null)
        {
            var actor = lookupActor(GotoState.ClassName);
            var state = actor?.GetState(GotoState.StateName);
            if (state != null && !visited.Contains(state))
            {
                visited.Add(this);
                return state.GetSprite(GotoState.SpriteOffset, visited, lookupActor);
            }
        }

        // No (resolvable) goto - the behavior really should depend on the
        // flow-control keyword (loop/stop), but UDB's own port doesn't
        // bother either, so neither does this.
        return Sprites.Count > 0 ? Sprites[0] : new FrameInfo();
    }
}
