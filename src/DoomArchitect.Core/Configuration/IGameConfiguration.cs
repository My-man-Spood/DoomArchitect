namespace DoomArchitect.Core.Configuration;

/// <summary>
/// A thing type entry as loaded from a <c>thingtypes</c> category/number
/// block - <see cref="SpriteName"/> is a full sprite lump name (e.g.
/// <c>"POSSA1"</c>), not just a 4-character prefix, so rendering can look
/// it up directly with no rotation-frame guessing. <see cref="ShowsDirection"/>
/// and <see cref="ColorIndex"/> mirror the <c>.cfg</c> <c>arrow</c>/
/// <c>color</c> fields - <see cref="ColorIndex"/> is the <c>.cfg</c>
/// <c>color</c> field value (0-19), resolved against
/// <c>Rendering.ThingCategoryColors</c> in the App layer (Core stays
/// rendering-agnostic).
/// <see cref="Category"/> is the raw <c>.cfg</c> category block key (e.g.
/// <c>"monsters"</c>) - the embedded thing-type picker's own grouping key,
/// same "plain taxonomy label" role <c>ActionInfo.Category</c> already
/// plays for the linedef action browser.
/// </summary>
/// <summary>
/// <paramref name="ClassName"/> is the real DECORATE/ZScript class this
/// static entry represents (e.g. <c>"ZombieMan"</c>) - a mod's own actor
/// that inherits from or replaces this name is how live actor discovery
/// (see the ZDoom namespace) resolves properties/sprite it doesn't itself
/// redeclare. Some entries have no real class at all (editor-only markers
/// like player starts) and use UDB's own <c>$</c>-prefixed pseudo-class
/// convention instead (e.g. <c>"$Player1Start"</c>) - never a real
/// inheritable name, kept as-is rather than stripped since nothing here
/// needs to treat it specially yet.
/// </summary>
public sealed record ThingTypeInfo(
    int DoomEdNum, string Title, string SpriteName, float Radius, float Height, bool Hangs,
    bool ShowsDirection, int ColorIndex, string Category, string ClassName);

/// <summary>
/// One argument slot (of the fixed 5, <c>arg0</c>-<c>arg4</c>) an action
/// may or may not actually use - <see cref="Used"/> is false when no
/// matching argN sub-block exists for this action (an unused slot gets a
/// generic disabled placeholder in the UI rather than being removed -
/// always 5 boxes, some disabled).
/// <see cref="EnumOptions"/> is null for a plain numeric argument, or the
/// value/label pairs to show as a dropdown instead when the action's
/// <c>.cfg</c> entry names a shared enum list.
/// <see cref="Str"/> (the <c>.cfg</c> <c>str</c> key) marks a slot that can
/// legitimately hold a string instead of a number - real UDB usage is
/// exactly arg0 of the ACS_Execute family (80/81/82/83/84/85/226): a
/// script reference naming the script instead of numbering it.
/// <see cref="TitleStr"/> (the <c>.cfg</c> <c>titlestr</c> key) is the
/// label to show while that slot is in its string form (e.g. "Script
/// Name" instead of "Script Number") - null when <see cref="Str"/> is
/// false, since nothing ever reads it then.
/// </summary>
public sealed record ArgumentInfo(string Title, bool Used, IReadOnlyList<ArgumentEnumOption>? EnumOptions, bool Str = false, string? TitleStr = null);

/// <summary>One labeled choice in a shared <c>enums</c> list an argument can reference by name.</summary>
public sealed record ArgumentEnumOption(long Value, string Title);

/// <summary>
/// A Hexen-style action special and its own arg0-arg4 metadata - shared
/// data, not Linedef-specific: a Thing's own <c>special</c>/<c>arg0-4</c>
/// fields resolve their argument metadata from this same table too, which
/// is why it's named plainly "Action" rather than "LinedefAction".
/// </summary>
public sealed record ActionInfo(int Number, string Title, string Category, IReadOnlyList<ArgumentInfo> Args);

public sealed record SectorSpecialInfo(int Number, string Title);

/// <summary>One entry from a real <c>skills</c> block (e.g. <c>3 = "Hurt me plenty";</c>) - the Test Map skill/monsters dropdown's own data source.</summary>
public sealed record SkillInfo(int Number, string Title);

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

    /// <summary>Every known action special, sorted by number - shared by the Linedef and Thing dialogs' own "browse actions" data source, mirroring <see cref="GetSectorSpecials"/>.</summary>
    IReadOnlyList<ActionInfo> GetActions();

    SectorSpecialInfo? GetSectorSpecial(int type);

    /// <summary>Every known sector special, sorted by number - the "browse specials" dialog's own data source.</summary>
    IReadOnlyList<SectorSpecialInfo> GetSectorSpecials();

    /// <summary>Every real per-sector UDMF boolean field this configuration defines - empty for a non-UDMF-namespace configuration (vanilla Doom/Doom2 have no such concept at all).</summary>
    IReadOnlyList<SectorFlagInfo> GetSectorFlags();

    /// <summary>Every real per-linedef UDMF boolean flag this configuration defines - same "empty for non-UDMF configs" rule as <see cref="GetSectorFlags"/>. Reuses <see cref="SectorFlagInfo"/>'s identical Key/Title shape rather than a new record.</summary>
    IReadOnlyList<SectorFlagInfo> GetLinedefFlags();

    /// <summary>Every real per-linedef UDMF activation-trigger field (e.g. <c>playercross</c>, <c>monsteruse</c>) - a distinct group from <see cref="GetLinedefFlags"/> in the UI, even though both are just named UDMF booleans under the hood.</summary>
    IReadOnlyList<SectorFlagInfo> GetLinedefActivations();

    /// <summary>Every real per-thing UDMF boolean flag this configuration defines - same "empty for non-UDMF configs" rule as <see cref="GetSectorFlags"/>. Reuses <see cref="SectorFlagInfo"/>'s identical Key/Title shape rather than a new record (third consumer now).</summary>
    IReadOnlyList<SectorFlagInfo> GetThingFlags();

    /// <summary>Known sector damage-type strings (e.g. <c>"Fire"</c>, <c>"Poison"</c>) - a fixed base list only; this project has no DECORATE parser to also discover map-defined ones.</summary>
    IReadOnlyList<string> GetDamageTypes();

    /// <summary>
    /// The <c>mixtexturesflats</c> <c>.cfg</c> setting: when true, a
    /// Sector's Floor/Ceiling texture picker and a Linedef's wall-texture
    /// picker each also offer the *other* namespace's names, since the
    /// engine's own texture manager doesn't actually distinguish them for
    /// either field - true for the ZDoom/GZDoom-family configs (inherited
    /// from <c>ZDoom_common.cfg</c>), false for vanilla Doom (matching
    /// <c>Doom_common.cfg</c>'s own explicit <c>false</c>, also this
    /// setting's default when a <c>.cfg</c> doesn't set it at all).
    /// </summary>
    bool MixTexturesAndFlats { get; }

    /// <summary>Every skill level this configuration defines, sorted by number - Test Map's own skill/monsters dropdown data source.</summary>
    IReadOnlyList<SkillInfo> GetSkills();

    /// <summary>
    /// The Test Map command-line template (e.g.
    /// <c>-iwad "%WP" -skill "%S" -file "%AP" "%F" -warp %L1%L2 %NM</c>) -
    /// see <see cref="TestLaunchCommandBuilder"/> for placeholder
    /// substitution. Empty when the <c>.cfg</c> doesn't set
    /// <c>testparameters</c> at all.
    /// </summary>
    string TestParameters { get; }

    /// <summary>
    /// The <c>.cfg</c> <c>testshortpaths</c> setting - real UDB converts
    /// every path substituted into the Test Map command line to its
    /// Windows 8.3 short form for source ports with poor long-path/space
    /// handling. Read here but deliberately not applied: short paths are a
    /// Windows filesystem concept with no equivalent on Linux/macOS, and
    /// this project has no Windows-only code path to gate it behind yet -
    /// a real, flagged gap rather than a silent no-op nobody could find.
    /// </summary>
    bool TestShortPaths { get; }

    /// <summary>
    /// The `.cfg` <c>decorategames</c> setting - a lowercase, space-free
    /// engine-family tag (e.g. <c>"doom"</c>) a DECORATE/ZScript actor's own
    /// `$game` property is checked against (see
    /// <see cref="ZDoom.ActorStructure.CheckActorSupported"/>) to decide
    /// whether it applies to this game at all - an actor with no `$game`
    /// property always applies.
    /// </summary>
    string DecorateGames { get; }

    /// <summary>
    /// Real engine archives (e.g. <c>gzdoom.pk3</c>) this configuration
    /// expects to be present among a map's own resources - see
    /// <see cref="RequiredArchive"/>. Empty for configs with no such
    /// requirement (vanilla Doom/Doom2).
    /// </summary>
    IReadOnlyList<RequiredArchive> GetRequiredArchives();
}

/// <summary>
/// One `.cfg` `requiredarchives` entry (e.g. `gzdoom.pk3`) - a content
/// fingerprint (<see cref="Entries"/>) used to recognize a specific
/// resource by what it actually contains, not its file name. Verified
/// against a resource via <see cref="ZDoom.RequiredArchiveDetector"/>.
/// </summary>
public sealed record RequiredArchive(string Id, string FileName, bool ExcludeFromTesting, IReadOnlyList<RequiredArchiveEntry> Entries);

/// <summary>One fingerprint condition - a resource must define a ZScript/DECORATE class named <see cref="ClassName"/> and/or contain a lump/file named <see cref="LumpName"/>. Exactly one of the two is set per entry.</summary>
public sealed record RequiredArchiveEntry(string? ClassName, string? LumpName);

public enum GameConfigurationKind
{
    Doom,
    Doom2,
    GZDoomDoom2UDMF,
}
