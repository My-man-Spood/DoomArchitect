using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Textures;

/// <summary>
/// Parses a TEXTURE1/TEXTURE2 lump into composite texture definitions.
/// Ported from UDB's <c>WADReader.LoadTextureSet</c>, with two deliberate
/// deviations from UDB's literal algorithm (both discussed and agreed
/// with the user, see the texture pipeline plan):
///
/// - **Real per-entry offsets are honored.** UDB reads the offset table
///   but never actually seeks with it - it just assumes texture
///   definitions are laid out sequentially right after the table (true
///   for every real-world WAD, but not what the format spec requires).
///   This reader seeks to each entry's real stored offset instead, which
///   produces identical results to UDB on every real file while also
///   correctly handling a theoretical non-sequential one.
/// - **The per-entry validation bug is fixed.** UDB's check is
///   <c>(width&gt;0 &amp;&amp; height&gt;0 &amp;&amp; patches&gt;0 &amp;&amp; scalex!=0) || scaley!=0</c>
///   - an operator-precedence bug that passes almost unconditionally
///   since <c>scaley</c> is virtually never zero. This reader uses the
///   evidently-intended all-AND condition. Texture scale isn't modeled as
///   a field here at all (nothing in this codebase uses it yet), and with
///   no game-configuration system to supply a possibly-zero default
///   scale, a byte-derived or default scale can never actually be zero in
///   this implementation - so the fixed condition reduces to simply
///   requiring positive width, height, and patch count.
///
/// Doom-format vs. Strife-format per-entry layout is auto-detected the
/// same way UDB does: Strife's <c>maptexture_t</c> omits the 4-byte
/// vanilla <c>columndirectory</c> field, so reading where its patch count
/// would sit and finding <c>0</c> means "this was actually the first half
/// of a Doom-format columndirectory" - skip the other half and read the
/// real (guaranteed nonzero) patch count instead.
/// </summary>
public static class TextureDefinitionReader
{
    public static IReadOnlyList<CompositeTextureDefinition> Read(
        byte[] data, IReadOnlyList<string> patchNames, bool isTexture1, List<string> warnings)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        var numTextures = reader.ReadInt32();
        var offsets = new int[numTextures];
        for (var i = 0; i < numTextures; i++) offsets[i] = reader.ReadInt32();

        var definitions = new List<CompositeTextureDefinition>(numTextures);
        for (var i = 0; i < numTextures; i++)
        {
            stream.Seek(offsets[i], SeekOrigin.Begin);
            var definition = ReadOne(reader, patchNames, i, warnings);
            if (definition != null) definitions.Add(definition);
        }

        // Index 0 of a real TEXTURE1 lump is a reserved/unusable slot in
        // vanilla Doom (a historical id Software quirk) - TEXTURE2 isn't
        // subject to this. This drops whichever entry ended up first in
        // the *filtered* list (i.e. skips over any entries that failed
        // validation above), matching UDB's own removal point exactly.
        if (isTexture1 && definitions.Count > 0) definitions.RemoveAt(0);

        return definitions;
    }

    private static CompositeTextureDefinition? ReadOne(
        BinaryReader reader, IReadOnlyList<string> patchNames, int entryIndex, List<string> warnings)
    {
        var name = DoomBinaryNames.Read(reader.ReadBytes(8));
        reader.ReadUInt16(); // flags (world-panning bit) - not modeled, unused until a game-config system exists
        reader.ReadByte(); // scalebytex - not modeled, see class remarks
        reader.ReadByte(); // scalebytey
        var width = reader.ReadInt16();
        var height = reader.ReadInt16();

        var patchCount = reader.ReadInt16();
        bool strifeFormat;
        if (patchCount == 0)
        {
            reader.BaseStream.Seek(2, SeekOrigin.Current); // remainder of vanilla's unused columndirectory field
            patchCount = reader.ReadInt16();
            strifeFormat = false;
        }
        else
        {
            strifeFormat = true;
        }

        if (width <= 0 || height <= 0 || patchCount <= 0)
        {
            warnings.Add($"Texture entry {entryIndex} ('{name}') has invalid dimensions or patch count - skipped.");
            return null;
        }

        var placements = new List<PatchPlacement>(patchCount);
        for (var p = 0; p < patchCount; p++)
        {
            var originX = reader.ReadInt16();
            var originY = reader.ReadInt16();
            var patchIndex = reader.ReadUInt16();
            if (!strifeFormat) reader.BaseStream.Seek(4, SeekOrigin.Current); // stepdir + colormap, unused

            if (patchIndex >= patchNames.Count || string.IsNullOrEmpty(patchNames[patchIndex]))
            {
                warnings.Add($"Texture '{name}' references invalid patch index {patchIndex} - patch skipped.");
                continue;
            }

            placements.Add(new PatchPlacement(originX, originY, patchNames[patchIndex]));
        }

        return new CompositeTextureDefinition(name, width, height, placements);
    }
}
