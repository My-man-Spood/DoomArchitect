using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Textures;
using DoomArchitect.Rendering;
using Godot;

/// <summary>
/// Browse and pick a wall texture or flat by thumbnail, ported from UDB's
/// real <c>TextureBrowserForm</c> - a per-resource tree ("All" plus one
/// node per loaded WAD/PK3, matching UDB's own real <c>ResourceTextureSet</c>
/// tree shape) alongside a live-filtered icon gallery. Single-click
/// selects only; double-click (or Enter, via <see cref="ItemList.ItemActivated"/>)
/// confirms and closes exactly like OK - matching UDB's real interaction
/// model precisely (verified against source, not guessed).
///
/// <see cref="Browse"/>'s <c>flats</c> parameter mirrors UDB's own real
/// fixed split (its <c>FlatSelectorControl</c> always browses flats,
/// <c>TextureSelectorControl</c> always browses textures - a Sector's
/// Floor/Ceiling vs. a Linedef's wall-texture fields, never varying per
/// map). <c>mixTexturesAndFlats</c> is the separate, genuinely
/// game-configuration-dependent axis - UDB's real <c>mixtexturesflats</c>
/// setting (see <see cref="DoomArchitect.Core.Configuration.IGameConfiguration.MixTexturesAndFlats"/>):
/// when true, both the flats and textures pickers additionally offer the
/// *other* namespace's names, since GZDoom/ZDoom-family configs' own real
/// texture manager doesn't distinguish them for either field; when false
/// (vanilla Doom), each picker stays restricted to its own namespace,
/// exactly as it always did before this parameter existed.
///
/// Never decodes anything itself - every icon comes from the ambient
/// <see cref="TextureIconCache"/>, which starts warming the moment a map
/// loads (see <c>MapView</c>), long before this dialog is ever opened. A
/// shared gray placeholder icon stands in for anything not decoded yet;
/// <see cref="_Process"/> just re-polls the cache each frame for whatever
/// is currently displayed and swaps in the real icon once it's ready.
/// </summary>
public partial class TextureBrowserDialog : AcceptDialog
{
	private Tree _tree;
	private TreeItem _allNode;
	private LineEdit _filterEdit;
	private ItemList _gallery;

	private readonly Dictionary<TreeItem, NamedResource> _resourceByTreeItem = new();

	private TextureSet _textures;
	private IReadOnlyList<NamedResource> _resources = Array.Empty<NamedResource>();
	private TextureIconCache _icons;
	private bool _flats;
	private bool _mixTexturesAndFlats;
	private Action<string> _onSelected;
	private List<string> _displayedNames = new();

	public override void _Ready()
	{
		_tree = GetNode<Tree>("Container/TreePanel/ResourceTree");
		_filterEdit = GetNode<LineEdit>("Container/GalleryPanel/FilterEdit");
		_gallery = GetNode<ItemList>("Container/GalleryPanel/Gallery");

		_tree.HideRoot = true;
		_tree.ItemSelected += RefreshGalleryList;
		_filterEdit.TextChanged += _ => RefreshGalleryList();
		_gallery.ItemActivated += index => ConfirmSelection(_displayedNames[(int)index]);

		Confirmed += OnConfirmed;
	}

	public void Browse(
		TextureSet textures, IReadOnlyList<NamedResource> resources, TextureIconCache icons,
		bool flats, bool mixTexturesAndFlats, string currentName, Action<string> onSelected)
	{
		_textures = textures;
		_resources = resources;
		_icons = icons;
		_flats = flats;
		_mixTexturesAndFlats = mixTexturesAndFlats;
		_onSelected = onSelected;

		PopulateTree();
		_allNode.Select(0);
		_filterEdit.Text = "";
		RefreshGalleryList();
		SelectName(currentName);

		Title = flats ? "Browse Flats" : "Browse Textures";
		PopupCentered();
	}

	private void PopulateTree()
	{
		_tree.Clear();
		_resourceByTreeItem.Clear();

		var root = _tree.CreateItem();
		_allNode = _tree.CreateItem(root);
		_allNode.SetText(0, "All");

		foreach (var resource in _resources)
		{
			var node = _tree.CreateItem(root);
			node.SetText(0, resource.DisplayName);
			_resourceByTreeItem[node] = resource;
		}
	}

	private void OnConfirmed()
	{
		var selected = _gallery.GetSelectedItems();
		if (selected.Length > 0) ConfirmSelection(_displayedNames[selected[0]]);
	}

	private void ConfirmSelection(string name)
	{
		_onSelected?.Invoke(name);
		Hide();
	}

	private IReadOnlyList<string> GetBaseNames()
	{
		var selected = _tree.GetSelected();
		var resource = selected == null || selected == _allNode ? null : _resourceByTreeItem[selected].Container;

		var ownNames = _flats
			? (resource == null ? _textures.GetFlatNames() : _textures.GetFlatNames(resource))
			: (resource == null ? _textures.GetWallTextureNames() : _textures.GetWallTextureNames(resource));

		if (!_mixTexturesAndFlats) return ownNames;

		var otherNames = _flats
			? (resource == null ? _textures.GetWallTextureNames() : _textures.GetWallTextureNames(resource))
			: (resource == null ? _textures.GetFlatNames() : _textures.GetFlatNames(resource));

		return ownNames.Concat(otherNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void RefreshGalleryList()
	{
		var baseNames = GetBaseNames();
		var filter = _filterEdit.Text.Trim();

		_displayedNames = (filter.Length == 0 ? baseNames : baseNames.Where(n => n.Contains(filter, StringComparison.OrdinalIgnoreCase)))
			.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
			.ToList();

		_gallery.Clear();
		foreach (var name in _displayedNames)
		{
			_gallery.AddItem(name, GetIcon(name));
		}
	}

	/// <summary>
	/// Case-insensitive on purpose: a map's stored texture name and the
	/// resource's own real lump/directory casing can legitimately differ
	/// (same reasoning as <see cref="TextureIconCache"/>'s own
	/// case-insensitive caches), and an exact-case miss here silently
	/// leaves the current selection un-highlighted with no visible error.
	/// </summary>
	private void SelectName(string name)
	{
		var index = _displayedNames.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
		if (index < 0) return;

		_gallery.Select(index);
		_gallery.EnsureCurrentIsVisible();
	}

	private ImageTexture ResolveIcon(string name) => _icons.GetIcon(name, preferFlat: _flats, _mixTexturesAndFlats);

	private ImageTexture GetIcon(string name) => ResolveIcon(name) ?? PlaceholderIcon.Instance;

	/// <summary>Re-checks every currently displayed name each frame and swaps in the real icon once the ambient <see cref="TextureIconCache"/> finishes it - this dialog never triggers decoding, only observes it.</summary>
	public override void _Process(double delta)
	{
		if (!Visible) return;

		for (var i = 0; i < _displayedNames.Count; i++)
		{
			var icon = ResolveIcon(_displayedNames[i]);
			if (icon != null && _gallery.GetItemIcon(i) != icon)
			{
				_gallery.SetItemIcon(i, icon);
			}
		}
	}
}
