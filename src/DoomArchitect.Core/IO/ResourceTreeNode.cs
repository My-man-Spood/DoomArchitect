namespace DoomArchitect.Core.IO;

/// <summary>
/// What one <see cref="ResourceTreeNode"/> actually represents - enough for
/// a UI consumer to pick a sensible icon/behavior without needing to know
/// which concrete <see cref="IResourceContainer"/> it came from.
/// </summary>
public enum ResourceTreeNodeKind
{
    WadContainer,
    Pk3Container,
    DirectoryContainer,

    /// <summary>One map's own contiguous lump run (<see cref="WadFile.FindMapLumpGroups"/>) - a WAD-only concept, acting as a folder-like group for its own <see cref="Lump"/> children.</summary>
    MapGroup,

    /// <summary>A single WAD lump, either inside a <see cref="MapGroup"/> or a flat top-level child of a <see cref="WadContainer"/> root.</summary>
    Lump,

    Folder,
    File,
}

/// <summary>
/// One node in a resource's own browsable tree, built by
/// <see cref="IResourceContainer.BuildTree"/> - a container-agnostic shape a
/// UI (e.g. a resource browser panel) can walk generically regardless of
/// whether it came from a <see cref="WadFile"/> (flat lumps, with
/// <see cref="ResourceTreeNodeKind.MapGroup"/> folding in each map's own
/// run), a <see cref="Pk3File"/>, or a <see cref="DirectoryResource"/> (both
/// real nested folder/file structure, via <see cref="PathTreeBuilder"/>).
/// Display-only for now - <see cref="Path"/> is carried along as the handle
/// a future action (open/add-script/consolidate) would need, but nothing
/// in this pass reads it back.
/// </summary>
public sealed class ResourceTreeNode
{
    public required string DisplayName { get; init; }
    public required ResourceTreeNodeKind Kind { get; init; }

    /// <summary>The lump name (WAD) or full relative path (PK3/directory) this node resolves to - null for a node that's purely structural (a container root, a <see cref="ResourceTreeNodeKind.MapGroup"/>, a <see cref="ResourceTreeNodeKind.Folder"/>).</summary>
    public string? Path { get; init; }

    public List<ResourceTreeNode> Children { get; } = new();
}
