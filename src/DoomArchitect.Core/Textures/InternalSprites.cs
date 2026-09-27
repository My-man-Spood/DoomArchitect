namespace DoomArchitect.Core.Textures;

/// <summary>
/// UDB's own real <c>"internal:"</c> sprite convention
/// (<c>DataManager.INTERNAL_PREFIX</c>) - a <c>.cfg</c> thing type's
/// <c>sprite</c> field pointing not at a WAD lump but at one of UDB's own
/// bundled editor-only marker icons (<c>MapSpot</c>, <c>Camera</c>,
/// <c>Teleport</c>, <c>Slope</c>, ...), for actor types with no actual
/// in-game visual. Bundled here as embedded resources
/// (<c>Textures/InternalSprites/*.png</c>, copied verbatim from UDB's own
/// <c>Assets/Common/Sprites/</c>), decoded once and cached for the
/// process lifetime - these never change at runtime, and the cache is
/// shared across every <see cref="TextureSet"/> instance rather than
/// reloaded per loaded WAD.
/// </summary>
public static class InternalSprites
{
    private const string Prefix = "internal:";
    private const string ResourceRootPrefix = "DoomArchitect.Core.Textures.InternalSprites.";

    private static readonly Dictionary<string, PixelImage?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string>? _resourceNameByIconName;

    public static bool IsInternalName(string spriteName) =>
        spriteName.Length > Prefix.Length && spriteName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <param name="spriteName">The full stored value, e.g. <c>"internal:MapSpot"</c> - the prefix is stripped here, not by the caller.</param>
    public static PixelImage? TryGet(string spriteName, PatchImageResolver patchResolver)
    {
        var iconName = spriteName[Prefix.Length..];
        if (Cache.TryGetValue(iconName, out var cached)) return cached;

        var image = Load(iconName, patchResolver);
        Cache[iconName] = image;
        return image;
    }

    private static PixelImage? Load(string iconName, PatchImageResolver patchResolver)
    {
        _resourceNameByIconName ??= BuildIndex();
        if (!_resourceNameByIconName.TryGetValue(iconName, out var resourceName)) return null;

        using var stream = typeof(InternalSprites).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return patchResolver.TryResolveModernImage(buffer.ToArray(), out var image) ? image : null;
    }

    /// <summary>Maps a bare icon name (e.g. <c>"MapSpot"</c>, matched case-insensitively - real <c>.cfg</c> data spells some of these lowercase, e.g. <c>"pointpusher"</c>) to its embedded resource name.</summary>
    private static Dictionary<string, string> BuildIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resourceName in typeof(InternalSprites).Assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourceRootPrefix, StringComparison.Ordinal)) continue;
            if (!resourceName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

            var iconName = resourceName[ResourceRootPrefix.Length..^".png".Length];
            index[iconName] = resourceName;
        }

        return index;
    }
}
