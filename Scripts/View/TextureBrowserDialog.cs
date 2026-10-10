using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Controls;
using DoomArchitect.Core.IO;
using DoomArchitect.Core.Textures;
using DoomArchitect.Rendering;
using Godot;

/// <summary>
/// Browse and pick a wall texture or flat by thumbnail - a per-resource
/// tree ("All" plus one node per loaded WAD/PK3) alongside a live-filtered
/// icon gallery, driven by the shared, reusable
/// <see cref="VirtualizedGrid{TItem,TCell}"/> (<see cref="_grid"/>) rather
/// than Godot's built-in <see cref="ItemList"/> (used here originally):
/// that control renders every item's icon/text internally with no
/// per-item overlay slot for a real size-badge node, and a first attempt
/// at giving every name its own permanent cell node instead was a real,
/// reported performance problem on a large resource set - see
/// <see cref="VirtualizedGrid{TItem,TCell}"/>'s own remarks for why.
///
/// Single click selects only; double-click (or Enter, via
/// <see cref="OnConfirmed"/>) confirms and closes exactly like OK.
///
/// <see cref="Browse"/>'s <c>flats</c> parameter is a fixed split - a
/// Sector's Floor/Ceiling fields always browse flats, a Linedef's
/// wall-texture fields always browse textures, never varying per map.
/// <c>mixTexturesAndFlats</c> is the separate, genuinely
/// game-configuration-dependent axis (see
/// <see cref="DoomArchitect.Core.Configuration.IGameConfiguration.MixTexturesAndFlats"/>):
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
/// <see cref="_Process"/> re-polls the cache each frame for whatever's
/// currently pooled by <see cref="_grid"/> (not the full list - only ever
/// a handful of names at once) and pushes the real icon in once it's
/// ready (<see cref="VirtualizedGrid{TItem,TCell}.UpdateItem"/>).
/// </summary>
public partial class TextureBrowserDialog : AcceptDialog
{
	private static readonly PackedScene CellScene = GD.Load<PackedScene>("res://Scenes/UI/TextureGalleryCell.tscn");

	/// <summary>
	/// <see cref="TextureGalleryCell.tscn"/>'s own real footprint (128
	/// wide root + 4px margins either side; ~120 preview + a few px
	/// VBoxContainer separation + a ~20px name label + 4px margins top/
	/// bottom) plus the gap the gallery used to get for free from a
	/// container's own spacing, before <see cref="VirtualizedGrid{TItem,TCell}"/>
	/// started positioning cells by hand.
	/// </summary>
	private static readonly Vector2 CellSize = new(128f, 152f);
	private static readonly Vector2 CellGap = new(12f, 8f);

	private Tree _tree;
	private TreeItem _allNode;
	private LineEdit _filterEdit;
	private VirtualizedGrid<TextureGalleryItem, TextureGalleryCell> _grid;

	private readonly Dictionary<TreeItem, NamedResource> _resourceByTreeItem = new();

	/// <summary>Names already handed a real (non-null) icon at least once - cleared on every new <see cref="Browse"/> call since the same name can legitimately mean a different real icon across different maps/resource sets (<see cref="_icons"/> itself gets reseeded then too).</summary>
	private readonly HashSet<string> _resolvedNames = new(StringComparer.OrdinalIgnoreCase);

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

		var galleryScroll = GetNode<ScrollContainer>("Container/GalleryPanel/Gallery");
		var galleryContent = GetNode<Control>("Container/GalleryPanel/Gallery/GalleryContent");
		_grid = new VirtualizedGrid<TextureGalleryItem, TextureGalleryCell>(galleryScroll, galleryContent, CellScene, CellSize, CellGap);
		_grid.ItemActivated += (_, item) => ConfirmSelection(item.Name);

		_tree.HideRoot = true;
		_tree.ItemSelected += RefreshGalleryList;
		_filterEdit.TextChanged += _ => RefreshGalleryList();

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
		_resolvedNames.Clear();

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
		var index = _grid.SelectedIndex;
		if (index >= 0 && index < _displayedNames.Count) ConfirmSelection(_displayedNames[index]);
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

		_grid.SetItems(_displayedNames.Select(n => new TextureGalleryItem(n, _resolvedNames.Contains(n) ? ResolveIcon(n) : null)).ToList());
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

		_grid.SelectIndex(index);
		_grid.ScrollToIndex(index);
	}

	private ImageTexture ResolveIcon(string name) => _icons.GetIcon(name, preferFlat: _flats, _mixTexturesAndFlats);

	public override void _Process(double delta)
	{
		if (!Visible) return;

		_grid.Update();

		foreach (var (index, item) in _grid.ActiveItems)
		{
			if (_resolvedNames.Contains(item.Name)) continue;

			var icon = ResolveIcon(item.Name);
			if (icon == null) continue;

			_resolvedNames.Add(item.Name);
			_grid.UpdateItem(index, new TextureGalleryItem(item.Name, icon));
		}
	}
}
