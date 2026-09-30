using DoomArchitect.Core.Configuration;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// Parses a DECORATE lump/file into a set of <see cref="ActorStructure"/>s -
/// ported from UDB's real <c>DecorateParser</c>. Real adaptations from the
/// original, each tracked in TODO/TODO.md rather than silently dropped:
/// <see cref="OnInclude"/> is a plain resolver delegate the caller supplies
/// (this project has no <c>DataReader</c>/<c>DataLocation</c> to reach
/// through) instead of one wired to an ambient resource stack;
/// <see cref="ZScriptActors"/> defaults to empty since Phase 2 parses one
/// format in isolation - Phase 4's merge step is expected to supply the
/// real cross-format lookup; and there's no `scriptresources`/live-reload
/// bookkeeping (dropped in <see cref="ZDTextParser"/> already) - the
/// `#include` cycle guard (<c>_parsedLumps</c>) is preserved in full since
/// that's a correctness requirement (an infinite include loop), not a
/// resource-tracking feature.
/// </summary>
public sealed class DecorateParser : ZDTextParser
{
    /// <summary>Resolves a literal `#include` filename to that file's raw bytes, or null if it can't be found - backed by <c>IResourceContainer.FindByPath</c> at the call site, not by this class.</summary>
    public Func<string, byte[]?>? OnInclude;

    /// <summary>Actors already parsed from ZScript in the same scan, keyed by lowercase class name - DECORATE can inherit from or replace a ZScript class. Empty until Phase 4 wires the real merge.</summary>
    public IReadOnlyDictionary<string, ActorStructure> ZScriptActors { get; set; } = new Dictionary<string, ActorStructure>();

    /// <summary>The game configuration actors here may inherit static properties from (sprite/radius/height/flags/args) when they extend a real engine actor class rather than another parsed one. Null skips that resolution entirely.</summary>
    public IGameConfiguration? GameConfiguration { get; set; }

    public bool NoWarnings;

    private static readonly char[] CategorySplitter = { '\\', '/' };

    private Dictionary<string, ActorStructure> _actors = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ActorStructure> _archivedActors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _parsedLumps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _damageTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every actor supported by the current game (per its own `game` property vs. <see cref="Configuration.IGameConfiguration.DecorateGames"/>).</summary>
    public IEnumerable<ActorStructure> Actors => _actors.Values;

    /// <summary>Every actor defined in the parsed DECORATE, including ones not supported by the current game.</summary>
    public ICollection<ActorStructure> AllActors => _archivedActors.Values;

    /// <summary>Same as <see cref="Actors"/>, keyed by lowercase class name - for the Phase 4 merge into <see cref="Configuration.ThingTypeInfo"/>.</summary>
    public IReadOnlyDictionary<string, ActorStructure> ActorsByClass => _actors;

    /// <summary>Same as <see cref="AllActors"/>, keyed by lowercase class name.</summary>
    public IReadOnlyDictionary<string, ActorStructure> AllActorsByClass => _archivedActors;

    public IEnumerable<string> DamageTypes => _damageTypes;

    public DecorateParser()
    {
        Whitespace = "\n \t\r ";
        SpecialTokens = ":{}[]()+-\n;,";
        SkipRegions = false;
    }

    internal void LogWarning(string message)
    {
        if (!NoWarnings) ReportWarning(message);
    }

    /// <summary>Small, local stand-in for UDB's real logged-warning list (see the class doc comment on why the full diagnostics pipeline isn't ported yet) - just enough to not lose the information entirely.</summary>
    public List<string> Warnings { get; } = new();

    private void ReportWarning(string message) => Warnings.Add(message);

    /// <summary>Parses <paramref name="data"/> (one DECORATE lump/file's raw bytes) and every file it `#include`s (resolved via <see cref="OnInclude"/>). Returns false on error - check <see cref="ZDTextParser.HasError"/>/<see cref="ZDTextParser.ErrorDescription"/>.</summary>
    public bool Parse(byte[] data, string sourceName)
    {
        if (!base.Parse(new MemoryStream(data), sourceName)) return false;

        var regions = new List<DecorateCategoryInfo>();
        var regionLines = new List<(int Line, string Title)>();

        while (SkipWhitespace(true))
        {
            var objDeclaration = ReadToken();
            if (string.IsNullOrEmpty(objDeclaration)) continue;

            objDeclaration = objDeclaration.ToLowerInvariant();
            if (objDeclaration == "$gzdb_skip") break;

            switch (objDeclaration)
            {
                case "actor":
                {
                    var actor = new DecorateActorStructure(this, regions.Count > 0 ? regions[^1] : null, GameConfiguration);
                    if (HasError) return false;

                    var key = actor.ClassName.ToLowerInvariant();
                    _archivedActors[key] = actor;
                    if (actor.CheckActorSupported(GameConfiguration?.DecorateGames ?? "")) _actors[key] = actor;

                    if (actor.ReplacesClass != null)
                    {
                        var replaceKey = actor.ReplacesClass.ToLowerInvariant();
                        if (GetArchivedActorByName(actor.ReplacesClass) != null)
                            _archivedActors[replaceKey] = actor;
                        else
                            LogWarning($"Unable to find \"{actor.ReplacesClass}\" class to replace, while parsing \"{actor.ClassName}\"");

                        if (actor.CheckActorSupported(GameConfiguration?.DecorateGames ?? "") && GetActorByName(actor.ReplacesClass) != null)
                            _actors[replaceKey] = actor;
                    }

                    break;
                }

                case "#include":
                {
                    SkipWhitespace(true);
                    var filename = StripQuotes(ReadToken(false)); // ZDoom includes: no relative/absolute paths, no backslashes, don't skip newline

                    if (string.IsNullOrEmpty(filename))
                    {
                        ReportError("Expected file name to include");
                        return false;
                    }

                    if (Path.IsPathRooted(filename))
                    {
                        ReportError("Absolute include paths are not supported by ZDoom");
                        return false;
                    }

                    if (filename.StartsWith("../") || filename.StartsWith("./") || filename.Contains("\\"))
                    {
                        ReportError("Relative include paths and backward slashes are not supported by ZDoom");
                        return false;
                    }

                    if (!_parsedLumps.Add(filename))
                    {
                        ReportError($"Already parsed \"{filename}\". Check your include directives");
                        return false;
                    }

                    var included = OnInclude?.Invoke(filename);
                    if (included == null) break; // matches UDB's own "include not found" tolerance - the include is simply skipped, not fatal

                    var savedStream = DataStream!;
                    var savedReader = DataReader!;
                    var savedSource = SourceName;

                    if (!Parse(included, filename)) return false;

                    RestoreParseTarget(savedStream, savedReader, savedSource);
                    break;
                }

                case "damagetype":
                {
                    SkipWhitespace(true);
                    var damageType = StripQuotes(ReadToken(false));
                    if (string.IsNullOrEmpty(damageType))
                    {
                        ReportError("Expected DamageType name");
                        return false;
                    }

                    SkipWhitespace(true);
                    if (!NextTokenIs("{")) return false;

                    SkipStructureBody();
                    _damageTypes.Add(damageType);
                    break;
                }

                case "enum":
                case "native":
                case "const":
                    SkipToSemicolon();
                    break;

                case "#region":
                {
                    var line = GetCurrentLineNumber();
                    SkipWhitespace(false);
                    var catTitle = ReadLine();
                    regionLines.Add((line, catTitle ?? ""));

                    if (!string.IsNullOrEmpty(catTitle))
                    {
                        var parts = catTitle.Split(CategorySplitter, StringSplitOptions.RemoveEmptyEntries);
                        var info = new DecorateCategoryInfo();
                        if (regions.Count > 0)
                        {
                            info.Category.AddRange(regions[^1].Category);
                            foreach (var (k, v) in regions[^1].Properties) info.Properties[k] = v;
                        }
                        info.Category.AddRange(parts);
                        regions.Add(info);
                    }

                    break;
                }

                case "#endregion":
                    if (regions.Count > 0) regions.RemoveAt(regions.Count - 1);
                    if (regionLines.Count > 0) regionLines.RemoveAt(regionLines.Count - 1);
                    else LogWarning("Unexpected #endregion");
                    break;

                default:
                    if (objDeclaration.StartsWith("$"))
                    {
                        if (regions.Count > 0)
                            regions[^1].Properties[objDeclaration] = new List<string> { SkipWhitespace(false) ? ReadLine() ?? "" : "" };
                        else
                            ReadLine();
                        break;
                    }

                    // Unknown top-level structure - skip to its opening brace, then its matching closing one.
                    string token2;
                    do
                    {
                        if (!SkipWhitespace(true)) break;
                        token2 = ReadToken();
                        if (string.IsNullOrEmpty(token2)) break;
                    }
                    while (token2 != "{");

                    var scopeLevel = 1;
                    do
                    {
                        if (!SkipWhitespace(true)) break;
                        token2 = ReadToken();
                        if (string.IsNullOrEmpty(token2)) break;
                        if (token2 == "{") scopeLevel++;
                        if (token2 == "}") scopeLevel--;
                    }
                    while (scopeLevel > 0);

                    break;
            }
        }

        foreach (var (line, title) in regionLines)
        {
            LogWarning(!string.IsNullOrEmpty(title) ? $"Unclosed #region \"{title}\" (line {line})" : $"Unclosed #region (line {line})");
        }

        return ErrorDescription == null;
    }

    private void SkipToSemicolon()
    {
        while (SkipWhitespace(true))
        {
            var t = ReadToken();
            if (string.IsNullOrEmpty(t) || t == ";") break;
        }
    }

    private void SkipStructureBody()
    {
        while (SkipWhitespace(true))
        {
            var t = ReadToken();
            if (string.IsNullOrEmpty(t) || t == "}") break;
        }
    }

    /// <summary>Supported actor by name, or null. O(1).</summary>
    public ActorStructure? GetActorByName(string name) => _actors.GetValueOrDefault(name.ToLowerInvariant());

    /// <summary>Supported actor by DoomEdNum, or null. O(n).</summary>
    public ActorStructure? GetActorByDoomEdNum(int doomEdNum) => _actors.Values.FirstOrDefault(a => a.DoomEdNum == doomEdNum);

    internal ActorStructure? GetArchivedActorByName(string name)
    {
        name = name.ToLowerInvariant();
        return ZScriptActors.GetValueOrDefault(name) ?? _archivedActors.GetValueOrDefault(name);
    }
}
