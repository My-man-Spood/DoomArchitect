namespace DoomArchitect.Core.Editing;

/// <summary>
/// Resolves one numeric property-dialog field's text against a given
/// "original" value - a real GZDoom/UDB-authoring convention this
/// project's own property dialogs port faithfully (verified against UDB's
/// actual <c>NumericTextbox.GetResultFloat</c>, not guessed): blank text
/// means "leave the original alone" (the multi-select "mixed values"
/// sentinel - see <c>Sector</c> property dialogs), a plain number is an
/// absolute replacement, and a doubled-sign or single <c>*</c>/<c>/</c>
/// prefix means "relative to the original" - so editing several elements
/// that started with different values at once can still raise/lower/scale
/// each one by the same amount without collapsing them to one shared
/// value. A single leading <c>+</c>/<c>-</c> is never a relative operator
/// on its own (only doubled <c>++</c>/<c>--</c> are) - so a plain signed
/// number like <c>-50</c> parses as the absolute value -50, with no
/// ambiguity against negative heights. The triple-prefix <c>+++</c>/<c>---</c>
/// "step per element" variant UDB also supports isn't ported - a niche
/// batch-distribute feature, not the core "edit relative to each element's
/// own value" behavior this exists for.
/// </summary>
public static class NumericFieldExpression
{
    public static double? Resolve(string text, double original)
    {
        text = text.Trim();
        if (text.Length == 0) return null;

        if (text.StartsWith("++") && double.TryParse(text[2..], out var add)) return original + add;
        if (text.StartsWith("--") && double.TryParse(text[2..], out var subtract)) return original - subtract;
        if (text.StartsWith("*") && double.TryParse(text[1..], out var multiplier)) return original * multiplier;
        if (text.StartsWith("/") && double.TryParse(text[1..], out var divisor)) return divisor == 0 ? original : original / divisor;

        // Unparsable non-blank text (e.g. mid-edit) is treated as "no
        // change" rather than thrown - a property dialog shouldn't corrupt
        // data because of a transient invalid keystroke.
        return double.TryParse(text, out var absolute) ? absolute : null;
    }

    public static long? ResolveInteger(string text, long original) =>
        Resolve(text, original) is { } result ? (long)Math.Round(result) : null;
}
