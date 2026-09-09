using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Textures;

/// <summary>
/// The texture/flat resolution facade for one loaded WAD - the only type
/// most calling code needs. Resolves PLAYPAL, PNAMES, and TEXTURE1/
/// TEXTURE2 once at load time; individual wall textures and flats are
/// decoded lazily on first request and cached for the lifetime of this
/// instance. Backed by a <see cref="WadResourceSet"/> rather than a single
/// raw <see cref="WadFile"/> - a PWAD with none of its own embedded
/// resources (common for UDMF maps) needs an additional resource (its
/// IWAD) layered underneath to resolve anything at all; <see cref="Load(WadFile,IModernImageDecoder)"/>
/// keeps the single-WAD case working exactly as before by wrapping it in
/// <see cref="WadResourceSet.Single"/>.
///
/// Callers never pass the map-format sentinel <c>"-"</c> ("no texture")
/// into <see cref="GetWallTexture"/>/<see cref="GetFlatTexture"/> - that
/// sentinel is a map-format-layer concept only (UDB's own data loader has
/// zero special-casing for it), so skipping the lookup entirely for "-" is
/// the caller's job.
/// </summary>
public sealed class TextureSet
{
    private static readonly PixelImage Placeholder = CreatePlaceholder();

    private readonly WadResourceSet _resources;
    private readonly Playpal _palette;
    private readonly PatchImageResolver _patchResolver;
    private readonly Dictionary<string, CompositeTextureDefinition> _wallDefinitions;
    private readonly Dictionary<string, PixelImage> _wallCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PixelImage> _flatCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PixelImage> _spriteCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _warnings;
    private IReadOnlyList<WadLump>? _spriteRange;

    private TextureSet(
        WadResourceSet resources, Playpal palette, PatchImageResolver patchResolver,
        Dictionary<string, CompositeTextureDefinition> wallDefinitions, List<string> warnings)
    {
        _resources = resources;
        _palette = palette;
        _patchResolver = patchResolver;
        _wallDefinitions = wallDefinitions;
        _warnings = warnings;
    }

    public IReadOnlyList<string> Warnings => _warnings;

    public static TextureSet Load(WadFile wad, IModernImageDecoder? modernDecoder = null) =>
        Load(WadResourceSet.Single(wad), modernDecoder);

    public static TextureSet Load(WadResourceSet resources, IModernImageDecoder? modernDecoder = null)
    {
        var warnings = new List<string>();

        var playpalLump = resources.FindLump("PLAYPAL");
        Playpal palette;
        if (playpalLump != null)
        {
            palette = Playpal.Read(playpalLump.Data);
        }
        else
        {
            palette = Playpal.CreateFallback();
            warnings.Add("No PLAYPAL lump found - using a fallback gray palette.");
        }

        var patchResolver = new PatchImageResolver(palette, modernDecoder);

        var patchNamesLump = resources.FindLump("PNAMES");
        var patchNames = patchNamesLump != null ? PatchNames.Read(patchNamesLump.Data) : Array.Empty<string>();

        var wallDefinitions = new Dictionary<string, CompositeTextureDefinition>(StringComparer.OrdinalIgnoreCase);

        var texture1Lump = resources.FindLump("TEXTURE1");
        if (texture1Lump != null)
        {
            foreach (var definition in TextureDefinitionReader.Read(texture1Lump.Data, patchNames, isTexture1: true, warnings))
            {
                wallDefinitions[definition.Name] = definition;
            }
        }

        var texture2Lump = resources.FindLump("TEXTURE2");
        if (texture2Lump != null)
        {
            foreach (var definition in TextureDefinitionReader.Read(texture2Lump.Data, patchNames, isTexture1: false, warnings))
            {
                wallDefinitions[definition.Name] = definition;
            }
        }

        return new TextureSet(resources, palette, patchResolver, wallDefinitions, warnings);
    }

    /// <summary>Creates an empty texture set with no WAD-backed data - every lookup returns the placeholder.</summary>
    public static TextureSet CreateEmpty() =>
        new(WadResourceSet.Single(WadFile.Read(new MemoryStream(EmptyWadBytes()))), Playpal.CreateFallback(),
            new PatchImageResolver(Playpal.CreateFallback()),
            new Dictionary<string, CompositeTextureDefinition>(StringComparer.OrdinalIgnoreCase), new List<string>());

    public PixelImage GetWallTexture(string name)
    {
        if (_wallCache.TryGetValue(name, out var cached)) return cached;

        PixelImage result;
        if (_wallDefinitions.TryGetValue(name, out var definition))
        {
            result = CompositeTextureBuilder.Build(definition, ResolvePatchByName, _warnings) ?? Placeholder;
        }
        else
        {
            _warnings.Add($"Unknown wall texture '{name}' - using the placeholder.");
            result = Placeholder;
        }

        _wallCache[name] = result;
        return result;
    }

    public PixelImage GetFlatTexture(string name)
    {
        if (_flatCache.TryGetValue(name, out var cached)) return cached;

        var lump = _resources.FindLump(name);
        PixelImage result;
        if (lump == null)
        {
            _warnings.Add($"Unknown flat '{name}' - using the placeholder.");
            result = Placeholder;
        }
        else
        {
            result = DoomFlatReader.TryRead(lump.Data, _palette) ?? Placeholder;
        }

        _flatCache[name] = result;
        return result;
    }

    /// <summary>
    /// Looks up a sprite by its exact lump name (e.g. <c>"POSSA1"</c> - a
    /// game-configuration thing-type entry stores the full, specific frame
    /// to show, not just a 4-character prefix, so there's no rotation-frame
    /// guessing to do here). Returns <c>null</c> rather than the
    /// magenta/black placeholder on a miss: sprites in particular usually
    /// live only in the IWAD - loading a PWAD-only map without also
    /// layering its parent IWAD as a resource is expected to hit this
    /// often, not a load failure the placeholder is meant to signal.
    /// Callers decide their own fallback (the generic Thing placeholder
    /// icon).
    /// </summary>
    public PixelImage? TryGetSpriteTexture(string spriteName)
    {
        if (_spriteCache.TryGetValue(spriteName, out var cached)) return cached;

        _spriteRange ??= _resources.FindLumpsBetweenMarkers("S_START", "S_END");
        var lump = _spriteRange.FirstOrDefault(l => l.Name.Equals(spriteName, StringComparison.OrdinalIgnoreCase));
        if (lump == null) return null;

        var image = DoomPictureReader.TryRead(lump.Data, _palette);
        if (image == null) return null;

        _spriteCache[spriteName] = image;
        return image;
    }

    private PixelImage? ResolvePatchByName(string name)
    {
        var lump = _resources.FindLump(name);
        if (lump == null) return null;

        if (_patchResolver.TryResolvePatch(lump.Data, out var image, out var warning)) return image;

        if (warning != null) _warnings.Add(warning);
        return null;
    }

    private static PixelImage CreatePlaceholder()
    {
        const int size = 16;
        const int checkerSize = 4;
        var rgba = new byte[size * size * 4];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var isMagenta = (x / checkerSize + y / checkerSize) % 2 == 0;
                var index = (y * size + x) * 4;
                rgba[index] = isMagenta ? (byte)255 : (byte)0;
                rgba[index + 1] = 0;
                rgba[index + 2] = isMagenta ? (byte)255 : (byte)0;
                rgba[index + 3] = 255;
            }
        }

        return new PixelImage(size, size, rgba);
    }

    private static byte[] EmptyWadBytes()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("PWAD".ToCharArray());
        writer.Write(0); // lump count
        writer.Write(12); // directory offset (right after this 12-byte header)
        return stream.ToArray();
    }
}
