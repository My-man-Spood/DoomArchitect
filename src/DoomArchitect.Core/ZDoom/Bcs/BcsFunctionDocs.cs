namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// Short, original hover descriptions for real ACS functions
/// (<see cref="BcsBuiltinFunctions"/>) and `special`-declared action
/// specials (e.g. <c>Thing_Activate</c>, <c>Door_Open</c>) - researched
/// from the ZDoom Wiki but deliberately NOT copied from it: the wiki's
/// content license (GNU FDL 1.2) makes reproducing its actual text,
/// verbatim or lightly reworded, something this codebase can't casually
/// ship. Every entry in <see cref="ByName"/> (the generated partial
/// holding the actual data - see <c>wiki-research/README.md</c>) is
/// this project's own fresh sentence written from the facts the wiki
/// page described, not an excerpt of it - the same way a person writes
/// original documentation after reading a manual once, rather than
/// copying it.
///
/// Consulted by hover *alongside*, never instead of, the real decoded
/// signature - a builtin's via <see cref="BcsBuiltinFunctions.TryDescribe"/>,
/// an action special's via its own <see cref="BcsSymbol.Signature"/> -
/// since a description with no signature (or vice versa) is strictly
/// worse than having both. <see cref="ApplyParameterNames"/> also feeds
/// back into both of those signatures themselves, for the one thing
/// neither real source format actually carries: parameter names.
/// </summary>
public static partial class BcsFunctionDocs
{
    /// <summary><see cref="Parameters"/> is ordered (declaration order) - a plain tuple list, not a dictionary, specifically so <see cref="ApplyParameterNames"/> can zip it positionally against a types-only signature without depending on dictionary enumeration order being insertion order (an implementation detail, not a real guarantee).</summary>
    public readonly record struct Doc(string Summary, IReadOnlyList<(string Name, string Description)> Parameters);

    /// <summary>The doc entry for <paramref name="name"/> (case-insensitive - BCS itself is case-insensitive), or null if nothing's been researched for it yet.</summary>
    public static Doc? TryGetDoc(string name) => ByName.TryGetValue(name, out var doc) ? doc : null;

    /// <summary>
    /// Renders a doc as hover-ready text: the summary, then one
    /// "- param: meaning" bullet per parameter that has one. Returns
    /// null, same as <see cref="TryGetDoc"/>, if there's no entry for
    /// this name.
    /// </summary>
    public static string? Format(string name)
    {
        if (!ByName.TryGetValue(name, out var doc)) return null;
        if (doc.Parameters.Count == 0) return doc.Summary;

        var bullets = string.Join("\n", doc.Parameters.Select(p => $"- {p.Name}: {p.Description}"));
        return $"{doc.Summary}\n\n{bullets}";
    }

    /// <summary>
    /// Inserts this project's own researched parameter names into an
    /// otherwise types-only signature - e.g.
    /// <c>"int SpawnSpot(str, int, [int], [int])"</c> becomes
    /// <c>"int SpawnSpot(str classname, int spottid, [int tid], [int angle])"</c>.
    /// Names are the one thing neither real source format actually
    /// carries - not `g_funcs[]`'s own format strings
    /// (<see cref="BcsBuiltinFunctions.Decode"/>), not the real
    /// `special`-declaration-list grammar (<c>BcsParser.ParseOneSpecialEntry</c>)
    /// - so unlike the types themselves, these are informational,
    /// researched from the wiki, not verified against any compiler
    /// source. Falls back to <paramref name="signature"/> completely
    /// unchanged if there's no researched doc for this name, or if its
    /// parameter count doesn't exactly match the signature's own -
    /// safer to show an unnamed parameter than a wrongly-named one (a
    /// mismatch means the signature and the researched param list
    /// disagree about something, e.g. an optional trailing parameter
    /// the wiki page never documented).
    /// </summary>
    public static string ApplyParameterNames(string signature, string name)
    {
        if (!ByName.TryGetValue(name, out var doc) || doc.Parameters.Count == 0) return signature;

        var openParen = signature.IndexOf('(');
        var closeParen = signature.LastIndexOf(')');
        if (openParen < 0 || closeParen < openParen) return signature;

        var inner = signature[(openParen + 1)..closeParen];
        var types = inner.Length == 0 ? Array.Empty<string>() : inner.Split(", ");
        if (types.Length != doc.Parameters.Count) return signature;

        var named = types.Zip(doc.Parameters, (type, param) =>
            type.StartsWith('[') && type.EndsWith(']')
                ? $"[{type[1..^1]} {param.Name}]"
                : $"{type} {param.Name}");

        return $"{signature[..(openParen + 1)]}{string.Join(", ", named)}{signature[closeParen..]}";
    }
}
