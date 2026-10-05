using DoomArchitect.Core.IO;
using Godot;

/// <summary>
/// Which icon a <see cref="ResourceTreeNode"/> gets in the resource browser -
/// the single source of truth so the panel itself doesn't need its own
/// switch statement. A curated set, deliberately not exhaustive (per the
/// browser's own design) - a lump/file name that isn't specifically
/// recognized here falls back to a plain generic icon rather than growing
/// this list to cover every possible name; new specific icons are easy to
/// add later without touching the tree-building logic itself.
/// </summary>
public static class ResourceTreeIcons
{
	private static readonly string[] TextureDefLumpNames = { "PNAMES", "TEXTURE1", "TEXTURE2" };
	private static readonly string[] ScriptLumpNames = { "SCRIPTS", "BEHAVIOR" };
	private static readonly string[] ScriptFileExtensions = { ".acs", ".bcs" };

	public static Texture2D For(ResourceTreeNode node) => node.Kind switch
	{
		ResourceTreeNodeKind.WadContainer => Load("icon_wad.png"),
		ResourceTreeNodeKind.Pk3Container => Load("icon_pk3.png"),
		ResourceTreeNodeKind.DirectoryContainer => Load("icon_folder.png"),
		ResourceTreeNodeKind.Folder => Load("icon_folder.png"),
		ResourceTreeNodeKind.MapGroup => Load("document_map.svg"),
		ResourceTreeNodeKind.Lump => ForLump(node.DisplayName),
		ResourceTreeNodeKind.File => ForFile(node.DisplayName),
		_ => Load("icon_lump.png"),
	};

	/// <summary>
	/// VERTEXES/LINEDEFS/SECTORS/THINGS each get the real icon UDB's own
	/// classic edit-mode toolbar uses for that same concept
	/// (VerticesMode.png/LinesMode.png/SectorsMode.png/ThingsMode.png) -
	/// SIDEDEFS shares the linedef icon since UDB has no separate sidedef
	/// edit mode of its own to borrow from (a sidedef never exists without
	/// its parent linedef).
	/// </summary>
	private static Texture2D ForLump(string lumpName)
	{
		if (lumpName.Equals("TEXTMAP", System.StringComparison.OrdinalIgnoreCase)) return Load("document_map.svg");
		if (Contains(ScriptLumpNames, lumpName)) return Load("document_script.svg");
		if (lumpName.Equals("VERTEXES", System.StringComparison.OrdinalIgnoreCase)) return Load("icon_vertices.png");
		if (lumpName.Equals("LINEDEFS", System.StringComparison.OrdinalIgnoreCase)) return Load("icon_linedefs.png");
		if (lumpName.Equals("SIDEDEFS", System.StringComparison.OrdinalIgnoreCase)) return Load("icon_linedefs.png");
		if (lumpName.Equals("SECTORS", System.StringComparison.OrdinalIgnoreCase)) return Load("icon_sectors.png");
		if (lumpName.Equals("THINGS", System.StringComparison.OrdinalIgnoreCase)) return Load("icon_things.png");
		if (Contains(TextureDefLumpNames, lumpName)) return Load("icon_texture_def.png");
		return Load("icon_lump.png");
	}

	private static Texture2D ForFile(string fileName)
	{
		var extension = System.IO.Path.GetExtension(fileName);
		foreach (var scriptExtension in ScriptFileExtensions)
		{
			if (extension.Equals(scriptExtension, System.StringComparison.OrdinalIgnoreCase)) return Load("document_script.svg");
		}

		return Load("icon_lump.png");
	}

	private static bool Contains(string[] names, string value)
	{
		foreach (var name in names)
		{
			if (name.Equals(value, System.StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static Texture2D Load(string fileName) => GD.Load<Texture2D>($"res://Assets/Icons/{fileName}");
}
