using DoomArchitect.Core.IO;

/// <summary>
/// Pairs a loaded resource container with a human-readable name (its file
/// name) - Core deliberately doesn't track file paths, so this pairing
/// lives at the App layer, built once in <c>OpenMapMenu</c> alongside the
/// <see cref="ResourceSet"/> itself and threaded through wherever a UI
/// needs to show "which loaded file is this from" (the texture browser's
/// per-resource tree).
/// </summary>
public readonly record struct NamedResource(string DisplayName, IResourceContainer Container);
