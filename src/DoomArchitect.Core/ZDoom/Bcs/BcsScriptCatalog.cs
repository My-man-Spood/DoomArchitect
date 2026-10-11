namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// One script declared somewhere in a map's own compiled source - UDB's
/// own real <c>ScriptItem</c> (<c>GZBuilder.Data</c>), scoped to just what
/// <c>ActionArgumentsEditor</c>'s arg0 dropdown needs: which script this
/// is (<see cref="Number"/> holds either the real numeric index as text,
/// or the script's own name, disambiguated by <see cref="IsNamedScript"/>)
/// and what it declares its own parameters as (<see cref="ParameterNames"/>),
/// used to relabel the *other* argument slots once this one's picked.
/// <see cref="SourcePath"/>/<see cref="Line"/>/<see cref="Column"/> are
/// the declaration's own <see cref="BcsNode.SourcePath"/>/<see cref="BcsScriptDeclaration.NumberLine"/>/
/// <see cref="BcsScriptDeclaration.NumberColumn"/>, feeding the arg0
/// "Go to Script" button's own go-to-definition - empty
/// <see cref="SourcePath"/> means "the map's own main SCRIPTS lump"
/// (<c>OpenMapMenu.FindScriptsLumpLocation</c>); a non-empty one is the
/// literal <c>#include</c> text (e.g. <c>"acs/souls.acs"</c>), resolved
/// back to whichever resource container actually supplied it by
/// <c>OpenMapMenu.FindIncludeOpenRequest</c>.
/// </summary>
public readonly record struct ScriptCatalogEntry(string Number, bool IsNamedScript, IReadOnlyList<string> ParameterNames, string SourcePath, int Line, int Column);

/// <summary>
/// Builds the full list of scripts declared anywhere in one parsed BCS
/// program (including every <c>#include</c>d file - <see cref="BcsCompilationUnit"/>'s
/// own <see cref="BcsCompilationUnit.Members"/> already has them all,
/// since <see cref="Bcs.BcsParser.ParseProgram"/> merges an included
/// file's declarations into the same unit, tagged with their own real
/// <see cref="BcsNode.SourcePath"/>) - every real <see cref="BcsScriptDeclaration"/>,
/// numbered or named alike, unlike <see cref="BcsCompilationUnit.CollectSymbols"/>'s
/// own symbol view, which deliberately skips numbered scripts as "not a
/// completable name." Sorted the same two-group way UDB's own real
/// <c>ScriptItem.SortByIndex</c>/<c>SortByName</c> do: every numbered
/// script first, ordered by its own real number, then every named
/// script after, ordered alphabetically - two separate dropdown groups,
/// not one interleaved list.
/// </summary>
public static class BcsScriptCatalog
{
    public static IReadOnlyList<ScriptCatalogEntry> Build(BcsCompilationUnit unit)
    {
        var numbered = new List<(int Number, ScriptCatalogEntry Entry)>();
        var named = new List<ScriptCatalogEntry>();

        foreach (var member in BcsCompilationUnit.AllMembers(unit.Members))
        {
            if (member is not BcsScriptDeclaration script) continue;

            var parameterNames = script.ParameterNames.Select(p => p.Name).ToList();
            var entry = new ScriptCatalogEntry(script.Number, script.IsNamedScript, parameterNames, script.SourcePath, script.NumberLine, script.NumberColumn);

            if (script.IsNamedScript)
            {
                named.Add(entry);
            }
            else if (int.TryParse(script.Number, out var number))
            {
                numbered.Add((number, entry));
            }
        }

        return numbered.OrderBy(n => n.Number).Select(n => n.Entry)
            .Concat(named.OrderBy(n => n.Number, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}
