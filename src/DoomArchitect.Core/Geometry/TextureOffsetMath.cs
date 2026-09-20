namespace DoomArchitect.Core.Geometry;

/// <summary>
/// The real UDB texture-offset-nudge math, ported directly from
/// <c>BaseVisualGeometrySidedef.GetRoundedTextureOffset</c> - shifts a
/// per-part UDMF offset field (<c>offsetx_top</c>/<c>offsety_mid</c> etc,
/// see <see cref="LinedefWallBuilder.GetPartTransform"/>'s own remarks) by
/// a keyboard-nudge delta and wraps the result within the texture's own
/// real pixel size, so repeatedly nudging never drifts into a ridiculous
/// value far outside any texture the image could ever actually show.
/// </summary>
public static class TextureOffsetMath
{
    /// <param name="oldValue">The field's current value.</param>
    /// <param name="delta">
    /// The nudge amount (already signed for direction - negative for
    /// left/down, positive for right/up). A delta of exactly <c>0</c>, or
    /// one that's an exact multiple of <paramref name="textureSize"/>
    /// (so wrapping alone would leave the value unchanged), is treated as
    /// a genuine no-op and returns <paramref name="oldValue"/> verbatim -
    /// matches UDB's own real short-circuit exactly.
    /// </param>
    /// <param name="textureSize">
    /// The texture's own real pixel width/height. A value <c>&lt;= 0</c>
    /// (texture not resolvable, e.g. "-" or an unknown name) skips
    /// wrapping entirely - the new value is just <c>oldValue + delta</c>,
    /// matching UDB's own real behavior for an unloaded texture.
    /// </param>
    public static double Nudge(double oldValue, double delta, double textureSize)
    {
        if (delta == 0 || (textureSize > 0 && delta % textureSize == 0)) return oldValue;

        var result = Math.Round(oldValue + delta);
        if (textureSize > 0) result %= textureSize;

        // Wrapping can land exactly back on the old value (e.g. old=0,
        // delta=8, textureSize=8) even though the nudge was real - bump
        // by one pixel in the nudge's own direction so the key press is
        // never a silent no-op, matching UDB's own real "why?" guard.
        if (result == oldValue) result += delta < 0 ? -1 : 1;

        return result;
    }
}
