namespace DoomArchitect.Core.Configuration;

/// <summary>
/// A thing type entry as loaded from a <c>thingtypes</c> category/number
/// block - <see cref="SpriteName"/> is a full sprite lump name (e.g.
/// <c>"POSSA1"</c>, matching real UDB <c>.cfg</c> data), not just a 4-
/// character prefix, so rendering can look it up directly with no
/// rotation-frame guessing. <see cref="ShowsDirection"/> and
/// <see cref="ColorIndex"/> mirror two more real UDB <c>.cfg</c> fields
/// (<c>arrow</c>/<c>color</c>) this project hadn't modeled until now -
/// <see cref="ColorIndex"/> is a small palette index the same way UDB's
/// own is, but resolved against DoomArchitect's own palette
/// (<c>Rendering.ThingCategoryColors</c> in the App layer - Core stays
/// rendering-agnostic), not UDB's actual editor color scheme, which is
/// UDB's own UI design choice rather than a vanilla-Doom fact.
/// </summary>
public sealed record ThingTypeInfo(
    int DoomEdNum, string Title, string SpriteName, float Radius, float Height, bool Hangs,
    bool ShowsDirection, int ColorIndex);

/// <summary>
/// One argument slot (of the fixed 5, <c>arg0</c>-<c>arg4</c>) an action
/// may or may not actually use - <see cref="Used"/> mirrors UDB's real
/// "does this argN block even exist for this action" check (an unused
/// slot gets a generic disabled placeholder in the UI rather than being
/// removed, matching UDB's real always-5-boxes layout).
/// <see cref="EnumOptions"/> is null for a plain numeric argument, or the
/// value/label pairs to show as a dropdown instead when the action's
/// <c>.cfg</c> entry names a shared enum list.
/// </summary>
public sealed record LinedefArgumentInfo(string Title, bool Used, IReadOnlyList<LinedefArgumentEnumOption>? EnumOptions);

/// <summary>One labeled choice in a shared <c>enums</c> list an argument can reference by name.</summary>
public sealed record LinedefArgumentEnumOption(long Value, string Title);

public sealed record LinedefActionInfo(int Number, string Title, string Category, IReadOnlyList<LinedefArgumentInfo> Args);

public sealed record SectorSpecialInfo(int Number, string Title);

/// <summary>One real per-sector UDMF boolean field (e.g. <c>silent</c>, <c>nofallingdamage</c>) - <see cref="Key"/> is the literal UDMF field name, read/written on a sector's <c>Fields</c> bag exactly like any other named field.</summary>
public sealed record SectorFlagInfo(string Key, string Title);

/// <summary>
/// Looks up what a DoomEd number/linedef special/sector type actually
/// means. An interface rather than a concrete class for two reasons: this
/// project's stated direction is full UDB feature parity including
/// DECORATE-defined custom actors (not built yet, but a future
/// DECORATE-lump-driven implementation should be able to sit behind this
/// same seam without any caller changing), and a user should eventually be
/// able to point DoomArchitect at their own external <c>.cfg</c> file
/// (real or DoomArchitect-authored) rather than only ever getting the two
/// bundled configs - see <see cref="GameConfigurationLoader"/> and
/// <see cref="GameConfigurations"/> for what's actually built today.
/// </summary>
public interface IGameConfiguration
{
    ThingTypeInfo? GetThingType(int doomEdNum);

    LinedefActionInfo? GetLinedefAction(int special);

    /// <summary>Every known linedef action, sorted by number - the "browse actions" dialog's own data source, mirroring <see cref="GetSectorSpecials"/>.</summary>
    IReadOnlyList<LinedefActionInfo> GetLinedefActions();

    SectorSpecialInfo? GetSectorSpecial(int type);

    /// <summary>Every known sector special, sorted by number - matches UDB's real <c>SortedSectorEffects</c> ordering; the "browse specials" dialog's own data source.</summary>
    IReadOnlyList<SectorSpecialInfo> GetSectorSpecials();

    /// <summary>Every real per-sector UDMF boolean field this configuration defines - empty for a non-UDMF-namespace configuration (vanilla Doom/Doom2 have no such concept at all).</summary>
    IReadOnlyList<SectorFlagInfo> GetSectorFlags();

    /// <summary>Every real per-linedef UDMF boolean flag this configuration defines - same "empty for non-UDMF configs" rule as <see cref="GetSectorFlags"/>. Reuses <see cref="SectorFlagInfo"/>'s identical Key/Title shape rather than a new record.</summary>
    IReadOnlyList<SectorFlagInfo> GetLinedefFlags();

    /// <summary>Every real per-linedef UDMF activation-trigger field (e.g. <c>playercross</c>, <c>monsteruse</c>) - a distinct group from <see cref="GetLinedefFlags"/> in UDB's own real dialog, even though both are just named UDMF booleans under the hood.</summary>
    IReadOnlyList<SectorFlagInfo> GetLinedefActivations();

    /// <summary>Known sector damage-type strings (e.g. <c>"Fire"</c>, <c>"Poison"</c>) - a fixed base list only; this project has no DECORATE parser to also discover map-defined ones the way UDB's real damage-type combo does.</summary>
    IReadOnlyList<string> GetDamageTypes();
}

public enum GameConfigurationKind
{
    Doom,
    Doom2,
    GZDoomDoom2UDMF,
}
