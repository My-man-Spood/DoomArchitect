using System.Text;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Builds the lumps for a saved UDMF map and writes them back into a WAD -
/// the mirror image of <see cref="MapFileLoader"/>. Splices the map's own
/// lump group into whatever else the target WAD already has (other maps,
/// resources) rather than rebuilding the whole file's contents from
/// scratch - only the target map's own group is ever touched.
/// </summary>
public static class MapFileSaver
{
    public static byte[] SaveUdmfMap(IReadOnlyList<WadLump>? originalLumps, UdmfDocument document, string mapName)
    {
        var udmfText = UdmfWriter.Write(document);
        var lumps = BuildLumpsForSave(originalLumps, mapName, udmfText);
        return WadWriter.Write(lumps);
    }

    /// <summary>
    /// Splices a fresh <c>TEXTMAP</c> for <paramref name="mapName"/> into
    /// <paramref name="originalLumps"/>:
    /// <list type="bullet">
    /// <item><paramref name="originalLumps"/> is null (brand-new file): returns just
    /// [marker, TEXTMAP, ENDMAP].</item>
    /// <item>an existing UDMF group under that marker: only the
    /// <c>TEXTMAP</c> lump is replaced, every other lump in the group
    /// (BEHAVIOR/ZNODES/etc.) is preserved byte-for-byte.</item>
    /// <item>an existing classic (binary-format) group under that marker:
    /// removed wholesale and replaced with a fresh UDMF group - saving a
    /// classic map is a deliberate upgrade-to-UDMF, since
    /// <see cref="UdmfWriter"/> is this project's only write path.</item>
    /// <item>no existing group under that name at all: a fresh group is
    /// appended at the end, every other lump untouched.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<WadLump> BuildLumpsForSave(
        IReadOnlyList<WadLump>? originalLumps, string mapName, string udmfText)
    {
        var textMapLump = new WadLump("TEXTMAP", Encoding.ASCII.GetBytes(udmfText));

        if (originalLumps == null)
        {
            return new[]
            {
                new WadLump(mapName, Array.Empty<byte>()),
                textMapLump,
                new WadLump("ENDMAP", Array.Empty<byte>()),
            };
        }

        var markerIndex = WadFile.FindMarkerIndex(originalLumps, mapName);
        if (markerIndex < 0)
        {
            var appended = new List<WadLump>(originalLumps)
            {
                new(mapName, Array.Empty<byte>()),
                textMapLump,
                new("ENDMAP", Array.Empty<byte>()),
            };
            return appended;
        }

        var nextIndex = markerIndex + 1;
        var nextName = nextIndex < originalLumps.Count ? originalLumps[nextIndex].Name : null;

        var result = new List<WadLump>(originalLumps.Count + 2);
        result.AddRange(originalLumps.Take(markerIndex + 1));

        if (nextName != null && nextName.Equals("TEXTMAP", StringComparison.OrdinalIgnoreCase))
        {
            var groupEnd = WadFile.FindGroupEnd(originalLumps, nextIndex, WadMapLumpNames.Udmf, stopAfterName: "ENDMAP");
            for (var i = nextIndex; i < groupEnd; i++)
            {
                var lump = originalLumps[i];
                result.Add(lump.Name.Equals("TEXTMAP", StringComparison.OrdinalIgnoreCase) ? textMapLump : lump);
            }

            result.AddRange(originalLumps.Skip(groupEnd));
            return result;
        }

        if (nextName != null && nextName.Equals("THINGS", StringComparison.OrdinalIgnoreCase))
        {
            var groupEnd = WadFile.FindGroupEnd(originalLumps, nextIndex, WadMapLumpNames.Classic);
            result.Add(textMapLump);
            result.Add(new WadLump("ENDMAP", Array.Empty<byte>()));
            result.AddRange(originalLumps.Skip(groupEnd));
            return result;
        }

        result.Add(textMapLump);
        result.Add(new WadLump("ENDMAP", Array.Empty<byte>()));
        result.AddRange(originalLumps.Skip(nextIndex));
        return result;
    }
}
