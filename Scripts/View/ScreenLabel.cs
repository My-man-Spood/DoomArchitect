using Godot;

/// <summary>
/// The screen-space-anchored, zoom-constant text label technique
/// <c>DrawOverlayHandler.DrawLengthLabel</c> established for Draw mode's
/// own length/angle readout, extracted so <see cref="TagIndicatorOverlayHandler"/>'s
/// sector tag-number label can reuse it exactly rather than duplicating
/// it - a background panel behind the text (a DoomArchitect-specific
/// legibility choice, not a UDB behavior - UDB draws its own text labels
/// against its editor theme's own background, which this project's
/// overlay has no equivalent concept of) drawn at a caller-supplied
/// baseline, so callers stay free to compute wherever that baseline
/// should sit (centered on a segment's own midpoint, a sector's own bbox
/// center, etc.) using this project's own established "derive the offset
/// from already-projected screen points, never from a map-space vector
/// scaled afterward" rule.
/// </summary>
public static class ScreenLabel
{
	private const int FontSize = 13;
	private const float Padding = 3f;

	private static readonly Color BackgroundColor = new(0f, 0f, 0f, 0.6f);

	/// <summary>The text's own screen-pixel size at <see cref="FontSize"/> - callers use this to compute a baseline that centers the text however they need to (horizontally, vertically, or both).</summary>
	public static Vector2 Measure(string text) => ThemeDB.FallbackFont.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize);

	/// <param name="target">Where to draw.</param>
	/// <param name="baseline">The text's own baseline origin (Godot's real <see cref="CanvasItem.DrawString(Font,Vector2,string,HorizontalAlignment,float,int,Color?,int,Color?,int,int,TextServer.JustificationFlag,TextServer.Direction,TextServer.Orientation)"/> convention - roughly the bottom-left of the glyphs).</param>
	/// <param name="text">Already-final display text - callers own their own zoom-dependent abbreviation, if any.</param>
	/// <param name="textColor">Foreground color - the background panel is always the same fixed, semi-transparent black regardless.</param>
	public static void Draw(CanvasItem target, Vector2 baseline, string text, Color textColor)
	{
		var font = ThemeDB.FallbackFont;
		var textSize = Measure(text);

		target.DrawRect(
			new Rect2(baseline - new Vector2(Padding, textSize.Y + Padding), textSize + new Vector2(Padding, Padding) * 2f),
			BackgroundColor);
		target.DrawString(font, baseline, text, HorizontalAlignment.Left, -1, FontSize, textColor);
	}
}
