namespace DoomArchitect.Core.IO;

/// <summary>
/// One loaded source of named, byte-blob game data - a real WAD
/// (<see cref="WadFile"/>) or PK3 archive (<see cref="Pk3File"/>), whichever
/// it is. <see cref="ResourceSet"/> and everything built on it (starting
/// with <see cref="Textures.TextureSet"/>) only ever talk to this
/// interface, so a texture/flat/sprite/patch lookup never needs to know or
/// care which kind of container it actually came from - the "same lump-
/// name/data lookup surface" both container kinds already shared informally
/// before this interface existed to name it.
/// </summary>
public interface IResourceContainer
{
    /// <summary>An exact, case-insensitive name match - a WAD's flat lump directory, or a PK3 file title (its own root, then each namespace folder in turn).</summary>
    WadLump? FindLump(string name);

    /// <summary>Every entry belonging to the given namespace, in container-internal order - a WAD's marker-bounded lump range, or a PK3 namespace folder's files.</summary>
    IReadOnlyList<WadLump> FindNamespaceLumps(ResourceNamespace ns);
}
