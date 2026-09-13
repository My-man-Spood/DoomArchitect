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
	private Action<string> _onSelected;
	private List<string> _displayedNames = new();
	private ImageTexture _placeholderIcon;

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

		_placeholderIcon = CreatePlaceholderIcon();
	}

	public void Browse(TextureSet textures, IReadOnlyList<NamedResource> resources, TextureIconCache icons, bool flats, string currentName, Action<string> onSelected)
	{
		_textures = textures;
		_resources = resources;
		_icons = icons;
		_flats = flats;
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
		if (selected == null || selected == _allNode)
		{
			return _flats ? _textures.GetFlatNames() : _textures.GetWallTextureNames();
		}

		var resource = _resourceByTreeItem[selected].Container;
		return _flats ? _textures.GetFlatNames(resource) : _textures.GetWallTextureNames(resource);
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

	private void SelectName(string name)
	{
		var index = _displayedNames.IndexOf(name);
		if (index < 0) return;

		_gallery.Select(index);
		_gallery.EnsureCurrentIsVisible();
	}

	private ImageTexture GetIcon(string name) =>
		(_flats ? _icons.GetFlatIcon(name) : _icons.GetWallIcon(name)) ?? _placeholderIcon;

	/// <summary>Re-checks every currently displayed name each frame and swaps in the real icon once the ambient <see cref="TextureIconCache"/> finishes it - this dialog never triggers decoding, only observes it.</summary>
	public override void _Process(double delta)
	{
		if (!Visible) return;

		for (var i = 0; i < _displayedNames.Count; i++)
		{
			var icon = _flats ? _icons.GetFlatIcon(_displayedNames[i]) : _icons.GetWallIcon(_displayedNames[i]);
			if (icon != null && _gallery.GetItemIcon(i) != icon)
			{
				_gallery.SetItemIcon(i, icon);
			}
		}
	}

	private static ImageTexture CreatePlaceholderIcon()
	{
		var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
		image.Fill(new Color(0.3f, 0.3f, 0.3f));
		return ImageTexture.CreateFromImage(image);
	}
}
