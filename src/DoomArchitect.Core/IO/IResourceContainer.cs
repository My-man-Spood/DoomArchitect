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
    /// include convention); a PK3 or a directory resource resolves the
    /// full nested path.
    /// </summary>
    byte[]? FindByPath(string path);

    /// <summary>A browsable tree for this whole container - see <see cref="ResourceTreeNode"/>'s own remarks. <paramref name="displayName"/> becomes the returned root's own name, since no container knows its own on-disk filename/label.</summary>
    ResourceTreeNode BuildTree(string displayName);

    /// <summary>
    /// Whether this container already provides the exact file at
    /// <paramref name="absolutePath"/> as one of its own entries - used to
    /// avoid double-counting a map's own backing WAD as a separate resource
    /// when it physically lives inside a folder/PK3 that's already
    /// configured as one (e.g. a GZDoom-convention <c>maps/MAP01.wad</c>
    /// sitting inside a mod's own resource folder). Only a
    /// <see cref="DirectoryResource"/> can ever really answer true here - a
    /// WAD or PK3 has no standalone, independently-addressable files of its
    /// own on disk to match against.
    /// </summary>
    bool ContainsFile(string absolutePath);

    /// <summary>
    /// The real on-disk absolute path backing <paramref name="relativePath"/>
    /// (one of this container's own entries, exactly as its own
    /// <see cref="BuildTree"/> output names it), or null if there isn't one -
    /// a WAD or PK3 has no standalone files of its own to resolve to, same
    /// as <see cref="ContainsFile"/>. Lets a caller that only has a tree
    /// node's own relative path (e.g. the resource browser, matching a
    /// folder-nested leaf against the currently open map's real file) go
    /// the other direction from <see cref="ContainsFile"/> without needing
    /// to know this container's own root itself.
    /// </summary>
    string? ResolveAbsolutePath(string relativePath);
}
