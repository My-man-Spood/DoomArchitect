using System.Globalization;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// Parses a GZDB `$color`-style color string - ported from UDB's real
/// <c>ZDTextParser.GetColorFromString</c> (itself a port of ZDoom's own
/// <c>V_GetColorFromString</c>). Handles `#RGB`/`#RRGGBB` and bare hex
/// (`"ff0000"`) fully - the one real gap is named colors ("red", "dodger
/// blue"): UDB resolves those against an X11 color-name table it loads at
/// runtime from `x11r6rgb.txt` (the very lump this project's required-
/// archive gzdoom.pk3 detection already keys off), which requires a loaded
/// resource this pure string-parsing utility has no access to on its own.
/// <paramref name="knownColors"/> is an injectable seam for that table
/// (case-insensitive color name to RGB) - omitted, named colors simply
/// don't resolve, matching every other "no resource, no data" seam
/// elsewhere in this port rather than hardcoding a partial table here.
/// </summary>
public static class GzdbColor
{
    public static bool TryParse(string name, out (byte R, byte G, byte B) color, IReadOnlyDictionary<string, (byte R, byte G, byte B)>? knownColors = null)
    {
        color = default;
        name = name.Trim('"').Replace(" ", "");

        var isHtmlColor = false;
        if (name.StartsWith('#'))
        {
            isHtmlColor = true;
            name = name[1..];

            if (name.Length == 3)
            {
                name = $"{name[0]}{name[0]}{name[1]}{name[1]}{name[2]}{name[2]}";
            }
            else if (name.Length != 6)
            {
                color = (0, 0, 0); // matches UDB's own "bad HTML-style color, pretend it's black" tolerance
                return true;
            }
        }

        if (int.TryParse(name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            color = ((byte)((hex >> 16) & 0xFF), (byte)((hex >> 8) & 0xFF), (byte)(hex & 0xFF));
            return true;
        }

        if (!isHtmlColor && knownColors != null && knownColors.TryGetValue(name, out var known))
        {
            color = known;
            return true;
        }

        return false;
    }
}
