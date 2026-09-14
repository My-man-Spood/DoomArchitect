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
/// Linedef properties, ported from UDB's real <c>LinedefEditFormUDMF</c> -
/// the largest of the three main property dialogs (see <c>TODO.md</c>),
/// almost entirely because of its dynamic argument-editing UI. The tab
/// strip mirrors the real dialog's 5 tabs (Properties/Front/Back/Comment/
/// Custom) - Properties and Front/Back are live, Comment/Custom are
/// <see cref="SectorEditDialog"/>'s own established "placeholder tab,
/// shape recognizable, not yet built" pattern.
///
/// **The dynamic argument system** (verified against
/// <c>LinedefEditFormUDMF.ArgumentsControl</c>, not guessed): a *fixed* 5
/// argument rows (never dynamically added/removed) - each row pairs a
/// <see cref="Label"/> with *both* a <see cref="StepperLineEdit"/> (plain
/// numeric) and an <see cref="OptionButton"/> (enum dropdown), toggling
/// which is <see cref="Control.Visible"/> based on whether the currently
/// typed action's <see cref="LinedefArgumentInfo.EnumOptions"/> is set -
/// this project's own established "always-present, toggle Visible" idiom
/// (e.g. <see cref="MapTagsEditor"/>'s Remove button) rather than
/// literally adding/removing controls. An unused slot (the action's own
/// <c>argN</c> block doesn't exist in the <c>.cfg</c>) shows a generic
/// disabled "Argument N" placeholder, matching UDB's real behavior of
/// never hiding a slot outright. Recomputed on <see cref="_actionEdit"/>'s
/// own <c>TextChanged</c>, mirroring how <see cref="SectorEditDialog"/>'s
/// <c>_specialEdit.TextChanged</c> already drives
/// <c>UpdateSpecialNameLabel</c>. A blank/unparsable/mixed-across-
/// selection action field falls back to all 5 slots showing the generic
/// placeholder (nothing wrong is ever written this way - see
/// <see cref="OnConfirmed"/>'s remarks on why this is safe).
///
/// Action/arguments/Flags/Activation/Tag are all deliberately OK-only (no
/// visual effect to preview), matching this project's established
/// "gameplay-only fields aren't live-applied" rule. Flags and Activation
/// are two separate real UDMF field groups (verified against UDB's own
/// dialog, which keeps them apart even though both are just named UDMF
/// booleans under the hood) - both rebuilt per game configuration the
/// exact same way <see cref="SectorEditDialog.RebuildFlagsCheckboxes"/>
/// already does for Sector's own Flags group, just parameterized over
/// which container/key set/touched-set to use so the same method serves
/// both groups here.
///
/// **Front/Back tabs** mirror UDB's real per-side layout: a "General"
/// group for the whole-sidedef fields (the already-modeled, already-
/// rendered <see cref="Sidedef.OffsetX"/>/<see cref="Sidedef.OffsetY"/> -
/// real-time, like Sector's own height/texture fields - plus the shared,
/// non-per-part <c>light</c>/<c>lightabsolute</c> pair, OK-only) and three
/// per-texture-part groups (Upper/Middle/Lower), each with a real-time
/// texture picker (reusing <see cref="TextureBrowserDialog"/> in wall
/// mode, exactly like <see cref="SectorEditDialog"/>'s own flat-mode
/// usage) plus OK-only per-part offset/scale/light-override fields -
/// <see cref="PartDefs"/> is the single source of truth for the real,
/// genuinely distinct UDMF field-name suffixes (<c>_top</c>/<c>_mid</c>/
/// <c>_bottom</c>) backing those three groups, verified directly against
/// UDB's own <c>UniversalStreamReader</c>/<c>Writer</c> and
/// <c>LinedefEditFormUDMF.cs</c> (all 18 per-part fields are genuinely
/// separate storage, not UI aliases of a shared field). A side with no
/// sidedef at all (a one-sided line's Back side) shows its whole tab's
/// fields disabled - matching UDB's own real <c>backgroup.Enabled =
/// (fl.Back != null)</c> - decided from the *first* selected linedef only
/// (also matching UDB's real <c>ShowLinedefProps</c>, which seeds every
/// field that way for a multi-selection); whatever is actually displayed
/// and written, though, is computed across every selected linedef that
/// does have that side (shared-or-blank, this project's own established
/// convention elsewhere) rather than UDB's own literal "first line only"
/// value-seeding - a deliberate, flagged divergence from UDB for
/// consistency with how every other multi-select field in this project's
/// dialogs already behaves. <see cref="OnConfirmed"/> still writes
/// per-linedef, individually skipping any linedef that turns out not to
/// have that side at all (matching UDB's own real per-linedef Apply
/// guard), so a mixed one-sided/two-sided selection can never corrupt a
/// one-sided line even though the tab itself was enabled from the first
/// line's shape.
/// </summary>
public partial class LinedefEditDialog : AcceptDialog
{
	private sealed record Snapshot(
		long ActionNumber, long[] Args,
		IReadOnlyDictionary<string, bool> Flags, IReadOnlyDictionary<string, bool> Activations,
		SideSnapshot Front, SideSnapshot Back);

	private sealed record ArgRow(Label Label, StepperLineEdit NumberEdit, OptionButton EnumEdit);

	private sealed record PartSnapshot(double OffsetX, double OffsetY, double ScaleX, double ScaleY, long Light, bool LightAbsolute);

	private sealed record SideSnapshot(int SectorIndex, int OffsetX, int OffsetY, long Light, bool LightAbsolute, string[] Textures, PartSnapshot[] Parts);

	private sealed record PartControls(
		TextureButton Preview, LineEdit TextureEdit, StepperLineEdit OffsetXEdit, StepperLineEdit OffsetYEdit,
		StepperLineEdit ScaleXEdit, StepperLineEdit ScaleYEdit, StepperLineEdit LightEdit, CheckBox LightAbsoluteCheck);

	private sealed record SideControls(
		Control TabRoot, Label SectorValueLabel, StepperLineEdit OffsetXEdit, StepperLineEdit OffsetYEdit,
		StepperLineEdit LightEdit, CheckBox LightAbsoluteCheck, PartControls[] Parts);

	/// <summary>
	/// The single source of truth for the 3 texture parts: the scene's own
	/// node-name suffix for each part's <see cref="GroupBox"/> ("Upper"/
	/// "Middle"/"Lower", matching UDB's own real group titles), the node-
	/// name prefix its child controls actually use ("Upper"/"Mid"/"Bottom" -
	/// kept exactly as authored in the scene, not renamed to match the box
	/// title), the real UDMF field-name suffix (<c>top</c>/<c>mid</c>/
	/// <c>bottom</c>), and the corresponding <see cref="Sidedef"/> texture
	/// accessor.
	/// </summary>
	private static readonly (string BoxSuffix, string FieldPrefix, string UdmfSuffix, Func<Sidedef, string> Get, Action<Sidedef, string> Set)[] PartDefs =
	{
		("Upper", "Upper", "top", s => s.UpperTexture, (s, v) => s.UpperTexture = v),
		("Middle", "Mid", "mid", s => s.MiddleTexture, (s, v) => s.MiddleTexture = v),
		("Lower", "Bottom", "bottom", s => s.LowerTexture, (s, v) => s.LowerTexture = v),
	};

	private TabContainer _tabs;
	private LineEdit _actionEdit;
	private Label _actionNameLabel;
	private Button _actionBrowseButton;
	private ArgRow[] _argRows;
	private Container _flagsContainer;
	private Container _activationsContainer;
	private CheckBox _flagCheckBoxTemplate;
	private MapTagsEditor _tagsEditor;
	private SideControls _front;
	private SideControls _back;

	private LinedefActionBrowserDialog _actionBrowserDialog;
	private TextureBrowserDialog _textureBrowserDialog;

	private readonly Dictionary<string, CheckBox> _flagCheckBoxes = new();
	private readonly Dictionary<string, CheckBox> _activationCheckBoxes = new();
	private readonly HashSet<string> _touchedFlags = new();
	private readonly HashSet<string> _touchedActivations = new();
	private readonly HashSet<int> _touchedArgs = new();
	private readonly HashSet<CheckBox> _touchedCheckBoxes = new();

	private IReadOnlyList<Linedef> _linedefs = Array.Empty<Linedef>();
	private Dictionary<Linedef, Snapshot> _snapshots = new();
	private MapData _map;
	private IGameConfiguration _gameConfiguration;
	private UndoStack _undoStack;
	private TextureSet _textureSet;
	private IReadOnlyList<NamedResource> _namedResources = Array.Empty<NamedResource>();
	private TextureIconCache _textureIconCache;
	private Action _onLiveChange;
	private bool _suppressLiveApply;

	private LinedefActionInfo _currentAction;
	private IReadOnlyList<LinedefArgumentInfo> _currentArgInfos = DefaultArgInfos();

	public override void _Ready()
	{
		_tabs = GetNode<TabContainer>("Container/Tabs");

		_actionEdit = GetNode<LineEdit>("Container/Tabs/Properties/VboxContainer/ActionBox/Content/ActionRow/ActionEdit");
		_actionNameLabel = GetNode<Label>("Container/Tabs/Properties/VboxContainer/ActionBox/Content/ActionRow/ActionNameLabel");
		_actionBrowseButton = GetNode<Button>("Container/Tabs/Properties/VboxContainer/ActionBox/Content/ActionRow/ActionBrowseButton");

		_argRows = new ArgRow[5];
		for (var i = 0; i < 5; i++)
		{
			var rowPath = $"Container/Tabs/Properties/VboxContainer/ActionBox/Content/Arg{i}Row";
			_argRows[i] = new ArgRow(
				GetNode<Label>($"{rowPath}/ArgLabel"),
				GetNode<StepperLineEdit>($"{rowPath}/ArgNumberEdit"),
				GetNode<OptionButton>($"{rowPath}/ArgEnumEdit"));
		}

		_flagsContainer = GetNode<Container>("Container/Tabs/Properties/VboxContainer/FlagsBox/Content/FlagsContainer");
		_activationsContainer = GetNode<Container>("Container/Tabs/Properties/VboxContainer/ActivationBox/Content/ActivationsContainer");
		_flagCheckBoxTemplate = GetNode<CheckBox>("Container/Tabs/Properties/VboxContainer/FlagCheckBoxTemplate");
		_tagsEditor = GetNode<MapTagsEditor>("Container/Tabs/Properties/VboxContainer/IdentificationBox/Content/MapTagsEditor");

		_front = LoadSideControls("Front");
		_back = LoadSideControls("Back");

		_actionEdit.TextChanged += _ => UpdateActionUi();
		_actionBrowseButton.Pressed += BrowseAction;

		for (var i = 0; i < 5; i++)
		{
			var slot = i;
			_argRows[slot].NumberEdit.TextChanged += _ => _touchedArgs.Add(slot);
			_argRows[slot].EnumEdit.ItemSelected += _ => _touchedArgs.Add(slot);
		}

		WireSide(_front, l => l.Front, s => s.Front);
		WireSide(_back, l => l.Back, s => s.Back);

		Confirmed += OnConfirmed;
		Canceled += OnCanceled;

		CallDeferred(nameof(EnsureTabBarFitsWithoutScrolling));
	}

	/// <summary>Re-polls every visible texture preview's icon each frame, exactly matching <see cref="SectorEditDialog._Process"/>'s own reasoning (this dialog never triggers decoding itself).</summary>
	public override void _Process(double delta)
	{
		if (!Visible) return;

		foreach (var controls in new[] { _front, _back })
		{
			foreach (var part in controls.Parts)
			{
				UpdateTexturePreview(part.Preview, part.TextureEdit.Text);
			}
		}
	}

	private static IReadOnlyList<LinedefArgumentInfo> DefaultArgInfos() =>
		Enumerable.Range(0, 5).Select(i => new LinedefArgumentInfo($"Argument {i + 1}", Used: false, EnumOptions: null)).ToList();

	private SideControls LoadSideControls(string side)
	{
		var general = $"Container/Tabs/{side}/VBoxContainer/GeneralBox/Content";
		var controls = new SideControls(
			GetNode<Control>($"Container/Tabs/{side}"),
			GetNode<Label>($"{general}/SectorRow/{side}SectorValueLabel"),
			GetNode<StepperLineEdit>($"{general}/OffsetRow/{side}OffsetXEdit"),
			GetNode<StepperLineEdit>($"{general}/OffsetRow/{side}OffsetYEdit"),
			GetNode<StepperLineEdit>($"{general}/LightRow/{side}LightEdit"),
			GetNode<CheckBox>($"{general}/LightRow/{side}LightAbsoluteCheck"),
			PartDefs.Select(def => LoadPartControls(side, def.BoxSuffix, def.FieldPrefix)).ToArray());

		foreach (var part in controls.Parts)
		{
			part.Preview.Resized += () => KeepSquare(part.Preview);
			WireHoverHighlight(part.Preview);
		}

		return controls;
	}

	private PartControls LoadPartControls(string side, string boxSuffix, string fieldPrefix)
	{
		var content = $"Container/Tabs/{side}/VBoxContainer/PartsRow/{side}{boxSuffix}Box/Content";
		var prefix = $"{side}{fieldPrefix}";
		return new PartControls(
			GetNode<TextureButton>($"{content}/TextureRow/{prefix}TexturePreview"),
			GetNode<LineEdit>($"{content}/TextureRow/{prefix}TextureEdit"),
			GetNode<StepperLineEdit>($"{content}/OffsetRow/{prefix}OffsetXEdit"),
			GetNode<StepperLineEdit>($"{content}/OffsetRow/{prefix}OffsetYEdit"),
			GetNode<StepperLineEdit>($"{content}/ScaleRow/{prefix}ScaleXEdit"),
			GetNode<StepperLineEdit>($"{content}/ScaleRow/{prefix}ScaleYEdit"),
			GetNode<StepperLineEdit>($"{content}/LightRow/{prefix}LightEdit"),
			GetNode<CheckBox>($"{content}/LightRow/{prefix}LightAbsoluteCheck"));
	}

	/// <summary>Wires one side's real-time (offset/texture) and touched-tracked (light-absolute checkboxes) handlers - shared by Front/Back via <paramref name="getSide"/>/<paramref name="getSnapshot"/>.</summary>
	private void WireSide(SideControls controls, Func<Linedef, Sidedef> getSide, Func<Snapshot, SideSnapshot> getSnapshot)
	{
		controls.OffsetXEdit.TextChanged += text => ApplyRealTimeSideOffset(getSide, getSnapshot, isX: true, text);
		controls.OffsetYEdit.TextChanged += text => ApplyRealTimeSideOffset(getSide, getSnapshot, isX: false, text);
		controls.LightAbsoluteCheck.Toggled += _ => _touchedCheckBoxes.Add(controls.LightAbsoluteCheck);

		for (var i = 0; i < controls.Parts.Length; i++)
		{
			var part = controls.Parts[i];
			var partIndex = i;

			part.TextureEdit.TextChanged += text =>
			{
				ApplyRealTimeSideTexture(getSide, getSnapshot, partIndex, text);
				UpdateTexturePreview(part.Preview, text);
			};
			part.Preview.Pressed += () => BrowseSideTexture(getSide, getSnapshot, part, partIndex);
			part.LightAbsoluteCheck.Toggled += _ => _touchedCheckBoxes.Add(part.LightAbsoluteCheck);
		}
	}

	/// <summary>Same reasoning as <see cref="SectorEditDialog.EnsureTabBarFitsWithoutScrolling"/> - measured from the tab bar's own live theme, not a guessed pixel width.</summary>
	private void EnsureTabBarFitsWithoutScrolling()
	{
		const int perTabPadding = 28;

		var tabBar = _tabs.GetTabBar();
		var font = tabBar.GetThemeFont("font");
		var fontSize = tabBar.GetThemeFontSize("font_size");

		var totalWidth = 0f;
		for (var i = 0; i < _tabs.GetTabCount(); i++)
		{
			totalWidth += font.GetStringSize(_tabs.GetTabTitle(i), HorizontalAlignment.Left, -1, fontSize).X + perTabPadding;
		}

		var required = (int)Math.Ceiling(totalWidth) + 16;
		if (required > MinSize.X) MinSize = new Vector2I(required, MinSize.Y);
	}

	public void SetLinedefs(
		IReadOnlyList<Linedef> linedefs, MapData map, IGameConfiguration gameConfiguration, UndoStack undoStack, Action onLiveChange,
		TextureSet textureSet, IReadOnlyList<NamedResource> namedResources, TextureIconCache textureIconCache)
	{
		_linedefs = linedefs;
		_map = map;
		_gameConfiguration = gameConfiguration;
		_undoStack = undoStack;
		_onLiveChange = onLiveChange;
		_textureSet = textureSet;
		_namedResources = namedResources;
		_textureIconCache = textureIconCache;

		var flagKeys = gameConfiguration.GetLinedefFlags();
		var activationKeys = gameConfiguration.GetLinedefActivations();

		_snapshots = linedefs.ToDictionary(l => l, l => new Snapshot(
			l.Fields.GetInteger("special", 0),
			Enumerable.Range(0, 5).Select(i => l.Fields.GetInteger($"arg{i}", 0)).ToArray(),
			flagKeys.ToDictionary(f => f.Key, f => l.Fields.GetBool(f.Key, false)),
			activationKeys.ToDictionary(a => a.Key, a => l.Fields.GetBool(a.Key, false)),
			BuildSideSnapshot(l.Front, map),
			BuildSideSnapshot(l.Back, map)));

		_tagsEditor.SetLinedefs(linedefs, map);

		_touchedFlags.Clear();
		_touchedActivations.Clear();
		_touchedArgs.Clear();
		_touchedCheckBoxes.Clear();

		_actionEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.ActionNumber));
		UpdateActionUi();

		RebuildCheckboxes(_flagsContainer, flagKeys, _flagCheckBoxes, _touchedFlags, s => s.Flags);
		RebuildCheckboxes(_activationsContainer, activationKeys, _activationCheckBoxes, _touchedActivations, s => s.Activations);

		SetupSideDisplay(_front, s => s.Front, l => l.Front);
		SetupSideDisplay(_back, s => s.Back, l => l.Back);
	}

	private static SideSnapshot BuildSideSnapshot(Sidedef side, MapData map)
	{
		if (side == null) return null;

		return new SideSnapshot(
			map.Sectors.ToList().IndexOf(side.Sector),
			side.OffsetX, side.OffsetY,
			side.Fields.GetInteger("light", 0), side.Fields.GetBool("lightabsolute", false),
			new[] { side.UpperTexture, side.MiddleTexture, side.LowerTexture },
			PartDefs.Select(def => new PartSnapshot(
				side.Fields.GetFloat($"offsetx_{def.UdmfSuffix}", 0.0),
				side.Fields.GetFloat($"offsety_{def.UdmfSuffix}", 0.0),
				side.Fields.GetFloat($"scalex_{def.UdmfSuffix}", 1.0),
				side.Fields.GetFloat($"scaley_{def.UdmfSuffix}", 1.0),
				side.Fields.GetInteger($"light_{def.UdmfSuffix}", 0),
				side.Fields.GetBool($"lightabsolute_{def.UdmfSuffix}", false))).ToArray());
	}

	/// <summary>
	/// Whether the whole tab is enabled is decided from the *first* selected
	/// linedef only (matching UDB's real <c>backgroup.Enabled = (fl.Back !=
	/// null)</c>); the values actually shown, though, are shared-or-blank
	/// across every selected linedef that has that side at all - see this
	/// class's own remarks for why that's a deliberate divergence from
	/// UDB's literal first-line-only value seeding.
	/// </summary>
	private void SetupSideDisplay(SideControls controls, Func<Snapshot, SideSnapshot> getSnapshot, Func<Linedef, Sidedef> getSide)
	{
		var enabled = _linedefs.Count > 0 && getSide(_linedefs[0]) != null;
		SetSideEnabled(controls, enabled);
		if (!enabled) return;

		var sideSnapshots = _linedefs.Select(l => getSnapshot(_snapshots[l])).Where(s => s != null).ToList();
		if (sideSnapshots.Count == 0) return;

		controls.SectorValueLabel.Text = SharedOrBlank(sideSnapshots.Select(s => (double)s.SectorIndex));

		_suppressLiveApply = true;
		controls.OffsetXEdit.Text = SharedOrBlank(sideSnapshots.Select(s => (double)s.OffsetX));
		controls.OffsetYEdit.Text = SharedOrBlank(sideSnapshots.Select(s => (double)s.OffsetY));
		_suppressLiveApply = false;

		controls.LightEdit.Text = SharedOrBlank(sideSnapshots.Select(s => (double)s.Light));
		controls.LightAbsoluteCheck.ButtonPressed = sideSnapshots.All(s => s.LightAbsolute);

		for (var i = 0; i < controls.Parts.Length; i++)
		{
			var part = controls.Parts[i];
			var partIndex = i;

			_suppressLiveApply = true;
			part.TextureEdit.Text = SharedOrBlank(sideSnapshots.Select(s => s.Textures[partIndex]));
			_suppressLiveApply = false;
			UpdateTexturePreview(part.Preview, part.TextureEdit.Text);

			part.OffsetXEdit.Text = SharedOrBlank(sideSnapshots.Select(s => s.Parts[partIndex].OffsetX));
			part.OffsetYEdit.Text = SharedOrBlank(sideSnapshots.Select(s => s.Parts[partIndex].OffsetY));
			part.ScaleXEdit.Text = SharedOrBlank(sideSnapshots.Select(s => s.Parts[partIndex].ScaleX));
			part.ScaleYEdit.Text = SharedOrBlank(sideSnapshots.Select(s => s.Parts[partIndex].ScaleY));
			part.LightEdit.Text = SharedOrBlank(sideSnapshots.Select(s => (double)s.Parts[partIndex].Light));
			part.LightAbsoluteCheck.ButtonPressed = sideSnapshots.All(s => s.Parts[partIndex].LightAbsolute);
		}
	}

	/// <summary>Disables (and dims) the whole tab's field set outright rather than merely hiding it - matching UDB's real <c>Enabled = false</c> treatment for a one-sided line's Back side.</summary>
	private static void SetSideEnabled(SideControls controls, bool enabled)
	{
		controls.TabRoot.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.5f);
		controls.OffsetXEdit.Editable = enabled;
		controls.OffsetYEdit.Editable = enabled;
		controls.LightEdit.Editable = enabled;
		controls.LightAbsoluteCheck.Disabled = !enabled;

		foreach (var part in controls.Parts)
		{
			part.TextureEdit.Editable = enabled;
			part.Preview.Disabled = !enabled;
			part.OffsetXEdit.Editable = enabled;
			part.OffsetYEdit.Editable = enabled;
			part.ScaleXEdit.Editable = enabled;
			part.ScaleYEdit.Editable = enabled;
			part.LightEdit.Editable = enabled;
			part.LightAbsoluteCheck.Disabled = !enabled;
		}
	}

	/// <summary>Resolves against each linedef's own original value (never its current live value), exactly matching <see cref="SectorEditDialog.ApplyRealTimeNumber"/>'s own reasoning - skips any linedef that doesn't actually have this side.</summary>
	private void ApplyRealTimeSideOffset(Func<Linedef, Sidedef> getSide, Func<Snapshot, SideSnapshot> getSnapshot, bool isX, string text)
	{
		if (_suppressLiveApply) return;

		foreach (var linedef in _linedefs)
		{
			var side = getSide(linedef);
			var snapshot = getSnapshot(_snapshots[linedef]);
			if (side == null || snapshot == null) continue;

			var original = isX ? snapshot.OffsetX : snapshot.OffsetY;
			var newValue = (int)Math.Round(NumericFieldExpression.Resolve(text, original) ?? original);
			if (isX) side.OffsetX = newValue; else side.OffsetY = newValue;
			_map.MarkDirty(side.Sector);
		}

		_onLiveChange?.Invoke();
	}

	/// <summary>Texture fields have no relative-expression grammar - blank means "this side's own original," matching <see cref="SectorEditDialog.ApplyRealTimeTexture"/>.</summary>
	private void ApplyRealTimeSideTexture(Func<Linedef, Sidedef> getSide, Func<Snapshot, SideSnapshot> getSnapshot, int partIndex, string text)
	{
		if (_suppressLiveApply) return;

		var trimmed = text.Trim();
		var setTexture = PartDefs[partIndex].Set;

		foreach (var linedef in _linedefs)
		{
			var side = getSide(linedef);
			var snapshot = getSnapshot(_snapshots[linedef]);
			if (side == null || snapshot == null) continue;

			setTexture(side, trimmed.Length == 0 ? snapshot.Textures[partIndex] : trimmed);
			_map.MarkDirty(side.Sector);
		}

		_onLiveChange?.Invoke();
	}

	private void BrowseSideTexture(Func<Linedef, Sidedef> getSide, Func<Snapshot, SideSnapshot> getSnapshot, PartControls part, int partIndex)
	{
		_textureBrowserDialog ??= CreateTextureBrowserDialog();
		_textureBrowserDialog.Browse(_textureSet, _namedResources, _textureIconCache, flats: false, part.TextureEdit.Text, name =>
		{
			part.TextureEdit.Text = name;
			ApplyRealTimeSideTexture(getSide, getSnapshot, partIndex, name);
			UpdateTexturePreview(part.Preview, name);
		});
	}

	private TextureBrowserDialog CreateTextureBrowserDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/TextureBrowserDialog.tscn").Instantiate<TextureBrowserDialog>();
		AddChild(dialog);
		return dialog;
	}

	/// <summary>Wall-mode counterpart to <see cref="SectorEditDialog.UpdateTexturePreview"/> - uses <see cref="TextureIconCache.GetOrDecodeWallIcon"/> since every sidedef texture part is a wall texture, never a flat.</summary>
	private void UpdateTexturePreview(TextureButton preview, string text)
	{
		var trimmed = text.Trim();
		preview.TextureNormal = trimmed.Length == 0 ? PlaceholderIcon.Instance : _textureIconCache?.GetOrDecodeWallIcon(trimmed) ?? PlaceholderIcon.Instance;
	}

	/// <summary>Identical reasoning to <see cref="SectorEditDialog.KeepSquare"/> - Godot has no built-in "stay square while filling available width."</summary>
	private static void KeepSquare(TextureButton preview)
	{
		var width = preview.Size.X;
		if (width > 0 && !Mathf.IsEqualApprox(preview.CustomMinimumSize.Y, width))
		{
			preview.CustomMinimumSize = new Vector2(preview.CustomMinimumSize.X, width);
		}
	}

	private static void WireHoverHighlight(TextureButton preview)
	{
		preview.MouseEntered += () => preview.Modulate = new Color(1.3f, 1.3f, 1.3f);
		preview.MouseExited += () => preview.Modulate = Colors.White;
	}

	/// <summary>
	/// Re-derives <see cref="_currentAction"/>/<see cref="_currentArgInfos"/>
	/// from whatever's currently typed in <see cref="_actionEdit"/> - a
	/// blank, unparsable, or unrecognized (including cross-selection
	/// "mixed") action number simply falls back to
	/// <see cref="DefaultArgInfos"/> (all 5 slots generic and disabled),
	/// same "blank means don't know/don't touch" convention as everywhere
	/// else in this dialog.
	/// </summary>
	private void UpdateActionUi()
	{
		var text = _actionEdit.Text.Trim();
		_currentAction = long.TryParse(text, out var number) ? _gameConfiguration?.GetLinedefAction((int)number) : null;
		_currentArgInfos = _currentAction?.Args ?? DefaultArgInfos();
		_actionNameLabel.Text = _currentAction?.Title ?? "";

		for (var i = 0; i < 5; i++)
		{
			var info = _currentArgInfos[i];
			var row = _argRows[i];
			var isEnum = info.EnumOptions != null;

			row.Label.Text = info.Title;
			row.Label.Modulate = info.Used ? Colors.White : new Color(1, 1, 1, 0.5f);
			row.NumberEdit.Visible = !isEnum;
			row.EnumEdit.Visible = isEnum;
			row.NumberEdit.Editable = info.Used;
			row.EnumEdit.Disabled = !info.Used;

			if (isEnum)
			{
				row.EnumEdit.Clear();
				foreach (var option in info.EnumOptions!)
				{
					row.EnumEdit.AddItem(option.Title);
					row.EnumEdit.SetItemMetadata(row.EnumEdit.ItemCount - 1, option.Value);
				}
			}
		}

		RefreshArgValueDisplays();
	}

	/// <summary>
	/// Shows each selected linedef's own current stored <c>argN</c> value,
	/// shared-or-blank across the selection for a plain numeric slot; an
	/// enum slot only gets a pre-selected item when every selected linedef
	/// already agrees on a value that's actually one of that enum's real
	/// options (otherwise it's left showing the dropdown's own first item
	/// purely cosmetically - never written unless the user actually
	/// touches it, see <see cref="OnConfirmed"/>).
	/// </summary>
	private void RefreshArgValueDisplays()
	{
		for (var i = 0; i < 5; i++)
		{
			var row = _argRows[i];
			var info = _currentArgInfos[i];
			var values = _snapshots.Values.Select(s => s.Args[i]).ToList();
			if (values.Count == 0) continue;

			if (info.EnumOptions != null)
			{
				var shared = values.All(v => v == values[0]) ? (long?)values[0] : null;
				if (shared == null) continue;

				var matchIndex = -1;
				for (var idx = 0; idx < row.EnumEdit.ItemCount; idx++)
				{
					if (row.EnumEdit.GetItemMetadata(idx).AsInt64() != shared.Value) continue;
					matchIndex = idx;
					break;
				}

				if (matchIndex >= 0) row.EnumEdit.Selected = matchIndex;
			}
			else
			{
				row.NumberEdit.Text = SharedOrBlank(values.Select(v => (double)v));
			}
		}
	}

	private void BrowseAction()
	{
		_actionBrowserDialog ??= CreateActionBrowserDialog();
		_actionBrowserDialog.Browse(_gameConfiguration, _actionEdit.Text, number => _actionEdit.Text = number);
	}

	private LinedefActionBrowserDialog CreateActionBrowserDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/LinedefActionBrowserDialog.tscn").Instantiate<LinedefActionBrowserDialog>();
		AddChild(dialog);
		return dialog;
	}

	/// <summary>
	/// Shared by the Flags and Activation groups - same duplicate-the-
	/// hidden-template mechanism as <see cref="SectorEditDialog.RebuildFlagsCheckboxes"/>,
	/// just parameterized over which container/key-set/touched-set/value-
	/// selector to use so one method serves both real, distinct UDMF field
	/// groups.
	/// </summary>
	private void RebuildCheckboxes(
		Container container, IReadOnlyList<SectorFlagInfo> keys, Dictionary<string, CheckBox> checkBoxes,
		HashSet<string> touched, Func<Snapshot, IReadOnlyDictionary<string, bool>> selector)
	{
		foreach (var child in container.GetChildren()) child.QueueFree();
		checkBoxes.Clear();
		touched.Clear();

		foreach (var key in keys)
		{
			var checkBox = (CheckBox)_flagCheckBoxTemplate.Duplicate();
			checkBox.Visible = true;
			checkBox.Text = key.Title;
			checkBox.ButtonPressed = _snapshots.Values.All(s => selector(s)[key.Key]);
			checkBox.Toggled += _ => touched.Add(key.Key);
			container.AddChild(checkBox);
			checkBoxes[key.Key] = checkBox;
		}
	}

	/// <summary>
	/// Nothing here was ever live-applied except the Front/Back tabs' own
	/// whole-sidedef offset and per-part texture fields (see this class's
	/// own remarks) - those revert directly, exactly matching
	/// <see cref="SectorEditDialog.OnCanceled"/>'s reasoning.
	/// </summary>
	private void OnCanceled()
	{
		foreach (var linedef in _linedefs)
		{
			var snapshot = _snapshots[linedef];
			RevertSide(linedef.Front, snapshot.Front);
			RevertSide(linedef.Back, snapshot.Back);
		}

		_onLiveChange?.Invoke();
	}

	private void RevertSide(Sidedef side, SideSnapshot snapshot)
	{
		if (side == null || snapshot == null) return;

		side.OffsetX = snapshot.OffsetX;
		side.OffsetY = snapshot.OffsetY;
		for (var i = 0; i < PartDefs.Length; i++) PartDefs[i].Set(side, snapshot.Textures[i]);
		_map.MarkDirty(side.Sector);
	}

	/// <summary>
	/// Everything else is resolved here, for the first time, against each
	/// linedef's original <c>Fields</c> value - deliberately never live-
	/// applied while the dialog was open (nothing to preview), matching
	/// <see cref="SectorEditDialog.OnConfirmed"/>'s own reasoning for its
	/// own gameplay-only fields. A mixed/blank action field resolves to
	/// each linedef's own original action (never written), and since
	/// <see cref="_currentArgInfos"/> then falls back to all-generic-
	/// unused slots in that case too, no argument write is attempted
	/// either - a mixed selection's own individual action numbers (and
	/// whatever arguments belong to them) are simply left alone, exactly
	/// matching UDB's real practice of only editing what the dialog can
	/// actually make sense of.
	/// </summary>
	private void OnConfirmed()
	{
		var commands = new List<ICommand>();
		var argInfos = _currentArgInfos;

		foreach (var linedef in _linedefs)
		{
			var snapshot = _snapshots[linedef];

			var newAction = NumericFieldExpression.ResolveInteger(_actionEdit.Text, snapshot.ActionNumber) ?? snapshot.ActionNumber;
			if (newAction != snapshot.ActionNumber)
			{
				commands.Add(new SetFieldCommand(linedef.Fields, "special", newAction == 0 ? null : new UniValue(UniversalType.Integer, newAction)));
			}

			for (var i = 0; i < 5; i++)
			{
				var row = _argRows[i];
				long newValue;

				if (argInfos[i].EnumOptions != null)
				{
					if (!_touchedArgs.Contains(i)) continue;
					newValue = row.EnumEdit.GetItemMetadata(row.EnumEdit.Selected).AsInt64();
				}
				else
				{
					newValue = NumericFieldExpression.ResolveInteger(row.NumberEdit.Text, snapshot.Args[i]) ?? snapshot.Args[i];
				}

				if (newValue == snapshot.Args[i]) continue;
				commands.Add(new SetFieldCommand(linedef.Fields, $"arg{i}", newValue == 0 ? null : new UniValue(UniversalType.Integer, newValue)));
			}

			foreach (var key in _touchedFlags)
			{
				var newValue = _flagCheckBoxes[key].ButtonPressed;
				if (newValue != snapshot.Flags[key])
				{
					commands.Add(new SetFieldCommand(linedef.Fields, key, newValue ? new UniValue(UniversalType.Boolean, true) : null));
				}
			}

			foreach (var key in _touchedActivations)
			{
				var newValue = _activationCheckBoxes[key].ButtonPressed;
				if (newValue != snapshot.Activations[key])
				{
					commands.Add(new SetFieldCommand(linedef.Fields, key, newValue ? new UniValue(UniversalType.Boolean, true) : null));
				}
			}

			AddSideCommands(commands, linedef.Front, snapshot.Front, _front);
			AddSideCommands(commands, linedef.Back, snapshot.Back, _back);
		}

		commands.AddRange(_tagsEditor.BuildCommands());

		if (commands.Count > 0) _undoStack.Record(new CommandGroup(commands));
	}

	/// <summary>Skips outright when this particular linedef doesn't actually have this side - matching UDB's own real per-linedef Apply guard, so a mixed one-sided/two-sided selection can never corrupt a one-sided line even though the tab itself was enabled from the first selected linedef's own shape.</summary>
	private void AddSideCommands(List<ICommand> commands, Sidedef side, SideSnapshot snapshot, SideControls controls)
	{
		if (side == null || snapshot == null) return;

		if (side.OffsetX != snapshot.OffsetX)
		{
			commands.Add(new SetPropertyCommand<Sidedef, int>(side, (s, v) => s.OffsetX = v, snapshot.OffsetX, side.OffsetX, s => _map.MarkDirty(s.Sector)));
		}

		if (side.OffsetY != snapshot.OffsetY)
		{
			commands.Add(new SetPropertyCommand<Sidedef, int>(side, (s, v) => s.OffsetY = v, snapshot.OffsetY, side.OffsetY, s => _map.MarkDirty(s.Sector)));
		}

		for (var i = 0; i < PartDefs.Length; i++)
		{
			var def = PartDefs[i];
			var currentTexture = def.Get(side);
			if (currentTexture != snapshot.Textures[i])
			{
				commands.Add(new SetPropertyCommand<Sidedef, string>(side, def.Set, snapshot.Textures[i], currentTexture, s => _map.MarkDirty(s.Sector)));
			}
		}

		AddIfChangedIntegerField(commands, side.Fields, "light", snapshot.Light, controls.LightEdit.Text, defaultValue: 0);
		if (_touchedCheckBoxes.Contains(controls.LightAbsoluteCheck))
		{
			AddIfChangedBoolField(commands, side.Fields, "lightabsolute", snapshot.LightAbsolute, controls.LightAbsoluteCheck.ButtonPressed);
		}

		for (var i = 0; i < controls.Parts.Length; i++)
		{
			var part = controls.Parts[i];
			var partSnapshot = snapshot.Parts[i];
			var suffix = PartDefs[i].UdmfSuffix;

			AddIfChangedFloatField(commands, side.Fields, $"offsetx_{suffix}", partSnapshot.OffsetX, part.OffsetXEdit.Text, defaultValue: 0.0);
			AddIfChangedFloatField(commands, side.Fields, $"offsety_{suffix}", partSnapshot.OffsetY, part.OffsetYEdit.Text, defaultValue: 0.0);
			AddIfChangedFloatField(commands, side.Fields, $"scalex_{suffix}", partSnapshot.ScaleX, part.ScaleXEdit.Text, defaultValue: 1.0);
			AddIfChangedFloatField(commands, side.Fields, $"scaley_{suffix}", partSnapshot.ScaleY, part.ScaleYEdit.Text, defaultValue: 1.0);
			AddIfChangedIntegerField(commands, side.Fields, $"light_{suffix}", partSnapshot.Light, part.LightEdit.Text, defaultValue: 0);
			if (_touchedCheckBoxes.Contains(part.LightAbsoluteCheck))
			{
				AddIfChangedBoolField(commands, side.Fields, $"lightabsolute_{suffix}", partSnapshot.LightAbsolute, part.LightAbsoluteCheck.ButtonPressed);
			}
		}
	}

	private static void AddIfChangedIntegerField(List<ICommand> commands, UniFields fields, string key, long originalValue, string fieldText, long defaultValue)
	{
		var newValue = NumericFieldExpression.ResolveInteger(fieldText, originalValue) ?? originalValue;
		if (newValue == originalValue) return;

		commands.Add(new SetFieldCommand(fields, key, newValue == defaultValue ? null : new UniValue(UniversalType.Integer, newValue)));
	}

	private static void AddIfChangedFloatField(List<ICommand> commands, UniFields fields, string key, double originalValue, string fieldText, double defaultValue)
	{
		var newValue = NumericFieldExpression.Resolve(fieldText, originalValue) ?? originalValue;
		if (newValue == originalValue) return;

		commands.Add(new SetFieldCommand(fields, key, newValue == defaultValue ? null : new UniValue(UniversalType.Float, newValue)));
	}

	private static void AddIfChangedBoolField(List<ICommand> commands, UniFields fields, string key, bool originalValue, bool newValue)
	{
		if (newValue == originalValue) return;

		commands.Add(new SetFieldCommand(fields, key, newValue ? new UniValue(UniversalType.Boolean, true) : null));
	}

	private static string SharedOrBlank(IEnumerable<double> values)
	{
		var list = values.ToList();
		return list.Count > 0 && list.All(v => v == list[0]) ? FormatNumber(list[0]) : "";
	}

	private static string SharedOrBlank(IEnumerable<string> values)
	{
		var list = values.ToList();
		return list.Count > 0 && list.All(v => v == list[0]) ? list[0] : "";
	}

	private static string FormatNumber(double value) =>
		value == Math.Floor(value) ? ((long)value).ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
}
