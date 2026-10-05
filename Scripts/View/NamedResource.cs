using DoomArchitect.Core.IO;

/// <summary>
/// Pairs a loaded resource container with a human-readable name (its file
/// name) and its own real source path - Core deliberately doesn't track
/// file paths, so this pairing lives at the App layer, built once in
/// <c>OpenMapMenu</c> alongside the <see cref="ResourceSet"/> itself and
/// threaded through wherever a UI needs to show "which loaded file is this
/// from" (the texture browser's per-resource tree) or needs the real file
/// back (the resource browser's own "Open" action, resolving a top-level
/// WAD/PK3/folder's own path - <see cref="IResourceContainer.ContainsFile"/>/
/// <see cref="IResourceContainer.ResolveAbsolutePath"/> only ever answer
/// for a <c>DirectoryResource</c>'s own *nested* entries, never a top-level
/// container's own path).
/// </summary>
public readonly record struct NamedResource(string DisplayName, IResourceContainer Container, string SourcePath);
