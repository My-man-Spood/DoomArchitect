namespace DoomArchitect.Core.Map;

/// <summary>
/// The kind of value a <see cref="UniValue"/> holds. Numbered to match
/// UDB's own <c>UniversalType</c> enum for its first four cases, but
/// deliberately starts with only those four - the parser behind
/// <see cref="UniFields"/> today only ever produces int/float/string/bool
/// values, and UDB's remaining ~23 cases (textures, colors, enums, tag
/// variants, etc.) are purely UI-control hints that are meaningless
/// without a schema system this codebase doesn't have yet. Extend this
/// additively (<c>LinedefType = 4</c>, etc.) once a specific generic
/// field editor actually needs to distinguish one of those kinds.
/// </summary>
public enum UniversalType
{
    Integer = 0,
    Float = 1,
    String = 2,
    Boolean = 3,
}
