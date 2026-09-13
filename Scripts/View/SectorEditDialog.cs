using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Editing;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using Godot;

/// <summary>
/// Sector properties, ported from UDB's real <c>SectorEditFormUDMF</c> -
/// v1 scope is its "Properties" tab's core fields only (floor/ceiling
/// height+texture, brightness, special, tag); see <c>TODO.md</c> for what's
/// deliberately deferred (Colors/Surfaces-extended/Slopes/Comment/Custom
/// tabs, the texture browser, richer tag range-assignment modes).
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
	private sealed record Snapshot(double FloorHeight, double CeilingHeight, string FloorTexture, string CeilingTexture, int Brightness, long Special, long Tag);

	private LineEdit _floorHeightEdit;
	private LineEdit _ceilingHeightEdit;
	private LineEdit _floorTextureEdit;
	private LineEdit _ceilingTextureEdit;
	private LineEdit _brightnessEdit;
	private LineEdit _specialEdit;
	private Label _specialNameLabel;
	private LineEdit _tagEdit;

	private IReadOnlyList<Sector> _sectors = Array.Empty<Sector>();
	private Dictionary<Sector, Snapshot> _snapshots = new();
	private MapData _map;
	private IGameConfiguration _gameConfiguration;
	private UndoStack _undoStack;
	private Action _onLiveChange;
	private bool _suppressLiveApply;

	public override void _Ready()
	{
		_floorHeightEdit = GetNode<LineEdit>("Container/Grid/FloorHeightEdit");
		_ceilingHeightEdit = GetNode<LineEdit>("Container/Grid/CeilingHeightEdit");
		_floorTextureEdit = GetNode<LineEdit>("Container/Grid/FloorTextureEdit");
		_ceilingTextureEdit = GetNode<LineEdit>("Container/Grid/CeilingTextureEdit");
		_brightnessEdit = GetNode<LineEdit>("Container/Grid/BrightnessEdit");
		_specialEdit = GetNode<LineEdit>("Container/Grid/SpecialRow/SpecialEdit");
		_specialNameLabel = GetNode<Label>("Container/Grid/SpecialRow/SpecialNameLabel");
		_tagEdit = GetNode<LineEdit>("Container/Grid/TagEdit");

		_floorHeightEdit.TextChanged += text => ApplyRealTimeNumber(text, s => s.FloorHeight, (s, v) => s.FloorHeight = v);
		_ceilingHeightEdit.TextChanged += text => ApplyRealTimeNumber(text, s => s.CeilingHeight, (s, v) => s.CeilingHeight = v);
		_brightnessEdit.TextChanged += text => ApplyRealTimeNumber(text, s => s.Brightness, (s, v) => s.Brightness = (int)Math.Round(v));
		_floorTextureEdit.TextChanged += text => ApplyRealTimeTexture(text, s => s.FloorTexture, (s, v) => s.FloorTexture = v);
		_ceilingTextureEdit.TextChanged += text => ApplyRealTimeTexture(text, s => s.CeilingTexture, (s, v) => s.CeilingTexture = v);
		_specialEdit.TextChanged += _ => UpdateSpecialNameLabel();

		Confirmed += OnConfirmed;
		Canceled += OnCanceled;
	}

	public void SetSectors(IReadOnlyList<Sector> sectors, MapData map, IGameConfiguration gameConfiguration, UndoStack undoStack, Action onLiveChange)
	{
		_sectors = sectors;
		_map = map;
		_gameConfiguration = gameConfiguration;
		_undoStack = undoStack;
		_onLiveChange = onLiveChange;

		_snapshots = sectors.ToDictionary(s => s, s => new Snapshot(
			s.FloorHeight, s.CeilingHeight, s.FloorTexture, s.CeilingTexture, s.Brightness,
			s.Fields.GetInteger("special", 0), s.Fields.GetInteger("id", 0)));

		_suppressLiveApply = true;
		_floorHeightEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorHeight));
		_ceilingHeightEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingHeight));
		_floorTextureEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.FloorTexture));
		_ceilingTextureEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => s.CeilingTexture));
		_brightnessEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Brightness));
		_specialEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Special));
		_tagEdit.Text = SharedOrBlank(_snapshots.Values.Select(s => (double)s.Tag));
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
	/// Special/tag are resolved here, for the first time, against each
	/// sector's original bag value - deliberately never live-applied to
	/// <c>Fields</c> while the dialog was open, because
	/// <see cref="SetFieldCommand"/> captures its "old" value from
	/// <c>Fields</c> at construction time; if these had already been
	/// mutated live, building the command here would capture the
	/// already-new value as "old" and corrupt undo. Height/texture/
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
