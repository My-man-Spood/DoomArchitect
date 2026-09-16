using System;
using System.Collections.Generic;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Rendering;
using Godot;

/// <summary>
/// Embedded thing-type picker, ported from UDB's real
/// <c>ThingBrowserControl</c> - unlike the Sector Special/Linedef Action
/// pickers (both a separate popup "Browse..." dialog), UDB's own real
/// control lives directly inside the Properties tab's own " Thing " group,
/// confirmed directly in <c>ThingEditFormUDMF.Designer.cs</c> (no popup
/// form involved at all). A filterable category/subcategory
/// <see cref="Tree"/> (the same grouping shape already built for
/// <see cref="LinedefActionBrowserDialog"/>, just embedded rather than in
/// a dialog), a <c>typeid</c> numeric field two-way-bound to the tree
/// selection, and a live sprite preview panel for whichever type is
/// currently selected - matching UDB's own real layout. Each tree leaf
/// also gets a small <see cref="SpriteIconCache"/> thumbnail - UDB's own
/// real row only shows a small colored category icon, upgraded here to an
/// actual sprite icon since sprite previews are in scope for this pass
/// (a deliberate, flagged enhancement, not a UDB behavior).
/// </summary>
public partial class ThingTypePicker : VBoxContainer
{
	/// <summary>
	/// UDB's own real tree row icon is a small, fixed-size colored category
	/// icon (not a full sprite) - this project's own real sprite icons
	/// (see this class's own remarks) can be arbitrarily large (a boss
	/// monster's own sprite is easily 100+ px), so the row icon is
	/// explicitly capped to a small fixed size via
	/// <see cref="TreeItem.SetIconMaxWidth"/> rather than left to render at
	/// its native decoded resolution - an uncapped icon here was blowing up
	/// both the row height and the icon column's own auto-sized width,
	/// which in turn squeezed the name column down to nothing.
	/// </summary>
	private const int TreeIconMaxSize = 16;

	/// <summary>
	/// Real UDB category keys/titles (verified against <c>Doom_things.cfg</c>'s/
	/// <c>ZDoom_things.cfg</c>'s/<c>GZDoom_things.cfg</c>'s/<c>Boom_things.cfg</c>'s
	/// own real top-level block names/<c>title</c> fields), covering every
	/// category this project's own bundled thing-type data actually
	/// populates (vanilla Doom/Doom2 plus the real ZDoom/GZDoom/Boom
	/// additions - see <c>GZDoomThings.cfg</c>/<c>ZDoomThings.cfg</c>/
	/// <c>BoomThings.cfg</c>) - plain taxonomy labels, the same kind of
	/// safe, objective reuse as a UDMF field name (see
	/// <see cref="LinedefActionBrowserDialog.CategoryOrder"/>'s own
	/// identical reasoning). A category key missing from this list would
	/// be silently invisible in the tree (see <see cref="RefreshList"/>'s
	/// own <c>byCategory[key]</c> lookup) even though its entries loaded
	/// fine into <see cref="Core.Configuration.IGameConfiguration.GetThingTypes"/> -
	/// this list is the one place that must stay in sync whenever the
	/// bundled thing-type category set grows.
	/// </summary>
	private static readonly (string Key, string Title)[] CategoryOrder =
	{
		("players", "Player Starts"),
		("teleports", "Teleports"),
		("monsters", "Monsters"),
		("stealthmonsters", "Stealth Monsters"),
		("weapons", "Weapons"),
		("ammunition", "Ammunition"),
		("health", "Health and Armor"),
		("powerups", "Powerups"),
		("keys", "Keys"),
		("obstacles", "Obstacles"),
		("lights", "Light Sources"),
		("decoration", "Decoration"),
		("bridges", "Bridges"),
		("marine", "Marines"),
		("sounds", "Sounds"),
		("cameras", "Cameras and Interpolation"),
		("sectors", "Sector Actions"),
		("slopes", "Slopes"),
		("portals", "Portals"),
		("boom", "Boom Items"),
		("dynlights", "Dynamic Lights"),
		("spotlights", "Dynamic Spot Lights"),
		("zdoom", "ZDoom"),
	};

	private LineEdit _filterEdit;
	private Tree _tree;
	private StepperLineEdit _typeIdEdit;
	private TextureRect _previewIcon;
	private Label _previewNameLabel;

	private SpriteIconCache _spriteIconCache;
	private IReadOnlyList<ThingTypeInfo> _allTypes = Array.Empty<ThingTypeInfo>();
	private readonly Dictionary<TreeItem, ThingTypeInfo> _typeByTreeItem = new();
	private bool _suppressEvents;

	/// <summary>
	/// Raised on every change to the underlying <c>typeid</c> text - manual
	/// typing, or a tree click (which sets <see cref="TypeIdText"/>'s own
	/// silent setter, then fires this manually, exactly the same "silent
	/// setter, caller re-fires the equivalent event" idiom
	/// <see cref="ActionArgumentsEditor.BrowseAction"/> already uses). Raw
	/// text, not a parsed number - the host dialog resolves it the same
	/// <see cref="NumericFieldExpression"/> way every other real-time field
	/// here does, including blank/mixed-selection handling.
	/// </summary>
	public event Action<string> TypeIdTextChanged;

	public override void _Ready()
	{
		_filterEdit = GetNode<LineEdit>("FilterEdit");
		_tree = GetNode<Tree>("Tree");
		_typeIdEdit = GetNode<StepperLineEdit>("TypeIdRow/TypeIdEdit");
		_previewIcon = GetNode<TextureRect>("PreviewRow/PreviewIcon");
		_previewNameLabel = GetNode<Label>("PreviewRow/PreviewNameLabel");

		_tree.Columns = 2;
		_tree.HideRoot = true;
		_tree.ColumnTitlesVisible = false;
		_tree.SetColumnExpand(0, false);
		_tree.SetColumnCustomMinimumWidth(0, TreeIconMaxSize + 4);
		_tree.SetColumnExpand(1, true);
		_tree.SetColumnExpandRatio(1, 1);

		_filterEdit.TextChanged += _ => RefreshList();
		_tree.ItemSelected += OnTreeItemSelected;
		_typeIdEdit.TextChanged += OnTypeIdTextChanged;
	}

	/// <summary>
	/// Re-polls every tree row's icon plus the bigger preview panel each
	/// frame, exactly matching <see cref="SectorEditDialog._Process"/>'s own
	/// reasoning - this control never triggers <see cref="SpriteIconCache"/>
	/// decoding itself, so a row created before its sprite finished
	/// decoding would otherwise show the placeholder forever instead of
	/// swapping in the real icon once it's ready.
	/// </summary>
	public override void _Process(double delta)
	{
		if (!Visible) return;

		foreach (var (item, type) in _typeByTreeItem)
		{
			item.SetIcon(0, _spriteIconCache?.GetSpriteIcon(type.SpriteName) ?? PlaceholderIcon.Instance);
			item.SetIconMaxWidth(0, TreeIconMaxSize);
		}

		// Only the preview icon, not the full SyncTreeAndPreviewFromTypeId -
		// that method also selects/expands/scrolls the tree, which must
		// only ever happen in response to an actual selection change, never
		// unconditionally every frame (it would fight the user's own manual
		// collapse/scroll/selection).
		if (int.TryParse(_typeIdEdit.Text.Trim(), out var doomEdNum))
		{
			UpdatePreview(_allTypes.FirstOrDefault(t => t.DoomEdNum == doomEdNum));
		}
	}

	/// <summary>Call once per fresh selection, mirroring <see cref="ActionArgumentsEditor.Setup"/>'s own role - rebuilds the whole tree from this configuration's real thing-type table.</summary>
	public void Setup(IGameConfiguration gameConfiguration, SpriteIconCache spriteIconCache)
	{
		_spriteIconCache = spriteIconCache;
		_allTypes = gameConfiguration.GetThingTypes();

		_filterEdit.Text = "";
		RefreshList();
	}

	/// <summary>Blank for a mixed-across-selection DoomEd number, matching every other numeric field's own blank convention in this project's dialogs.</summary>
	public string TypeIdText
	{
		get => _typeIdEdit.Text;
		set
		{
			_suppressEvents = true;
			_typeIdEdit.Text = value;
			_suppressEvents = false;
			SyncTreeAndPreviewFromTypeId();
		}
	}

	/// <summary>
	/// Rebuilds the whole tree - one collapsible category folder per real
	/// category (skipping any with zero matches), each containing its own
	/// types sorted by DoomEd number, same collapsed-by-default/auto-
	/// expand-on-filter behavior as <see cref="LinedefActionBrowserDialog.RefreshList"/>.
	/// </summary>
	private void RefreshList()
	{
		var filter = _filterEdit.Text.Trim();
		var filtering = filter.Length > 0;
		var previousTypeId = _typeIdEdit.Text;

		_tree.Clear();
		_typeByTreeItem.Clear();
		var root = _tree.CreateItem();

		var byCategory = _allTypes.ToLookup(t => t.Category);

		foreach (var (key, title) in CategoryOrder)
		{
			var types = byCategory[key]
				.Where(t => !filtering || $"{t.DoomEdNum} {t.Title} {title}".Contains(filter, StringComparison.OrdinalIgnoreCase))
				.OrderBy(t => t.DoomEdNum)
				.ToList();
			if (types.Count == 0) continue;

			var categoryItem = _tree.CreateItem(root);
			categoryItem.SetText(1, title);
			categoryItem.SetSelectable(0, false);
			categoryItem.SetSelectable(1, false);
			categoryItem.Collapsed = !filtering;

			foreach (var type in types)
			{
				var item = _tree.CreateItem(categoryItem);
				item.SetIcon(0, _spriteIconCache?.GetSpriteIcon(type.SpriteName) ?? PlaceholderIcon.Instance);
				item.SetIconMaxWidth(0, TreeIconMaxSize);
				item.SetText(1, $"{type.DoomEdNum}: {type.Title}");
				_typeByTreeItem[item] = type;
			}
		}

		SyncTreeAndPreviewFromTypeId(previousTypeId);
	}

	private void OnTreeItemSelected()
	{
		if (_suppressEvents) return;

		var selected = _tree.GetSelected();
		if (selected == null || !_typeByTreeItem.TryGetValue(selected, out var type)) return;

		_suppressEvents = true;
		_typeIdEdit.Text = type.DoomEdNum.ToString();
		_suppressEvents = false;

		UpdatePreview(type);
		TypeIdTextChanged?.Invoke(_typeIdEdit.Text);
	}

	private void OnTypeIdTextChanged(string text)
	{
		if (_suppressEvents) return;

		SyncTreeAndPreviewFromTypeId();
		TypeIdTextChanged?.Invoke(text);
	}

	private void SyncTreeAndPreviewFromTypeId() => SyncTreeAndPreviewFromTypeId(_typeIdEdit.Text);

	private void SyncTreeAndPreviewFromTypeId(string typeIdText)
	{
		if (!int.TryParse(typeIdText.Trim(), out var doomEdNum))
		{
			UpdatePreview(null);
			_tree.DeselectAll();
			return;
		}

		var type = _allTypes.FirstOrDefault(t => t.DoomEdNum == doomEdNum);
		UpdatePreview(type);

		foreach (var (item, itemType) in _typeByTreeItem)
		{
			if (itemType.DoomEdNum != doomEdNum) continue;

			_suppressEvents = true;
			item.GetParent().Collapsed = false;
			item.Select(0);
			_tree.ScrollToItem(item);
			_suppressEvents = false;
			return;
		}

		_tree.DeselectAll();
	}

	private void UpdatePreview(ThingTypeInfo type)
	{
		if (type == null)
		{
			_previewIcon.Texture = PlaceholderIcon.Instance;
			_previewNameLabel.Text = "";
			return;
		}

		_previewIcon.Texture = _spriteIconCache?.GetOrDecodeSpriteIcon(type.SpriteName) ?? PlaceholderIcon.Instance;
		_previewNameLabel.Text = type.Title;
	}
}
