namespace DoomArchitect.Core.Compilers;

/// <summary>
/// Builds <c>zt-bcc</c>'s own command-line arguments - confirmed from its
/// usage text (<c>zt-bcc [options] &lt;source-file&gt; [object-file]</c>,
/// <c>-i &lt;directory&gt;</c> to add an include search path) and Ultimate
/// Doom Builder's own bundled <c>zt-bcc.cfg</c> (<c>-I "%PT" -I "%PS" %FI %FO</c>) -
/// same order, every include dir first, then the input file, then the
/// output file. Returned as discrete array elements for
/// <c>ProcessStartInfo.ArgumentList</c>, which needs no manual quoting
/// around a path containing spaces the way a single combined argument
/// string would.
/// </summary>
public static class ZtBccArguments
{
    public static string[] Build(string inputFile, string outputFile, IEnumerable<string> includeDirs)
    {
        var args = new List<string>();
        foreach (var dir in includeDirs)
        {
            args.Add("-i");
            args.Add(dir);
        }

        args.Add(inputFile);
        args.Add(outputFile);
        return args.ToArray();
    }
}
