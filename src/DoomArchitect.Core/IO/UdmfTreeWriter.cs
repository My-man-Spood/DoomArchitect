using System.Text;

namespace DoomArchitect.Core.IO;

/// <summary>
/// Writes generic UDMF blocks back to text - used for re-emitting
/// preserved <see cref="UdmfDocument.UnknownBlocks"/> verbatim. A close
/// port of UDB's own <c>UniversalParser.OutputStructure</c>
/// (UniversalParser.cs:661-739): tab-per-level indentation, a blank line
/// before each nested block.
/// </summary>
internal static class UdmfTreeWriter
{
    public static void WriteAssignment(StringBuilder sb, int indent, string key, object value)
    {
        AppendIndent(sb, indent);
        sb.Append(key).Append(" = ").Append(UdmfFormatting.FormatValue(value)).Append(";\n");
    }

    public static void WriteBlock(StringBuilder sb, UdmfBlock block, int indent)
    {
        sb.Append('\n');
        AppendIndent(sb, indent);
        sb.Append(block.Name).Append('\n');
        AppendIndent(sb, indent);
        sb.Append("{\n");

        foreach (var assignment in block.Assignments)
        {
            WriteAssignment(sb, indent + 1, assignment.Key, assignment.Value.ToObject());
        }

        foreach (var child in block.Blocks)
        {
            WriteBlock(sb, child, indent + 1);
        }

        AppendIndent(sb, indent);
        sb.Append("}\n");
    }

    private static void AppendIndent(StringBuilder sb, int indent)
    {
        for (var i = 0; i < indent; i++) sb.Append('\t');
    }
}
