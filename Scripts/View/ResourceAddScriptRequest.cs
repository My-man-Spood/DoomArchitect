/// <summary>What the resource browser's "Add Script" action (toolbar button or context menu) resolved a <c>MapGroup</c> node down to - everything <c>AppShell</c> needs to create that map's own <c>SCRIPTS</c> lump and open it, without re-walking the tree.</summary>
public sealed class ResourceAddScriptRequest
{
	public required string WadPath { get; init; }

	/// <summary>The map marker lump's own real position in <see cref="WadPath"/>'s own lump list - what <see cref="DoomArchitect.Core.IO.WadFile.WithAddedScriptsLump"/> needs to find that map's own group.</summary>
	public required int MapMarkerLumpIndex { get; init; }
}
