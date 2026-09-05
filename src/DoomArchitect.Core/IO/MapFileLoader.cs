namespace DoomArchitect.Core.IO;

/// <summary>
/// One-shot "give me a loaded map" entry point for the common case: a
/// WAD file on disk containing a UDMF-format map. Just wires
/// <see cref="WadFile"/> and <see cref="UdmfReader"/> together.
/// </summary>
public static class MapFileLoader
{
    public static UdmfDocument LoadUdmfMap(string wadPath, string mapName)
    {
        var wad = WadFile.Read(wadPath);
        var text = wad.ReadMapTextMap(mapName);
        return UdmfReader.Read(text);
    }
}
