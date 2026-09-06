namespace DoomArchitect.Interop;

public static class BrightnessConversions
{
    /// <summary>
    /// A 0-255 brightness value (see <c>Core.Lighting.SectorBrightness</c>)
    /// as a flat grayscale multiplier - matches UDB's own conversion,
    /// which is a plain per-channel multiply with no palette/colormap
    /// tinting involved.
    ///
    /// <c>brightness / 255</c> is a *perceptual* value - how bright this
    /// should actually look, the same way Doom's original palette colors
    /// were authored to look correct directly on a CRT with no extra
    /// color-space math involved. Godot's renderer, though, works in
    /// linear light internally and doesn't gamma-decode vertex colors on
    /// its own (unlike an imported texture, which Godot does treat as
    /// sRGB automatically) - so a raw perceptual fraction used as a
    /// linear multiplier gets gamma-*encoded* a second time on the way to
    /// the screen, and gamma encoding disproportionately lifts low
    /// values (a linear 0.19 displays at roughly 0.47, not 0.19). Caught
    /// after dark sectors rendered barely darker than normal ones instead
    /// of "suffocatingly dark". <see cref="Godot.Color.SrgbToLinear"/>
    /// converts the perceptual value into the linear one Godot actually
    /// needs, so the two color-space conversions cancel out and the
    /// on-screen result matches the intended brightness.
    /// </summary>
    public static Godot.Color ToBrightnessColor(this int brightness)
    {
        var value = brightness / 255f;
        return new Godot.Color(value, value, value).SrgbToLinear();
    }
}
