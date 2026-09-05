using System.Globalization;
using System.Text;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Value-formatting rules shared by <see cref="UdmfTreeWriter"/> (unknown
/// blocks) and <see cref="UdmfWriter"/> (known blocks' own fields and each
/// element's <c>CustomFields</c>), ported from UDB's
/// <c>UniversalParser.OutputStructure</c> (UniversalParser.cs:661-739).
/// </summary>
internal static class UdmfFormatting
{
    public static string FormatValue(object value) => value switch
    {
        bool b => b ? "true" : "false",
        double d => FormatDouble(d),
        float f => f.ToString("0.000", CultureInfo.InvariantCulture),
        long or int or short or byte => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        string s => FormatString(s),
        _ => throw new ArgumentException($"Unsupported UDMF value type: {value.GetType()}", nameof(value)),
    };

    /// <summary>UDB's own double format: 1 mandatory + up to 14 optional decimals, invariant.</summary>
    public static string FormatDouble(double value) => value.ToString("0.0##############", CultureInfo.InvariantCulture);

    public static string FormatString(string value) => $"\"{EscapeString(value)}\"";

    /// <summary>
    /// Order matters: backslashes are escaped first so the escape
    /// sequences inserted afterward don't themselves get double-escaped.
    /// </summary>
    private static string EscapeString(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '"': sb.Append("\\\""); break;
                default: sb.Append(ch); break;
            }
        }

        return sb.ToString();
    }
}
