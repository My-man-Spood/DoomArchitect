namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// Parses one DECORATE state label's frame sequence (everything after
/// `Spawn:` up to the next label or `}`) - ported from UDB's real
/// <c>DecorateStateStructure</c>. The original uses `goto` to jump out of
/// several nested loops to one shared "done, trim and return" exit point;
/// restructured here as <see cref="Parse"/> returning a bool (true = reached
/// that exit point normally, false = a parse error was already reported)
/// with the constructor calling <see cref="StateStructure.TrimLeft"/> only
/// on the true path - same control flow, without `goto`.
/// </summary>
public sealed class DecorateStateStructure : StateStructure
{
    public DecorateStateStructure(ActorStructure actor, ZDTextParser zdParser)
    {
        var parser = (DecorateParser)zdParser;
        if (Parse(actor, parser)) TrimLeft();
    }

    private bool Parse(ActorStructure actor, DecorateParser parser)
    {
        var lastToken = "";

        while (parser.SkipWhitespace(true))
        {
            var token = parser.ReadToken().ToLowerInvariant();

            // Flow control keywords - "fail" in particular is sometimes
            // legitimately a sprite name (Skulltag/Zandronum), so only
            // treat these as directives when a newline immediately follows.
            if (token is "loop" or "stop" or "wait" or "fail")
            {
                var checkpoint = parser.DataStream!.Position;
                parser.SkipWhitespace(false);
                var newline = parser.ReadToken();
                parser.DataStream.Position = checkpoint;

                if (newline == "\n")
                {
                    lastToken = token;
                    continue;
                }
            }

            if (token == "goto")
            {
                GotoState = new DecorateStateGoto(actor, parser);
                if (parser.HasError) return false;
            }
            else if (token == ":")
            {
                if (!string.IsNullOrEmpty(lastToken))
                    parser.DataStream!.Seek(-(lastToken.Length + 1), SeekOrigin.Current); // rewind so this label can be read again

                return true;
            }
            else if (token == "{")
            {
                var braceLevel = 1;
                while (!string.IsNullOrEmpty(token) && braceLevel > 0)
                {
                    parser.SkipWhitespace(false);
                    token = parser.ReadToken();
                    switch (token)
                    {
                        case "{": braceLevel++; break;
                        case "}": braceLevel--; break;
                    }
                }
            }
            else if (token == "}")
            {
                parser.DataStream!.Seek(-1, SeekOrigin.Current); // rewind so this scope end can be read again
                return true;
            }
            else
            {
                if (!ParseFrame(parser, token)) return false;
                if (_frameParseHitEndOfScope) return true;
            }

            lastToken = token;
        }

        return true;
    }

    private bool _frameParseHitEndOfScope;

    /// <summary>One `SPRT AB 4` (etc.) frame line, plus any trailing keywords (`bright`, `light(...)`) up to the next newline.</summary>
    private bool ParseFrame(DecorateParser parser, string token)
    {
        _frameParseHitEndOfScope = false;

        token = parser.StripTokenQuotes(token); // the sprite name part can be quoted
        if (string.IsNullOrEmpty(token))
        {
            parser.ReportError("Expected sprite name");
            return false;
        }

        parser.SkipWhitespace(true);
        var spriteFrames = parser.StripTokenQuotes(parser.ReadToken()); // frames can be quoted too
        if (string.IsNullOrEmpty(spriteFrames))
        {
            parser.ReportError("Expected sprite frame");
            return false;
        }

        if (spriteFrames == ":")
        {
            parser.DataStream!.Seek(-(token.Length + 1), SeekOrigin.Current); // rewind so this label can be read again
            _frameParseHitEndOfScope = true;
            return true;
        }

        var info = new FrameInfo();
        if (spriteFrames.Length > 0)
        {
            if (token.Length != 4)
            {
                parser.LogWarning($"Invalid sprite name \"{token.ToUpperInvariant()}\". Sprite names must be exactly 4 characters long");
            }
            else
            {
                var spriteName = (token + spriteFrames[0]).ToUpperInvariant();

                // Ignore some odd ZDoom conventions - some actors have only
                // a TNT1 state and would otherwise get a random image because of this.
                if (!spriteName.StartsWith("----") && !spriteName.Contains('#'))
                {
                    info.Sprite = spriteName;

                    parser.SkipWhitespace(false);
                    var durationStr = parser.ReadToken();
                    if (durationStr == "-") durationStr += parser.ReadToken();

                    if (string.IsNullOrEmpty(durationStr) || durationStr == "\n")
                    {
                        parser.ReportError("Expected frame duration");
                        return false;
                    }

                    if (!int.TryParse(durationStr.Trim(), out var duration))
                        parser.DataStream!.Seek(-durationStr.Length, SeekOrigin.Current);
                    info.Duration = duration;
                    Sprites.Add(info);
                }
            }
        }

        return ParseTrailingKeywords(parser, info);
    }

    /// <summary>`bright`, `light(name)`, an inline `(...)` function-call param list, or the next line's frame - everything trailing a frame line up to its own newline.</summary>
    private bool ParseTrailingKeywords(DecorateParser parser, FrameInfo info)
    {
        parser.SkipWhitespace(false);
        var t = parser.ReadToken();

        while (!string.IsNullOrEmpty(t) && t != "\n")
        {
            if (t == "bright")
            {
                info.Bright = true;
            }
            else if (t == "light")
            {
                if (!parser.NextTokenIs("(")) return false;
                if (!parser.SkipWhitespace(true))
                {
                    parser.ReportError("Unexpected end of the structure");
                    return false;
                }

                info.LightName = parser.StripTokenQuotes(parser.ReadToken());
                if (string.IsNullOrEmpty(info.LightName))
                {
                    parser.ReportError("Expected dynamic light name");
                    return false;
                }

                if (!parser.SkipWhitespace(true))
                {
                    parser.ReportError("Unexpected end of the structure");
                    return false;
                }

                if (!parser.NextTokenIs(")"))
                {
                    parser.ReportError("Expected closing parenthesis in Light()");
                    return false;
                }
            }
            else if (t == "{")
            {
                parser.DataStream!.Seek(-1, SeekOrigin.Current); // rewind - the enclosing loop reparses this as a new scope
                break;
            }
            else if (t == "(")
            {
                var braceLevel = 1;
                var inner = t;
                while (!string.IsNullOrEmpty(inner) && braceLevel > 0)
                {
                    parser.SkipWhitespace(true);
                    inner = parser.ReadToken();
                    switch (inner)
                    {
                        case "(": braceLevel++; break;
                        case ")": braceLevel--; break;
                    }
                }
            }
            else if (t == "}")
            {
                // Valid: "Actor Oneliner { States { Spawn: WOOT A 1 A_FadeOut(0.1) Loop }}"
                parser.DataStream!.Seek(-1, SeekOrigin.Current);
                _frameParseHitEndOfScope = true;
                return true;
            }

            parser.SkipWhitespace(false);
            t = parser.ReadToken().ToLowerInvariant();
        }

        return true;
    }
}
