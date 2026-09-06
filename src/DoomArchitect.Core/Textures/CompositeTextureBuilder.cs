namespace DoomArchitect.Core.Textures;

/// <summary>
/// Composites a <see cref="CompositeTextureDefinition"/>'s patches into
/// one final <see cref="PixelImage"/> - a verbatim port of UDB's
/// <c>TextureImage.LocalLoadImage</c>/<c>DrawToPixelData</c>/
/// <c>BitmapIsMasked</c> (<c>Source/Core/Data/TextureImage.cs</c>),
/// including its vanilla negative-patch-offset rendering-bug emulation
/// (see https://doomwiki.org/wiki/Vertical_offsets_are_ignored_in_texture_patches).
///
/// UDB gates that emulation behind two game-configuration compatibility
/// flags (<c>FixNegativePatchOffsets</c>/<c>FixMaskedPatchOffsets</c>)
/// that both default to <c>false</c> (bug active) for vanilla game
/// configs. There's no game-configuration system here yet to make them
/// configurable, so both are hardcoded to their vanilla defaults - the
/// conditions below are UDB's literal logic with those two flags already
/// substituted in as constants.
/// </summary>
public static class CompositeTextureBuilder
{
    public static PixelImage? Build(
        CompositeTextureDefinition definition, Func<string, PixelImage?> resolvePatch, List<string> warnings)
    {
        var width = definition.Width;
        var height = definition.Height;

        var resolved = new List<(PatchPlacement Patch, PixelImage Image)>();
        var missing = 0;

        foreach (var patch in definition.Patches)
        {
            var image = resolvePatch(patch.PatchName);
            if (image == null)
            {
                warnings.Add($"Texture '{definition.Name}' references missing patch '{patch.PatchName}'.");
                missing++;
                continue;
            }

            resolved.Add((patch, image));
        }

        // "We can still display texture if at least one of the patches
        // was loaded" - only a total failure (every patch missing) fails
        // the whole texture, matching UDB exactly.
        if (missing >= definition.Patches.Count)
        {
            warnings.Add($"Texture '{definition.Name}' could not be composited - every patch failed to resolve.");
            return null;
        }

        var columnNumPatches = new int[width];
        var columnMasked = new bool[width];

        foreach (var (patch, image) in resolved)
        {
            var masked = IsMasked(image);
            for (var x = 0; x < image.Width; x++)
            {
                var ox = patch.OriginX + x;
                if (ox < 0 || ox >= width) continue;
                columnNumPatches[ox]++;
                if (masked) columnMasked[ox] = true;
            }
        }

        var target = new byte[width * height * 4];
        foreach (var (patch, image) in resolved)
        {
            DrawPatch(target, width, height, image, patch.OriginX, patch.OriginY, columnNumPatches, columnMasked);
        }

        return new PixelImage(width, height, target);
    }

    /// <summary>
    /// A patch is "masked" if it has at least one fully-transparent pixel
    /// (alpha exactly 0) anywhere - this is a whole-bitmap property, not
    /// per-column: even a column of a masked patch that's itself fully
    /// opaque still counts as masked once propagated to
    /// <c>columnMasked</c> below, exactly as UDB's <c>BitmapIsMasked</c>
    /// computes it.
    ///
    /// UDB's actual check is <c>pixel.a &lt;= 0.5f</c> against a raw byte
    /// (0-255) alpha field - since C# widens byte to float for that
    /// comparison, the smallest nonzero byte (1) already exceeds 0.5, so
    /// the real threshold is "exactly 0" vs. "anything else," not a
    /// half-opacity test. This matters for PNG-sourced patches with
    /// genuine partial alpha (e.g. 50%): UDB treats them as fully opaque
    /// and unmasked, not half-transparent - ported here exactly, even
    /// though it reads like a bug for real partial-alpha art.
    /// </summary>
    private static bool IsMasked(PixelImage image)
    {
        for (var i = 3; i < image.Rgba.Length; i += 4)
        {
            if (image.Rgba[i] == 0) return true;
        }

        return false;
    }

    private static void DrawPatch(
        byte[] target, int targetWidth, int targetHeight, PixelImage patch, int x, int y,
        int[] columnNumPatches, bool[] columnMasked)
    {
        for (var ox = 0; ox < patch.Width; ox++)
        {
            var tx = x + ox;
            var drawHeight = patch.Height;

            var columnHasMultiplePatches = tx >= 0 && tx < columnNumPatches.Length && columnNumPatches[tx] > 1;
            var columnIsMasked = tx >= 0 && tx < columnMasked.Length && columnMasked[tx];

            // If we have to emulate the negative vertical offset bug we
            // also have to recalculate the height of the patch that's
            // actually drawn, since it'll only draw as many pixels as it'd
            // draw as if the negative vertical offset was taken into
            // account. Note this compares tx against the *patch's own*
            // width, not the destination texture's width - preserved
            // exactly as UDB has it, even though it reads like an odd unit
            // mix, since it's unclear whether that's intentional.
            if (y < 0 && tx >= 0 && tx < patch.Width && columnHasMultiplePatches && !columnIsMasked)
            {
                drawHeight = patch.Height + y;
            }

            for (var oy = 0; oy < drawHeight; oy++)
            {
                var sourceIndex = (oy * patch.Width + ox) * 4;
                var alpha = patch.Rgba[sourceIndex + 3];
                // Matches UDB's effective "alpha != 0" test (see IsMasked's
                // remarks) - any nonzero alpha counts as opaque and is
                // drawn unconditionally, only alpha==0 is skipped.
                if (alpha == 0) continue; // transparent source pixel - leave the destination untouched

                var realY = y;
                if (columnIsMasked)
                {
                    if (tx >= 0 && tx < columnNumPatches.Length && columnNumPatches[tx] == 1) realY = 0;
                }
                else if (y < 0)
                {
                    realY = 0;
                }

                var ty = realY + oy;
                if (tx < 0 || tx >= targetWidth || ty < 0 || ty >= targetHeight) continue;

                var targetIndex = (ty * targetWidth + tx) * 4;
                target[targetIndex] = patch.Rgba[sourceIndex];
                target[targetIndex + 1] = patch.Rgba[sourceIndex + 1];
                target[targetIndex + 2] = patch.Rgba[sourceIndex + 2];
                target[targetIndex + 3] = patch.Rgba[sourceIndex + 3];
            }
        }
    }
}
