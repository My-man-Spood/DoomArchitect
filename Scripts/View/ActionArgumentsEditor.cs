using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DoomArchitect.Core.Configuration;
using DoomArchitect.Core.Editing;
using DoomArchitect.Core.Map;
using DoomArchitect.Core.Undo;
using DoomArchitect.Core.ZDoom.Bcs;
using Godot;

/// <summary>
/// The action-number + 5-fixed-argument-slot editor shared between the
/// Linedef and Thing dialogs - extracted here from what was originally
/// <c>LinedefEditDialog</c>'s own inline copy, once a second real consumer
/// (the Thing dialog) needed the identical behavior. Operates directly on
/// <see cref="UniFields"/> (never
/// a <see cref="Linedef"/>- or <see cref="Thing"/>-specific member), the
/// same generalization shape <see cref="MapTagsEditor"/> already proved
/// out for its own two real consumers.
///
/// Each of the fixed 5 argument rows pairs a clickable label
/// (<see cref="ArgRow.LabelButton"/>) with *both* a
/// <see cref="StepperLineEdit"/> (plain numeric) and an
/// <see cref="OptionButton"/> (enum dropdown) - see this class's own
/// remarks inline at <see cref="ToggleArgView"/> for why a manual toggle
/// exists at all. Action/arguments are always OK-only (no visual
/// effect to preview in either host dialog) - <see cref="BuildCommands"/>
/// is the only place anything is ever actually written, called once from
/// the host dialog's own OK handler.
///
/// arg0 of the real ACS_Execute family (80/81/82/83/84/85/226 - every
/// special whose own real UDB config marks arg0 <c>str = true</c>)
/// reuses this exact same numeric/dropdown toggle for a third purpose:
/// instead of a shared <c>enums</c> list, the dropdown lists every
/// script actually declared in the map's own compiled source
/// (<see cref="_scriptCatalog"/>, built by <c>OpenMapMenu.BuildScriptCatalog</c>),
/// since this slot can legitimately hold either a script *number* or a
/// script *name* (a UDMF string) - reading it as a plain number before
/// this existed silently defaulted to 0 for a named reference (see the
/// plan this shipped from). Picking an entry with declared parameters
/// also relabels the *other* slots (<see cref="RefreshScriptArgumentLabels"/>) -
/// UDB's own real per-special remap, confirmed directly against its
/// <c>ScriptItem.GetArgumentsDescriptions</c>.
/// </summary>
public partial class ActionArgumentsEditor : VBoxContainer
{
	private sealed record ElementSnapshot(long ActionNumber, long[] Args, string Arg0String);

	private sealed record ArgRow(Button LabelButton, StepperLineEdit NumberEdit, OptionButton EnumEdit);

	/// <summary>UDB's own real <c>ScriptItem.GetArgumentsDescriptions</c> switch, ported verbatim: which arg slot a script's own first declared parameter name lands on, for each real ACS_Execute-family special number. 81 (ACS_Suspend)/82 (ACS_Terminate) take no script arguments at all and are deliberately absent here.</summary>
	private static readonly Dictionary<int, int> ScriptArgFirstSlot = new()
	{
		[80] = 2, // ACS_Execute (script, map, s_arg1, s_arg2, s_arg3)
		[226] = 2, // ACS_ExecuteAlways (script, map, s_arg1, s_arg2, s_arg3)
		[83] = 2, // ACS_LockedExecute (script, map, s_arg1, s_arg2, lock)
		[85] = 2, // ACS_LockedExecuteDoor (script, map, s_arg1, s_arg2, lock)
		[84] = 1, // ACS_ExecuteWithResult (script, s_arg1, s_arg2, s_arg3, s_arg4)
	};

	private LineEdit _actionEdit;
	private Label _actionNameLabel;
	private Button _actionBrowseButton;
	private ArgRow[] _argRows;
	private Button _navigateButton;

	private LinedefActionBrowserDialog _actionBrowserDialog;

	private readonly HashSet<int> _touchedArgs = new();

	private IReadOnlyList<UniFields> _elements = Array.Empty<UniFields>();
	private List<ElementSnapshot> _snapshots = new();
	private IGameConfiguration _gameConfiguration;
	private IReadOnlyList<ScriptCatalogEntry> _scriptCatalog = Array.Empty<ScriptCatalogEntry>();

	private ActionInfo _currentAction;
	private IReadOnlyList<ArgumentInfo> _currentArgInfos = DefaultArgInfos();

	/// <summary>Raised by <see cref="_navigateButton"/> (shown only alongside a script entry declared directly in the map's own SCRIPTS lump, not reached via <c>#include</c> - see <see cref="TryGetNavigableScript"/>).</summary>
	public event Action<ScriptCatalogEntry> NavigateToScriptRequested;

	public override void _Ready()
	{
		_actionEdit = GetNode<LineEdit>("ActionRow/ActionEdit");
		_actionNameLabel = GetNode<Label>("ActionRow/ActionNameLabel");
		_actionBrowseButton = GetNode<Button>("ActionRow/ActionBrowseButton");
		_navigateButton = GetNode<Button>("ArgsGrid/Arg0Row/Arg0NavigateButton");
		_navigateButton.Icon = GD.Load<Texture2D>("res://Assets/Icons/document_script.svg");
		_navigateButton.Pressed += () =>
		{
			if (TryGetNavigableScript(out var entry)) NavigateToScriptRequested?.Invoke(entry);
		};

		_argRows = new ArgRow[5];
		for (var i = 0; i < 5; i++)
		{
			var rowPath = $"ArgsGrid/Arg{i}Row";
			_argRows[i] = new ArgRow(
				GetNode<Button>($"{rowPath}/ArgLabel"),
				GetNode<StepperLineEdit>($"{rowPath}/ArgNumberEdit"),
				GetNode<OptionButton>($"{rowPath}/ArgEnumEdit"));
		}

		_actionEdit.TextChanged += _ => UpdateActionUi();
		_actionBrowseButton.Pressed += BrowseAction;

		for (var i = 0; i < 5; i++)
		{
			var slot = i;
			_argRows[slot].NumberEdit.TextChanged += _ => _touchedArgs.Add(slot);
			_argRows[slot].EnumEdit.ItemSelected += _ =>
			{
				_touchedArgs.Add(slot);
				if (slot == 0) RefreshScriptArgumentLabels();
			};
			_argRows[slot].LabelButton.Pressed += () => ToggleArgView(slot);

			// Godot's own theme-default "disabled" font color (used here purely
			// to block clicks on a non-toggleable label, not to signal "this
			// argument is invalid") reads far too dim to comfortably read a
			// perfectly normal, in-use argument's own title - override it to a
			// gentler dim than the theme default. Needs enough contrast against
			// the fully-bright, actually-clickable case to still read as "not
			// interactive" at a glance (0.85 turned out too close to full
			// brightness for that), while staying well short of the theme
			// default's much heavier dimming - distinct from (and stacking
			// under) the separate Modulate-based dimming UpdateActionUi already
			// applies for a genuinely *unused* slot.
			_argRows[slot].LabelButton.AddThemeColorOverride("font_disabled_color", new Color(1, 1, 1, 0.6f));
		}
	}

	private static IReadOnlyList<ArgumentInfo> DefaultArgInfos() =>
		Enumerable.Range(0, 5).Select(i => new ArgumentInfo($"Argument {i + 1}", Used: false, EnumOptions: null)).ToList();

	/// <summary>Sets up for a fresh selection - <paramref name="elements"/> is each selected linedef's/thing's own <see cref="UniFields"/> bag directly (the host projects <c>.Fields</c> itself), matching <see cref="MapTagsEditor"/>'s own established shape. <paramref name="scriptCatalog"/> is the current map's own declared scripts (<c>OpenMapMenu.BuildScriptCatalog</c>) - only ever consulted for an arg0 slot whose <see cref="ArgumentInfo.Str"/> is true.</summary>
	public void Setup(IReadOnlyList<UniFields> elements, IGameConfiguration gameConfiguration, IReadOnlyList<ScriptCatalogEntry> scriptCatalog)
	{
		_elements = elements;
		_gameConfiguration = gameConfiguration;
		_scriptCatalog = scriptCatalog ?? Array.Empty<ScriptCatalogEntry>();

		_snapshots = elements.Select(fields =>
		{
			var args = Enumerable.Range(0, 5).Select(i => fields.GetInteger($"arg{i}", 0)).ToArray();
			var arg0String = fields.TryGetValue("arg0", out var raw) && raw.Value is string s ? s : null;
			return new ElementSnapshot(fields.GetInteger("special", 0), args, arg0String);
		}).ToList();

		_touchedArgs.Clear();

		_actionEdit.Text = SharedOrBlank(_snapshots.Select(s => (double)s.ActionNumber));
		UpdateActionUi();
	}

	/// <summary>
	/// Re-derives <see cref="_currentAction"/>/<see cref="_currentArgInfos"/>
	/// from whatever's currently typed in <see cref="_actionEdit"/> - a
	/// blank, unparsable, or unrecognized (including cross-selection
	/// "mixed") action number simply falls back to
	/// <see cref="DefaultArgInfos"/> (all 5 slots generic and disabled),
	/// same "blank means don't know/don't touch" convention as everywhere
	/// else in this project's dialogs.
	/// </summary>
	private void UpdateActionUi()
	{
		var text = _actionEdit.Text.Trim();
		_currentAction = long.TryParse(text, out var number) ? _gameConfiguration?.GetAction((int)number) : null;
		_currentArgInfos = _currentAction?.Args ?? DefaultArgInfos();
		_actionNameLabel.Text = _currentAction?.Title ?? "";
		_navigateButton.Visible = false; // re-shown below only if the rebuilt row turns out to have a navigable entry already selected

		for (var i = 0; i < 5; i++)
		{
			var info = _currentArgInfos[i];
			var row = _argRows[i];
			var isScriptSlot = i == 0 && info.Str;
			var isDropdown = info.EnumOptions != null || isScriptSlot;

			// A script slot defaults to its dropdown/string view - that's
			// the form real UDB named-script usage actually needs; the
			// numeric view stays one click away via the same toggle the
			// enum case already has (see ToggleArgView).
			row.LabelButton.Text = isScriptSlot ? (info.TitleStr ?? info.Title) : info.Title;
			row.LabelButton.Modulate = info.Used ? Colors.White : new Color(1, 1, 1, 0.5f);
			row.LabelButton.Disabled = !isDropdown;
			row.LabelButton.MouseDefaultCursorShape = isDropdown ? Control.CursorShape.PointingHand : Control.CursorShape.Arrow;
			row.NumberEdit.Visible = !isDropdown;
			row.EnumEdit.Visible = isDropdown;
			row.NumberEdit.Editable = info.Used;
			row.EnumEdit.Disabled = !info.Used;

			if (info.EnumOptions != null)
			{
				row.EnumEdit.Clear();
				foreach (var option in info.EnumOptions!)
				{
					row.EnumEdit.AddItem(option.Title);
					row.EnumEdit.SetItemMetadata(row.EnumEdit.ItemCount - 1, option.Value);
				}
			}
			else if (isScriptSlot)
			{
				row.EnumEdit.Clear();
				for (var s = 0; s < _scriptCatalog.Count; s++)
				{
					row.EnumEdit.AddItem(_scriptCatalog[s].Number);
					row.EnumEdit.SetItemMetadata(row.EnumEdit.ItemCount - 1, s);
				}
			}
		}

		RefreshArgValueDisplays();
	}

	/// <summary>
	/// Shows each selected element's own current stored <c>argN</c> value,
	/// shared-or-blank across the selection for a plain numeric slot; an
	/// enum slot only gets a pre-selected item when every selected element
	/// already agrees on a value that's actually one of that enum's real
	/// options (otherwise it's left showing the dropdown's own first item
	/// purely cosmetically - never written unless the user actually
	/// touches it, see <see cref="BuildCommands"/>). arg0 of a Str-capable
	/// special goes through <see cref="RefreshScriptArgDisplay"/> instead -
	/// its own stored value may be a string, which <see cref="ElementSnapshot.Args"/>
	/// alone can't represent.
	/// </summary>
	private void RefreshArgValueDisplays()
	{
		for (var i = 0; i < 5; i++)
		{
			var row = _argRows[i];
			var info = _currentArgInfos[i];

			if (i == 0 && info.Str)
			{
				RefreshScriptArgDisplay(row);
				continue;
			}

			var values = _snapshots.Select(s => s.Args[i]).ToList();
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

	/// <summary>
	/// arg0's own version of <see cref="RefreshArgValueDisplays"/> - shared-
	/// or-blank across the selection, but checking the *string* snapshot
	/// first (a named-script reference) before falling back to the
	/// numeric one, since exactly one of the two is ever the real stored
	/// value for a given element.
	/// </summary>
	private void RefreshScriptArgDisplay(ArgRow row)
	{
		var strings = _snapshots.Select(s => s.Arg0String).ToList();
		var numbers = _snapshots.Select(s => s.Args[0]).ToList();
		var sharedString = strings.Count > 0 && strings.All(v => v == strings[0]) ? strings[0] : null;
		var sharedNumber = numbers.Count > 0 && numbers.All(v => v == numbers[0]) ? (long?)numbers[0] : null;

		row.NumberEdit.Text = "";

		if (sharedString != null)
		{
			SelectScriptEntry(row, entry => entry.IsNamedScript && entry.Number == sharedString);
		}
		else if (sharedNumber != null)
		{
			row.NumberEdit.Text = sharedNumber.Value.ToString(CultureInfo.InvariantCulture);
			SelectScriptEntry(row, entry => !entry.IsNamedScript && long.TryParse(entry.Number, out var n) && n == sharedNumber.Value);
		}

		RefreshScriptArgumentLabels();
	}

	/// <summary>Each arg0 dropdown item's metadata is the entry's own index into <see cref="_scriptCatalog"/> (a plain int - Godot's <see cref="Variant"/> has no native slot for an arbitrary C# struct), resolved back here rather than at every call site.</summary>
	private ScriptCatalogEntry ScriptEntryAt(OptionButton enumEdit, int itemIndex) => _scriptCatalog[(int)enumEdit.GetItemMetadata(itemIndex).AsInt64()];

	private void SelectScriptEntry(ArgRow row, Func<ScriptCatalogEntry, bool> predicate)
	{
		for (var idx = 0; idx < row.EnumEdit.ItemCount; idx++)
		{
			if (!predicate(ScriptEntryAt(row.EnumEdit, idx))) continue;
			row.EnumEdit.Selected = idx;
			return;
		}
	}

	/// <summary>
	/// Relabels arg1-arg4 (never arg0 itself) using whichever script is
	/// currently selected in arg0's own dropdown - UDB's own real
	/// <c>ScriptItem.GetArgumentsDescriptions</c>, ported via
	/// <see cref="ScriptArgFirstSlot"/>. Always resets to the action's own
	/// generic titles first, then overlays the selected script's own
	/// declared parameter names (if any) starting at that special's own
	/// first script-argument slot - so a script with fewer (or no)
	/// declared parameters, or nothing selected at all, correctly leaves
	/// the generic titles in place rather than a stale previous script's
	/// names.
	/// </summary>
	private void RefreshScriptArgumentLabels()
	{
		// Independent of the ScriptArgFirstSlot lookup below (81/ACS_Suspend
		// and 82/ACS_Terminate are deliberately absent from that table, but
		// both still take a navigable script as arg0) - always recomputed
		// first so the button's own visibility never gets stuck stale for
		// those two.
		_navigateButton.Visible = TryGetNavigableScript(out _);

		if (_currentAction == null || _currentArgInfos.Count == 0 || !_currentArgInfos[0].Str) return;
		if (!ScriptArgFirstSlot.TryGetValue(_currentAction.Number, out var firstSlot)) return;

		for (var i = firstSlot; i < 5; i++) _argRows[i].LabelButton.Text = _currentArgInfos[i].Title;

		var row = _argRows[0];
		if (!row.EnumEdit.Visible || row.EnumEdit.Selected < 0) return;

		var entry = ScriptEntryAt(row.EnumEdit, row.EnumEdit.Selected);
		for (var i = 0; i < entry.ParameterNames.Count && firstSlot + i < 5; i++)
		{
			_argRows[firstSlot + i].LabelButton.Text = entry.ParameterNames[i];
		}
	}

	/// <summary>
	/// Whether <see cref="_navigateButton"/> should currently do anything -
	/// just needs a real entry actually selected in arg0's own dropdown
	/// (whether it's declared in the map's own main SCRIPTS lump or
	/// reached via <c>#include</c> - <c>MainMenuBar.OnNavigateToScriptRequested</c>
	/// resolves either). Also the button's own click handler's guard,
	/// via <see cref="NavigateToScriptRequested"/>.
	/// </summary>
	private bool TryGetNavigableScript(out ScriptCatalogEntry entry)
	{
		entry = default;
		var row = _argRows[0];
		if (!_currentArgInfos[0].Str || !row.EnumEdit.Visible || row.EnumEdit.Selected < 0) return false;

		entry = ScriptEntryAt(row.EnumEdit, row.EnumEdit.Selected);
		return true;
	}

	/// <summary>
	/// Manually flips one argument row between its numeric and dropdown
	/// view - UDB's own real <c>ArgumentBox</c> is a single WinForms
	/// *editable* combo box, so typing any raw integer always works even
	/// for a dropdown-backed argument (an unrecognized typed value just
	/// becomes a synthesized one-off entry showing that raw number).
	/// Godot has no equivalent editable-combo control, so this reproduces
	/// the same end capability (type any exact number, even for a
	/// dropdown-backed arg) via two separate purpose-built controls and a
	/// manual switch between them instead of one hybrid control. A no-op
	/// when the argument has no dropdown to toggle to at all
	/// (<see cref="ArgRow.LabelButton"/> is disabled in that case, so this
	/// should never actually fire then, but the plain-numeric-only guard
	/// stays here too as a direct safety net).
	///
	/// A Str-capable arg0 shares this exact mechanism for a third
	/// purpose (see this class's own top-level remarks) rather than a
	/// fourth dedicated control: dropdown-to-numeric copies the
	/// selected script's own number as plain text (blank for a named
	/// script - there's no numeric form of a name to show); numeric-to-
	/// dropdown looks for a numbered script matching the typed text
	/// exactly (no "nearest" concept makes sense for scripts the way it
	/// does for a numeric enum range).
	/// </summary>
	private void ToggleArgView(int slot)
	{
		var info = _currentArgInfos[slot];
		var isScriptSlot = slot == 0 && info.Str;
		if (info.EnumOptions == null && !isScriptSlot) return;

		var row = _argRows[slot];
		if (row.EnumEdit.Visible)
		{
			if (isScriptSlot)
			{
				var selected = row.EnumEdit.Selected;
				var entry = selected >= 0 ? (ScriptCatalogEntry?)ScriptEntryAt(row.EnumEdit, selected) : null;
				row.NumberEdit.Text = entry is { IsNamedScript: false } ? entry.Value.Number : "";
			}
			else
			{
				var selected = row.EnumEdit.Selected;
				row.NumberEdit.Text = selected >= 0 ? row.EnumEdit.GetItemMetadata(selected).AsInt64().ToString(CultureInfo.InvariantCulture) : "";
			}

			row.LabelButton.Text = info.Title;
			row.NumberEdit.Visible = true;
			row.EnumEdit.Visible = false;
		}
		else
		{
			if (isScriptSlot)
			{
				var typedNumber = row.NumberEdit.Text.Trim();
				SelectScriptEntry(row, entry => !entry.IsNamedScript && entry.Number == typedNumber);
				row.LabelButton.Text = info.TitleStr ?? info.Title;
			}
			else
			{
				var typed = NumericFieldExpression.Resolve(row.NumberEdit.Text, 0.0);
				if (typed.HasValue)
				{
					var nearestIndex = FindNearestEnumIndex(row.EnumEdit, (long)Math.Round(typed.Value));
					if (nearestIndex >= 0) row.EnumEdit.Selected = nearestIndex;
				}

				row.LabelButton.Text = info.Title;
			}

			row.EnumEdit.Visible = true;
			row.NumberEdit.Visible = false;
		}

		if (isScriptSlot) RefreshScriptArgumentLabels();
	}

	private static int FindNearestEnumIndex(OptionButton enumEdit, long value)
	{
		var bestIndex = -1;
		var bestDistance = long.MaxValue;

		for (var idx = 0; idx < enumEdit.ItemCount; idx++)
		{
			var distance = Math.Abs(enumEdit.GetItemMetadata(idx).AsInt64() - value);
			if (distance >= bestDistance) continue;

			bestDistance = distance;
			bestIndex = idx;
		}

		return bestIndex;
	}

	/// <summary>
	/// Setting <see cref="LineEdit.Text"/> directly doesn't raise
	/// <c>TextChanged</c> (the same plain Godot behavior already relied on
	/// elsewhere, e.g. <see cref="SectorEditDialog.BrowseTexture"/>) - so
	/// the callback also calls <see cref="UpdateActionUi"/> itself,
	/// exactly reproducing what typing the action number by hand would
	/// have done (relabeling the 5 argument slots, in particular).
	/// </summary>
	private void BrowseAction()
	{
		_actionBrowserDialog ??= CreateActionBrowserDialog();
		_actionBrowserDialog.Browse(_gameConfiguration, _actionEdit.Text, number =>
		{
			_actionEdit.Text = number;
			UpdateActionUi();
		});
	}

	private LinedefActionBrowserDialog CreateActionBrowserDialog()
	{
		var dialog = GD.Load<PackedScene>("res://Scenes/UI/LinedefActionBrowserDialog.tscn").Instantiate<LinedefActionBrowserDialog>();
		AddChild(dialog);
		return dialog;
	}

	/// <summary>
	/// Never live-applied (no visual effect to preview in either host
	/// dialog) - resolved here, for the first time, against each
	/// element's original <c>Fields</c> value, matching every other
	/// OK-only field in this project's dialogs. A mixed/blank action
	/// field resolves to each element's own original action (never
	/// written), and since <see cref="_currentArgInfos"/> then falls back
	/// to all-generic-unused slots in that case too, no argument write is
	/// attempted either - a mixed selection's own individual action
	/// numbers (and whatever arguments belong to them) are simply left
	/// alone. arg0 of a Str-capable special goes through
	/// <see cref="BuildArg0Command"/> instead, since it's the one slot
	/// that can legitimately write either an integer or a string field.
	/// </summary>
	public IReadOnlyList<ICommand> BuildCommands()
	{
		var commands = new List<ICommand>();

		for (var e = 0; e < _elements.Count; e++)
		{
			var fields = _elements[e];
			var snapshot = _snapshots[e];

			var newAction = NumericFieldExpression.ResolveInteger(_actionEdit.Text, snapshot.ActionNumber) ?? snapshot.ActionNumber;
			if (newAction != snapshot.ActionNumber)
			{
				commands.Add(new SetFieldCommand(fields, "special", newAction == 0 ? null : new UniValue(UniversalType.Integer, newAction)));
			}

			if (_currentArgInfos[0].Str)
			{
				var arg0Command = BuildArg0Command(fields, snapshot);
				if (arg0Command != null) commands.Add(arg0Command);
			}

			for (var i = _currentArgInfos[0].Str ? 1 : 0; i < 5; i++)
			{
				var row = _argRows[i];
				long newValue;

				// Reads whichever control is *currently visible*, not whether the
				// argument is statically enum-capable - a manual toggle (see
				// ToggleArgView) can put an enum-backed argument into number view,
				// and a typed custom value there must not be silently ignored.
				if (row.EnumEdit.Visible)
				{
					if (!_touchedArgs.Contains(i)) continue;
					newValue = row.EnumEdit.GetItemMetadata(row.EnumEdit.Selected).AsInt64();
				}
				else
				{
					newValue = NumericFieldExpression.ResolveInteger(row.NumberEdit.Text, snapshot.Args[i]) ?? snapshot.Args[i];
				}

				if (newValue == snapshot.Args[i]) continue;
				commands.Add(new SetFieldCommand(fields, $"arg{i}", newValue == 0 ? null : new UniValue(UniversalType.Integer, newValue)));
			}
		}

		return commands;
	}

	/// <summary>
	/// arg0's own write path: a dropdown selection writes a string field
	/// for a named script or an integer one for a numbered script (never
	/// touched if the row itself was never touched - same "don't
	/// overwrite what the user didn't actually interact with" rule the
	/// generic enum path already follows); the numeric view writes a
	/// plain integer the same way every other numeric slot already does.
	/// Returns null when nothing actually needs to change.
	/// </summary>
	private ICommand BuildArg0Command(UniFields fields, ElementSnapshot snapshot)
	{
		var row = _argRows[0];

		if (row.EnumEdit.Visible)
		{
			if (!_touchedArgs.Contains(0) || row.EnumEdit.Selected < 0) return null;

			var entry = ScriptEntryAt(row.EnumEdit, row.EnumEdit.Selected);
			if (entry.IsNamedScript)
			{
				return entry.Number == snapshot.Arg0String
					? null
					: new SetFieldCommand(fields, "arg0", new UniValue(UniversalType.String, entry.Number));
			}

			var newNumber = long.Parse(entry.Number, CultureInfo.InvariantCulture);
			return newNumber == snapshot.Args[0] && snapshot.Arg0String == null
				? null
				: new SetFieldCommand(fields, "arg0", newNumber == 0 ? null : new UniValue(UniversalType.Integer, newNumber));
		}

		// Blank/unresolvable text is always "no change" here, even for an
		// arg0 that originally held a *number* (unlike every other slot,
		// where falling back to the snapshot value alone is already
		// correct) - a named-script original shows this control blank
		// (there's no numeric form of a name to pre-fill), and
		// snapshot.Args[0] is only ever a meaningless 0 placeholder in
		// that case, not the real value to fall back to. Toggling to this
		// view (or toggling back and forth) without actually typing a new
		// number must never flip a genuine string reference to 0.
		if (NumericFieldExpression.Resolve(row.NumberEdit.Text, 0) == null) return null;

		var newValue = NumericFieldExpression.ResolveInteger(row.NumberEdit.Text, snapshot.Args[0]) ?? snapshot.Args[0];
		return newValue == snapshot.Args[0] && snapshot.Arg0String == null
			? null
			: new SetFieldCommand(fields, "arg0", newValue == 0 ? null : new UniValue(UniversalType.Integer, newValue));
	}

	private static string SharedOrBlank(IEnumerable<double> values)
	{
		var list = values.ToList();
		return list.Count > 0 && list.All(v => v == list[0]) ? FormatNumber(list[0]) : "";
	}

	private static string FormatNumber(double value) =>
		value == Math.Floor(value) ? ((long)value).ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
}
