using System.Globalization;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// A narrow port of UDB's real <c>MapinfoParser</c> - enough to find and
/// parse a `DoomEdNums { }` block anywhere in a MAPINFO lump, which is the
/// only thing this project needs MAPINFO for: giving a ZScript-only actor
/// (which never carries its own editor number, see
/// <see cref="ZScriptActorStructure"/>'s own remark) a real DoomEdNum. Every
/// other MAPINFO block (`map`/`defaultmap`/`adddefaultmap`/`gameinfo`/
/// `spawnnums`/anything else) is skipped via the already-ported
/// <see cref="ZDTextParser.SkipStructure()"/> rather than actually parsed -
/// this project reads no other MAPINFO data today, and a full MAPINFO
/// parser (map titles, sky/fog settings, intermissions, ...) is a
/// substantial, separate feature with no current consumer.
/// </summary>
public sealed class MapinfoParser : ZDTextParser
{
    /// <summary>Resolves a literal `include` filename to that file's raw bytes, or null if it can't be found.</summary>
    public Func<string, byte[]?>? OnInclude;

    /// <summary>DoomEdNum -> lowercase class name, exactly UDB's own real shape.</summary>
    public Dictionary<int, string> DoomEdNums { get; } = new();

    private readonly HashSet<string> _parsedLumps = new(StringComparer.OrdinalIgnoreCase);

    public MapinfoParser()
    {
        Whitespace = "\n \t\r ";
        SpecialTokens = ",{}=\n";
    }

    public bool Parse(byte[] data, string sourceName)
    {
        if (!base.Parse(new MemoryStream(data), sourceName)) return false;

        while (SkipWhitespace(true))
        {
            var token = ReadToken().ToLowerInvariant();
            if (string.IsNullOrEmpty(token) || token == "$gzdb_skip") break;

            switch (token)
            {
                case "doomednums":
                    if (!ParseDoomEdNums()) return false;
                    break;

                case "include":
                    if (!ParseIncludeDirective()) return false;
                    break;

                default:
                    // map/defaultmap/adddefaultmap/gameinfo/spawnnums/anything
                    // else - not needed for DoomEdNums resolution, so just
                    // skip past its block (SkipStructure finds the next "{"
                    // regardless of what header tokens precede it, then
                    // tracks nesting to the matching "}").
                    SkipStructure();
                    break;
            }
        }

        return !HasError;
    }

    private bool ParseIncludeDirective()
    {
        SkipWhitespace(true);
        var includeLump = StripQuotes(ReadToken(false)); // don't skip newline

        if (string.IsNullOrEmpty(includeLump))
        {
            ReportError("Expected filename to include");
            return false;
        }

        if (Path.IsPathRooted(includeLump))
        {
            ReportError("Absolute include paths are not supported by ZDoom");
            return false;
        }

        if (includeLump.StartsWith("../") || includeLump.StartsWith("./") || includeLump.Contains('\\'))
        {
            ReportError("Relative include paths and backward slashes are not supported by ZDoom");
            return false;
        }

        if (!_parsedLumps.Add(includeLump))
        {
            ReportError($"Already parsed \"{includeLump}\". Check your include directives");
            return false;
        }

        var savedStream = DataStream!;
        var savedReader = DataReader!;
        var savedSource = SourceName;

        var included = OnInclude?.Invoke(includeLump);
        if (included != null && !Parse(included, includeLump)) return false;

        RestoreParseTarget(savedStream, savedReader, savedSource);
        return true;
    }

    private bool ParseDoomEdNums()
    {
        if (!NextTokenIs("{")) return false;

        while (SkipWhitespace(true))
        {
            var token = ReadToken();
            if (string.IsNullOrEmpty(token))
            {
                ReportError("Failed to find the end of DoomEdNums block");
                return false;
            }

            if (token == "}") break;

            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                ReportError($"Expected DoomEdNums entry number, but got \"{token}\"");
                return false;
            }

            if (!NextTokenIs("=")) return false;

            SkipWhitespace(false);
            var className = StripQuotes(ReadToken());
            if (string.IsNullOrEmpty(className))
            {
                ReportError("Expected DoomEdNums class definition");
                return false;
            }

            // A special + up to 5 args can follow, comma-separated - not modeled anywhere in this project, so just skip them.
            for (var i = 0; i < 6; i++)
            {
                if (!NextTokenIs(",", false)) break;
                if (!SkipWhitespace(true) || string.IsNullOrEmpty(ReadToken())) return false;
            }

            if (id != 0) DoomEdNums[id] = className.ToLowerInvariant();
        }

        return true;
    }
}
