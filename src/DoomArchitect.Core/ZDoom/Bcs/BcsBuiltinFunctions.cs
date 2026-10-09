namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// Real ACS/BCS compiler intrinsic functions - confirmed verbatim from
/// `zt-bcc`'s own `src/builtin.c` (<c>g_funcs[]</c>: a name plus a
/// compact format string, <c>[return];[required];[optional]</c>,
/// decoded here by the exact same algorithm as that file's own
/// `setup_return_type`/`setup_param_list`). These are never declared in
/// any source file at all - not `zcommon.bcs`, not anywhere - they're
/// wired directly to real opcodes at compiler startup
/// (`t_create_builtins`), which is exactly why no amount of fixing
/// `#include` resolution ever surfaced them for completion/hover; this
/// is the one place that actually can. `t_create_builtins` iterates
/// exactly this array, and a `STATIC_ASSERT` in that same real source
/// ties its length to the sum of its three backing-implementation
/// tables - confirmed this really is the complete, authoritative list,
/// not a partial one.
///
/// Deliberately NOT wired into <see cref="BcsCompilationUnit"/>/
/// <c>FindDeclaration</c> - there's no real source position to navigate
/// to, and teaching `FindDeclaration` to resolve these would need a
/// position-less-match guard in three separate navigation call sites
/// across two projects (<c>ScriptDocument.OnBcsSymbolLookup</c>/
/// `OnBcsSymbolValidate`, `BcsDefinitionHandler`), for a feature
/// (go-to-definition) nobody asked for here. Completion/hover each
/// consult this directly instead, as an addition alongside (not a
/// replacement for) the real declared-symbol lookup.
/// </summary>
public static class BcsBuiltinFunctions
{
    // Verbatim name/format pairs from g_funcs[] - deliberately kept in
    // the real source's own lowercase spelling and order, so this stays
    // a direct, line-countable transcription to diff against if zt-bcc
    // is ever updated, rather than something already "cleaned up" and
    // harder to verify against the original.
    private static readonly (string Name, string Format)[] Table =
    {
        ("delay", ";i"),
        ("random", "i;ii"),
        ("thingcount", "i;i;i"),
        ("tagwait", ";i"),
        ("polywait", ";i"),
        ("changefloor", ";is"),
        ("changeceiling", ";is"),
        ("lineside", "i"),
        ("scriptwait", ";i"),
        ("clearlinespecial", ""),
        ("playercount", "i"),
        ("gametype", "i"),
        ("gameskill", "i"),
        ("timer", "i"),
        ("sectorsound", ";si"),
        ("ambientsound", ";si"),
        ("soundsequence", ";s"),
        ("setlinetexture", ";iiis"),
        ("setlineblocking", ";ii"),
        ("setlinespecial", ";ii;rrrrr"),
        ("thingsound", ";isi"),
        ("activatorsound", ";si"),
        ("localambientsound", ";si"),
        ("setlinemonsterblocking", ";ii"),
        ("isnetworkgame", "b"),
        ("playerteam", "i"),
        ("playerhealth", "i"),
        ("playerarmorpoints", "i"),
        ("playerfrags", "i"),
        ("bluecount", "i"),
        ("blueteamcount", "i"),
        ("redcount", "i"),
        ("redteamcount", "i"),
        ("bluescore", "i"),
        ("blueteamscore", "i"),
        ("redscore", "i"),
        ("redteamscore", "i"),
        ("isoneflagctf", "b"),
        ("getinvasionwave", "i"),
        ("getinvasionstate", "i"),
        ("music_change", ";si"),
        ("consolecommand", ";s;ii"),
        ("singleplayer", "b"),
        ("fixedmul", "f;ff"),
        ("fixeddiv", "f;ff"),
        ("setgravity", ";f"),
        ("setaircontrol", ";f"),
        ("clearinventory", ""),
        ("giveinventory", ";si"),
        ("takeinventory", ";si"),
        ("checkinventory", "i;s"),
        ("spawn", "i;sfff;ii"),
        ("spawnspot", "i;si;ii"),
        ("setmusic", ";s;ii"),
        ("localsetmusic", ";s;ii"),
        ("setfont", ";s"),
        ("setthingspecial", ";ii;rrrrr"),
        ("fadeto", ";iiiff"),
        ("faderange", ";iiifiiiff"),
        ("cancelfade", ""),
        ("playmovie", "i;s"),
        ("setfloortrigger", ";iii;rrrrr"),
        ("setceilingtrigger", ";iii;rrrrr"),
        ("getactorx", "f;i"),
        ("getactory", "f;i"),
        ("getactorz", "f;i"),
        ("sin", "f;f"),
        ("cos", "f;f"),
        ("vectorangle", "f;ff"),
        ("checkweapon", "b;s"),
        ("setweapon", "b;s"),
        ("setmarineweapon", ";ii"),
        ("setactorproperty", ";iir"),
        ("getactorproperty", "r;ii"),
        ("playernumber", "i"),
        ("activatortid", "i"),
        ("setmarinesprite", ";is"),
        ("getscreenwidth", "i"),
        ("getscreenheight", "i"),
        ("thing_projectile2", ";iiiiiii"),
        ("strlen", "i;s"),
        ("sethudsize", ";iib"),
        ("getcvar", "i;s"),
        ("setresultvalue", ";i"),
        ("getlinerowoffset", "i"),
        ("getactorfloorz", "f;i"),
        ("getactorangle", "f;i"),
        ("getsectorfloorz", "f;iii"),
        ("getsectorceilingz", "f;iii"),
        ("getsigilpieces", "i"),
        ("getlevelinfo", "i;i"),
        ("changesky", ";ss"),
        ("playeringame", "b;i"),
        ("playerisbot", "b;i"),
        ("setcameratotexture", ";isi"),
        ("getammocapacity", "i;s"),
        ("setammocapacity", ";si"),
        ("setactorangle", ";if"),
        ("spawnprojectile", ";isiiiii"),
        ("getsectorlightlevel", "i;i"),
        ("getactorceilingz", "f;i"),
        ("setactorposition", "b;ifffb"),
        ("clearactorinventory", ";i"),
        ("giveactorinventory", ";isi"),
        ("takeactorinventory", ";isi"),
        ("checkactorinventory", "i;is"),
        ("thingcountname", "i;si"),
        ("spawnspotfacing", "i;si;i"),
        ("playerclass", "i;i"),
        ("getplayerinfo", "i;ii"),
        ("changelevel", ";sii;i"),
        ("sectordamage", ";iissi"),
        ("replacetextures", ";ss;i"),
        ("getactorpitch", "f;i"),
        ("setactorpitch", ";if"),
        ("setactorstate", "i;is;b"),
        ("thing_damage2", "i;iis"),
        ("useinventory", "i;s"),
        ("useactorinventory", "i;is"),
        ("checkactorceilingtexture", "b;is"),
        ("checkactorfloortexture", "b;is"),
        ("getactorlightlevel", "i;i"),
        ("setmugshotstate", ";s"),
        ("thingcountsector", "i;iii"),
        ("thingcountnamesector", "i;sii"),
        ("checkplayercamera", "i;i"),
        ("morphactor", "i;i;ssiiss"),
        ("unmorphactor", "i;i;i"),
        ("getplayerinput", "i;ii"),
        ("classifyactor", "i;i"),
        ("namedscriptwait", ";s"),
        // Format functions.
        ("print", ""),
        ("printbold", ""),
        ("hudmessage", ";iiifff;fff"),
        ("hudmessagebold", ";iiifff;fff"),
        ("log", ""),
        ("strparam", "s"),
        // Internal functions.
        ("acs_executewait", ";i;rrrr"),
        ("acs_namedexecutewait", ";s;rrrr"),
    };

    /// <summary>
    /// `print`/`printbold`/`log` have an empty format string in the
    /// real table - they're real ACS functions, but use a fundamentally
    /// different, keyword-tagged variadic argument list
    /// (<c>Print(s:expr, i:expr, ...)</c>) the simple format-string
    /// scheme can't express at all. Decoding an empty string naively
    /// would claim "void, zero parameters", which is actively
    /// misleading, not just incomplete - these three get a manually
    /// written signature instead, clearly not derived from `Table`'s
    /// own (empty, hence useless here) entry for them.
    /// </summary>
    private static readonly Dictionary<string, string> ManualSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["print"] = "void Print(...)",
        ["printbold"] = "void PrintBold(...)",
        ["log"] = "void Log(...)",
    };

    private static readonly Dictionary<string, string> SignaturesByName = BuildSignatures();

    /// <summary>Every builtin's own display name - what a completion item actually inserts. Capitalizes only the very first letter (and the letter right after each real, already-present underscore, e.g. `thing_projectile2` -> `Thing_Projectile2`) - a deterministic transform with zero invented word-boundaries, rather than guessing at a "prettier" camel-case spelling this project has no authoritative source to verify against.</summary>
    public static IReadOnlyList<string> AllNames { get; } = Table.Select(entry => DisplayName(entry.Name)).ToList();

    /// <summary>
    /// The decoded signature for <paramref name="name"/> (case-insensitive
    /// - BCS itself is case-insensitive), or null if it isn't one of
    /// these. Parameter names, where <see cref="BcsFunctionDocs"/> has
    /// researched them, are merged in via
    /// <see cref="BcsFunctionDocs.ApplyParameterNames"/> - everything
    /// else here stays a direct, verified transcription of `g_funcs[]`
    /// itself, but a bare type list with no names was never going to be
    /// as useful on hover as the real thing.
    /// </summary>
    public static string? TryDescribe(string name) =>
        SignaturesByName.TryGetValue(name, out var signature) ? BcsFunctionDocs.ApplyParameterNames(signature, name) : null;

    private static Dictionary<string, string> BuildSignatures()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, format) in Table)
        {
            result[name] = ManualSignatures.TryGetValue(name, out var manual) ? manual : Decode(DisplayName(name), format);
        }

        return result;
    }

    private static string DisplayName(string name) =>
        string.Join("_", name.Split('_').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>
    /// Confirmed verbatim from `builtin.c`'s own `setup_func`/
    /// `setup_return_type`/`setup_param_list`: a leading type char
    /// (absent, or `;`/end-of-string, means `void`) is the return type;
    /// if a `;` follows, every char after it is one parameter (`i`/`r`/
    /// `f`/`b`/`s`) - required, until a *second* `;` is seen, which
    /// flips every parameter after it to optional. Optional parameters
    /// are shown in `[...]` here - a deliberate, value-adding choice
    /// over this AST's existing plain-comma-list convention for a
    /// declared function's own parameters (which has no "optional"
    /// concept to show in the first place): the real format string
    /// *does* know which parameters are optional, so showing that
    /// rather than discarding it seemed worth the one small, explained
    /// divergence.
    /// </summary>
    private static string Decode(string displayName, string format)
    {
        var index = 0;
        var returnType = "void";
        if (index < format.Length && format[index] != ';')
        {
            returnType = TypeName(format[index]);
            index++;
        }

        var parameters = new List<string>();
        if (index < format.Length && format[index] == ';')
        {
            index++;
            var optional = false;
            while (index < format.Length)
            {
                if (format[index] == ';')
                {
                    optional = true;
                    index++;
                    continue;
                }

                var type = TypeName(format[index]);
                parameters.Add(optional ? $"[{type}]" : type);
                index++;
            }
        }

        return $"{returnType} {displayName}({string.Join(", ", parameters)})";
    }

    private static string TypeName(char code) => code switch
    {
        'i' => "int",
        'r' => "raw",
        'f' => "fixed",
        'b' => "bool",
        's' => "str",
        _ => "?",
    };
}
