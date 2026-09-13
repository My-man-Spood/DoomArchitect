using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Editing;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Textures;
using DoomArchitect.Core.Undo;
using DoomArchitect.Rendering;
using Godot;

/// <summary>
/// Sector properties, ported from UDB's real sector dialogs - both field
/// scope and on-screen layout. The tab strip mirrors the real UDMF dialog's
/// 5 remaining tabs (Properties/Colors/Slopes-Portals/Comment/Custom,
/// <c>SectorEditFormUDMF</c>) - only Properties is live, the rest are
/// placeholders so the dialog's overall shape is recognizable even before
/// they're built. There's no separate Surfaces tab - with only floor/
/// ceiling texture names built so far (no offsets/scale/rotation/etc.
/// yet), a whole tab for two fields wasn't worth it; they're folded into
/// Properties' "Floor and Ceiling" section instead, which - not
/// coincidentally - is exactly how the older classic-format
/// <c>SectorEditForm</c> lays them out too (heights and texture previews
/// side by side in one "Floor and ceiling" group box). Effects and
/// Identification sections still come from the real UDMF dialog's
/// <c>groupeffect</c>/<c>groupaction</c> group boxes (verified against
/// <c>SectorEditFormUDMF.Designer.cs</c>'s actual control positions, not
/// guessed) - Flags and Sector Damage's group boxes are omitted entirely
/// rather than shown empty, since neither has any backing data yet (no
/// game-config schema for sector flags/damage types). A bold section-
/// header <see cref="Label"/> stands in for UDB's actual drawn GroupBox
/// border - a deliberate, flagged rendering simplification, not a fidelity
/// gap in what's editable. See <c>TODO.md</c> for what's deliberately
/// deferred - including the texture browser/preview this dialog's texture
/// fields still just take as plain typed names for now.
///
/// Height/texture/brightness fields apply live to the selected sectors as
/// you type (matching UDB's own real-time-apply-while-open feel) and
/// revert completely on Cancel, since nothing is pushed to
/// <see cref="UndoStack"/> until <see cref="Confirmed"/> fires - exactly
/// one combined undo step for the whole dialog session, same as UDB's own
/// single <c>CreateUndo</c> block. Special/tag are deliberately *not*
/// applied live (matching UDB's own real split too) - see
/// <see cref="OnConfirmed"/>'s remarks for why that split is also required
/// by <see cref="SetFieldCommand"/>'s construction-time snapshot, not just
/// a fidelity choice.
///
/// Multi-select "mixed value" handling matches UDB's real
/// <c>NumericTextbox</c> grammar (see <see cref="NumericFieldExpression"/>):
/// a field shows blank when the selected sectors disagree, and resolving
/// that blank text always means "this sector's own original value," never
/// "zero."
/// </summary>
public partial class SectorEditDialog : AcceptDialog
{
	private sealed record Snapshot(double FloorHeight, double CeilingHeight, string FloorTexture, string CeilingTexture, int Brightness, long Special, long Tag, double Gravity);

	private TabContainer _tabs;
	private StepperLineEdit _floorHeightEdit;
	private StepperLineEdit _ceilingHeightEdit;
	private LineEdit _floorTextureEdit;
	private LineEdit _ceilingTextureEdit;
	private Button _floorTextureBrowseButton;
	private Button _ceilingTextureBrowseButton;
	private StepperLineEdit _brightnessEdit;
	private StepperLineEdit _gravityEdit;
	private LineEdit _specialEdit;
	private Label _specialNameLabel;
	private LineEdit _tagEdit;

	private TextureBrowserDialog _textureBrowserDialog;

	private IReadOnlyList<Sector> _sectors = Array.Empty<Sector>();
	private Dictionary<Sector, Snapshot> _snapshots = new();
	private MapData _map;
	private IGameConfiguration _gameConfiguration;
	private TextureSet _textureSet;
	private IReadOnlyList<NamedResource> _namedResources = Array.Empty<NamedResource>();
	private TextureIconCache _textureIconCache;
	private UndoStack _undoStack;
	private Action _onLiveChange;
	private bool _suppressLiveApply;

	public override void _Ready()
	{
		_tabs = GetNode<TabContainer>("Container/Tabs");
		_tabs.SetTabTitle(2, "Slopes / Portals");

		_floorHeightEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/FloorCeilingRow/HeightsGrid/FloorHeightEdit");
		_ceilingHeightEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/FloorCeilingRow/HeightsGrid/CeilingHeightEdit");
		_floorTextureEdit = GetNode<LineEdit>("Container/Tabs/Properties/FloorCeilingRow/TexturesGrid/FloorTextureRow/FloorTextureEdit");
		_ceilingTextureEdit = GetNode<LineEdit>("Container/Tabs/Properties/FloorCeilingRow/TexturesGrid/CeilingTextureRow/CeilingTextureEdit");
		_floorTextureBrowseButton = GetNode<Button>("Container/Tabs/Properties/FloorCeilingRow/TexturesGrid/FloorTextureRow/FloorTextureBrowseButton");
		_ceilingTextureBrowseButton = GetNode<Button>("Container/Tabs/Properties/FloorCeilingRow/TexturesGrid/CeilingTextureRow/CeilingTextureBrowseButton");
		_specialEdit = GetNode<LineEdit>("Container/Tabs/Properties/EffectsGrid/SpecialRow/SpecialEdit");
		_specialNameLabel = GetNode<Label>("Container/Tabs/Properties/EffectsGrid/SpecialRow/SpecialNameLabel");
		_brightnessEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/EffectsGrid/BrightnessEdit");
		_gravityEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/EffectsGrid/GravityEdit");
		_tagEdit = GetNode<LineEdit>("Container/Tabs/Properties/IdentificationGrid/TagEdit");

		_floorHeightEdit.TextChanged += text => ApplyRealTimeNumber(text, s => s.FloorHeight, (s, v) => s.FloorHeight = v);
		_ceilingHeightEdit.TextChanged += text => ApplyRealTimeNumber(text, s => s.CeilingHeight, (s, v) => s.CeilingHeight = v);
		_brightnessEdit.TextChanged += text => ApplyRealTimeNumber(text, s => s.Brightness, (s, v) => s.Brightness = (int)Math.Round(v));
		_floorTextureEdit.TextChanged += text => ApplyRealTimeTexture(text, s => s.FloorTexture, (s, v) => s.FloorTexture = v);
		_ceilingTextureEdit.TextChanged += text => ApplyRealTimeTexture(text, s => s.CeilingTexture, (s, v) => s.CeilingTexture = v);
		_floorTextureBrowseButton.Pressed += () => BrowseTexture(_floorTextureEdit, s => s.FloorTexture, (s, v) => s.FloorTexture = v);
		_ceilingTextureBrowseButton.Pressed += () => BrowseTexture(_ceilingTextureEdit, s => s.CeilingTexture, (s, v) => s.CeilingTexture = v);
		_specialEdit.TextChanged += _ => UpdateSpecialNameLabel();

		Confirmed += OnConfirmed;
		Canceled += OnCanceled;

		CallDeferred(nameof(EnsureTabBarFitsWithoutScrolling));
	}

	/// <summary>
	/// <see cref="TabContainer"/>'s own minimum-size computation
	/// deliberately excludes its tab bar's width - tabs are allowed to
	/// scroll independently of whatever the current page needs, so
	/// <c>wrap_controls</c> alone can never guarantee all 6 real UDB tab
	/// headers are visible without scroll arrows. Measured here from the
	/// tab bar's actual live theme font/size rather than a guessed pixel
	/// number, so it stays correct across different themes, font sizes, and
	/// content scale settings instead of only happening to work on one
	/// machine. Deferred one frame so the tab bar's theme is fully resolved
	/// before measuring it.
	/// </summary>
	private void EnsureTabBarFitsWithoutScrolling()
	{
		const int perTabPadding = 28; // rough tab stylebox content margin, either side combined

		var tabBar = _tabs.GetTabBar();
		var font = tabBar.GetThemeFont("font");
		var fontSize = tabBar.GetThemeFontSize("font_size");

		var totalWidth = 0f;
		for (var i = 0; i < _tabs.GetTabCount(); i++)
		{
			totalWidth += font.GetStringSize(_tabs.GetTabTitle(i), HorizontalAlignment.Left, -1, fontSize).X + perTabPadding;
		}

		var required = (int)Math.Ceiling(totalWidth) + 16; // + Container's own left/right offsets
		if (required > MinSize.X) MinSize = new Vector2I(required, MinSize.Y);
	}

	public void SetSectors(
		IReadOnlyList<Sector> sectors, MapData map, IGameConfiguration gameConfiguration, UndoStack undoStack, Action onLiveChange,
		TextureSet textureSet, IReadOnlyList<NamedResource> namedResources, TextureIconCache textureIconCache)
	{
		_sectors = sectors;
		_map = map;
		_gameConfiguration = gameConfiguration;
		_undoStack = undoStack;
		_onLiveChange = onLiveChange;
		_textureSet = textureSet;
		_namedResources = namedResources;
		_textureIconCache = textureIconCache;

		_snapshots = sectors.ToDictionary(s => s, s => new Snapshot(
			s.FloorHeight, s.CeilingHeight, s.FloorTexture, s.CeilingTexture, s.Brightness,
			s.Fields.GetInteger("special", 0), s.Fields.GetInteger("id", 0), s.Fields.GetFloat("gravity", 1.0)));

		_suppressLiveApply = true;
		_floorHeightEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorHeight));
		_ceilingHeightEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingHeight));
		_floorTextureEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorTexture));
		_ceilingTextureEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingTexture));
		_brightnessEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Brightness));
		_specialEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Special));
		_tagEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Tag));
		_gravityEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.Gravity));
		_suppressLiveApply = false;

		UpdateSpecialNameLabel();
	}

	/// <summary>
	/// Resolves <paramref name="text"/> against each sector's own
	/// original value (never its current live value, so re-editing the
	/// same field twice stays idempotent) and writes it straight to the
	/// live sector, marking it dirty for <c>MapView</c>'s existing
	/// per-frame rebuild loop to pick up. Blank/unparsable text resolves
	/// to "this sector's own original," which correctly un-does an
	/// earlier keystroke's edit rather than merely skipping this one.
	/// </summary>
	private void ApplyRealTimeNumber(string text, Func<Snapshot, double> original, Action<Sector, double> setter)
	{
		if (_suppressLiveApply) return;

		foreach (var sector in _sectors)
		{
			var snapshotValue = original(_snapshots[sector]);
			setter(sector, NumericFieldExpression.Resolve(text, snapshotValue) ?? snapshotValue);
			_map.MarkDirty(sector);
		}

		_onLiveChange?.Invoke();
	}

	/// <summary>Texture fields have no relative-expression grammar - blank means "this sector's own original," non-blank is an absolute name applied to every selected sector.</summary>
	private void ApplyRealTimeTexture(string text, Func<Snapshot, string> original, Action<Sector, string> setter)
	{
		if (_suppressLiveApply) return;

		var trimmed = text.Trim();
		foreach (var sector in _sectors)
		{
			setter(sector, trimmed.Length == 0 ? original(_snapshots[sector]) : trimmed);
			_map.MarkDirty(sector);
		}

		_onLiveChange?.Invoke();
	}

	/// <summary>
	/// Opens the shared texture browser in Flats mode (both fields here are
	/// flats, never wall textures). Setting <see cref="LineEdit.Text"/>
	/// directly doesn't raise <c>TextChanged</c> (a plain Godot behavior
	/// already relied on elsewhere, e.g. <see cref="StepperLineEdit.Text"/>'s
	/// own silent setter) - so the callback also calls
	/// <see cref="ApplyRealTimeTexture"/> itself, exactly reproducing what
	/// typing the name by hand would have done.
	/// </summary>
	private void BrowseTexture(LineEdit edit, Func<Snapshot, string> original, Action<Sector, string> setter)
	{
		_textureBrowserDialog ??= CreateTextureBrowserDialog();
		_textureBrowserDialog.Browse(_textureSet, _namedResources, _textureIconCache, flats: true, edit.Text, name =>
		{
			edit.Text = name;
			ApplyRealTimeTexture(name, original, setter);
		});
	}

	private TextureBrowserDialog CreateTextureBrowserDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/TextureBrowserDialog.tscn").Instantiate<TextureBrowserDialog>();
		AddChild(dialog);
		return dialog;
	}

	private void UpdateSpecialNameLabel()
	{
		var text = _specialEdit.Text.Trim();
		_specialNameLabel.Text = long.TryParse(text, out var special)
			? _gameConfiguration?.GetSectorSpecial((int)special)?.Title ?? "Unknown"
			: "";
	}

	/// <summary>
	/// Nothing was ever pushed to <see cref="UndoStack"/>, so canceling
	/// only needs the real-time fields' live edits reverted directly -
	/// special/tag were never touched in the first place.
	/// </summary>
	private void OnCanceled()
	{
		foreach (var (sector, snapshot) in _snapshots)
		{
			sector.FloorHeight = snapshot.FloorHeight;
			sector.CeilingHeight = snapshot.CeilingHeight;
			sector.FloorTexture = snapshot.FloorTexture;
			sector.CeilingTexture = snapshot.CeilingTexture;
			sector.Brightness = snapshot.Brightness;
			_map.MarkDirty(sector);
		}

		_onLiveChange?.Invoke();
	}

	/// <summary>
	/// Special/tag/gravity are resolved here, for the first time, against
	/// each sector's original <c>Fields</c> value - deliberately never
	/// live-applied while the dialog was open, because
	/// <see cref="SetFieldCommand"/> captures its "old" value from
	/// <c>Fields</c> at construction time; if these had already been
	/// mutated live, building the command here would capture the
	/// already-new value as "old" and corrupt undo (gravity also has no
	/// visual effect worth live-previewing anyway). Height/texture/
	/// brightness were already live-applied, so their commands are built
	/// from a straight snapshot-vs-current-live-value comparison instead.
	/// </summary>
	private void OnConfirmed()
	{
		var commands = new List<ICommand>();

		foreach (var sector in _sectors)
		{
			var snapshot = _snapshots[sector];

			AddIfChanged(commands, sector, snapshot.FloorHeight, sector.FloorHeight, (s, v) => s.FloorHeight = v);
			AddIfChanged(commands, sector, snapshot.CeilingHeight, sector.CeilingHeight, (s, v) => s.CeilingHeight = v);
			AddIfChanged(commands, sector, snapshot.FloorTexture, sector.FloorTexture, (s, v) => s.FloorTexture = v);
			AddIfChanged(commands, sector, snapshot.CeilingTexture, sector.CeilingTexture, (s, v) => s.CeilingTexture = v);
			AddIfChanged(commands, sector, snapshot.Brightness, sector.Brightness, (s, v) => s.Brightness = v);

			var newSpecial = NumericFieldExpression.ResolveInteger(_specialEdit.Text, snapshot.Special) ?? snapshot.Special;
			if (newSpecial != snapshot.Special)
			{
				commands.Add(new SetFieldCommand(sector.Fields, "special", newSpecial == 0 ? null : new UniValue(UniversalType.Integer, newSpecial)));
			}

			var newTag = NumericFieldExpression.ResolveInteger(_tagEdit.Text, snapshot.Tag) ?? snapshot.Tag;
			if (newTag != snapshot.Tag)
			{
				commands.Add(new SetFieldCommand(sector.Fields, "id", newTag == 0 ? null : new UniValue(UniversalType.Integer, newTag)));
			}

			var newGravity = NumericFieldExpression.Resolve(_gravityEdit.Text, snapshot.Gravity) ?? snapshot.Gravity;
			if (newGravity != snapshot.Gravity)
			{
				commands.Add(new SetFieldCommand(sector.Fields, "gravity", newGravity == 1.0 ? null : new UniValue(UniversalType.Float, newGravity)));
			}
		}

		if (commands.Count > 0) _undoStack.Record(new CommandGroup(commands));
	}

	private void AddIfChanged<T>(List<ICommand> commands, Sector sector, T oldValue, T newValue, Action<Sector, T> setter)
	{
		if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) return;
		commands.Add(new SetPropertyCommand<Sector, T>(sector, setter, oldValue, newValue, s => _map.MarkDirty(s)));
	}

	private static string SharedOrBlank(IEnumerable<double> values)
	{
		var list = values.ToList();
		return list.All(v => v == list[0]) ? FormatNumber(list[0]) : "";
	}

	private static string SharedOrBlank(IEnumerable<string> values)
	{
		var list = values.ToList();
		return list.All(v => v == list[0]) ? list[0] : "";
	}

	private static string FormatNumber(double value) =>
		value == Math.Floor(value) ? ((long)value).ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
}
