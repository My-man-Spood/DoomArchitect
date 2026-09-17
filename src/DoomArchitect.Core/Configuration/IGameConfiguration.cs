namespace DoomArchitect.Core.Configuration;

/// <summary>
/// A thing type entry as loaded from a <c>thingtypes</c> category/number
/// block - <see cref="SpriteName"/> is a full sprite lump name (e.g.
/// <c>"POSSA1"</c>, matching real UDB <c>.cfg</c> data), not just a 4-
/// character prefix, so rendering can look it up directly with no
/// rotation-frame guessing. <see cref="ShowsDirection"/> and
/// <see cref="ColorIndex"/> mirror two more real UDB <c>.cfg</c> fields
/// (<c>arrow</c>/<c>color</c>) this project hadn't modeled until now -
/// <see cref="ColorIndex"/> is the real <c>.cfg</c> <c>color</c> field
/// value (0-19), resolved against <c>Rendering.ThingCategoryColors</c> in
/// the App layer (Core stays rendering-agnostic) - that palette is UDB's
/// own real shipped-default thing-color palette
/// (<c>ColorCollection.THINGCOLOR00</c>-<c>19</c>), not a DoomArchitect
/// invention, since there's no reason to diverge from something this
/// familiar to a UDB user.
/// <see cref="Category"/> is the raw <c>.cfg</c> category block key (e.g.
/// <c>"monsters"</c>) - the embedded thing-type picker's own grouping key,
/// same "plain taxonomy label" role <c>ActionInfo.Category</c> already
/// plays for the linedef action browser.
/// </summary>
public sealed record ThingTypeInfo(
    int DoomEdNum, string Title, string SpriteName, float Radius, float Height, bool Hangs,
    bool ShowsDirection, int ColorIndex, string Category);

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
public sealed record ArgumentInfo(string Title, bool Used, IReadOnlyList<ArgumentEnumOption>? EnumOptions);

/// <summary>One labeled choice in a shared <c>enums</c> list an argument can reference by name.</summary>
public sealed record ArgumentEnumOption(long Value, string Title);

/// <summary>
/// A Hexen-style action special and its own real arg0-arg4 metadata -
/// genuinely shared data, not Linedef-specific (named plainly "Action",
/// not "LinedefAction", once confirmed a Thing's own <c>special</c>/
/// <c>arg0-4</c> fields resolve their argument metadata from this exact
/// same table in real UDB - see <c>ArgumentsControl</c>'s own parallel
/// <c>SetValue(Linedef,...)</c>/<c>SetValue(Thing,...)</c> overloads).
/// </summary>
public sealed record ActionInfo(int Number, string Title, string Category, IReadOnlyList<ArgumentInfo> Args);

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

    /// <summary>Every known thing type, sorted by DoomEd number - the embedded thing-type picker's own data source, mirroring <see cref="GetSectorSpecials"/>/<see cref="GetActions"/>.</summary>
    IReadOnlyList<ThingTypeInfo> GetThingTypes();

    ActionInfo? GetAction(int special);

    /// <summary>Every known action special, sorted by number - shared by the Linedef and Thing dialogs' own "browse actions" data source (a Thing's own <c>special</c>/<c>arg0-4</c> resolve against this same table in real UDB), mirroring <see cref="GetSectorSpecials"/>.</summary>
    IReadOnlyList<ActionInfo> GetActions();

    SectorSpecialInfo? GetSectorSpecial(int type);

    /// <summary>Every known sector special, sorted by number - matches UDB's real <c>SortedSectorEffects</c> ordering; the "browse specials" dialog's own data source.</summary>
    IReadOnlyList<SectorSpecialInfo> GetSectorSpecials();

    /// <summary>Every real per-sector UDMF boolean field this configuration defines - empty for a non-UDMF-namespace configuration (vanilla Doom/Doom2 have no such concept at all).</summary>
    IReadOnlyList<SectorFlagInfo> GetSectorFlags();

    /// <summary>Every real per-linedef UDMF boolean flag this configuration defines - same "empty for non-UDMF configs" rule as <see cref="GetSectorFlags"/>. Reuses <see cref="SectorFlagInfo"/>'s identical Key/Title shape rather than a new record.</summary>
    IReadOnlyList<SectorFlagInfo> GetLinedefFlags();

    /// <summary>Every real per-linedef UDMF activation-trigger field (e.g. <c>playercross</c>, <c>monsteruse</c>) - a distinct group from <see cref="GetLinedefFlags"/> in UDB's own real dialog, even though both are just named UDMF booleans under the hood.</summary>
    IReadOnlyList<SectorFlagInfo> GetLinedefActivations();

    /// <summary>Every real per-thing UDMF boolean flag this configuration defines - same "empty for non-UDMF configs" rule as <see cref="GetSectorFlags"/>. Reuses <see cref="SectorFlagInfo"/>'s identical Key/Title shape rather than a new record (third consumer now).</summary>
    IReadOnlyList<SectorFlagInfo> GetThingFlags();

    /// <summary>Known sector damage-type strings (e.g. <c>"Fire"</c>, <c>"Poison"</c>) - a fixed base list only; this project has no DECORATE parser to also discover map-defined ones the way UDB's real damage-type combo does.</summary>
    IReadOnlyList<string> GetDamageTypes();

    /// <summary>
    /// UDB's real <c>mixtexturesflats</c> <c>.cfg</c> setting, verified
    /// directly against its source: when true, a Sector's Floor/Ceiling
    /// texture picker (real UDB's <c>FlatSelectorControl</c>) and a
    /// Linedef's wall-texture picker (<c>TextureSelectorControl</c>) each
    /// also offer the *other* namespace's names, since the engine's own
    /// texture manager doesn't actually distinguish them for either field
    /// - true for the ZDoom/GZDoom-family configs (inherited from
    /// <c>ZDoom_common.cfg</c>), false for vanilla Doom (matching
    /// <c>Doom_common.cfg</c>'s own explicit <c>false</c>, also this
    /// setting's real default when a <c>.cfg</c> doesn't set it at all).
    /// </summary>
    bool MixTexturesAndFlats { get; }
}

public enum GameConfigurationKind
{
    Doom,
    Doom2,
    GZDoomDoom2UDMF,
}
