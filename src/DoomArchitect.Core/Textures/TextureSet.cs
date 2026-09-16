using DoomArchitect.Core.IO;

namespace DoomArchitect.Core.Textures;

/// <summary>
/// The texture/flat resolution facade for one loaded WAD - the only type
/// most calling code needs. Resolves PLAYPAL, PNAMES, and TEXTURE1/
/// TEXTURE2 once at load time; individual wall textures and flats are
/// decoded lazily on first request and cached for the lifetime of this
/// instance. Backed by a <see cref="ResourceSet"/> rather than a single
/// raw <see cref="IResourceContainer"/> - a PWAD with none of its own
/// embedded resources (common for UDMF maps) needs an additional resource
/// (its IWAD, or nowadays a PK3 such as <c>gzdoom.pk3</c>) layered
/// underneath to resolve anything at all; <see cref="Load(IResourceContainer,IModernImageDecoder)"/>
/// keeps the single-resource case working exactly as before by wrapping it
/// in <see cref="ResourceSet.Single"/>.
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

    private readonly ResourceSet _resources;
    private readonly Playpal _palette;
    private readonly PatchImageResolver _patchResolver;
    private readonly Dictionary<string, CompositeTextureDefinition> _wallDefinitions;
    private readonly Dictionary<string, PixelImage> _wallCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PixelImage> _flatCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PixelImage> _spriteCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _warnings;
    private IReadOnlyList<WadLump>? _spriteRange;
    private IReadOnlyList<WadLump>? _folderWallImages;

    private TextureSet(
        ResourceSet resources, Playpal palette, PatchImageResolver patchResolver,
        Dictionary<string, CompositeTextureDefinition> wallDefinitions, List<string> warnings)
    {
        _resources = resources;
        _palette = palette;
        _patchResolver = patchResolver;
        _wallDefinitions = wallDefinitions;
        _warnings = warnings;
    }

    public IReadOnlyList<string> Warnings => _warnings;

    public static TextureSet Load(IResourceContainer resource, IModernImageDecoder? modernDecoder = null) =>
        Load(ResourceSet.Single(resource), modernDecoder);

    public static TextureSet Load(ResourceSet resources, IModernImageDecoder? modernDecoder = null)
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
        new(ResourceSet.Single(WadFile.Read(new MemoryStream(EmptyWadBytes()))), Playpal.CreateFallback(),
            new PatchImageResolver(Playpal.CreateFallback()),
            new Dictionary<string, CompositeTextureDefinition>(StringComparer.OrdinalIgnoreCase), new List<string>());

    /// <summary>
    /// Checks the classic <c>TEXTURE1</c>/<c>TEXTURE2</c>-composed
    /// definitions first, then falls back to a plain image sitting in a
    /// PK3's <c>textures/</c> folder - real GZDoom/UDB behavior verified
    /// against <c>PK3StructuredReader.LoadTextures</c>: a folder image is
    /// used directly as the complete picture (never patch-composited) and
    /// only fills in a name nothing else already defines ("Textures
    /// defined in TEXTURES override ones in 'textures' folder" - the same
    /// first-write-wins precedence applies to <c>TEXTURE1</c>/<c>TEXTURE2</c>,
    /// which this project resolves before ever consulting the folder).
    /// </summary>
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
            var folderImage = FolderWallImages.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (folderImage != null)
            {
                result = ResolveAsModernImage(folderImage.Data);
            }
            else
            {
                _warnings.Add($"Unknown wall texture '{name}' - using the placeholder.");
                result = Placeholder;
            }
        }

        _wallCache[name] = result;
        return result;
    }

    /// <summary>
    /// Every wall texture name this set can resolve - the classic
    /// <c>TEXTURE1</c>/<c>TEXTURE2</c> definitions plus any PK3
    /// <c>textures/</c>-folder image not already covered by them.
    /// </summary>
    public IReadOnlyList<string> GetWallTextureNames() =>
        _wallDefinitions.Keys
            .Concat(FolderWallImages.Select(l => l.Name).Where(n => !_wallDefinitions.ContainsKey(n)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// One resource's own contribution to wall textures - its own
    /// <c>textures/</c>-folder images, plus the classic
    /// <c>TEXTURE1</c>/<c>TEXTURE2</c> names only if <paramref name="resource"/>
    /// is <see cref="WallTextureSource"/> (the one resource that actually
    /// won them - see its own remarks for why that's winner-take-all,
    /// never merged, matching UDB's own real behavior).
    /// </summary>
    public IReadOnlyList<string> GetWallTextureNames(IResourceContainer resource) =>
        (resource.Equals(WallTextureSource) ? (IEnumerable<string>)_wallDefinitions.Keys : Array.Empty<string>())
            .Concat(resource.FindNamespaceLumps(ResourceNamespace.Textures).Select(l => l.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// The one resource <c>TEXTURE1</c>/<c>TEXTURE2</c> actually came from
    /// today - these are single indivisible lumps, so layering several
    /// resources never merges their texture definitions together the way
    /// e.g. flats do; the highest-priority resource that defines either
    /// lump wins the whole thing, matching UDB's own real
    /// <c>DataManager</c> precedence. <c>null</c> if neither is defined
    /// anywhere in this set.
    /// </summary>
    public IResourceContainer? WallTextureSource => _resources.FindLumpSource("TEXTURE1") ?? _resources.FindLumpSource("TEXTURE2");

    /// <summary>
    /// Tries the raw headerless 64x64 classic flat format first, then
    /// falls back to a modern image (e.g. a PNG sitting in a PK3's
    /// <c>flats/</c> folder) - real GZDoom/UDB treat flats and wall
    /// textures symmetrically for decode-format detection (both go
    /// through the same <c>PK3FileImage</c> loader in UDB), so this
    /// project doesn't special-case one over the other either.
    /// </summary>
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
            result = DoomFlatReader.TryRead(lump.Data, _palette) ?? ResolveAsModernImage(lump.Data);
        }

        _flatCache[name] = result;
        return result;
    }

    /// <summary>Every flat name this set can resolve, merged across every layered resource (unlike <see cref="WallTextureSource"/>'s classic definitions, flats are already resolved per-container and naturally combine).</summary>
    public IReadOnlyList<string> GetFlatNames() =>
        _resources.FindNamespaceLumps(ResourceNamespace.Flats)
            .Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>One resource's own flat names, ignoring every other layered resource.</summary>
    public IReadOnlyList<string> GetFlatNames(IResourceContainer resource) =>
        resource.FindNamespaceLumps(ResourceNamespace.Flats).Select(l => l.Name).ToList();

    private IReadOnlyList<WadLump> FolderWallImages => _folderWallImages ??= _resources.FindNamespaceLumps(ResourceNamespace.Textures);

    /// <summary>Shared by the PK3-folder fallback paths on both <see cref="GetWallTexture"/> and <see cref="GetFlatTexture"/> - a folder image is a complete picture, never a composite, so this just reuses the same sniff-then-decode resolver already trusted for embedded-PNG patches.</summary>
    private PixelImage ResolveAsModernImage(byte[] data)
    {
        if (_patchResolver.TryResolvePatch(data, out var image, out var warning)) return image!;
        if (warning != null) _warnings.Add(warning);
        return Placeholder;
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

        _spriteRange ??= _resources.FindNamespaceLumps(ResourceNamespace.Sprites);
        var lump = _spriteRange.FirstOrDefault(l => l.Name.Equals(spriteName, StringComparison.OrdinalIgnoreCase));
        if (lump == null) return null;

        var image = DoomPictureReader.TryRead(lump.Data, _palette);
        if (image == null) return null;

        _spriteCache[spriteName] = image;
        return image;
    }

    /// <summary>
    /// Resolves the real Doom sprite-rotation naming convention (a public,
    /// objective engine fact - not UDB's own creative content, the same
    /// kind of safe-to-implement-directly fact as a UDMF field name) into an
    /// 8-entry table, one per viewing angle (index 0 = rotation digit 1,
    /// index 7 = rotation digit 8 - matching UDB's own real
    /// <c>ThingTypeInfo.SpriteFrame</c> array indexing exactly, verified
    /// directly against its own <c>SetupSpriteFrame</c>/render-time
    /// <c>info.SpriteFrame[spriteangle]</c> lookup). <paramref name="representativeSpriteName"/>
    /// is a game-configuration thing-type's own stored <c>sprite</c> value
    /// (e.g. <c>"TROOA2A8"</c>) - only its first 5 characters (actor code +
    /// frame letter, e.g. <c>"TROOA"</c>) matter here; the specific
    /// rotation digit(s) already baked into that one representative string
    /// are irrelevant since every other real rotation lump sharing that
    /// same actor+frame prefix is enumerated directly from the loaded
    /// resources. A lump named <c>base + "0"</c> (e.g. <c>"TROOA0"</c>)
    /// means "this frame doesn't rotate at all" - every slot resolves to
    /// it. An 8-character lump (<c>base + digit + frame + digit</c>, e.g.
    /// <c>"TROOA2A8"</c>) is Doom's real mirrored-pair optimization: the
    /// same drawn image serves two opposite rotations, one of them
    /// horizontally flipped - <see cref="SpriteRotationFrame.Mirror"/>
    /// flags exactly which. A rotation with no lump at all falls back to
    /// <paramref name="representativeSpriteName"/> itself (never a null
    /// entry), matching this project's own established "always resolve to
    /// something renderable" convention.
    /// </summary>
    public IReadOnlyList<SpriteRotationFrame> ResolveSpriteRotations(string representativeSpriteName)
    {
        var slots = new SpriteRotationFrame?[8];

        if (representativeSpriteName.Length >= 5)
        {
            var basePrefix = representativeSpriteName[..5];
            var frameLetter = representativeSpriteName[4];

            _spriteRange ??= _resources.FindNamespaceLumps(ResourceNamespace.Sprites);
            foreach (var lump in _spriteRange)
            {
                if (lump.Name.Length is not (6 or 8)) continue;
                if (!lump.Name.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase)) continue;

                var rotation1 = lump.Name[5];
                if (rotation1 == '0')
                {
                    for (var i = 0; i < 8; i++) slots[i] ??= new SpriteRotationFrame(lump.Name, Mirror: false);
                    continue;
                }

                if (rotation1 is >= '1' and <= '8') slots[rotation1 - '1'] ??= new SpriteRotationFrame(lump.Name, Mirror: false);

                if (lump.Name.Length != 8) continue;
                var frameLetter2 = lump.Name[6];
                var rotation2 = lump.Name[7];
                if (char.ToUpperInvariant(frameLetter2) != char.ToUpperInvariant(frameLetter)) continue;
                if (rotation2 is >= '1' and <= '8') slots[rotation2 - '1'] ??= new SpriteRotationFrame(lump.Name, Mirror: true);
            }
        }

        for (var i = 0; i < 8; i++) slots[i] ??= new SpriteRotationFrame(representativeSpriteName, Mirror: false);

        return slots!;
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

/// <summary>One resolved rotation slot from <see cref="TextureSet.ResolveSpriteRotations"/> - the real sprite lump to show for that viewing angle, and whether it needs to be drawn horizontally flipped (Doom's real mirrored-rotation-pair convention).</summary>
public sealed record SpriteRotationFrame(string LumpName, bool Mirror);
