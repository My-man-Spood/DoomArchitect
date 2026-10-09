"""
Regenerates src/DoomArchitect.Core/ZDoom/Bcs/BcsFunctionDocsData.cs from
wiki-research/merged_docs.json - the merged, hand-reviewed result of the
distillation pass described in wiki-research/README.md. Run this after
editing merged_docs.json directly, or after re-merging a fresh set of
wiki-research/batch-out/*.json files into it.
"""
import json, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MERGED_PATH = os.path.join(ROOT, "wiki-research", "merged_docs.json")
OUT_CS_PATH = os.path.join(ROOT, "src", "DoomArchitect.Core", "ZDoom", "Bcs", "BcsFunctionDocsData.cs")


def csharp_string(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main():
    final = json.load(open(MERGED_PATH, encoding="utf-8"))

    lines = [
        "// GENERATED - do not hand-edit. Regenerate via wiki-research/generate_docs_data.py",
        "// (see wiki-research/README.md), which merges the distilled, original-wording",
        "// descriptions (never the wiki's own text - see BcsFunctionDocs's own remarks)",
        "// into this dictionary.",
        "namespace DoomArchitect.Core.ZDoom.Bcs;",
        "",
        "public static partial class BcsFunctionDocs",
        "{",
        # Must come before ByName: static field initializers run in
        # textual declaration order, and ByName's own initializer
        # references this - declaring it after leaves it null at that
        # point (confirmed by a real CS8604 warning before this order
        # was fixed).
        "    private static readonly (string Name, string Description)[] EmptyParams = Array.Empty<(string Name, string Description)>();",
        "",
        "    private static readonly Dictionary<string, Doc> ByName = new(StringComparer.OrdinalIgnoreCase)",
        "    {",
    ]
    for name in sorted(final.keys(), key=str.lower):
        entry = final[name]
        summary = csharp_string(entry["summary"])
        params = entry.get("params", {})
        if not params:
            params_expr = "EmptyParams"
        else:
            # Order matters here - ApplyParameterNames zips this
            # positionally against a types-only signature, so it must
            # match the real declaration order, not alphabetical.
            items = ", ".join(f"({csharp_string(k)}, {csharp_string(v)})" for k, v in params.items())
            params_expr = f"new (string Name, string Description)[] {{ {items} }}"
        lines.append(f"        [{csharp_string(name)}] = new({summary}, {params_expr}),")
    lines.append("    };")
    lines.append("}")

    with open(OUT_CS_PATH, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    print(f"wrote {OUT_CS_PATH} with {len(final)} entries")


if __name__ == "__main__":
    main()
