using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.IO;

/// <summary>
/// One-shot "give me a loaded map" entry points for a WAD file on disk -
/// one per supported format, just wiring <see cref="WadFile"/> together
/// with the matching reader. Deciding which format a given map actually
/// is (via <see cref="WadFile.FindUdmfMapNames"/>/
/// <see cref="WadFile.FindClassicMapNames"/>) is left to the caller,
/// since that's a UI-level concern (which map to offer/pick), not a
/// loading concern.
/// </summary>
public static class MapFileLoader
{
    public static UdmfDocument LoadUdmfMap(string wadPath, string mapName)
    {
        var wad = WadFile.Read(wadPath);
        var text = wad.ReadMapTextMap(mapName);
        return UdmfReader.Read(text);
    }

    public static (MapData Map, IReadOnlyList<string> Warnings) LoadClassicMap(string wadPath, string mapName)
    {
        var wad = WadFile.Read(wadPath);
        return ClassicMapReader.Read(wad, mapName);
    }
}
