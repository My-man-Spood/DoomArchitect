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

public sealed record LinedefActionInfo(int Number, string Title, string Category);

public sealed record SectorSpecialInfo(int Number, string Title);

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

    SectorSpecialInfo? GetSectorSpecial(int type);
}

public enum GameConfigurationKind
{
    Doom,
    Doom2,
}
