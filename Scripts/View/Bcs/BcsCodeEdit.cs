using Godot;

/// <summary>
/// A plain <see cref="CodeEdit"/>'s tooltip is always flat, single-color
/// text. Overrides Godot's own "let a Control provide a custom tooltip"
/// hook (confirmed from <c>scene/main/viewport.cpp</c>'s
/// <c>_gui_show_tooltip_at</c>: it calls <c>get_tooltip(pos)</c> for the
/// text - which <c>TextEdit</c> already overrides to return whatever
/// <see cref="CodeEdit.SetTooltipRequestFunc"/>'s callback produces - then
/// <c>make_custom_tooltip(text)</c> to let a subclass replace the default
/// plain-text popup with a real <see cref="Control"/>, which Godot itself
/// then wraps in its own themed tooltip panel) to show that text in a
/// BBCode-enabled <see cref="RichTextLabel"/> instead of a plain
/// <see cref="Label"/>. <paramref name="forText"/> arrives already fully
/// formatted - <c>ScriptDocument.GetBcsTooltip</c> is the one place that
/// knows whether it's showing a colored signature/type
/// (<see cref="BcsBbcodeFormatter.ColorizeCode"/>) or an escaped plain
/// diagnostic message (<see cref="BcsBbcodeFormatter.EscapePlainText"/>),
/// so this control only needs to display it, not decide how.
///
/// Only used by <c>.bcs</c>/<c>.acs</c> documents - plain-text files never
/// set a tooltip request function at all, so <paramref name="forText"/>
/// is always empty for them and this returns <c>null</c> (falls back to
/// Godot's own default, which is a no-op here since there's nothing to
/// show).
/// </summary>
public partial class BcsCodeEdit : CodeEdit
{
    public override GodotObject _MakeCustomTooltip(string forText)
    {
        if (string.IsNullOrEmpty(forText)) return null;

        return new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            // Confirmed from Godot's own rich_text_label.cpp
            // (get_minimum_size): with autowrap at its default (anything
            // other than Off) and no explicit max width set, FitContent's
            // computed width is discarded entirely in favor of a hardcoded
            // 1px - real, not theoretical: this is exactly what collapsed
            // the tooltip down to one letter per line, wrapped tall,
            // confirmed live. Off is what lets FitContent size to the
            // text's own natural (unwrapped) width instead.
            AutowrapMode = TextServer.AutowrapMode.Off,
            ScrollActive = false,
            Text = forText,
        };
    }
}
