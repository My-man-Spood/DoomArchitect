namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// Parses one ZScript state label's frame sequence - ported from UDB's real
/// <c>ZScriptStateStructure</c>. <see cref="TryReadSprite"/> is UDB's own
/// documented hack for reading `####`/`----`/`[\]`-style placeholder sprite
/// runs, which aren't valid identifiers/strings on their own.
/// </summary>
public sealed class ZScriptStateStructure : StateStructure
{
    private static readonly string[] ControlKeywords = { "goto", "loop", "wait", "fail", "stop" };
    private static readonly string[] DataTypes = { "double", "int", "uint" };
    private static readonly string[] AllSpecials = { "bright", "light", "offset", "fast", "slow", "nodelay", "canraise" };

    private ZScriptToken? TryReadSprite(Stream stream, ZScriptTokenizer tokenizer)
    {
        const string specialInvalid = "-#";
        var outs = "";
        var cpos = stream.Position;

        while (true)
        {
            cpos = stream.Position;
            var token = tokenizer.ReadToken(true);

            if (token == null || token.Type != ZScriptTokenType.Invalid || token.Value == ";" ||
                (outs.Length > 0 && token.Value[0] != outs[0] && specialInvalid.Contains(token.Value[0])))
            {
                if (outs.Length == 0 && token != null && (token.Type == ZScriptTokenType.String || token.Type == ZScriptTokenType.Name))
                    return token;

                stream.Position = cpos;
                break;
            }

            outs += token.Value[0];
        }

        if (outs.Length > 0) return new ZScriptToken { Position = cpos, Type = ZScriptTokenType.String, Value = outs };

        stream.Position = cpos;
        return null;
    }

    internal ZScriptStateStructure(ActorStructure actor, ZDTextParser zdParser)
    {
        var parser = (ZScriptParser)zdParser;
        var stream = parser.DataStream!;
        var tokenizer = new ZScriptTokenizer(parser.DataReader!);
        parser.Tokenizer = tokenizer;

        while (true)
        {
            tokenizer.SkipWhitespace();
            var cpos = stream.Position;
            var token = tokenizer.ExpectToken(ZScriptTokenType.Identifier, ZScriptTokenType.String, ZScriptTokenType.CloseCurly);
            if (token is not { IsValid: true })
            {
                var spriteToken = TryReadSprite(stream, tokenizer);
                if (spriteToken == null)
                {
                    parser.ReportError($"Expected identifier or string, got {(object?)token ?? "<null>"}");
                    return;
                }
                token = spriteToken;
            }

            if (token.Type == ZScriptTokenType.CloseCurly)
            {
                stream.Position--;
                break;
            }

            if (token.Type == ZScriptTokenType.Identifier)
            {
                var keyword = token.Value.ToLowerInvariant();
                if (ControlKeywords.Contains(keyword))
                {
                    if (keyword == "goto")
                    {
                        GotoState = new ZScriptStateGoto(actor, parser);
                        parser.Tokenizer = tokenizer;
                        if (parser.HasError) return;
                    }

                    tokenizer.SkipWhitespace();
                    token = tokenizer.ExpectToken(ZScriptTokenType.Semicolon);
                    if (token is not { IsValid: true })
                    {
                        parser.ReportError($"Expected ;, got {(object?)token ?? "<null>"}");
                        return;
                    }

                    continue;
                }

                tokenizer.SkipWhitespace(); // there can be whitespace between a state label and its colon
            }

            // Not a control keyword - could be the next state's own label; peek for ":" or "." (a dotted label) and bail out to let the caller read it.
            var labelCheckpoint = stream.Position;
            var labelPeek = tokenizer.ExpectToken(ZScriptTokenType.Colon, ZScriptTokenType.Dot);
            var isNextLabel = labelPeek is { IsValid: true };
            stream.Position = labelCheckpoint;
            if (isNextLabel)
            {
                stream.Position = cpos;
                break;
            }

            var spriteName = token.Value.ToLowerInvariant();
            if (spriteName.Length != 4)
            {
                parser.ReportError($"Sprite name should be exactly 4 characters long (got {spriteName})");
                return;
            }

            tokenizer.SkipWhitespace();
            var framesToken = TryReadSprite(stream, tokenizer);
            if (framesToken == null)
            {
                parser.ReportError("Expected sprite frame(s)");
                return;
            }

            var spriteFrames = framesToken.Value;

            int duration;
            tokenizer.SkipWhitespace();
            token = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
            if (token is { IsValid: true })
            {
                if (DataTypes.Contains(token.Value))
                {
                    // A known data type is hopefully followed by .min or .max (e.g. int.max).
                    token = tokenizer.ExpectToken(ZScriptTokenType.Dot);
                    if (token is not { IsValid: true }) { parser.ReportError($"Expected ., got {(object?)token ?? "<null>"}"); return; }

                    token = tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                    if (token is not { IsValid: true }) { parser.ReportError($"Expected an identifier, got {(object?)token ?? "<null>"}"); return; }

                    if (token.Value == "min") duration = int.MinValue;
                    else if (token.Value == "max") duration = int.MaxValue;
                    else { parser.ReportError($"Expected min or max, got {token}"); return; }
                }
                else
                {
                    duration = -1;
                    tokenizer.SkipWhitespace();
                    token = tokenizer.ExpectToken(ZScriptTokenType.OpenParen);
                    if (token is { IsValid: true })
                    {
                        if (parser.ParseExpression(true) == null) return;
                        tokenizer.SkipWhitespace();
                        token = tokenizer.ExpectToken(ZScriptTokenType.CloseParen);
                        if (token is not { IsValid: true }) { parser.ReportError($"Expected ), got {(object?)token ?? "<null>"}"); return; }
                    }
                }
            }
            else
            {
                if (!parser.ParseInteger(out duration)) return;
            }

            var specials = new HashSet<string>();
            var info = new FrameInfo();

            var realSpriteName = (spriteName + spriteFrames[0]).ToUpperInvariant();
            if (!realSpriteName.StartsWith("----") && !realSpriteName.Contains('#'))
            {
                info.Sprite = realSpriteName;
                info.Duration = duration;
                Sprites.Add(info);
            }

            while (true)
            {
                tokenizer.SkipWhitespace();
                var frameCheckpoint = stream.Position;
                token = tokenizer.ExpectToken(ZScriptTokenType.Identifier, ZScriptTokenType.Semicolon, ZScriptTokenType.OpenCurly);
                if (token is not { IsValid: true })
                {
                    parser.ReportError($"Expected identifier, ;, or {{, got {(object?)token ?? "<null>"}");
                    return;
                }

                if (token.Type == ZScriptTokenType.OpenCurly)
                {
                    stream.Position--;
                    if (!parser.SkipBlock()) return;
                    break;
                }

                if (token.Type == ZScriptTokenType.Semicolon) break;

                var special = token.Value.ToLowerInvariant();
                if (AllSpecials.Contains(special))
                {
                    if (!specials.Add(special))
                    {
                        parser.ReportError($"'{special}' cannot be used twice");
                        return;
                    }

                    if (special == "bright")
                    {
                        info.Bright = true;
                    }
                    else if (special is "light" or "offset")
                    {
                        tokenizer.SkipWhitespace();
                        token = tokenizer.ExpectToken(ZScriptTokenType.OpenParen);
                        if (token is not { IsValid: true }) { parser.ReportError($"Expected (, got {(object?)token ?? "<null>"}"); return; }

                        var tokens = parser.ParseExpression(true);
                        if (tokens == null) return;
                        tokenizer.SkipWhitespace();
                        token = tokenizer.ExpectToken(ZScriptTokenType.CloseParen);
                        if (token is not { IsValid: true }) { parser.ReportError($"Expected ), got {(object?)token ?? "<null>"}"); return; }

                        if (special == "light")
                        {
                            if (tokens.Count != 1 || (tokens[0].Type != ZScriptTokenType.String && tokens[0].Type != ZScriptTokenType.Identifier))
                            {
                                parser.ReportError("Light() special takes one string argument");
                                return;
                            }

                            info.LightName = tokens[0].Value;
                        }
                    }
                }
                else
                {
                    stream.Position = frameCheckpoint;
                    var actionFunction = parser.ParseDottedIdentifier();
                    if (actionFunction == null) return;

                    tokenizer.SkipWhitespace();
                    token = tokenizer.ExpectToken(ZScriptTokenType.OpenParen);
                    if (token is { IsValid: true })
                    {
                        if (parser.ParseExpression(true) == null) return;
                        tokenizer.SkipWhitespace();
                        token = tokenizer.ExpectToken(ZScriptTokenType.CloseParen);
                        if (token is not { IsValid: true }) { parser.ReportError($"Expected ), got {(object?)token ?? "<null>"}"); return; }
                    }

                    tokenizer.SkipWhitespace();
                    token = tokenizer.ExpectToken(ZScriptTokenType.Semicolon);
                    if (token is not { IsValid: true }) { parser.ReportError($"Expected ;, got {(object?)token ?? "<null>"}"); return; }

                    break;
                }
            }
        }

        TrimLeft();
    }
}
