using System.Globalization;
using System.Text;

namespace DoomArchitect.Core.Configuration;

/// <summary>
/// The write half of the <c>.cfg</c> grammar <see cref="CfgParser"/>/
/// <see cref="CfgLoader"/> already read faithfully - mirrors UDB's own
/// real <c>Configuration.OutputStructure</c> formatting (read in full
/// this session): <c>key = value;</c> for scalars, <c>key { ... }</c>
/// recursively for nested blocks, tab-per-level indentation. One
/// deliberate simplification: UDB distinguishes a single-precision
/// <c>float</c> (written with a trailing <c>f</c>) from a <c>double</c>
/// purely for its own round-trip formatting needs - nothing this project
/// persists needs that distinction, so every <see cref="CfgValueKind.Double"/>
/// here just writes a plain decimal number.
/// </summary>
public static class CfgWriter
{
    public static string Write(CfgBlock document)
    {
        var sb = new StringBuilder();
        WriteBody(sb, document, level: 0);
        return sb.ToString();
    }

    private static void WriteBody(StringBuilder sb, CfgBlock block, int level)
    {
        foreach (var assignment in block.Assignments) WriteAssignment(sb, assignment, level);
        foreach (var child in block.Blocks) WriteBlock(sb, child, level);
    }

    private static void WriteBlock(StringBuilder sb, CfgBlock block, int level)
    {
        Indent(sb, level);
        sb.Append(block.Key).Append('\n');
        Indent(sb, level);
        sb.Append("{\n");
        WriteBody(sb, block, level + 1);
        Indent(sb, level);
        sb.Append("}\n");
    }

    private static void WriteAssignment(StringBuilder sb, CfgAssignment assignment, int level)
    {
        Indent(sb, level);
        sb.Append(assignment.Key).Append(" = ");
        WriteValue(sb, assignment.Value);
        sb.Append(";\n");
    }

    private static void WriteValue(StringBuilder sb, CfgValue value)
    {
        switch (value.Kind)
        {
            case CfgValueKind.Bool:
                sb.Append(value.AsBool() ? "true" : "false");
                break;
            case CfgValueKind.Int:
                sb.Append(value.AsLong().ToString(CultureInfo.InvariantCulture));
                break;
            case CfgValueKind.Double:
                sb.Append(value.AsDouble().ToString(CultureInfo.InvariantCulture));
                break;
            case CfgValueKind.String:
                sb.Append('"').Append(Escape(value.AsString())).Append('"');
                break;
        }
    }

    private static string Escape(string s) => s
        .Replace("\\", "\\\\")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t")
        .Replace("\"", "\\\"");

    private static void Indent(StringBuilder sb, int level) => sb.Append('\t', level);
}
