namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// Parses a ZScript `goto [ClassName::]StateName[+Offset]` directive -
/// ported verbatim from UDB's real <c>ZScriptStateGoto</c>. Real ZScript
/// allows arbitrary math in the offset expression (`Goto Spawn + 5 * 2 - 7`)
/// - UDB's own port doesn't evaluate that either, always using a plain
/// integer/identifier token as-is (identifiers resolve to 0), so this
/// doesn't lose anything relative to the original.
/// </summary>
internal sealed class ZScriptStateGoto : StateGoto
{
    internal ZScriptStateGoto(ActorStructure actor, ZDTextParser zdParser)
    {
        var parser = (ZScriptParser)zdParser;
        var tokenizer = new ZScriptTokenizer(parser.DataReader!);
        parser.Tokenizer = tokenizer;

        tokenizer.SkipWhitespace();
        var firstTarget = parser.ParseDottedIdentifier();
        if (firstTarget == null) return;

        string? secondTarget = null;
        var offset = 0;

        tokenizer.SkipWhitespace();
        var token = tokenizer.ExpectToken(ZScriptTokenType.DoubleColon);
        if (token is { IsValid: true })
        {
            secondTarget = parser.ParseDottedIdentifier();
            if (secondTarget == null) return;
        }

        tokenizer.SkipWhitespace();
        token = tokenizer.ExpectToken(ZScriptTokenType.OpAdd);
        if (token is { IsValid: true })
        {
            tokenizer.SkipWhitespace();
            token = tokenizer.ExpectToken(ZScriptTokenType.Integer, ZScriptTokenType.Identifier);
            if (token is not { IsValid: true })
            {
                parser.ReportError($"Expected state offset, got {(object?)token ?? "<null>"}");
                return;
            }

            offset = token.ValueInt;
        }

        if (string.IsNullOrEmpty(secondTarget))
        {
            ClassName = actor.ClassName;
            StateName = firstTarget.ToLowerInvariant().Trim();
        }
        else
        {
            ClassName = firstTarget.ToLowerInvariant().Trim();
            StateName = secondTarget.ToLowerInvariant().Trim();
        }

        SpriteOffset = offset;

        if (ClassName == "super" && actor.BaseClass != null) ClassName = actor.BaseClass.ClassName;
    }
}
