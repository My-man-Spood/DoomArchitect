namespace DoomArchitect.Core.Compilers;

/// <summary>
/// Builds <c>zdbsp</c>'s own command-line arguments for rebuilding one
/// map's nodes - confirmed from its real usage text (<c>zdbsp [options]
/// sourcefile.wad</c>, <c>-m MAP</c>/<c>-o FILE</c>) and Ultimate Doom
/// Builder's own bundled <c>zdbsp.cfg</c> node-builder profiles for UDMF
/// maps specifically: <c>"-c -X -o%FO %FI"</c> for a real save
/// (<c>zdbsp_udmf_normal</c>), <c>"-R -X -o%FO %FI"</c> for Test Map
/// (<c>zdbsp_udmf_fast</c> - <c>-R</c> zeroes out the REJECT table
/// rather than actually computing one, trading the correctness of a
/// pure optimization for speed, exactly what a quick test iteration
/// wants). <c>-X</c> is required either way for a UDMF map - it's what
/// makes zdbsp write the extended node format UDMF's own ZNODES lump
/// needs; UDB's own real "_udmf_"-suffixed profiles never omit it.
/// <c>-m</c> targets just this one map, in case the WAD has others.
/// </summary>
public static class ZdbspArguments
{
    public static string[] Build(string inputFile, string outputFile, string mapName, bool forTesting) =>
        new[] { forTesting ? "-R" : "-c", "-X", "-m", mapName, "-o", outputFile, inputFile };
}
