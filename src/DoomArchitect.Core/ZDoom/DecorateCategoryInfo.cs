using System.Globalization;

namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// A DECORATE/ZScript `#region`-derived category (title, ordering) plus any
/// GZDB `$`-comment properties attached to it - ported verbatim from UDB's
/// real <c>DecorateCategoryInfo</c>, which has no dependency beyond
/// <see cref="ZDTextParser.StripQuotes"/>.
/// </summary>
public sealed class DecorateCategoryInfo
{
    public List<string> Category { get; } = new(1);
    public Dictionary<string, List<string>> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetPropertyValueString(string propname, int valueindex, string defaultvalue, bool stripquotes = true)
    {
        if (Properties.TryGetValue(propname, out var values) && values.Count > valueindex)
            return stripquotes ? ZDTextParser.StripQuotes(values[valueindex]) : values[valueindex];
        return defaultvalue;
    }

    public bool GetPropertyValueBool(string propname, int valueindex, bool defaultvalue)
    {
        var str = GetPropertyValueString(propname, valueindex, string.Empty, false).ToLowerInvariant();
        return string.IsNullOrEmpty(str) ? defaultvalue : str == "true";
    }

    public int GetPropertyValueInt(string propname, int valueindex, int defaultvalue)
    {
        var str = GetPropertyValueString(propname, valueindex, string.Empty, false);

        if (str == "-" && Properties.Count > valueindex + 1) // it can be negative
            str += GetPropertyValueString(propname, valueindex + 1, string.Empty, false);

        return int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intvalue) ? intvalue : defaultvalue;
    }

    public float GetPropertyValueFloat(string propname, int valueindex, float defaultvalue)
    {
        var str = GetPropertyValueString(propname, valueindex, string.Empty, false);

        if (str == "-" && Properties.Count > valueindex + 1)
            str += GetPropertyValueString(propname, valueindex + 1, string.Empty, false);

        return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var fvalue) ? fvalue : defaultvalue;
    }
}
