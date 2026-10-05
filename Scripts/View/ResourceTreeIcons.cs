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
	private static readonly string[] GeometryLumpNames = { "VERTEXES", "LINEDEFS", "SIDEDEFS", "SECTORS", "THINGS" };
	private static readonly string[] TextureDefLumpNames = { "PNAMES", "TEXTURE1", "TEXTURE2" };
	private static readonly string[] ScriptLumpNames = { "SCRIPTS", "BEHAVIOR" };
	private static readonly string[] ScriptFileExtensions = { ".acs", ".bcs" };

	public static Texture2D For(ResourceTreeNode node) => node.Kind switch
	{
		ResourceTreeNodeKind.WadContainer => Load("icon_wad.svg"),
		ResourceTreeNodeKind.Pk3Container => Load("icon_pk3.svg"),
		ResourceTreeNodeKind.DirectoryContainer => Load("icon_folder.svg"),
		ResourceTreeNodeKind.Folder => Load("icon_folder.svg"),
		ResourceTreeNodeKind.MapGroup => Load("document_map.svg"),
		ResourceTreeNodeKind.Lump => ForLump(node.DisplayName),
		ResourceTreeNodeKind.File => ForFile(node.DisplayName),
		_ => Load("icon_lump.svg"),
	};

	private static Texture2D ForLump(string lumpName)
	{
		if (lumpName.Equals("TEXTMAP", System.StringComparison.OrdinalIgnoreCase)) return Load("document_map.svg");
		if (Contains(ScriptLumpNames, lumpName)) return Load("document_script.svg");
		if (Contains(GeometryLumpNames, lumpName)) return Load("icon_geometry.svg");
		if (Contains(TextureDefLumpNames, lumpName)) return Load("icon_texture_def.svg");
		return Load("icon_lump.svg");
	}

	private static Texture2D ForFile(string fileName)
	{
		var extension = System.IO.Path.GetExtension(fileName);
		foreach (var scriptExtension in ScriptFileExtensions)
		{
			if (extension.Equals(scriptExtension, System.StringComparison.OrdinalIgnoreCase)) return Load("document_script.svg");
		}

		return Load("icon_lump.svg");
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
