using DoomArchitect.Core.Map;

namespace DoomArchitect.Core.IO;

/// <summary>
/// A loaded (or about-to-be-saved) UDMF document. <see cref="Namespace"/>
/// and <see cref="UnknownBlocks"/> live here rather than on
/// <see cref="MapData"/> because they're document metadata with no
/// equivalent concept in the map model itself - UDMF's mandatory first
/// line, and any top-level block type (e.g. <c>thing</c>) we don't have a
/// data model for yet, preserved verbatim so a load-then-save round-trip
/// doesn't silently delete it.
/// </summary>
public sealed class UdmfDocument
{
    public UdmfDocument(
        MapData map, string @namespace, IReadOnlyList<UdmfBlock> unknownBlocks, IReadOnlyList<string> warnings)
    {
        Map = map;
        Namespace = @namespace;
        UnknownBlocks = unknownBlocks;
        Warnings = warnings;
    }

    public MapData Map { get; }

    public string Namespace { get; set; }

    public IReadOnlyList<UdmfBlock> UnknownBlocks { get; }

    public IReadOnlyList<string> Warnings { get; }
}
