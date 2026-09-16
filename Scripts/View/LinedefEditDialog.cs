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
/// **The dynamic argument system** now lives in the shared
/// <see cref="ActionArgumentsEditor"/> control (extracted from what was
/// originally this class's own inline copy, once the Thing dialog needed
/// the exact same behavior - see that class's own remarks for the real
/// UDB verification and reasoning behind its manual enum/number toggle).
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
/// <see cref="TexturePreviewEdit"/> (reusing <see cref="TextureBrowserDialog"/>
/// in wall mode, exactly like <see cref="SectorEditDialog"/>'s own flat-mode
/// usage of the same shared control) plus OK-only per-part offset/scale/
/// light-override fields -
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
		IReadOnlyDictionary<string, bool> Flags, IReadOnlyDictionary<string, bool> Activations,
		SideSnapshot Front, SideSnapshot Back);

	private sealed record PartSnapshot(double OffsetX, double OffsetY, double ScaleX, double ScaleY, long Light, bool LightAbsolute);

	private sealed record SideSnapshot(int SectorIndex, int OffsetX, int OffsetY, long Light, bool LightAbsolute, string[] Textures, PartSnapshot[] Parts);

	private sealed record PartControls(
		TexturePreviewEdit Texture, StepperLineEdit OffsetXEdit, StepperLineEdit OffsetYEdit,
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
	private ActionArgumentsEditor _actionEditor;
	private Container _flagsContainer;
	private Container _activationsContainer;
	private CheckBox _flagCheckBoxTemplate;
	private MapTagsEditor _tagsEditor;
	private SideControls _front;
	private SideControls _back;

	private TextureBrowserDialog _textureBrowserDialog;

	private readonly Dictionary<string, CheckBox> _flagCheckBoxes = new();
	private readonly Dictionary<string, CheckBox> _activationCheckBoxes = new();
	private readonly HashSet<string> _touchedFlags = new();
	private readonly HashSet<string> _touchedActivations = new();
	private readonly HashSet<CheckBox> _touchedCheckBoxes = new();

	private IReadOnlyList<Linedef> _linedefs = Array.Empty<Linedef>();
	private Dictionary<Linedef, Snapshot> _snapshots = new();
	private MapData _map;
	private UndoStack _undoStack;
	private TextureSet _textureSet;
	private IReadOnlyList<NamedResource> _namedResources = Array.Empty<NamedResource>();
	private TextureIconCache _textureIconCache;
	private Action _onLiveChange;
	private bool _suppressLiveApply;

	public override void _Ready()
	{
		_tabs = GetNode<TabContainer>("Container/Tabs");

		_actionEditor = GetNode<ActionArgumentsEditor>("Container/Tabs/Properties/VboxContainer/ActionBox/Content/ActionArgumentsEditor");

		_flagsContainer = GetNode<Container>("Container/Tabs/Properties/VboxContainer/FlagsBox/Content/FlagsContainer");
		_activationsContainer = GetNode<Container>("Container/Tabs/Properties/VboxContainer/ActivationBox/Content/ActivationsContainer");
		_flagCheckBoxTemplate = GetNode<CheckBox>("Container/Tabs/Properties/VboxContainer/FlagCheckBoxTemplate");
		_tagsEditor = GetNode<MapTagsEditor>("Container/Tabs/Properties/VboxContainer/IdentificationBox/Content/MapTagsEditor");

		_front = LoadSideControls("Front");
		_back = LoadSideControls("Back");

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
				UpdateTexturePreview(part.Texture, part.Texture.Text);
			}
		}
	}

	private SideControls LoadSideControls(string side)
	{
		var general = $"Container/Tabs/{side}/VBoxContainer/GeneralBox/Content";
		return new SideControls(
			GetNode<Control>($"Container/Tabs/{side}"),
			GetNode<Label>($"{general}/SectorRow/{side}SectorValueLabel"),
			GetNode<StepperLineEdit>($"{general}/OffsetRow/{side}OffsetXEdit"),
			GetNode<StepperLineEdit>($"{general}/OffsetRow/{side}OffsetYEdit"),
			GetNode<StepperLineEdit>($"{general}/LightRow/{side}LightEdit"),
			GetNode<CheckBox>($"{general}/LightRow/{side}LightAbsoluteCheck"),
			PartDefs.Select(def => LoadPartControls(side, def.BoxSuffix, def.FieldPrefix)).ToArray());
	}

	private PartControls LoadPartControls(string side, string boxSuffix, string fieldPrefix)
	{
		var content = $"Container/Tabs/{side}/VBoxContainer/PartsRow/{side}{boxSuffix}Box/Content";
		var prefix = $"{side}{fieldPrefix}";
		return new PartControls(
			GetNode<TexturePreviewEdit>($"{content}/{prefix}TexturePreviewEdit"),
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

			part.Texture.TextChanged += text =>
			{
				ApplyRealTimeSideTexture(getSide, getSnapshot, partIndex, text);
				UpdateTexturePreview(part.Texture, text);
			};
			part.Texture.PreviewPressed += () => BrowseSideTexture(getSide, getSnapshot, part, partIndex);
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
		_undoStack = undoStack;
		_onLiveChange = onLiveChange;
		_textureSet = textureSet;
		_namedResources = namedResources;
		_textureIconCache = textureIconCache;

		var flagKeys = gameConfiguration.GetLinedefFlags();
		var activationKeys = gameConfiguration.GetLinedefActivations();

		_snapshots = linedefs.ToDictionary(l => l, l => new Snapshot(
			flagKeys.ToDictionary(f => f.Key, f => l.Fields.GetBool(f.Key, false)),
			activationKeys.ToDictionary(a => a.Key, a => l.Fields.GetBool(a.Key, false)),
			BuildSideSnapshot(l.Front, map),
			BuildSideSnapshot(l.Back, map)));

		_actionEditor.Setup(linedefs.Select(l => l.Fields).ToList(), gameConfiguration);
		_tagsEditor.SetLinedefs(linedefs, map);

		_touchedFlags.Clear();
		_touchedActivations.Clear();
		_touchedCheckBoxes.Clear();

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
			part.Texture.Text = SharedOrBlank(sideSnapshots.Select(s => s.Textures[partIndex]));
			_suppressLiveApply = false;
			UpdateTexturePreview(part.Texture, part.Texture.Text);

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
			part.Texture.Editable = enabled;
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
		_textureBrowserDialog.Browse(_textureSet, _namedResources, _textureIconCache, flats: false, part.Texture.Text, name =>
		{
			part.Texture.Text = name;
			ApplyRealTimeSideTexture(getSide, getSnapshot, partIndex, name);
			UpdateTexturePreview(part.Texture, name);
		});
	}

	private TextureBrowserDialog CreateTextureBrowserDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/TextureBrowserDialog.tscn").Instantiate<TextureBrowserDialog>();
		AddChild(dialog);
		return dialog;
	}

	/// <summary>Wall-mode counterpart to <see cref="SectorEditDialog.UpdateTexturePreview"/> - uses <see cref="TextureIconCache.GetOrDecodeWallIcon"/> since every sidedef texture part is a wall texture, never a flat. Decoding/placeholder-fallback/hover/square-aspect are all now owned by <see cref="TexturePreviewEdit"/> itself - this only ever decides *which* texture to hand it.</summary>
	private void UpdateTexturePreview(TexturePreviewEdit control, string text)
	{
		var trimmed = text.Trim();
		control.SetPreviewTexture(trimmed.Length == 0 ? null : _textureIconCache?.GetOrDecodeWallIcon(trimmed));
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
	/// own gameplay-only fields. Action/arguments are resolved by
	/// <see cref="_actionEditor"/> itself (see its own remarks for how a
	/// mixed/blank action field is handled).
	/// </summary>
	private void OnConfirmed()
	{
		var commands = new List<ICommand>(_actionEditor.BuildCommands());

		foreach (var linedef in _linedefs)
		{
			var snapshot = _snapshots[linedef];

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

		// Execute, not Record - see SectorEditDialog.OnConfirmed's own
		// identical remarks: only the Front/Back offset/texture commands
		// above were ever live-applied, everything else here (Action/Args/
		// Flags/Activation/Tag/per-part offset-scale-light) was never
		// applied anywhere else and Record alone would leave it unwritten.
		if (commands.Count > 0) _undoStack.Execute(new CommandGroup(commands));
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
