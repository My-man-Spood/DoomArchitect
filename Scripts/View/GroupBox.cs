using Godot;

/// <summary>
/// A reusable titled section box: a 1px border with a gap in its top edge
/// where <see cref="Title"/> sits - the classic GroupBox/fieldset look
/// UDB's own real dialogs use for grouping a handful of related property-
/// dialog fields under one labeled border. Replaces this project's earlier
/// "bold Label above plain content, no border at all" stand-in used across
/// the Sector dialog's Properties tab (Flags/Heights/Sector damage/
/// Effects/Identification) - a deliberate, flagged rendering
/// simplification at the time, now addressed with one real shared control
/// instead of one-off styling per dialog.
///
/// A genuine <see cref="Container"/> subclass (not a plain
/// <see cref="Control"/> hand-positioning children from signal callbacks) -
/// <see cref="_GetMinimumSize"/> and the <see cref="NotificationSortChildren"/>
/// handler (using <see cref="Container.FitChildInRect"/>) are the actual
/// framework contract custom containers implement, the same one every
/// stock container (<see cref="VBoxContainer"/>, <see cref="MarginContainer"/>,
/// etc.) implements in engine code. Add section content to
/// <see cref="Content"/> (a plain <see cref="VBoxContainer"/>) from code,
/// or from the Godot editor via "Editable Children" on an instanced
/// <c>GroupBox.tscn</c>.
///
/// <see cref="ExternalMargin"/> is the gap between this control's own
/// bounds and the border line itself (breathing room when several of
/// these are stacked with zero container separation); <see cref="InternalMargin"/>
/// is the gap between the border line and <see cref="Content"/>.
///
/// Marked <see cref="ToolAttribute"/> so all of this - sizing, sorting,
/// and drawing - also runs live in the Godot editor, not just when the
/// dialog actually opens at runtime: no script's virtual overrides run
/// while merely editing a scene otherwise, regardless of whether the
/// script extends <see cref="Control"/> or <see cref="Container"/> - that
/// rule isn't specific to either base class.
/// </summary>
[Tool]
public partial class GroupBox : Container
{
	[Export]
	public string Title
	{
		get => _title;
		set
		{
			_title = value;
			if (_titleLabel != null) _titleLabel.Text = value;
			UpdateMinimumSize();
			QueueSort();
			QueueRedraw();
		}
	}

	private string _title = "";

	[Export]
	public int ExternalMargin
	{
		get => _externalMargin;
		set { _externalMargin = value; UpdateMinimumSize(); QueueSort(); QueueRedraw(); }
	}

	private int _externalMargin = 4;

	[Export]
	public int InternalMargin
	{
		get => _internalMargin;
		set { _internalMargin = value; UpdateMinimumSize(); QueueSort(); QueueRedraw(); }
	}

	private int _internalMargin = 8;

	[Export]
	public int TitleIndent
	{
		get => _titleIndent;
		set { _titleIndent = value; QueueSort(); QueueRedraw(); }
	}

	private int _titleIndent = 10;

	private static readonly Color BorderColor = new(1, 1, 1, 0.78431374f);

	private Label _titleLabel;

	public VBoxContainer Content { get; private set; }

	public override void _Ready()
	{
		_titleLabel = GetNode<Label>("TitleLabel");
		Content = GetNode<VBoxContainer>("Content");
		_titleLabel.Text = _title;
	}

	/// <summary>
	/// The actual framework hook a custom <see cref="Container"/> reports
	/// its own size through - Godot calls this itself (in the editor and
	/// at runtime alike) whenever it needs to know how big this control
	/// wants to be, the same way it calls <see cref="VBoxContainer"/>'s own
	/// equivalent internally.
	/// </summary>
	public override Vector2 _GetMinimumSize()
	{
		if (_titleLabel == null || Content == null) return Vector2.Zero;

		var titleSize = _titleLabel.GetMinimumSize();
		var contentSize = Content.GetCombinedMinimumSize();
		var width = Mathf.Max(
			titleSize.X + 2 * (ExternalMargin + TitleIndent),
			contentSize.X + 2 * (ExternalMargin + InternalMargin));
		var height = ExternalMargin + titleSize.Y + InternalMargin * 2 + contentSize.Y + ExternalMargin;

		return new Vector2(width, height);
	}

	/// <summary>
	/// The other half of the <see cref="Container"/> contract: whenever
	/// Godot actually lays this control out (its own size changed, a
	/// child's minimum size changed, etc. - <see cref="Container"/>'s base
	/// implementation already listens for all of that and requests this
	/// notification, no manual signal wiring needed), place each child via
	/// <see cref="Container.FitChildInRect"/> - the title indented from the
	/// top-left, <see cref="Content"/> filling the remaining rect below the
	/// border line, inset by <see cref="InternalMargin"/>.
	/// </summary>
	public override void _Notification(int what)
	{
		if (what != NotificationSortChildren || _titleLabel == null) return;

		var titleSize = _titleLabel.GetMinimumSize();
		FitChildInRect(_titleLabel, new Rect2(ExternalMargin + TitleIndent, ExternalMargin, titleSize.X, titleSize.Y));

		var contentTop = ExternalMargin + titleSize.Y + InternalMargin;
		var contentRect = new Rect2(
			ExternalMargin + InternalMargin,
			contentTop,
			Size.X - 2 * (ExternalMargin + InternalMargin),
			Size.Y - contentTop - ExternalMargin - InternalMargin);
		FitChildInRect(Content, contentRect);

		QueueRedraw();
	}

	/// <summary>
	/// The border's top edge passes through the vertical middle of the
	/// title text (straddling it, like an HTML <c>fieldset</c>/<c>legend</c>)
	/// rather than sitting above or below it - drawn as two separate
	/// segments with a gap around the title's measured width instead of
	/// one continuous line, which is what actually reads as "interrupted
	/// by the title" instead of a line the title merely overlaps.
	/// </summary>
	public override void _Draw()
	{
		if (_titleLabel == null) return;

		var titleSize = _titleLabel.GetMinimumSize();
		const float gapPadding = 6f;

		var top = ExternalMargin + titleSize.Y / 2f;
		var left = ExternalMargin;
		var right = Size.X - ExternalMargin;
		var bottom = Size.Y - ExternalMargin;
		var gapStart = Mathf.Max(left, ExternalMargin + TitleIndent - gapPadding);
		var gapEnd = Mathf.Min(right, ExternalMargin + TitleIndent + titleSize.X + gapPadding);

		DrawLine(new Vector2(left, top), new Vector2(gapStart, top), BorderColor);
		DrawLine(new Vector2(gapEnd, top), new Vector2(right, top), BorderColor);
		DrawLine(new Vector2(left, top), new Vector2(left, bottom), BorderColor);
		DrawLine(new Vector2(right, top), new Vector2(right, bottom), BorderColor);
		DrawLine(new Vector2(left, bottom), new Vector2(right, bottom), BorderColor);
	}
}
