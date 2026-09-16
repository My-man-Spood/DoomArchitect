using System;
using DoomArchitect.Rendering;
using Godot;

/// <summary>
/// Ports UDB's real <c>ImageSelectorControl</c> shape exactly: a preview
/// image with a small floating corner label showing the decoded texture's
/// own real pixel dimensions (UDB's own real <c>labelSize</c>, verified
/// directly against <c>ImageSelectorControl.cs</c>/<c>.Designer.cs</c> -
/// top-left corner, dark semi-transparent background, small light text,
/// "WIDTHxHEIGHT"), stacked vertically above a plain texture-name field -
/// not the label+small-preview+field horizontal row this project
/// originally used for every texture picker, which UDB's own real control
/// never actually has (an earlier pass modeled it that way from a
/// screenshot rather than the real control's own real layout, corrected
/// here since it also made the preview uncomfortably small - freed from
/// sharing a row with a caption label, the preview can be shown much
/// bigger). Reused identically by every texture-picking field in this
/// project (Sector's Floor/Ceiling, Linedef's Front/Back Upper/Middle/
/// Lower) - previously duplicated ad hoc per dialog.
///
/// Deliberately does not own texture *decoding* - a host dialog still
/// decides wall vs. flat (<see cref="TextureIconCache.GetOrDecodeWallIcon"/>
/// vs <see cref="TextureIconCache.GetOrDecodeFlatIcon"/>) and hands the
/// already-decoded <see cref="Texture2D"/> (or null for blank/mixed/not-
/// yet-decoded) to <see cref="SetPreviewTexture"/> - this control only
/// ever renders whatever it's given and reads that texture's own real
/// <see cref="Texture2D.GetWidth"/>/<see cref="Texture2D.GetHeight"/> for
/// the size label (never a separate lookup), so it has no opinion on
/// where textures come from and needs no reference to a
/// <see cref="TextureIconCache"/> at all.
///
/// UDB's real control also has a short/long-name toggle button
/// (<c>togglefullname</c>) - deliberately not built here, matching this
/// project's existing "no long-texture-name support" scope (texture names
/// are always typed/stored as-is, no character-casing/truncation rules
/// applied anywhere in this project yet).
/// </summary>
public partial class TexturePreviewEdit : VBoxContainer
{
	private TextureButton _preview;
	private PanelContainer _sizeBox;
	private Label _sizeLabel;
	private LineEdit _textEdit;

	/// <summary>Fires on user typing (proxied from the inner <see cref="LineEdit"/>) - matches <see cref="StepperLineEdit.TextChanged"/>'s own established shape.</summary>
	public event Action<string> TextChanged;

	/// <summary>Fires when the preview image itself is clicked - the host wires this to open <see cref="TextureBrowserDialog"/>, matching UDB's real "click the preview to browse" gesture.</summary>
	public event Action PreviewPressed;

	/// <summary><see cref="LineEdit.Text"/>'s setter is silent (no <see cref="TextChanged"/>), matching <see cref="StepperLineEdit.Text"/>'s own established real Godot behavior.</summary>
	public string Text
	{
		get => _textEdit.Text;
		set => _textEdit.Text = value;
	}

	public bool Editable
	{
		get => _textEdit.Editable;
		set
		{
			_textEdit.Editable = value;
			_preview.Disabled = !value;
		}
	}

	public override void _Ready()
	{
		_preview = GetNode<TextureButton>("Preview");
		_sizeBox = GetNode<PanelContainer>("Preview/SizeBox");
		_sizeLabel = GetNode<Label>("Preview/SizeBox/SizeLabel");
		_textEdit = GetNode<LineEdit>("TextureEdit");

		_preview.Resized += KeepSquare;
		_preview.MouseEntered += () => _preview.Modulate = new Color(1.3f, 1.3f, 1.3f);
		_preview.MouseExited += () => _preview.Modulate = Colors.White;
		_preview.Pressed += () => PreviewPressed?.Invoke();
		_textEdit.TextChanged += text => TextChanged?.Invoke(text);
	}

	/// <summary>
	/// Renders the given already-decoded texture (or a shared placeholder
	/// for null, matching every other texture preview in this project) and
	/// updates the corner size label from that texture's own real pixel
	/// dimensions - never guessed or looked up separately, exactly matching
	/// UDB's real <c>DisplayImageSize</c> reading the actual loaded image.
	/// <see cref="PanelContainer"/> has no parent <see cref="Container"/>
	/// here (its parent, <see cref="_preview"/>, is a plain
	/// <see cref="TextureButton"/>) to auto-size it to its own content, so
	/// its size is set explicitly from its own real computed minimum size
	/// every time the shown text can change length (e.g. "8x8" vs.
	/// "256x256").
	/// </summary>
	public void SetPreviewTexture(Texture2D texture)
	{
		_preview.TextureNormal = texture ?? PlaceholderIcon.Instance;
		_sizeBox.Visible = texture != null;

		if (texture == null) return;

		_sizeLabel.Text = $"{texture.GetWidth()}x{texture.GetHeight()}";
		_sizeBox.Size = _sizeBox.GetCombinedMinimumSize();
	}

	/// <summary>Identical reasoning to <see cref="SectorEditDialog.KeepSquare"/> - Godot has no built-in "stay square while filling available width."</summary>
	private void KeepSquare()
	{
		var width = _preview.Size.X;
		if (width > 0 && !Mathf.IsEqualApprox(_preview.CustomMinimumSize.Y, width))
		{
			_preview.CustomMinimumSize = new Vector2(_preview.CustomMinimumSize.X, width);
		}
	}
}
