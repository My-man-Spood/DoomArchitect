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

    /// <summary>
    /// Resolves a literal `#include`-style path (e.g. a ZScript
    /// `#include "zscript/actors/actor.zs"`) to that entry's raw bytes, or
    /// null if it can't be found - the one lookup <see cref="FindLump"/>
    /// deliberately doesn't cover, since that one is root/namespace-only by
    /// design. A WAD has no real path hierarchy at all, so it treats
    /// <paramref name="path"/> as a bare lump name (its own real ZScript
    /// include convention); a PK3 resolves the full nested path.
    /// </summary>
    byte[]? FindByPath(string path);
}
