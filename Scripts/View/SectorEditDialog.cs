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
/// 6 tabs (Properties/Surfaces/Colors/Slopes-Portals/Comment/Custom,
/// <c>SectorEditFormUDMF</c>) - only Properties and Surfaces are live, the
/// rest are placeholders so the dialog's overall shape is recognizable even
/// before they're built. Floor/Ceiling Texture live on their own Surfaces
/// tab, not folded into Properties - an earlier version of this dialog did
/// fold them into Properties' Heights section, modeled after the older
/// classic-format <c>SectorEditForm</c>'s single combined "Floor and
/// ceiling" group box, but that's not how the real UDMF dialog
/// (<c>SectorEditFormUDMF</c>) actually lays it out - corrected once this
/// was checked against the real thing directly, not the classic-format
/// screenshot this had been mistakenly modeled on. Heights (Floor/Ceiling
/// Height, Height Offset, read-only Sector Height), Effects, and
/// Identification sections still come from the real UDMF dialog's
/// <c>groupfloorceiling</c>/<c>groupeffect</c>/<c>groupaction</c> group
/// boxes (verified against <c>SectorEditFormUDMF.Designer.cs</c>'s actual
/// control positions, not guessed). Flags is rebuilt per game configuration
/// from
/// <see cref="IGameConfiguration.GetSectorFlags"/> (see
/// <see cref="RebuildFlagsCheckboxes"/>) rather than authored statically in
/// the scene, since the flag set itself differs by configuration. Damage
/// Type and Sound Sequence are plain free-text fields rather than UDB's
/// real combo boxes - both of UDB's real backing lists come from parsing
/// the map's own DECORATE actors/SNDSEQ lumps respectively, which this
/// project has no parser for yet; the additional-tags field
/// (<c>moreids</c>) is likewise a plain space-separated text field rather
/// than UDB's real add/remove-chip list - all three deliberate, flagged v1
/// simplifications, not fidelity gaps in what's actually editable. Every
/// section header is a real <see cref="GroupBox"/> (a titled, bordered
/// section box), not a bare <see cref="Label"/>. Floor/Ceiling Texture
/// each pair a plain <see cref="LineEdit"/> with an inline clickable
/// thumbnail (<see cref="TextureButton"/>) that opens
/// <see cref="TextureBrowserDialog"/> - matching UDB's real
/// <c>ImageSelectorControl</c> (typing a name and clicking the preview
/// both work, and the preview updates live either way). The Surfaces tab
/// mirrors UDB's real per-surface layout (two group boxes, "Ceiling" then
/// "Floor" - verified against <c>SectorEditFormUDMF.Designer.cs</c>, not
/// assumed to be Properties-adjacent) with the real UDMF texture offset
/// (<c>x/ypanningfloor</c>/<c>ceiling</c>), scale (<c>x/yscalefloor</c>/
/// <c>ceiling</c>), rotation (<c>rotationfloor</c>/<c>ceiling</c>), and
/// per-surface light override (<c>lightfloor</c>/<c>ceiling</c> +
/// <c>lightfloorabsolute</c>/<c>lightceilingabsolute</c>) fields added
/// alongside the texture name - all OK-only, since this project's mesh
/// builders don't apply any of them yet (no visual effect to preview,
/// unlike Floor/Ceiling Height/Texture/Brightness). UDB's real rotation
/// dial widget, "use linedef angles" checkbox, render-style dropdown,
/// terrain dropdown, and reflectivity field are deliberately not built -
/// the dial/checkbox because a plain typed rotation field covers the same
/// data with no exotic custom widget, the rest because they need real
/// infrastructure (a render-style enum, terrain game-config schema) this
/// project doesn't have yet. See <c>TODO.md</c> for what else is
/// deliberately deferred.
///
/// Height/texture/brightness/height-offset fields apply live to the
/// selected sectors as you type (matching UDB's own real-time-apply-while-
/// open feel) and revert completely on Cancel, since nothing is pushed to
/// <see cref="UndoStack"/> until <see cref="Confirmed"/> fires - exactly
/// one combined undo step for the whole dialog session, same as UDB's own
/// single <c>CreateUndo</c> block. Everything else (Special/Tag/Gravity,
/// and now Flags/Sector damage/Sound Sequence/Fog Density/additional tags)
/// is deliberately *not* applied live (matching UDB's own real split too,
/// and it's the same split: gameplay-only fields with nothing to preview
/// in the 2D/3D view) - see <see cref="OnConfirmed"/>'s remarks for why
/// that split is also required by <see cref="SetFieldCommand"/>'s
/// construction-time snapshot, not just a fidelity choice.
///
/// Multi-select "mixed value" handling matches UDB's real
/// <c>NumericTextbox</c> grammar (see <see cref="NumericFieldExpression"/>):
/// a field shows blank when the selected sectors disagree, and resolving
/// that blank text always means "this sector's own original value," never
/// "zero."
/// </summary>
public partial class SectorEditDialog : AcceptDialog
{
	private sealed record Snapshot(
		double FloorHeight, double CeilingHeight, string FloorTexture, string CeilingTexture, int Brightness,
		long Special, double Gravity,
		string DamageType, long DamageAmount, long DamageInterval, long Leakiness,
		string SoundSequence, long FogDensity,
		double FloorOffsetX, double FloorOffsetY, double CeilingOffsetX, double CeilingOffsetY,
		double FloorScaleX, double FloorScaleY, double CeilingScaleX, double CeilingScaleY,
		double FloorRotation, double CeilingRotation,
		long FloorLight, long CeilingLight, bool FloorLightAbsolute, bool CeilingLightAbsolute,
		IReadOnlyDictionary<string, bool> Flags);

	private TabContainer _tabs;
	private Container _flagsContainer;
	private CheckBox _flagCheckBoxTemplate;
	private StepperLineEdit _floorHeightEdit;
	private StepperLineEdit _ceilingHeightEdit;
	private StepperLineEdit _heightOffsetEdit;
	private Label _sectorHeightValue;
	private LineEdit _floorTextureEdit;
	private LineEdit _ceilingTextureEdit;
	private TextureButton _floorTexturePreview;
	private TextureButton _ceilingTexturePreview;
	private StepperLineEdit _floorOffsetXEdit;
	private StepperLineEdit _floorOffsetYEdit;
	private StepperLineEdit _ceilingOffsetXEdit;
	private StepperLineEdit _ceilingOffsetYEdit;
	private StepperLineEdit _floorScaleXEdit;
	private StepperLineEdit _floorScaleYEdit;
	private StepperLineEdit _ceilingScaleXEdit;
	private StepperLineEdit _ceilingScaleYEdit;
	private StepperLineEdit _floorRotationEdit;
	private StepperLineEdit _ceilingRotationEdit;
	private StepperLineEdit _floorLightEdit;
	private StepperLineEdit _ceilingLightEdit;
	private CheckBox _floorLightAbsoluteCheck;
	private CheckBox _ceilingLightAbsoluteCheck;
	private LineEdit _damageTypeEdit;
	private StepperLineEdit _damageAmountEdit;
	private StepperLineEdit _damageIntervalEdit;
	private StepperLineEdit _leakinessEdit;
	private StepperLineEdit _brightnessEdit;
	private StepperLineEdit _gravityEdit;
	private LineEdit _soundSequenceEdit;
	private StepperLineEdit _fogDensityEdit;
	private LineEdit _specialEdit;
	private Label _specialNameLabel;
	private Button _specialBrowseButton;
	private SectorTagsEditor _tagsEditor;

	private TextureBrowserDialog _textureBrowserDialog;
	private SectorSpecialBrowserDialog _sectorSpecialBrowserDialog;

	private readonly Dictionary<string, CheckBox> _flagCheckBoxes = new();
	private readonly HashSet<string> _touchedFlags = new();
	private bool _floorLightAbsoluteTouched;
	private bool _ceilingLightAbsoluteTouched;

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
		_tabs.SetTabTitle(3, "Slopes / Portals");

		_flagsContainer = GetNode<Container>("Container/Tabs/Properties/VboxContainer/FlagsBox/Content/FlagsContainer");
		_flagCheckBoxTemplate = GetNode<CheckBox>("Container/Tabs/Properties/VboxContainer/FlagCheckBoxTemplate");
		_floorHeightEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/HBoxContainer/HeightsBox/Content/FloorHeightRow/FloorHeightEdit");
		_ceilingHeightEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/HBoxContainer/HeightsBox/Content/CeilingHeightRow/CeilingHeightEdit");
		_heightOffsetEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/HBoxContainer/HeightsBox/Content/HeightOffsetRow/HeightOffsetEdit");
		_sectorHeightValue = GetNode<Label>("Container/Tabs/Properties/VboxContainer/HBoxContainer/HeightsBox/Content/SectorHeightRow/SectorHeightValue");
		_floorTextureEdit = GetNode<LineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/FloorBox/Content/TextureRow/FloorTextureEdit");
		_ceilingTextureEdit = GetNode<LineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/CeilingBox/Content/TextureRow/CeilingTextureEdit");
		_floorTexturePreview = GetNode<TextureButton>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/FloorBox/Content/TextureRow/FloorTexturePreview");
		_ceilingTexturePreview = GetNode<TextureButton>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/CeilingBox/Content/TextureRow/CeilingTexturePreview");
		_floorTexturePreview.Resized += () => KeepSquare(_floorTexturePreview);
		_ceilingTexturePreview.Resized += () => KeepSquare(_ceilingTexturePreview);
		WireHoverHighlight(_floorTexturePreview);
		WireHoverHighlight(_ceilingTexturePreview);
		_floorOffsetXEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/FloorBox/Content/OffsetRow/FloorOffsetXEdit");
		_floorOffsetYEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/FloorBox/Content/OffsetRow/FloorOffsetYEdit");
		_ceilingOffsetXEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/CeilingBox/Content/OffsetRow/CeilingOffsetXEdit");
		_ceilingOffsetYEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/CeilingBox/Content/OffsetRow/CeilingOffsetYEdit");
		_floorScaleXEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/FloorBox/Content/ScaleRow/FloorScaleXEdit");
		_floorScaleYEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/FloorBox/Content/ScaleRow/FloorScaleYEdit");
		_ceilingScaleXEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/CeilingBox/Content/ScaleRow/CeilingScaleXEdit");
		_ceilingScaleYEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/CeilingBox/Content/ScaleRow/CeilingScaleYEdit");
		_floorRotationEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/FloorBox/Content/RotationRow/FloorRotationEdit");
		_ceilingRotationEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/CeilingBox/Content/RotationRow/CeilingRotationEdit");
		_floorLightEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/FloorBox/Content/LightRow/FloorLightEdit");
		_ceilingLightEdit = GetNode<StepperLineEdit>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/CeilingBox/Content/LightRow/CeilingLightEdit");
		_floorLightAbsoluteCheck = GetNode<CheckBox>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/FloorBox/Content/LightRow/FloorLightAbsoluteCheck");
		_ceilingLightAbsoluteCheck = GetNode<CheckBox>("Container/Tabs/Surfaces/VBoxContainer/SurfacesRow/CeilingBox/Content/LightRow/CeilingLightAbsoluteCheck");
		_damageTypeEdit = GetNode<LineEdit>("Container/Tabs/Properties/VboxContainer/HBoxContainer/DamageBox/Content/DamageTypeRow/DamageTypeEdit");
		_damageAmountEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/HBoxContainer/DamageBox/Content/DamageAmountRow/DamageAmountEdit");
		_damageIntervalEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/HBoxContainer/DamageBox/Content/DamageIntervalRow/DamageIntervalEdit");
		_leakinessEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/HBoxContainer/DamageBox/Content/LeakinessRow/LeakinessEdit");
		_specialEdit = GetNode<LineEdit>("Container/Tabs/Properties/VboxContainer/EffectsBox/Content/SpecialFieldRow/SpecialRow/SpecialEdit");
		_specialNameLabel = GetNode<Label>("Container/Tabs/Properties/VboxContainer/EffectsBox/Content/SpecialFieldRow/SpecialRow/SpecialNameLabel");
		_specialBrowseButton = GetNode<Button>("Container/Tabs/Properties/VboxContainer/EffectsBox/Content/SpecialFieldRow/SpecialRow/SpecialBrowseButton");
		_brightnessEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/EffectsBox/Content/BrightnessRow/BrightnessEdit");
		_gravityEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/EffectsBox/Content/GravityRow/GravityEdit");
		_soundSequenceEdit = GetNode<LineEdit>("Container/Tabs/Properties/VboxContainer/EffectsBox/Content/SoundSequenceRow/SoundSequenceEdit");
		_fogDensityEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/EffectsBox/Content/FogDensityRow/FogDensityEdit");
		_tagsEditor = GetNode<SectorTagsEditor>("Container/Tabs/Properties/VboxContainer/IdentificationBox/Content/SectorTagsEditor");

		_floorHeightEdit.TextChanged += _ => RecomputeHeights();
		_ceilingHeightEdit.TextChanged += _ => RecomputeHeights();
		_heightOffsetEdit.TextChanged += _ => RecomputeHeights();
		_brightnessEdit.TextChanged += text => ApplyRealTimeNumber(text, s => s.Brightness, (s, v) => s.Brightness = (int)Math.Round(v));
		_floorTextureEdit.TextChanged += text =>
		{
			ApplyRealTimeTexture(text, s => s.FloorTexture, (s, v) => s.FloorTexture = v);
			UpdateTexturePreview(_floorTexturePreview, text);
		};
		_ceilingTextureEdit.TextChanged += text =>
		{
			ApplyRealTimeTexture(text, s => s.CeilingTexture, (s, v) => s.CeilingTexture = v);
			UpdateTexturePreview(_ceilingTexturePreview, text);
		};
		_floorTexturePreview.Pressed += () => BrowseTexture(_floorTextureEdit, _floorTexturePreview, s => s.FloorTexture, (s, v) => s.FloorTexture = v);
		_ceilingTexturePreview.Pressed += () => BrowseTexture(_ceilingTextureEdit, _ceilingTexturePreview, s => s.CeilingTexture, (s, v) => s.CeilingTexture = v);
		_specialEdit.TextChanged += _ => UpdateSpecialNameLabel();
		_specialBrowseButton.Pressed += BrowseSpecial;
		_floorLightAbsoluteCheck.Toggled += _ => _floorLightAbsoluteTouched = true;
		_ceilingLightAbsoluteCheck.Toggled += _ => _ceilingLightAbsoluteTouched = true;

		Confirmed += OnConfirmed;
		Canceled += OnCanceled;

		CallDeferred(nameof(EnsureTabBarFitsWithoutScrolling));
	}

	/// <summary>
	/// Re-checks both texture fields' currently displayed names every frame
	/// and swaps in the real icon once the ambient <see cref="TextureIconCache"/>
	/// finishes decoding it - this dialog never triggers decoding itself,
	/// same as <see cref="TextureBrowserDialog"/>'s own per-frame re-poll.
	/// </summary>
	public override void _Process(double delta)
	{
		if (!Visible) return;

		UpdateTexturePreview(_floorTexturePreview, _floorTextureEdit.Text);
		UpdateTexturePreview(_ceilingTexturePreview, _ceilingTextureEdit.Text);
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

		var flagKeys = gameConfiguration.GetSectorFlags();
		_snapshots = sectors.ToDictionary(s => s, s => new Snapshot(
			s.FloorHeight, s.CeilingHeight, s.FloorTexture, s.CeilingTexture, s.Brightness,
			s.Fields.GetInteger("special", 0), s.Fields.GetFloat("gravity", 1.0),
			s.Fields.GetString("damagetype", ""), s.Fields.GetInteger("damageamount", 0), s.Fields.GetInteger("damageinterval", 32), s.Fields.GetInteger("leakiness", 0),
			s.Fields.GetString("soundsequence", ""), s.Fields.GetInteger("fogdensity", 0),
			s.Fields.GetFloat("xpanningfloor", 0.0), s.Fields.GetFloat("ypanningfloor", 0.0),
			s.Fields.GetFloat("xpanningceiling", 0.0), s.Fields.GetFloat("ypanningceiling", 0.0),
			s.Fields.GetFloat("xscalefloor", 1.0), s.Fields.GetFloat("yscalefloor", 1.0),
			s.Fields.GetFloat("xscaleceiling", 1.0), s.Fields.GetFloat("yscaleceiling", 1.0),
			s.Fields.GetFloat("rotationfloor", 0.0), s.Fields.GetFloat("rotationceiling", 0.0),
			s.Fields.GetInteger("lightfloor", 0), s.Fields.GetInteger("lightceiling", 0),
			s.Fields.GetBool("lightfloorabsolute", false), s.Fields.GetBool("lightceilingabsolute", false),
			flagKeys.ToDictionary(f => f.Key, f => s.Fields.GetBool(f.Key, false))));
		_tagsEditor.SetSectors(sectors, map);

		_suppressLiveApply = true;
		_floorHeightEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorHeight));
		_ceilingHeightEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingHeight));
		_heightOffsetEdit.Text = "0"; // always transient/UI-only - never reflects a stored value, see this class's own remarks
		_floorTextureEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorTexture));
		_ceilingTextureEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingTexture));
		_damageTypeEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.DamageType));
		_damageAmountEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.DamageAmount));
		_damageIntervalEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.DamageInterval));
		_leakinessEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Leakiness));
		_brightnessEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Brightness));
		_specialEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Special));
		_gravityEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.Gravity));
		_soundSequenceEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.SoundSequence));
		_fogDensityEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.FogDensity));
		_floorOffsetXEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorOffsetX));
		_floorOffsetYEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorOffsetY));
		_ceilingOffsetXEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingOffsetX));
		_ceilingOffsetYEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingOffsetY));
		_floorScaleXEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorScaleX));
		_floorScaleYEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorScaleY));
		_ceilingScaleXEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingScaleX));
		_ceilingScaleYEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingScaleY));
		_floorRotationEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorRotation));
		_ceilingRotationEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingRotation));
		_floorLightEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.FloorLight));
		_ceilingLightEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.CeilingLight));
		_suppressLiveApply = false;

		_floorLightAbsoluteCheck.ButtonPressed = _snapshots.Values.All(s => s.FloorLightAbsolute);
		_ceilingLightAbsoluteCheck.ButtonPressed = _snapshots.Values.All(s => s.CeilingLightAbsolute);
		_floorLightAbsoluteTouched = false;
		_ceilingLightAbsoluteTouched = false;

		RebuildFlagsCheckboxes(flagKeys);
		UpdateSpecialNameLabel();
		UpdateTexturePreview(_floorTexturePreview, _floorTextureEdit.Text);
		UpdateTexturePreview(_ceilingTexturePreview, _ceilingTextureEdit.Text);
		UpdateSectorHeight();
	}

	/// <summary>
	/// The tallest a single column of flag checkboxes is allowed to get
	/// before <see cref="_flagsContainer"/> (a <c>VFlowContainer</c>) starts
	/// a second column - chosen generously enough that every flag set this
	/// project currently ships (11, for GZDoom's Doom2 UDMF configuration)
	/// fits in one column with room to spare, matching UDB's own real
	/// dialog. It only matters at all for a hypothetical future
	/// configuration with meaningfully more flags than that - this is the
	/// one knob that decides when such a set would start spilling into a
	/// second column instead of growing the dialog taller indefinitely.
	/// </summary>
	private const float MaxFlagsColumnHeight = 320f;

	/// <summary>
	/// Rebuilt from scratch every time the dialog opens, since the flag set
	/// itself comes from the current <see cref="IGameConfiguration"/> and
	/// can differ between game configurations - unlike every other field in
	/// this dialog, it can't be authored once in the scene as a fixed set of
	/// nodes. Each checkbox is a <see cref="Node.Duplicate"/> of
	/// <see cref="_flagCheckBoxTemplate"/> (a real scene node, kept hidden
	/// with <c>visible = false</c>, sitting right next to
	/// <see cref="_flagsContainer"/>) rather than a bare <c>new CheckBox()</c> -
	/// that template is the actual node to select in the Godot editor to
	/// restyle every flag checkbox at once (font, theme, spacing), even
	/// though none of its clones exist until this method runs. A checkbox's
	/// displayed state when the selection disagrees is purely cosmetic
	/// (shows checked only if every selected sector already has it set) -
	/// it's never read back unless the user actually toggles that specific
	/// checkbox (see <see cref="_touchedFlags"/> and <see cref="OnConfirmed"/>),
	/// matching UDB's real tri-state semantics without needing an
	/// indeterminate checkbox state.
	///
	/// <see cref="_flagsContainer"/>'s <see cref="Control.CustomMinimumSize"/>
	/// is set here (not once in the scene) to exactly fit however many
	/// flags this game configuration actually has, capped at
	/// <see cref="MaxFlagsColumnHeight"/> - a plain <c>VFlowContainer</c>
	/// with no height constraint would just report one tall column as its
	/// own minimum size and never wrap, so this cap is what actually makes
	/// "spill into a second column" possible at all once a flag set
	/// outgrows it.
	/// </summary>
	private void RebuildFlagsCheckboxes(IReadOnlyList<SectorFlagInfo> flags)
	{
		foreach (var child in _flagsContainer.GetChildren()) child.QueueFree();
		_flagCheckBoxes.Clear();
		_touchedFlags.Clear();

		foreach (var flag in flags)
		{
			var checkBox = (CheckBox)_flagCheckBoxTemplate.Duplicate();
			checkBox.Visible = true;
			checkBox.Text = flag.Title;
			checkBox.ButtonPressed = _snapshots.Values.All(s => s.Flags[flag.Key]);
			checkBox.Toggled += _ => _touchedFlags.Add(flag.Key);
			_flagsContainer.AddChild(checkBox);
			_flagCheckBoxes[flag.Key] = checkBox;
		}

		var rowHeight = _flagCheckBoxTemplate.GetMinimumSize().Y;
		_flagsContainer.CustomMinimumSize = new Vector2(0, Mathf.Min(rowHeight * flags.Count, MaxFlagsColumnHeight));
	}

	/// <summary>
	/// UDB's real read-only "Sector Height" field (<c>sectorheightlabel</c>/
	/// <c>sectorheight</c> in <c>groupfloorceiling</c>) - ceiling minus
	/// floor, recomputed from each selected sector's own current live
	/// values (already kept in sync by <see cref="ApplyRealTimeNumber"/>
	/// as you type), shown shared-or-blank exactly like every other field
	/// here. Never itself editable or applied anywhere - purely derived.
	/// </summary>
	private void UpdateSectorHeight() =>
		_sectorHeightValue.Text = SharedOrBlank(_sectors.Select(s => s.CeilingHeight - s.FloorHeight));

	/// <summary>
	/// Floor Height, Ceiling Height, and Height Offset are resolved
	/// together rather than by three independent handlers, since Height
	/// Offset only ever nudges the other two - it's never itself a stored
	/// sector value (confirmed against UDB's real dialog: the field always
	/// shows "0" and is never written anywhere). Both height fields resolve
	/// against each sector's own original snapshot value (never the
	/// sector's current live value), exactly as before, so re-editing any
	/// of the three fields stays idempotent instead of compounding. The
	/// offset itself is resolved by <see cref="ResolveHeightOffset"/>
	/// against that sector's own resolved (not original) height, matching
	/// UDB's real tooltip-documented <c>++</c>/<c>--</c> behavior.
	/// </summary>
	private void RecomputeHeights()
	{
		if (_suppressLiveApply) return;

		foreach (var sector in _sectors)
		{
			var snapshot = _snapshots[sector];
			var newFloor = NumericFieldExpression.Resolve(_floorHeightEdit.Text, snapshot.FloorHeight) ?? snapshot.FloorHeight;
			var newCeiling = NumericFieldExpression.Resolve(_ceilingHeightEdit.Text, snapshot.CeilingHeight) ?? snapshot.CeilingHeight;
			var offset = ResolveHeightOffset(_heightOffsetEdit.Text, newCeiling - newFloor);

			sector.FloorHeight = newFloor + offset;
			sector.CeilingHeight = newCeiling + offset;
			_map.MarkDirty(sector);
		}

		UpdateSectorHeight();
		_onLiveChange?.Invoke();
	}

	/// <summary>
	/// Blank means no offset. A bare <c>++</c>/<c>--</c> (no trailing
	/// number) means "by this sector's own current height" - a literal
	/// case <see cref="NumericFieldExpression"/> doesn't cover on its own,
	/// since there's no "original value" for a field that's always "0" -
	/// so it's special-cased here instead. Anything else falls back to
	/// <see cref="NumericFieldExpression"/> resolved against zero (a plain
	/// number is an absolute delta; <c>++N</c>/<c>--N</c>/<c>*N</c>/<c>/N</c>
	/// against zero degrade to sensible results - doubling zero is still
	/// zero, adding N to zero is just N).
	/// </summary>
	private static double ResolveHeightOffset(string text, double sectorOwnHeight)
	{
		var trimmed = text.Trim();
		if (trimmed == "++") return sectorOwnHeight;
		if (trimmed == "--") return -sectorOwnHeight;
		return NumericFieldExpression.Resolve(trimmed, 0) ?? 0;
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
	/// flats, never wall textures) - the click target is the thumbnail
	/// itself, matching UDB's real <c>ImageSelectorControl</c> (its inline
	/// preview image is what you click to browse, not a separate button).
	/// Setting <see cref="LineEdit.Text"/> directly doesn't raise
	/// <c>TextChanged</c> (a plain Godot behavior already relied on
	/// elsewhere, e.g. <see cref="StepperLineEdit.Text"/>'s own silent
	/// setter) - so the callback also calls <see cref="ApplyRealTimeTexture"/>
	/// and <see cref="UpdateTexturePreview"/> itself, exactly reproducing
	/// what typing the name by hand would have done.
	/// </summary>
	private void BrowseTexture(LineEdit edit, TextureButton preview, Func<Snapshot, string> original, Action<Sector, string> setter)
	{
		_textureBrowserDialog ??= CreateTextureBrowserDialog();
		_textureBrowserDialog.Browse(_textureSet, _namedResources, _textureIconCache, flats: true, edit.Text, name =>
		{
			edit.Text = name;
			ApplyRealTimeTexture(name, original, setter);
			UpdateTexturePreview(preview, name);
		});
	}

	private TextureBrowserDialog CreateTextureBrowserDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/TextureBrowserDialog.tscn").Instantiate<TextureBrowserDialog>();
		AddChild(dialog);
		return dialog;
	}

	/// <summary>
	/// Special is deliberately never live-applied to the sector (see this
	/// class's own doc comment and <see cref="OnConfirmed"/>'s remarks) -
	/// so picking one from the browser only needs to update the text field
	/// and its name label, exactly as if the number had been typed by
	/// hand. No sector mutation happens here at all.
	/// </summary>
	private void BrowseSpecial()
	{
		_sectorSpecialBrowserDialog ??= CreateSectorSpecialBrowserDialog();
		_sectorSpecialBrowserDialog.Browse(_gameConfiguration, _specialEdit.Text, number =>
		{
			_specialEdit.Text = number;
			UpdateSpecialNameLabel();
		});
	}

	private SectorSpecialBrowserDialog CreateSectorSpecialBrowserDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/SectorSpecialBrowserDialog.tscn").Instantiate<SectorSpecialBrowserDialog>();
		AddChild(dialog);
		return dialog;
	}

	/// <summary>
	/// Blank/mixed text shows the shared <see cref="PlaceholderIcon"/>.
	/// Otherwise uses <see cref="TextureIconCache.GetOrDecodeFlatIcon"/> -
	/// deliberately the eager variant, not just a read of whatever the
	/// ambient warm cache already has: this dialog only ever needs at most
	/// two images at once, cheap enough to decode on the spot, unlike the
	/// picker's full gallery. This also covers a name the ambient cache's
	/// own namespace-scanned seeding might never have enumerated at all
	/// (see that method's remarks) - exactly the case of an existing
	/// sector's own already-set texture never showing a thumbnail
	/// otherwise.
	/// </summary>
	private void UpdateTexturePreview(TextureButton preview, string text)
	{
		var trimmed = text.Trim();
		preview.TextureNormal = trimmed.Length == 0 ? PlaceholderIcon.Instance : _textureIconCache?.GetOrDecodeFlatIcon(trimmed) ?? PlaceholderIcon.Instance;
	}

	/// <summary>
	/// Godot's container layout has no built-in "stay square while filling
	/// available width" - <c>size_flags_horizontal = Fill|Expand</c> (set
	/// in the scene) makes the button claim its column's full width, but
	/// nothing makes its height follow along. Reacting to <see cref="Control.Resized"/>
	/// and pinning <see cref="Control.CustomMinimumSize"/>'s height to
	/// match the current width keeps it square at whatever size the
	/// surrounding layout actually gives it - stable rather than
	/// oscillating, since changing a preview's height here never changes
	/// its own or any sibling's width in this vertical stack.
	/// </summary>
	private static void KeepSquare(TextureButton preview)
	{
		var width = preview.Size.X;
		if (width > 0 && !Mathf.IsEqualApprox(preview.CustomMinimumSize.Y, width))
		{
			preview.CustomMinimumSize = new Vector2(preview.CustomMinimumSize.X, width);
		}
	}

	/// <summary>A pointing-hand cursor (set in the scene, <c>MouseDefaultCursorShape</c>) plus a slight brighten on hover - both together signal "clickable" without needing a custom hover texture, which a decoded texture preview doesn't have one of.</summary>
	private static void WireHoverHighlight(TextureButton preview)
	{
		preview.MouseEntered += () => preview.Modulate = new Color(1.3f, 1.3f, 1.3f);
		preview.MouseExited += () => preview.Modulate = Colors.White;
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

			var newGravity = NumericFieldExpression.Resolve(_gravityEdit.Text, snapshot.Gravity) ?? snapshot.Gravity;
			if (newGravity != snapshot.Gravity)
			{
				commands.Add(new SetFieldCommand(sector.Fields, "gravity", newGravity == 1.0 ? null : new UniValue(UniversalType.Float, newGravity)));
			}

			AddIfChangedStringField(commands, sector, "damagetype", snapshot.DamageType, _damageTypeEdit.Text);
			AddIfChangedIntegerField(commands, sector, "damageamount", snapshot.DamageAmount, _damageAmountEdit.Text, defaultValue: 0);
			AddIfChangedIntegerField(commands, sector, "damageinterval", snapshot.DamageInterval, _damageIntervalEdit.Text, defaultValue: 32);
			AddIfChangedIntegerField(commands, sector, "leakiness", snapshot.Leakiness, _leakinessEdit.Text, defaultValue: 0);
			AddIfChangedStringField(commands, sector, "soundsequence", snapshot.SoundSequence, _soundSequenceEdit.Text);
			AddIfChangedIntegerField(commands, sector, "fogdensity", snapshot.FogDensity, _fogDensityEdit.Text, defaultValue: 0);

			AddIfChangedFloatField(commands, sector, "xpanningfloor", snapshot.FloorOffsetX, _floorOffsetXEdit.Text, defaultValue: 0.0);
			AddIfChangedFloatField(commands, sector, "ypanningfloor", snapshot.FloorOffsetY, _floorOffsetYEdit.Text, defaultValue: 0.0);
			AddIfChangedFloatField(commands, sector, "xpanningceiling", snapshot.CeilingOffsetX, _ceilingOffsetXEdit.Text, defaultValue: 0.0);
			AddIfChangedFloatField(commands, sector, "ypanningceiling", snapshot.CeilingOffsetY, _ceilingOffsetYEdit.Text, defaultValue: 0.0);
			AddIfChangedFloatField(commands, sector, "xscalefloor", snapshot.FloorScaleX, _floorScaleXEdit.Text, defaultValue: 1.0);
			AddIfChangedFloatField(commands, sector, "yscalefloor", snapshot.FloorScaleY, _floorScaleYEdit.Text, defaultValue: 1.0);
			AddIfChangedFloatField(commands, sector, "xscaleceiling", snapshot.CeilingScaleX, _ceilingScaleXEdit.Text, defaultValue: 1.0);
			AddIfChangedFloatField(commands, sector, "yscaleceiling", snapshot.CeilingScaleY, _ceilingScaleYEdit.Text, defaultValue: 1.0);
			AddIfChangedFloatField(commands, sector, "rotationfloor", snapshot.FloorRotation, _floorRotationEdit.Text, defaultValue: 0.0);
			AddIfChangedFloatField(commands, sector, "rotationceiling", snapshot.CeilingRotation, _ceilingRotationEdit.Text, defaultValue: 0.0);
			AddIfChangedIntegerField(commands, sector, "lightfloor", snapshot.FloorLight, _floorLightEdit.Text, defaultValue: 0);
			AddIfChangedIntegerField(commands, sector, "lightceiling", snapshot.CeilingLight, _ceilingLightEdit.Text, defaultValue: 0);

			if (_floorLightAbsoluteTouched && _floorLightAbsoluteCheck.ButtonPressed != snapshot.FloorLightAbsolute)
			{
				commands.Add(new SetFieldCommand(sector.Fields, "lightfloorabsolute", _floorLightAbsoluteCheck.ButtonPressed ? new UniValue(UniversalType.Boolean, true) : null));
			}

			if (_ceilingLightAbsoluteTouched && _ceilingLightAbsoluteCheck.ButtonPressed != snapshot.CeilingLightAbsolute)
			{
				commands.Add(new SetFieldCommand(sector.Fields, "lightceilingabsolute", _ceilingLightAbsoluteCheck.ButtonPressed ? new UniValue(UniversalType.Boolean, true) : null));
			}

			foreach (var key in _touchedFlags)
			{
				var newFlagValue = _flagCheckBoxes[key].ButtonPressed;
				if (newFlagValue != snapshot.Flags[key])
				{
					commands.Add(new SetFieldCommand(sector.Fields, key, newFlagValue ? new UniValue(UniversalType.Boolean, true) : null));
				}
			}
		}

		commands.AddRange(_tagsEditor.BuildCommands());

		if (commands.Count > 0) _undoStack.Record(new CommandGroup(commands));
	}

	private void AddIfChanged<T>(List<ICommand> commands, Sector sector, T oldValue, T newValue, Action<Sector, T> setter)
	{
		if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) return;
		commands.Add(new SetPropertyCommand<Sector, T>(sector, setter, oldValue, newValue, s => _map.MarkDirty(s)));
	}

	/// <summary>Blank text means "keep this sector's own original" (the same multi-select convention every other field here follows), never "clear it to empty."</summary>
	private static void AddIfChangedStringField(List<ICommand> commands, Sector sector, string key, string originalValue, string fieldText)
	{
		var trimmed = fieldText.Trim();
		var newValue = trimmed.Length == 0 ? originalValue : trimmed;
		if (newValue == originalValue) return;

		commands.Add(new SetFieldCommand(sector.Fields, key, newValue.Length == 0 ? null : new UniValue(UniversalType.String, newValue)));
	}

	private static void AddIfChangedIntegerField(List<ICommand> commands, Sector sector, string key, long originalValue, string fieldText, long defaultValue)
	{
		var newValue = NumericFieldExpression.ResolveInteger(fieldText, originalValue) ?? originalValue;
		if (newValue == originalValue) return;

		commands.Add(new SetFieldCommand(sector.Fields, key, newValue == defaultValue ? null : new UniValue(UniversalType.Integer, newValue)));
	}

	private static void AddIfChangedFloatField(List<ICommand> commands, Sector sector, string key, double originalValue, string fieldText, double defaultValue)
	{
		var newValue = NumericFieldExpression.Resolve(fieldText, originalValue) ?? originalValue;
		if (newValue == originalValue) return;

		commands.Add(new SetFieldCommand(sector.Fields, key, newValue == defaultValue ? null : new UniValue(UniversalType.Float, newValue)));
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
