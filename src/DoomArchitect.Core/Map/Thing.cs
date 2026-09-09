using System.Numerics;

namespace DoomArchitect.Core.Map;

/// <summary>
/// A map object - a monster, item, decoration, player start, etc. Only the
/// fields every format actually treats as always-present/meaningful are
/// modeled as typed properties (matching UDB's own required/defaulted
/// UDMF <c>thing</c> fields); everything else (id, pitch/roll/scale,
/// Hexen-style special/args, and every boolean flag) round-trips through
/// <see cref="CustomFields"/> instead - see the Things plan for why this
/// is enough for a faithful load-then-save without needing a translation
/// table this codebase has no data for yet.
/// </summary>
public sealed class Thing
{
    private readonly Dictionary<string, object> _customFields = new();

    internal Thing(Vector2 position, int type)
    {
        Position = position;
        Type = type;
    }

    public Vector2 Position { get; set; }

    /// <summary>Z-offset above the containing sector's floor. Classic binary format has no such field, so it's always 0 there.</summary>
    public double Height { get; set; }

    public int Angle { get; set; }

    /// <summary>The DoomEd number - which actor/item/decoration this is.</summary>
    public int Type { get; set; }

    /// <summary>
    /// The raw classic-format flags bitmask (skill levels, ambush,
    /// multiplayer-only, plus Boom/MBF extensions) - preserved as-is
    /// rather than decoded into named booleans, since that decoding is
    /// real translation-table work this codebase has no game-configuration
    /// data for yet. Always 0 for UDMF-loaded things, which instead carry
    /// their flags as named boolean fields in <see cref="CustomFields"/>.
    /// </summary>
    public ushort RawFlags { get; set; }

    /// <summary>
    /// Set whenever this Thing moves such that its rendered position is
    /// stale. Cleared by the rendering layer once it has resynced (see
    /// <c>MapView</c>); Core never clears it on its own - the same idiom
    /// as <see cref="Sector.NeedsRebuild"/>, named for what a Thing
    /// actually needs (a position sync, not a mesh rebuild).
    /// </summary>
    public bool NeedsUpdate { get; internal set; }

    public IReadOnlyDictionary<string, object> CustomFields => _customFields;

    internal void SetCustomField(string key, object value) => _customFields[key] = value;
}
