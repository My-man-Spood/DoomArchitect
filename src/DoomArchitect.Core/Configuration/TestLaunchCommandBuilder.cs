namespace DoomArchitect.Core.Configuration;

/// <summary>
/// Builds a Test Map launch argument list from a game configuration's own
/// <see cref="IGameConfiguration.TestParameters"/> template - UDB's real
/// placeholder convention (<c>%F</c>/<c>%WP</c>/<c>%WF</c>/<c>%AP</c>/
/// <c>%L</c>/<c>%L1</c>/<c>%L2</c>/<c>%S</c>/<c>%NM</c>, ported from
/// <c>Launcher.ConvertParameters</c>), but producing a real argument array
/// rather than one shell-escaped string. UDB has to hand-quote every
/// substituted value because Windows' own process-launch API only ever
/// takes a single string it re-parses itself; this project launches
/// through Godot's own process API, which already takes each argument as
/// its own array element - so a path containing spaces just works, with
/// none of UDB's own quoting fragility (`%AP`'s own per-file quoting
/// dropped into the template's own surrounding quotes) to replicate.
/// </summary>
public static class TestLaunchCommandBuilder
{
    public static IReadOnlyList<string> Build(
        string template, string tempWadPath, string iwadPath, IReadOnlyList<string> additionalResourcePaths,
        string mapName, int skill, bool noMonsters)
    {
        var (l1, l2) = SplitDigitRuns(mapName);
        var args = new List<string>();

        foreach (var rawToken in template.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            // The template quotes %F/%WP/%AP for its own (string-based)
            // sake - meaningless once each argument is its own array
            // element, so stripped rather than carried into the real
            // argument value.
            var token = rawToken.Trim('"');

            // %AP alone expands to a variable number of real array
            // elements (zero, one, or many resource paths) - not
            // something a plain substring replace can do, since the
            // number of resulting *arguments* changes, not just the
            // text of one. Every other placeholder is always exactly
            // one value, so a substring replace (below) is sufficient
            // even if the template ever glues one to other text.
            if (token == "%AP")
            {
                args.AddRange(additionalResourcePaths);
                continue;
            }

            // %L1/%L2 before the bare %L - both are text-prefixed by
            // it, so replacing %L first would corrupt them.
            var substituted = token
                .Replace("%WF", Path.GetFileName(iwadPath))
                .Replace("%WP", iwadPath)
                .Replace("%AP", string.Join(' ', additionalResourcePaths))
                .Replace("%F", tempWadPath)
                .Replace("%L1", l1)
                .Replace("%L2", l2)
                .Replace("%NM", noMonsters ? "-nomonsters" : "")
                .Replace("%S", skill.ToString())
                .Replace("%L", mapName);

            // A token that was only ever a placeholder with no value
            // (e.g. bare "%NM" when monsters are on, or "%L2" for a map
            // name with only one digit run) disappears entirely, rather
            // than becoming a spurious empty argument no real shell
            // command line would ever have produced.
            if (substituted.Length > 0) args.Add(substituted);
        }

        return args;
    }

    /// <summary>
    /// UDB's own real %L1/%L2 rule (<c>Launcher.cs</c>) - not an ExMy-vs-
    /// MAPxx special case, just the first two runs of consecutive digits
    /// found anywhere in the map name, each re-stringified from its own
    /// parsed int (drops leading zeros: <c>"MAP01"</c> -&gt; <c>"1"</c>,
    /// landing correctly on the vanilla_mapxx template's own single-number
    /// <c>-warp</c>; <c>"E1M2"</c> -&gt; <c>("1","2")</c>, landing on
    /// vanilla_exmx's own two-number <c>-warp</c>). A map name with fewer
    /// than two digit runs leaves the missing slot(s) empty.
    /// </summary>
    public static (string L1, string L2) SplitDigitRuns(string mapName)
    {
        var l1 = "";
        var l2 = "";
        var digits = "";
        var foundFirst = false;

        void Flush()
        {
            if (digits.Length == 0) return;

            var value = int.Parse(digits).ToString();
            if (!foundFirst)
            {
                l1 = value;
                foundFirst = true;
            }
            else
            {
                l2 = value;
            }

            digits = "";
        }

        foreach (var c in mapName)
        {
            if (char.IsAsciiDigit(c)) digits += c;
            else Flush();
        }

        Flush();
        return (l1, l2);
    }
}
