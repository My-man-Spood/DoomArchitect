using System.Text.RegularExpressions;
using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.Configuration;

/// <summary>
/// Best-guess default selection for the always-shown Doom/Doom2 picker a
/// WAD load presents (see <c>OpenMapMenu</c>) - never authoritative on its
/// own, since UDB itself has no reliable way to tell Doom and Doom2 apart
/// from a bare WAD's content either (confirmed via its own source: it
/// relies on an explicit project setting, a sidecar file, or asking the
/// user). Signals are checked in order, most reliable first, rather than
/// combined into a weighted score - simpler to write, read, and test for
/// the same practical result:
///
/// 1. Map name pattern - <c>ExMy</c> is a very reliable Doom-family
///    signal, <c>MAPxy</c> a very reliable Doom2-family signal.
/// 2. Content signature - any Thing anywhere in the map using a DoomEd
///    number that only exists in Doom2's table (not Doom's) is a strong
///    Doom2 signal - Doom2 only ever adds new thing types, never removes
///    or renumbers Doom's.
/// 3. Filename containing "2" as a last-resort weak hint.
/// 4. Default to Doom.
/// </summary>
public static class GameConfigurationDetector
{
    private static readonly Regex DoomMapName = new(@"^E\dM\d$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Doom2MapName = new(@"^MAP\d\d$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static GameConfigurationKind Detect(MapData map, string mapName, string? wadFileName)
    {
        if (DoomMapName.IsMatch(mapName)) return GameConfigurationKind.Doom;
        if (Doom2MapName.IsMatch(mapName)) return GameConfigurationKind.Doom2;

        var doom = GameConfigurations.Get(GameConfigurationKind.Doom);
        var doom2 = GameConfigurations.Get(GameConfigurationKind.Doom2);
        foreach (var thing in map.Things)
        {
            if (doom.GetThingType(thing.Type) == null && doom2.GetThingType(thing.Type) != null)
            {
                return GameConfigurationKind.Doom2;
            }
        }

        if (wadFileName != null && wadFileName.Contains('2'))
        {
            return GameConfigurationKind.Doom2;
        }

        return GameConfigurationKind.Doom;
    }
}
