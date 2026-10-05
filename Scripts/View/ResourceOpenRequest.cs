/// <summary>
/// What <see cref="ResourceBrowserPanel"/>'s own "Open" action (double-click
/// or the right-click context menu) resolved a tree node down to -
/// everything <c>AppShell</c> needs to actually open it, without having to
/// re-walk the tree or re-resolve the node's own container itself.
/// <see cref="SourcePath"/>'s real meaning is "the WAD file to actually
/// open" - for a top-level <c>MapGroup</c> that's the owning resource's
/// own real path (from <c>NamedResource.SourcePath</c>); for a nested
/// <c>maps/MAP01.wad</c>-style <c>File</c> leaf (a map name is set, same
/// as a <c>MapGroup</c>, but it isn't one) it's that one file's own
/// resolved path instead, since the owning *folder's* path wouldn't mean
/// anything to <c>OpenMapMenu.OpenSpecificMap</c>. Exactly one of
/// <see cref="MapName"/>/<see cref="FilePath"/>/<see cref="LumpName"/> is
/// set, matching which of the openable kinds this request represents (see
/// <see cref="ResourceBrowserPanel"/>'s own "What's openable" gating).
/// </summary>
public sealed class ResourceOpenRequest
{
	public required string SourcePath { get; init; }

	/// <summary>Set for a <c>MapGroup</c> node, or a nested <c>maps/MAP01.wad</c>-style <c>File</c> leaf resolved to the map it holds - either way, the map name to open/focus.</summary>
	public string MapName { get; init; }

	/// <summary>Set only for a <c>File</c> node - its own resolved real path (distinct from <see cref="SourcePath"/>, which stays the owning folder's own path, not this one file's).</summary>
	public string FilePath { get; init; }

	/// <summary>Set only for a <c>Lump</c> node, alongside <see cref="LumpIndex"/> and <see cref="LumpData"/>.</summary>
	public string LumpName { get; init; }

	/// <summary>This lump's own real position in <see cref="SourcePath"/>'s own lump list - needed since a WAD can have more than one lump sharing a name.</summary>
	public int LumpIndex { get; init; }

	public byte[] LumpData { get; init; }
}
