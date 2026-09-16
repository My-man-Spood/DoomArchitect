using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MapVector2 = System.Numerics.Vector2;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Editing;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using DoomArchitect.Rendering;
using Godot;

/// <summary>
/// Thing properties, ported from UDB's real <c>ThingEditFormUDMF</c> - the
/// last of the three main property dialogs (Sector and Linedef are both
/// already done). The tab strip mirrors the real dialog's 4 tabs
/// (Properties/"Action / Tag / Misc."/Comment/Custom) - Comment/Custom are
/// this project's own established "placeholder tab, shape recognizable,
/// not yet built" pattern.
///
/// **The Properties tab's " Thing " group is an embedded live picker**
/// (<see cref="ThingTypePicker"/>), not a popup "Browse..." dialog like
/// Sector Special/Linedef Action use - confirmed directly against
/// <c>ThingEditFormUDMF.Designer.cs</c>, which has no popup form involved
/// at all (a deliberate scope call: UDB's own real shape here, not the
/// simpler popup this project's other two pickers use). " Flags " is a
/// flat checkbox list (no skill/class/gamemode sub-grouping - UDB's own
/// real dialog doesn't group them either), rebuilt per game configuration
/// exactly like <see cref="SectorEditDialog.RebuildFlagsCheckboxes"/>
/// already does. " Position " (X/Y/Z) and Type/Angle are real-time
/// (already rendered - matching Sector/Linedef's own real-time-vs-OK-only
/// split rule); Pitch/Roll are OK-only (this project's mesh builder
/// doesn't apply either to a thing's sprite billboard yet, no visual
/// effect to preview). " Rotation "'s three numeric fields each pair with
/// their own real <see cref="AngleDialControl"/> compass dial (a genuine
/// port of UDB's real <c>AngleControlEx</c>, not a decorative stand-in) -
/// UDB's own real "Random" checkbox per axis is reproduced too: checking
/// it disables that axis's own field+dial for the rest of this dialog
/// session and assigns a fresh independent random 0-359 value per thing
/// only once, at <see cref="OnConfirmed"/> - matching UDB's own real
/// one-shot-at-Apply-time behavior exactly (confirmed directly against
/// <c>ThingEditFormUDMF.cs</c>'s own <c>cbrandomangle</c>/etc. handlers -
/// it is not a persistent per-thing flag).
///
/// UDB's real "Absolute Height" checkbox (a pure display-mode toggle
/// between "Z relative to the containing sector's floor" and "Z as an
/// absolute world height," never itself a stored field) is deliberately
/// not built - it needs a point-in-sector lookup this project's Core has
/// no public helper for yet, and <see cref="Thing.Height"/> is already
/// always floor-relative (see that property's own doc comment), so the
/// field still works correctly without it - a flagged v1 simplification,
/// not a missing capability.
///
/// **"Action / Tag / Misc." tab**: " Rendering " (Scale X/Y, Alpha + a
/// Reset button, and Render Style) and " Behaviour " (Gravity/Score/
/// Health/Conversation ID/Float Bob Phase) are both real, UDMF-only OK-
/// only fields (verified directly against <c>ThingEditFormUDMF.cs</c>'s
/// own real field names/defaults) - UDB's own real dynamic-light Color
/// picker and config-driven Render Style dropdown (<c>General.Map.Config.ThingRenderStyles</c>)
/// are deliberately not built: this project has no color-picker control
/// or render-style game-configuration schema yet, and doesn't render
/// dynamic lights at all - Render Style is a plain free-text field
/// instead, the same kind of flagged v1 simplification
/// <see cref="SectorEditDialog"/>'s own Damage Type/Sound Sequence fields
/// already are. " Action " reuses the exact same shared
/// <see cref="ActionArgumentsEditor"/> <see cref="LinedefEditDialog"/>
/// uses - confirmed directly in UDB's own source that a Thing's own
/// <c>special</c>/<c>arg0-4</c> resolve against the identical
/// <see cref="ActionInfo"/> table a Linedef's action number does.
/// " Identification " reuses <see cref="MapTagsEditor"/> via its own
/// <see cref="MapTagsEditor.SetThings"/> overload - the third confirmed
/// real shared consumer of that control.
/// </summary>
public partial class ThingEditDialog : AcceptDialog
{
	private sealed record Snapshot(
		double PositionX, double PositionY, double Height, int Angle, int Type,
		long Pitch, long Roll,
		double ScaleX, double ScaleY, double Alpha, string RenderStyle,
		double Gravity, long Score, double Health, long ConversationId, long FloatBobPhase,
		IReadOnlyDictionary<string, bool> Flags);

	private TabContainer _tabs;
	private ThingTypePicker _typePicker;
	private Container _flagsContainer;
	private CheckBox _flagCheckBoxTemplate;

	private StepperLineEdit _positionXEdit;
	private StepperLineEdit _positionYEdit;
	private StepperLineEdit _heightEdit;

	private StepperLineEdit _angleEdit;
	private CheckBox _angleRandomCheck;
	private AngleDialControl _angleDial;
	private StepperLineEdit _pitchEdit;
	private CheckBox _pitchRandomCheck;
	private AngleDialControl _pitchDial;
	private StepperLineEdit _rollEdit;
	private CheckBox _rollRandomCheck;
	private AngleDialControl _rollDial;

	private StepperLineEdit _scaleXEdit;
	private StepperLineEdit _scaleYEdit;
	private StepperLineEdit _alphaEdit;
	private Button _resetAlphaButton;
	private LineEdit _renderStyleEdit;

	private StepperLineEdit _gravityEdit;
	private StepperLineEdit _scoreEdit;
	private StepperLineEdit _healthEdit;
	private StepperLineEdit _conversationIdEdit;
	private StepperLineEdit _floatBobPhaseEdit;

	private ActionArgumentsEditor _actionEditor;
	private MapTagsEditor _tagsEditor;

	private readonly Dictionary<string, CheckBox> _flagCheckBoxes = new();
	private readonly HashSet<string> _touchedFlags = new();

	private IReadOnlyList<Thing> _things = Array.Empty<Thing>();
	private Dictionary<Thing, Snapshot> _snapshots = new();
	private MapData _map;
	private UndoStack _undoStack;
	private Action _onLiveChange;
	private bool _suppressLiveApply;

	public override void _Ready()
	{
		_tabs = GetNode<TabContainer>("Container/Tabs");

		_typePicker = GetNode<ThingTypePicker>("Container/Tabs/Properties/VboxContainer/PropertiesRow/ThingBox/Content/ThingTypePicker");
		_flagsContainer = GetNode<Container>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/TopRow/FlagsBox/Content/FlagsContainer");
		_flagCheckBoxTemplate = GetNode<CheckBox>("Container/Tabs/Properties/VboxContainer/FlagCheckBoxTemplate");

		_positionXEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/BottomRow/PositionBox/Content/XRow/PositionXEdit");
		_positionYEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/BottomRow/PositionBox/Content/YRow/PositionYEdit");
		_heightEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/BottomRow/PositionBox/Content/ZRow/HeightEdit");

		_angleEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/BottomRow/RotationBox/Content/AngleRow/AngleEdit");
		_angleRandomCheck = GetNode<CheckBox>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/BottomRow/RotationBox/Content/AngleRow/AngleRandomCheck");
		_pitchEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/BottomRow/RotationBox/Content/PitchRow/PitchEdit");
		_pitchRandomCheck = GetNode<CheckBox>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/BottomRow/RotationBox/Content/PitchRow/PitchRandomCheck");
		_rollEdit = GetNode<StepperLineEdit>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/BottomRow/RotationBox/Content/RollRow/RollEdit");
		_rollRandomCheck = GetNode<CheckBox>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/BottomRow/RotationBox/Content/RollRow/RollRandomCheck");

		_angleDial = GetNode<AngleDialControl>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/TopRow/DialsColumn/AngleDialBox/Content/AngleDialControl");
		_pitchDial = GetNode<AngleDialControl>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/TopRow/DialsColumn/PitchDialBox/Content/AngleDialControl");
		_rollDial = GetNode<AngleDialControl>("Container/Tabs/Properties/VboxContainer/PropertiesRow/RightColumn/TopRow/DialsColumn/RollDialBox/Content/AngleDialControl");

		_scaleXEdit = GetNode<StepperLineEdit>("Container/Tabs/ActionTagMisc/VboxContainer/HBoxContainer/RenderingBox/Content/ScaleRow/ScaleXEdit");
		_scaleYEdit = GetNode<StepperLineEdit>("Container/Tabs/ActionTagMisc/VboxContainer/HBoxContainer/RenderingBox/Content/ScaleRow/ScaleYEdit");
		_alphaEdit = GetNode<StepperLineEdit>("Container/Tabs/ActionTagMisc/VboxContainer/HBoxContainer/RenderingBox/Content/AlphaRow/AlphaEdit");
		_resetAlphaButton = GetNode<Button>("Container/Tabs/ActionTagMisc/VboxContainer/HBoxContainer/RenderingBox/Content/AlphaRow/ResetAlphaButton");
		_renderStyleEdit = GetNode<LineEdit>("Container/Tabs/ActionTagMisc/VboxContainer/HBoxContainer/RenderingBox/Content/RenderStyleRow/RenderStyleEdit");

		_gravityEdit = GetNode<StepperLineEdit>("Container/Tabs/ActionTagMisc/VboxContainer/HBoxContainer/BehaviourBox/Content/GravityRow/GravityEdit");
		_scoreEdit = GetNode<StepperLineEdit>("Container/Tabs/ActionTagMisc/VboxContainer/HBoxContainer/BehaviourBox/Content/ScoreRow/ScoreEdit");
		_healthEdit = GetNode<StepperLineEdit>("Container/Tabs/ActionTagMisc/VboxContainer/HBoxContainer/BehaviourBox/Content/HealthRow/HealthEdit");
		_conversationIdEdit = GetNode<StepperLineEdit>("Container/Tabs/ActionTagMisc/VboxContainer/HBoxContainer/BehaviourBox/Content/ConversationIdRow/ConversationIdEdit");
		_floatBobPhaseEdit = GetNode<StepperLineEdit>("Container/Tabs/ActionTagMisc/VboxContainer/HBoxContainer/BehaviourBox/Content/FloatBobPhaseRow/FloatBobPhaseEdit");

		_actionEditor = GetNode<ActionArgumentsEditor>("Container/Tabs/ActionTagMisc/VboxContainer/ActionBox/Content/ActionArgumentsEditor");
		_tagsEditor = GetNode<MapTagsEditor>("Container/Tabs/ActionTagMisc/VboxContainer/IdentificationBox/Content/MapTagsEditor");

		_typePicker.TypeIdTextChanged += ApplyRealTimeType;
		_positionXEdit.TextChanged += ApplyRealTimePositionX;
		_positionYEdit.TextChanged += ApplyRealTimePositionY;
		_heightEdit.TextChanged += ApplyRealTimeHeight;
		_angleEdit.TextChanged += ApplyRealTimeAngle;

		_angleEdit.TextChanged += text => SyncDialFromText(_angleDial, text);
		_pitchEdit.TextChanged += text => SyncDialFromText(_pitchDial, text);
		_rollEdit.TextChanged += text => SyncDialFromText(_rollDial, text);
		_angleDial.AngleChanged += degrees => { _angleEdit.Text = degrees.ToString(CultureInfo.InvariantCulture); ApplyRealTimeAngle(_angleEdit.Text); };
		_pitchDial.AngleChanged += degrees => _pitchEdit.Text = degrees.ToString(CultureInfo.InvariantCulture);
		_rollDial.AngleChanged += degrees => _rollEdit.Text = degrees.ToString(CultureInfo.InvariantCulture);

		_angleRandomCheck.Toggled += random => SetRandomAxisEnabled(_angleEdit, _angleDial, random);
		_pitchRandomCheck.Toggled += random => SetRandomAxisEnabled(_pitchEdit, _pitchDial, random);
		_rollRandomCheck.Toggled += random => SetRandomAxisEnabled(_rollEdit, _rollDial, random);

		_resetAlphaButton.Pressed += () => _alphaEdit.Text = "1";

		Confirmed += OnConfirmed;
		Canceled += OnCanceled;

		CallDeferred(nameof(EnsureTabBarFitsWithoutScrolling));
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

	public void SetThings(
		IReadOnlyList<Thing> things, MapData map, IGameConfiguration gameConfiguration, UndoStack undoStack, Action onLiveChange,
		SpriteIconCache spriteIconCache)
	{
		_things = things;
		_map = map;
		_undoStack = undoStack;
		_onLiveChange = onLiveChange;

		var flagKeys = gameConfiguration.GetThingFlags();
		_snapshots = things.ToDictionary(t => t, t => new Snapshot(
			t.Position.X, t.Position.Y, t.Height, t.Angle, t.Type,
			t.Fields.GetInteger("pitch", 0), t.Fields.GetInteger("roll", 0),
			t.Fields.GetFloat("scalex", 1.0), t.Fields.GetFloat("scaley", 1.0), t.Fields.GetFloat("alpha", 1.0), t.Fields.GetString("renderstyle", "normal"),
			t.Fields.GetFloat("gravity", 1.0), t.Fields.GetInteger("score", 0), t.Fields.GetFloat("health", 1.0), t.Fields.GetInteger("conversation", 0), t.Fields.GetInteger("floatbobphase", -1),
			flagKeys.ToDictionary(f => f.Key, f => t.Fields.GetBool(f.Key, false))));

		_typePicker.Setup(gameConfiguration, spriteIconCache);
		_actionEditor.Setup(things.Select(t => t.Fields).ToList(), gameConfiguration);
		_tagsEditor.SetThings(things, map);

		_touchedFlags.Clear();

		_suppressLiveApply = true;
		_positionXEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.PositionX));
		_positionYEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.PositionY));
		_heightEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.Height));
		_angleEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Angle));
		_typePicker.TypeIdText = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Type));
		_suppressLiveApply = false;

		_pitchEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Pitch));
		_rollEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Roll));
		SyncDialFromText(_angleDial, _angleEdit.Text);
		SyncDialFromText(_pitchDial, _pitchEdit.Text);
		SyncDialFromText(_rollDial, _rollEdit.Text);

		_angleRandomCheck.ButtonPressed = false;
		_pitchRandomCheck.ButtonPressed = false;
		_rollRandomCheck.ButtonPressed = false;
		SetRandomAxisEnabled(_angleEdit, _angleDial, false);
		SetRandomAxisEnabled(_pitchEdit, _pitchDial, false);
		SetRandomAxisEnabled(_rollEdit, _rollDial, false);

		_scaleXEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.ScaleX));
		_scaleYEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.ScaleY));
		_alphaEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.Alpha));
		_renderStyleEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.RenderStyle));

		_gravityEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.Gravity));
		_scoreEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Score));
		_healthEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.Health));
		_conversationIdEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.ConversationId));
		_floatBobPhaseEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.FloatBobPhase));

		RebuildCheckboxes(flagKeys);
	}

	/// <summary>UDB's own real "Random" checkbox is never a stored field - checking it just disables that axis's own field+dial for the rest of this dialog session (a fresh independent value is assigned per thing once, at <see cref="OnConfirmed"/>).</summary>
	private static void SetRandomAxisEnabled(StepperLineEdit edit, AngleDialControl dial, bool random)
	{
		edit.Editable = !random;
		dial.Editable = !random;
	}

	private static void SyncDialFromText(AngleDialControl dial, string text)
	{
		dial.Angle = NumericFieldExpression.Resolve(text, 0) is { } value ? Mathf.PosMod(Mathf.RoundToInt(value), 360) : null;
	}

	/// <summary>Same reasoning and value as <see cref="SectorEditDialog.MaxFlagsColumnHeight"/> - a Thing's own real flag set (25 for GZDoom's Doom2 UDMF configuration) is more than double Sector's, so this is what actually makes it wrap into a second/third column instead of growing one column tall enough to blow out the whole dialog.</summary>
	private const float MaxFlagsColumnHeight = 320f;

	/// <summary>Same duplicate-the-hidden-template mechanism as <see cref="SectorEditDialog.RebuildFlagsCheckboxes"/>, including the height cap on <see cref="_flagsContainer"/> (a <c>VFlowContainer</c>) that makes wrapping into extra columns possible at all.</summary>
	private void RebuildCheckboxes(IReadOnlyList<SectorFlagInfo> flags)
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

	private void ApplyRealTimePositionX(string text)
	{
		if (_suppressLiveApply) return;

		foreach (var thing in _things)
		{
			var snapshot = _snapshots[thing];
			var newX = NumericFieldExpression.Resolve(text, snapshot.PositionX) ?? snapshot.PositionX;
			thing.Position = new MapVector2((float)newX, thing.Position.Y);
			_map.MarkDirty(thing);
		}

		_onLiveChange?.Invoke();
	}

	private void ApplyRealTimePositionY(string text)
	{
		if (_suppressLiveApply) return;

		foreach (var thing in _things)
		{
			var snapshot = _snapshots[thing];
			var newY = NumericFieldExpression.Resolve(text, snapshot.PositionY) ?? snapshot.PositionY;
			thing.Position = new MapVector2(thing.Position.X, (float)newY);
			_map.MarkDirty(thing);
		}

		_onLiveChange?.Invoke();
	}

	private void ApplyRealTimeHeight(string text)
	{
		if (_suppressLiveApply) return;

		foreach (var thing in _things)
		{
			var snapshot = _snapshots[thing];
			thing.Height = NumericFieldExpression.Resolve(text, snapshot.Height) ?? snapshot.Height;
			_map.MarkDirty(thing);
		}

		_onLiveChange?.Invoke();
	}

	private void ApplyRealTimeAngle(string text)
	{
		if (_suppressLiveApply) return;

		foreach (var thing in _things)
		{
			var snapshot = _snapshots[thing];
			thing.Angle = (int)(NumericFieldExpression.ResolveInteger(text, snapshot.Angle) ?? snapshot.Angle);
			_map.MarkDirty(thing);
		}

		_onLiveChange?.Invoke();
	}

	private void ApplyRealTimeType(string text)
	{
		if (_suppressLiveApply) return;

		foreach (var thing in _things)
		{
			var snapshot = _snapshots[thing];
			thing.Type = (int)(NumericFieldExpression.ResolveInteger(text, snapshot.Type) ?? snapshot.Type);
			_map.MarkDirty(thing);
		}

		_onLiveChange?.Invoke();
	}

	/// <summary>Only Position/Height/Angle/Type were ever live-applied (see this class's own remarks) - those revert directly, matching <see cref="SectorEditDialog.OnCanceled"/>'s reasoning.</summary>
	private void OnCanceled()
	{
		foreach (var (thing, snapshot) in _snapshots)
		{
			thing.Position = new MapVector2((float)snapshot.PositionX, (float)snapshot.PositionY);
			thing.Height = snapshot.Height;
			thing.Angle = snapshot.Angle;
			thing.Type = snapshot.Type;
			_map.MarkDirty(thing);
		}

		_onLiveChange?.Invoke();
	}

	/// <summary>
	/// Position/Height/Angle/Type were already live-applied, so their
	/// commands are built from a straight snapshot-vs-current-live-value
	/// comparison; everything else is resolved here, for the first time,
	/// against each thing's original <see cref="UniFields"/> value -
	/// deliberately never live-applied, matching
	/// <see cref="SectorEditDialog.OnConfirmed"/>'s own reasoning for its
	/// own gameplay-only/no-visual-effect fields.
	/// </summary>
	private void OnConfirmed()
	{
		var commands = new List<ICommand>(_actionEditor.BuildCommands());
		var random = new Random();

		foreach (var thing in _things)
		{
			var snapshot = _snapshots[thing];

			AddIfChanged(commands, thing, new MapVector2((float)snapshot.PositionX, (float)snapshot.PositionY), thing.Position, (t, v) => t.Position = v);
			AddIfChanged(commands, thing, snapshot.Height, thing.Height, (t, v) => t.Height = v);

			var newAngle = _angleRandomCheck.ButtonPressed ? random.Next(0, 360) : thing.Angle;
			AddIfChanged(commands, thing, snapshot.Angle, newAngle, (t, v) => t.Angle = v);
			AddIfChanged(commands, thing, snapshot.Type, thing.Type, (t, v) => t.Type = v);

			var newPitch = _pitchRandomCheck.ButtonPressed ? random.Next(0, 360) : NumericFieldExpression.ResolveInteger(_pitchEdit.Text, snapshot.Pitch) ?? snapshot.Pitch;
			AddIfChangedIntegerField(commands, thing, "pitch", snapshot.Pitch, newPitch, defaultValue: 0);

			var newRoll = _rollRandomCheck.ButtonPressed ? random.Next(0, 360) : NumericFieldExpression.ResolveInteger(_rollEdit.Text, snapshot.Roll) ?? snapshot.Roll;
			AddIfChangedIntegerField(commands, thing, "roll", snapshot.Roll, newRoll, defaultValue: 0);

			AddIfChangedFloatField(commands, thing, "scalex", snapshot.ScaleX, _scaleXEdit.Text, defaultValue: 1.0);
			AddIfChangedFloatField(commands, thing, "scaley", snapshot.ScaleY, _scaleYEdit.Text, defaultValue: 1.0);
			AddIfChangedFloatField(commands, thing, "alpha", snapshot.Alpha, _alphaEdit.Text, defaultValue: 1.0);
			AddIfChangedStringField(commands, thing, "renderstyle", snapshot.RenderStyle, _renderStyleEdit.Text, defaultValue: "normal");

			AddIfChangedFloatField(commands, thing, "gravity", snapshot.Gravity, _gravityEdit.Text, defaultValue: 1.0);
			AddIfChangedIntegerField(commands, thing, "score", snapshot.Score, NumericFieldExpression.ResolveInteger(_scoreEdit.Text, snapshot.Score) ?? snapshot.Score, defaultValue: 0);
			AddIfChangedFloatField(commands, thing, "health", snapshot.Health, _healthEdit.Text, defaultValue: 1.0);
			AddIfChangedIntegerField(commands, thing, "conversation", snapshot.ConversationId, NumericFieldExpression.ResolveInteger(_conversationIdEdit.Text, snapshot.ConversationId) ?? snapshot.ConversationId, defaultValue: 0);
			AddIfChangedIntegerField(commands, thing, "floatbobphase", snapshot.FloatBobPhase, NumericFieldExpression.ResolveInteger(_floatBobPhaseEdit.Text, snapshot.FloatBobPhase) ?? snapshot.FloatBobPhase, defaultValue: -1);

			foreach (var key in _touchedFlags)
			{
				var newValue = _flagCheckBoxes[key].ButtonPressed;
				if (newValue != snapshot.Flags[key])
				{
					commands.Add(new SetFieldCommand(thing.Fields, key, newValue ? new UniValue(UniversalType.Boolean, true) : null));
				}
			}
		}

		commands.AddRange(_tagsEditor.BuildCommands());

		// Execute, not Record - see SectorEditDialog.OnConfirmed's own
		// identical remarks (a real, verified bug fixed alongside this
		// dialog): only Position/Height/Angle/Type were ever live-applied,
		// so Record alone would silently leave every other command here
		// (Pitch/Roll/Rendering/Behaviour/Action/Args/Flags/Tag) unwritten.
		if (commands.Count > 0) _undoStack.Execute(new CommandGroup(commands));
	}

	private void AddIfChanged<T>(List<ICommand> commands, Thing thing, T oldValue, T newValue, Action<Thing, T> setter)
	{
		if (EqualityComparer<T>.Default.Equals(oldValue, newValue)) return;
		commands.Add(new SetPropertyCommand<Thing, T>(thing, setter, oldValue, newValue, t => _map.MarkDirty(t)));
	}

	private static void AddIfChangedIntegerField(List<ICommand> commands, Thing thing, string key, long originalValue, long newValue, long defaultValue)
	{
		if (newValue == originalValue) return;
		commands.Add(new SetFieldCommand(thing.Fields, key, newValue == defaultValue ? null : new UniValue(UniversalType.Integer, newValue)));
	}

	private static void AddIfChangedFloatField(List<ICommand> commands, Thing thing, string key, double originalValue, string fieldText, double defaultValue)
	{
		var newValue = NumericFieldExpression.Resolve(fieldText, originalValue) ?? originalValue;
		if (newValue == originalValue) return;

		commands.Add(new SetFieldCommand(thing.Fields, key, newValue == defaultValue ? null : new UniValue(UniversalType.Float, newValue)));
	}

	/// <summary>Blank text means "keep this thing's own original," matching <see cref="SectorEditDialog"/>'s identical convention for its own free-text fields.</summary>
	private static void AddIfChangedStringField(List<ICommand> commands, Thing thing, string key, string originalValue, string fieldText, string defaultValue)
	{
		var trimmed = fieldText.Trim();
		var newValue = trimmed.Length == 0 ? originalValue : trimmed;
		if (newValue == originalValue) return;

		commands.Add(new SetFieldCommand(thing.Fields, key, newValue == defaultValue ? null : new UniValue(UniversalType.String, newValue)));
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
