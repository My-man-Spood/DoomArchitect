using System;
using System.Globalization;
using DoomArchitect.Core.Editing;
using Godot;

/// <summary>
/// A numeric text field with up/down nudge buttons - ports UDB's real
/// <c>ButtonsNumericTextbox</c> (verified against its actual source: it's
/// exactly this shape, a plain textbox plus a spinner), used by property
/// dialogs for height/brightness/gravity-style fields. Reusable across
/// dialogs the same way <c>ResourceListEditor</c> is - a scene+script
/// composite embedded via <c>instance=</c>.
///
/// Deliberately not Godot's built-in <see cref="SpinBox"/>: that control
/// owns and validates its own numeric value directly, which can't
/// represent this project's own field grammar (blank = "each element's own
/// value," <c>++N</c>/<c>--N</c>/<c>*N</c>/<c>/N</c> = relative -
/// see <see cref="NumericFieldExpression"/>). This wraps a plain
/// <see cref="LineEdit"/> instead and only ever nudges its *text*, so the
/// same real-time-apply pipeline callers already wire to a plain
/// <see cref="LineEdit"/>'s <c>TextChanged</c> keeps working unchanged.
///
/// <see cref="Text"/>'s setter is intentionally silent (no
/// <see cref="TextChanged"/>) to match <see cref="LineEdit.Text"/>'s own
/// real behavior - callers populating a field programmatically (e.g. a
/// dialog's initial <c>Setup</c>) already rely on that not firing a live
/// apply.
/// </summary>
public partial class StepperLineEdit : HBoxContainer
{
	/// <summary>Matches UDB's real <c>ButtonStep</c> - the plain nudge amount with no modifier held.</summary>
	[Export] public float Step { get; set; } = 1f;

	/// <summary>Matches UDB's real <c>ButtonStepBig</c> - the nudge amount while Shift is held.</summary>
	[Export] public float StepBig { get; set; } = 1f;

	/// <summary>Matches UDB's real <c>ButtonStepSmall</c> - the nudge amount while Ctrl is held.</summary>
	[Export] public float StepSmall { get; set; } = 1f;

	/// <summary>Whether a nudge result is written back as a decimal (gravity) or rounded to a whole number (heights/brightness) - matches UDB's real per-field <c>AllowDecimal</c>.</summary>
	[Export] public bool AllowDecimal { get; set; }

	private LineEdit _lineEdit;
	private Button _upButton;
	private Button _downButton;

	/// <summary>Fires on user typing (proxied from the inner <see cref="LineEdit"/>) and on a spin-button nudge - callers only ever need this one event, never the inner <see cref="LineEdit"/>'s own.</summary>
	public event Action<string> TextChanged;

	public string Text
	{
		get => _lineEdit.Text;
		set => _lineEdit.Text = value;
	}

	public override void _Ready()
	{
		_lineEdit = GetNode<LineEdit>("LineEdit");
		_upButton = GetNode<Button>("Spinner/Up");
		_downButton = GetNode<Button>("Spinner/Down");

		_lineEdit.TextChanged += text =>
		{
			UpdateSpinnerEnabled();
			TextChanged?.Invoke(text);
		};
		_upButton.Pressed += () => Nudge(+1);
		_downButton.Pressed += () => Nudge(-1);

		_lineEdit.Resized += MatchSpinnerHeightToLineEdit;
	}

	/// <summary>
	/// Keeps the two spin buttons' combined height matching the
	/// <see cref="LineEdit"/> beside them exactly, whatever that happens to
	/// be - safe to do now that both buttons' own theme styles carry zero
	/// content margin (see <c>StepperLineEdit.tscn</c>'s
	/// <c>StyleBoxFlat_spinner_*</c> resources), so this is the only thing
	/// left influencing their size; reacting to the field's own
	/// <see cref="Control.Resized"/> means a theme/font change elsewhere
	/// keeps this correct automatically instead of a hand-tuned pixel
	/// guess drifting out of sync - the same pattern
	/// <c>SectorEditDialog.KeepSquare</c> already uses for texture preview
	/// thumbnails.
	/// </summary>
	private void MatchSpinnerHeightToLineEdit()
	{
		var halfHeight = _lineEdit.Size.Y / 2f;
		_upButton.CustomMinimumSize = new Vector2(_upButton.CustomMinimumSize.X, halfHeight);
		_downButton.CustomMinimumSize = new Vector2(_downButton.CustomMinimumSize.X, halfHeight);
	}

	/// <summary>
	/// Reads the field's current text as though its "original" were 0 (an
	/// indeterminate/mixed field has no single original to nudge from, so
	/// this matches UDB's own real fallback - <c>ButtonsNumericTextbox</c>
	/// calls <c>textbox.GetResult(0)</c> the same way), applies one step
	/// scaled by whichever modifier is held, and writes the plain absolute
	/// result back as text - indistinguishable to <see cref="TextChanged"/>
	/// subscribers from the user having typed that number directly.
	/// </summary>
	private void Nudge(int direction)
	{
		if (NumericFieldExpression.IsRelativeExpression(_lineEdit.Text)) return;

		var current = NumericFieldExpression.Resolve(_lineEdit.Text, 0) ?? 0;
		var ctrl = Input.IsKeyPressed(Key.Ctrl);
		var shift = Input.IsKeyPressed(Key.Shift);
		var amount = ctrl ? StepSmall : shift ? StepBig : Step;
		var result = current + direction * amount;

		_lineEdit.Text = AllowDecimal
			? result.ToString(CultureInfo.InvariantCulture)
			: ((long)Math.Round(result)).ToString(CultureInfo.InvariantCulture);

		UpdateSpinnerEnabled();
		TextChanged?.Invoke(_lineEdit.Text);
	}

	private void UpdateSpinnerEnabled()
	{
		var isRelative = NumericFieldExpression.IsRelativeExpression(_lineEdit.Text);
		_upButton.Disabled = isRelative;
		_downButton.Disabled = isRelative;
	}
}
