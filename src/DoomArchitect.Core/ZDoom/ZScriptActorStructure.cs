namespace DoomArchitect.Core.ZDoom;

/// <summary>
/// One parsed `class Name : Parent { ... }` ZScript class body - ported
/// from UDB's real <c>ZScriptActorStructure</c>. Only the `Default`/
/// `States` blocks are actually parsed for content (matching UDB's own
/// real comment: "we are skipping everything, except Defaults and
/// States") - everything else in a ZScript class body (fields, methods,
/// properties, structs, enums, generics/function pointers) is parsed only
/// far enough to correctly skip past it, exactly as faithfully as the
/// original. The one deliberate cut: UDB's own `user_*` field capture
/// (~115 lines at the end of its real constructor) is dropped entirely -
/// see <see cref="ActorStructure"/>'s own remark on why - which also means
/// this port never needs UDB's <c>UniversalType</c>/color-parsing helpers
/// that capture existed for.
/// </summary>
public sealed class ZScriptActorStructure : ActorStructure
{
    private readonly ZScriptParser _parser;
    private readonly Stream _stream;
    private ZScriptTokenizer _tokenizer;

    public List<string> Mixins { get; } = new();

    internal static bool ParseGZDBComment(Dictionary<string, List<string>> props, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text[0] != '$') return false;

        var nextWhitespace = text.IndexOfAny(new[] { ' ', '\t', '\r', ' ' });
        var propertyName = text;
        var propertyValue = "";
        if (nextWhitespace >= 0)
        {
            propertyName = propertyName[..nextWhitespace];
            propertyValue = text[(nextWhitespace + 1)..].Trim();
        }

        props[propertyName.ToLowerInvariant()] = new List<string> { propertyValue };
        return true;
    }

    internal ZScriptActorStructure(ZDTextParser zdParser, DecorateCategoryInfo? categoryInfo, string className, string? replacesName, string? parentName)
    {
        CategoryInfo = categoryInfo;

        _parser = (ZScriptParser)zdParser;
        _stream = _parser.DataStream!;
        _tokenizer = new ZScriptTokenizer(_parser.DataReader!);
        _parser.Tokenizer = _tokenizer;

        ClassName = className;
        ReplacesClass = replacesName;
        // BaseClass isn't resolved here - not guaranteed to exist yet (ZScript allows forward references); ZScriptParser.CompleteParsing resolves it once every class in the scan is known.

        var clsOpen = _tokenizer.ExpectToken(ZScriptTokenType.OpenCurly, ZScriptTokenType.Semicolon);
        if (clsOpen is not { IsValid: true })
        {
            _parser.ReportError($"Expected {{ or ;, got {(object?)clsOpen ?? "<null>"}");
            return;
        }

        var varProps = new Dictionary<string, List<string>>();

        // A class body can hold: a Default block, a States block, a method
        // signature/body, a field (incl. arrays), a nested struct, or an
        // enum/const declaration - everything except Default/States is
        // parsed only far enough to skip correctly.
        while (true)
        {
            varProps.Clear();
            while (true)
            {
                var tt = _tokenizer.ExpectToken(ZScriptTokenType.Whitespace, ZScriptTokenType.BlockComment, ZScriptTokenType.LineComment, ZScriptTokenType.Newline);
                if (tt is not { IsValid: true }) break;
                if (tt.Type == ZScriptTokenType.LineComment) ParseGZDBComment(varProps, tt.Value);
            }

            var ocpos = _stream.Position;
            var token = _tokenizer.ExpectToken(ZScriptTokenType.Identifier, ZScriptTokenType.CloseCurly);
            if (token is not { IsValid: true })
            {
                if (token == null && clsOpen.Type == ZScriptTokenType.Semicolon) break; // forward declaration only

                _parser.ReportError($"Expected identifier, got {(object?)clsOpen}");
                return;
            }

            if (token.Type == ZScriptTokenType.CloseCurly) break;

            var lower = token.Value.ToLowerInvariant();
            switch (lower)
            {
                case "default":
                    if (!ParseDefaultBlock()) return;
                    continue;
                case "states":
                    if (!ParseStatesBlock()) return;
                    continue;
                case "enum":
                    if (!_parser.ParseEnum()) return;
                    continue;
                case "const":
                    if (!_parser.ParseConst()) return;
                    continue;
                case "struct": // a struct can nest inside a class, but not another class
                    if (!_parser.ParseClassOrStruct(true, false, false, null)) return;
                    continue;
                case "property":
                    if (!ParseProperty()) return;
                    continue;
                case "flagdef":
                    if (!ParseFlagdef()) return;
                    continue;
                case "mixin":
                    if (!ParseMixin()) return;
                    continue;
            }

            // Not one of the special-cased keywords above - rewind to
            // before this identifier and reparse it as the start of a
            // modifier/type/field-or-method declaration.
            _stream.Position = ocpos;
            if (!SkipMemberDeclaration()) return;
        }

        ParseCustomArguments();
    }

    /// <summary>The generic field/method skip path - everything a class body can hold besides the cases already dispatched above. Called with the stream positioned right before the first modifier/type-name token.</summary>
    private bool SkipMemberDeclaration()
    {
        string[] availableModifiers = { "static", "native", "action", "internal", "readonly", "protected", "private", "virtual", "override", "meta", "transient", "deprecated", "final", "play", "ui", "clearscope", "virtualscope", "version", "const", "abstract", "norollback" };
        string[] versionedModifiers = { "version", "deprecated" };
        string[] methodModifiers = { "action", "virtual", "override", "final", "abstract" };

        var isMethod = false;
        var modifiers = new HashSet<string>();
        var isArray = false;

        while (true)
        {
            _tokenizer.SkipWhitespace();
            var cpos = _stream.Position;
            var token = _tokenizer.ExpectToken(ZScriptTokenType.Identifier);
            if (token is not { IsValid: true })
            {
                _parser.ReportError("Expected modifier or name, got <null>");
                return false;
            }

            var b_lower = token.Value.ToLowerInvariant();

            if (b_lower == "readonly")
            {
                // readonly can be a modifier ("readonly int x") or a type ("readonly<int> x") - peek for '<'.
                var peek = _tokenizer.ExpectToken(ZScriptTokenType.OpLessThan);
                if (peek is { IsValid: true })
                {
                    _stream.Position = cpos;
                    break;
                }
            }

            if (!availableModifiers.Contains(b_lower))
            {
                _stream.Position = cpos;
                break;
            }

            if (!modifiers.Add(b_lower))
            {
                _parser.ReportError($"Field/method modifier '{b_lower}' was specified twice");
                return false;
            }

            if (methodModifiers.Contains(b_lower)) isMethod = true;

            if (versionedModifiers.Contains(b_lower))
            {
                var version = ParseVersion(b_lower == "version");
                if (version == null && b_lower == "version") return false;
            }

            if (b_lower == "action")
            {
                var context = ParseAction();
                if (context == null) return false;
            }
        }

        var types = new List<string>();
        while (true)
        {
            _tokenizer.SkipWhitespace();
            var typeName = ParseTypeName();
            if (typeName == null) return false;

            types.Add(typeName.ToLowerInvariant());

            var cpos = _stream.Position;
            _tokenizer.SkipWhitespace();
            var token = _tokenizer.ExpectToken(ZScriptTokenType.Comma, ZScriptTokenType.Identifier, ZScriptTokenType.OpenSquare);

            if (token is { IsValid: false })
            {
                _parser.ReportError($"Expected comma, identifier or array dimensions, got {token}");
                return false;
            }

            if (token == null || token.Type != ZScriptTokenType.Comma)
            {
                _stream.Position = cpos;
                if (token?.Type == ZScriptTokenType.OpenSquare)
                {
                    if (ParseArrayDimensions() == null) return false;
                    isArray = true;
                }
                break;
            }
        }

        var names = new List<string>();
        while (true)
        {
            _tokenizer.SkipWhitespace();
            var nameToken = _tokenizer.ExpectToken(ZScriptTokenType.Identifier);
            if (nameToken is not { IsValid: true })
            {
                _parser.ReportError($"Expected field/method name, got {(object?)nameToken ?? "<null>"}");
                return false;
            }

            _tokenizer.SkipWhitespace();
            var cpos = _stream.Position;
            ZScriptToken? next;
            if (!isArray)
            {
                next = _tokenizer.ExpectToken(ZScriptTokenType.Comma, ZScriptTokenType.OpenParen, ZScriptTokenType.OpenSquare, ZScriptTokenType.Semicolon);
                if (next is not { IsValid: true })
                {
                    _parser.ReportError($"Expected comma, ;, [, or argument list, got {(object?)next ?? "<null>"}");
                    return false;
                }
            }
            else
            {
                next = _tokenizer.ExpectToken(ZScriptTokenType.Comma, ZScriptTokenType.Semicolon, ZScriptTokenType.OpAssign);
                if (next is not { IsValid: true })
                {
                    _parser.ReportError($"Expected comma, ;, or =, got {(object?)next ?? "<null>"}");
                    return false;
                }
            }

            if (next.Type == ZScriptTokenType.OpenParen)
            {
                if (names.Count > 0)
                {
                    _parser.ReportError("Cannot have multiple names in a method");
                    return false;
                }

                isMethod = true;
                if (_parser.ParseExpression(true) == null) return false; // fake-parse the argument list, never used

                var closeParen = _tokenizer.ExpectToken(ZScriptTokenType.CloseParen);
                if (closeParen is not { IsValid: true })
                {
                    _parser.ReportError($"Expected ), got {(object?)closeParen ?? "<null>"}");
                    return false;
                }

                _tokenizer.SkipWhitespace();
                var tail = _tokenizer.ExpectToken(ZScriptTokenType.Semicolon, ZScriptTokenType.OpenCurly, ZScriptTokenType.Identifier);
                if (tail is not { IsValid: true })
                {
                    _parser.ReportError($"Expected 'const', ; or {{, got {(object?)tail ?? "<null>"}");
                    return false;
                }

                if (tail.Type == ZScriptTokenType.Identifier)
                {
                    if (!tail.Value.Equals("const", StringComparison.OrdinalIgnoreCase))
                    {
                        _parser.ReportError($"Expected 'const', got {tail}");
                        return false;
                    }

                    _tokenizer.SkipWhitespace();
                    var afterConstCheckpoint = _stream.Position;
                    tail = _tokenizer.ExpectToken(ZScriptTokenType.Semicolon, ZScriptTokenType.OpenCurly);
                    if (tail is not { IsValid: true })
                    {
                        _parser.ReportError($"Expected ; or {{, got {(object?)tail ?? "<null>"}");
                        return false;
                    }
                    _ = afterConstCheckpoint;
                }

                if (tail.Type == ZScriptTokenType.OpenCurly)
                {
                    _stream.Position--; // put the '{' back for SkipBlock to consume
                    if (!_parser.SkipBlock()) return false;
                }

                break; // end of method parsing
            }

            if (isMethod)
            {
                _parser.ReportError("Cannot have virtual, override or action fields");
                return false;
            }

            if (next.Type is ZScriptTokenType.OpenSquare or ZScriptTokenType.OpAssign)
            {
                _stream.Position = cpos;

                if (!isArray && ParseArrayDimensions() == null) return false;

                _tokenizer.SkipWhitespace();
                var expectTokens = modifiers.Contains("static")
                    ? new[] { ZScriptTokenType.Semicolon, ZScriptTokenType.Comma, ZScriptTokenType.OpAssign }
                    : new[] { ZScriptTokenType.Semicolon, ZScriptTokenType.Comma };
                var afterArray = _tokenizer.ExpectToken(expectTokens);
                if (afterArray is not { IsValid: true })
                {
                    _parser.ReportError($"Expected ;, =, or comma, got {(object?)afterArray ?? "<null>"}");
                    return false;
                }

                if (afterArray.Type == ZScriptTokenType.OpAssign)
                {
                    _tokenizer.SkipWhitespace();
                    if (!_parser.SkipBlock()) return false;
                    _tokenizer.SkipWhitespace();
                    var afterInit = _tokenizer.ExpectToken(ZScriptTokenType.Semicolon, ZScriptTokenType.Comma);
                    if (afterInit is not { IsValid: true })
                    {
                        _parser.ReportError($"Expected ; or comma, got {(object?)afterInit ?? "<null>"}");
                        return false;
                    }
                    next = afterInit;
                }
                else
                {
                    next = afterArray;
                }
            }

            names.Add(nameToken.Value.ToLowerInvariant());
            if (next.Type != ZScriptTokenType.Comma) break;
        }

        if (isMethod)
        {
            if (modifiers.Contains("protected") && modifiers.Contains("private"))
            {
                _parser.ReportError("Cannot have protected and private on the same method");
                return false;
            }

            var exclusivity = (modifiers.Contains("virtual") ? 1 : 0) + (modifiers.Contains("override") ? 1 : 0) + (modifiers.Contains("final") ? 1 : 0);
            if (exclusivity > 1)
            {
                _parser.ReportError("Cannot have virtual, override and final on the same method");
                return false;
            }

            if (modifiers.Contains("meta"))
            {
                _parser.ReportError("Cannot have meta on a method");
                return false;
            }
        }

        return true;
    }

    private bool ParseDefaultBlock()
    {
        _tokenizer.SkipWhitespace();
        var token = _tokenizer.ExpectToken(ZScriptTokenType.OpenCurly);
        if (token is not { IsValid: true })
        {
            _parser.ReportError($"Expected {{, got {(object?)token ?? "<null>"}");
            return false;
        }

        while (true)
        {
            var cpos = _stream.Position;
            token = _tokenizer.ExpectToken(
                ZScriptTokenType.Whitespace, ZScriptTokenType.BlockComment, ZScriptTokenType.Newline, ZScriptTokenType.LineComment,
                ZScriptTokenType.OpAdd, ZScriptTokenType.OpSubtract, ZScriptTokenType.Identifier, ZScriptTokenType.CloseCurly, ZScriptTokenType.Semicolon);
            if (token is not { IsValid: true })
            {
                _parser.ReportError($"Expected comment, flag, property, or }}, got {(object?)token ?? "<null>"}");
                return false;
            }

            if (token.Type == ZScriptTokenType.CloseCurly) break;

            switch (token.Type)
            {
                case ZScriptTokenType.Whitespace:
                case ZScriptTokenType.BlockComment:
                case ZScriptTokenType.Newline:
                    break;

                case ZScriptTokenType.LineComment:
                    ParseGZDBComment(Properties, token.Value);
                    break;

                case ZScriptTokenType.OpAdd:
                case ZScriptTokenType.OpSubtract:
                {
                    var flagSet = token.Type == ZScriptTokenType.OpAdd;
                    var flagName = _parser.ParseDottedIdentifier();
                    if (flagName == null) return false;
                    Flags[flagName] = flagSet;
                    break;
                }

                case ZScriptTokenType.Identifier:
                {
                    _stream.Position = cpos;
                    var propertyName = _parser.ParseDottedIdentifier();
                    if (propertyName == null) return false;

                    var values = new List<string>();
                    while (true)
                    {
                        _tokenizer.SkipWhitespace();
                        var expr = _parser.ParseExpression();
                        if (expr == null) return false;
                        values.Add(ZScriptTokenizer.TokensToString(expr));

                        token = _tokenizer.ExpectToken(ZScriptTokenType.Comma, ZScriptTokenType.Semicolon);
                        if (token is not { IsValid: true })
                        {
                            _parser.ReportError($"Expected comma or ;, got {(object?)token ?? "<null>"}");
                            return false;
                        }

                        if (token.Type == ZScriptTokenType.Semicolon) break;
                    }

                    if (propertyName == "scale") Properties["xscale"] = Properties["yscale"] = values;
                    else Properties[propertyName] = values;
                    break;
                }
            }
        }

        return true;
    }

    private bool ParseStatesBlock()
    {
        _tokenizer.SkipWhitespace();
        var token = _tokenizer.ExpectToken(ZScriptTokenType.OpenParen, ZScriptTokenType.OpenCurly);
        if (token is not { IsValid: true })
        {
            _parser.ReportError($"Expected ( or {{, got {(object?)token ?? "<null>"}");
            return false;
        }

        // An optional cast-type list can follow the States keyword: States(Actor, Item) { ... }
        if (token.Type == ZScriptTokenType.OpenParen)
        {
            if (_parser.ParseExpression(true) == null) return false;

            _tokenizer.SkipWhitespace();
            token = _tokenizer.ExpectToken(ZScriptTokenType.CloseParen);
            if (token is not { IsValid: true })
            {
                _parser.ReportError($"Expected ), got {(object?)token ?? "<null>"}");
                return false;
            }

            _tokenizer.SkipWhitespace();
            token = _tokenizer.ExpectToken(ZScriptTokenType.OpenCurly);
            if (token is not { IsValid: true })
            {
                _parser.ReportError($"Expected {{, got {(object?)token ?? "<null>"}");
                return false;
            }
        }

        var stateLabel = "";
        while (true)
        {
            var state = new ZScriptStateStructure(this, _parser);
            _parser.Tokenizer = _tokenizer; // ZScriptStateStructure creates its own tokenizer view internally - resync before continuing
            if (_parser.HasError) return false;
            States[stateLabel] = state;

            _tokenizer.SkipWhitespace();
            var cpos = _stream.Position;
            token = _tokenizer.ExpectToken(ZScriptTokenType.Identifier, ZScriptTokenType.CloseCurly);
            if (token is not { IsValid: true })
            {
                _parser.ReportError($"Expected state label or }}, got {(object?)token ?? "<null>"}");
                return false;
            }

            if (token.Type == ZScriptTokenType.CloseCurly) break;

            _stream.Position = cpos;
            var label = _parser.ParseDottedIdentifier();
            if (label == null) return false;
            stateLabel = label;

            _tokenizer.SkipWhitespace(); // there can be whitespace between a state label and its colon
            token = _tokenizer.ExpectToken(ZScriptTokenType.Colon);
            if (token is not { IsValid: true })
            {
                _parser.ReportError($"Expected :, got {(object?)token ?? "<null>"}");
                return false;
            }
        }

        return true;
    }

    private string? ParseTypeName()
    {
        var token = _tokenizer.ExpectToken(ZScriptTokenType.Identifier);
        if (token is not { IsValid: true })
        {
            _parser.ReportError($"Expected type name, got {(object?)token ?? "<null>"}");
            return null;
        }

        var name = token.Value.ToLowerInvariant();
        if (name == "function") return ParseFunctionPointer();

        var cpos = _stream.Position;
        _tokenizer.SkipWhitespace();
        var next = _tokenizer.ReadToken();
        if (next is { Type: ZScriptTokenType.OpLessThan }) return ParseGenericTemplate(name);

        _stream.Position = cpos;
        return name;
    }

    private List<int>? ParseArrayDimensions()
    {
        var dimensions = new List<int>();
        while (true)
        {
            _tokenizer.SkipWhitespace();
            var open = _tokenizer.ExpectToken(ZScriptTokenType.OpenSquare);
            if (open is not { IsValid: true }) return dimensions; // no more array dimensions

            _tokenizer.SkipWhitespace();
            var cpos = _stream.Position;
            var token = _tokenizer.ExpectToken(ZScriptTokenType.Integer, ZScriptTokenType.Identifier, ZScriptTokenType.CloseSquare);
            if (token is not { IsValid: true })
            {
                _parser.ReportError($"Expected integer or const, got {(object?)token ?? "<null>"}");
                return null;
            }

            var length = -1;
            if (token.Type == ZScriptTokenType.Integer)
            {
                length = token.ValueInt;
            }
            else if (token.Type == ZScriptTokenType.CloseSquare)
            {
                _stream.Position = cpos; // the closing bracket is expected again below
            }
            else
            {
                // a dotted constant reference - value not determinable here, not needed either
                while (true)
                {
                    cpos = _stream.Position;
                    var dot = _tokenizer.ExpectToken(ZScriptTokenType.Dot);
                    if (dot is not { IsValid: true })
                    {
                        _stream.Position = cpos;
                        break;
                    }

                    var identifier = _tokenizer.ExpectToken(ZScriptTokenType.Identifier);
                    if (identifier is not { IsValid: true })
                    {
                        _parser.ReportError($"Expected identifier, got {(object?)identifier ?? "<null>"}");
                        return null;
                    }
                }
            }

            dimensions.Add(length);

            _tokenizer.SkipWhitespace();
            var close = _tokenizer.ExpectToken(ZScriptTokenType.CloseSquare);
            if (close is not { IsValid: true })
            {
                _parser.ReportError($"Expected ], got {(object?)close ?? "<null>"}");
                return null;
            }
        }
    }

    private bool ParseFlagdef()
    {
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.Identifier) is not { IsValid: true }) { _parser.ReportError("Expected flag name"); return false; }
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.Colon) is not { IsValid: true }) { _parser.ReportError("Expected :"); return false; }
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.Identifier) is not { IsValid: true }) { _parser.ReportError("Expected flag base variable"); return false; }
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.Comma) is not { IsValid: true }) { _parser.ReportError("Expected comma"); return false; }
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.Integer) is not { IsValid: true }) { _parser.ReportError("Expected flag bit index"); return false; }
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.Semicolon) is not { IsValid: true }) { _parser.ReportError("Expected semicolon"); return false; }
        return true;
    }

    private bool ParseMixin()
    {
        _tokenizer.SkipWhitespace();
        var token = _tokenizer.ExpectToken(ZScriptTokenType.Identifier);
        if (token is not { IsValid: true }) { _parser.ReportError($"Expected mixin class name, got {(object?)token ?? "<null>"}"); return false; }

        Mixins.Add(token.Value.ToLowerInvariant());

        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.Semicolon) is not { IsValid: true }) { _parser.ReportError("Expected semicolon"); return false; }
        return true;
    }

    private string? ParseFunctionPointer()
    {
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.OpLessThan) is not { IsValid: true }) { _parser.ReportError("Expected '<' after 'Function'"); return null; }

        _tokenizer.SkipWhitespace();
        var token = _tokenizer.ExpectToken(ZScriptTokenType.Identifier);
        string? scope = null;
        if (token is { IsValid: true })
        {
            var value = token.Value.ToLowerInvariant();
            if (value == "void")
            {
                _tokenizer.SkipWhitespace();
                if (_tokenizer.ExpectToken(ZScriptTokenType.OpGreaterThan) is not { IsValid: true }) { _parser.ReportError("Expected '>' after void"); return null; }
                return "Function<void>";
            }

            if (value is "play" or "ui" or "clearscope") scope = value;
            else { _parser.ReportError("Expected function scope or 'void'"); return null; }
        }

        var returnTypes = new List<string>();
        while (true)
        {
            _tokenizer.SkipWhitespace();
            var returnType = ParseTypeName();
            if (returnType == null) return null;
            returnTypes.Add(returnType);

            _tokenizer.SkipWhitespace();
            token = _tokenizer.ExpectToken(ZScriptTokenType.Comma, ZScriptTokenType.OpenParen);
            if (token is not { IsValid: true }) { _parser.ReportError("Expected ',' or '('"); return null; }
            if (token.Type == ZScriptTokenType.OpenParen) break;
        }

        var argumentTypes = new List<string>();
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.CloseParen) is not { IsValid: true })
        {
            while (true)
            {
                _tokenizer.SkipWhitespace();
                var argType = ParseTypeName();
                if (argType == null) return null;
                argumentTypes.Add(argType);

                _tokenizer.SkipWhitespace();
                token = _tokenizer.ExpectToken(ZScriptTokenType.Comma, ZScriptTokenType.CloseParen);
                if (token is not { IsValid: true }) { _parser.ReportError("Expected ',' or ')'"); return null; }
                if (token.Type == ZScriptTokenType.CloseParen) break;
            }
        }

        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.OpGreaterThan) is not { IsValid: true }) { _parser.ReportError("Expected '>' after argument list"); return null; }

        return $"Function<{scope} {string.Join(", ", returnTypes)}({string.Join(", ", argumentTypes)})>";
    }

    private string? ParseGenericTemplate(string genericType)
    {
        _tokenizer.SkipWhitespace();
        var innerType = ParseTypeName();
        if (innerType == null) return null;

        _tokenizer.SkipWhitespace();
        var token = _tokenizer.ReadToken();
        if (token == null || (token.Type != ZScriptTokenType.OpGreaterThan && token.Type != ZScriptTokenType.Comma))
        {
            _parser.ReportError($"Expected > or ,, got {(object?)token ?? "<null>"}");
            return null;
        }

        if (token.Type == ZScriptTokenType.OpGreaterThan) return $"{genericType}<{innerType}>";

        _tokenizer.SkipWhitespace();
        var secondInnerType = ParseTypeName();
        if (secondInnerType == null) return null;

        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.OpGreaterThan) is not { IsValid: true }) { _parser.ReportError("Expected >"); return null; }

        return $"{genericType}<{innerType},{secondInnerType}>";
    }

    private bool ParseProperty()
    {
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.Identifier) is not { IsValid: true }) { _parser.ReportError("Expected property name"); return false; }
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.Colon) is not { IsValid: true }) { _parser.ReportError("Expected :"); return false; }

        while (true)
        {
            _tokenizer.SkipWhitespace();
            if (_tokenizer.ExpectToken(ZScriptTokenType.Identifier) is not { IsValid: true }) { _parser.ReportError("Expected variable"); return false; }

            _tokenizer.SkipWhitespace();
            var token = _tokenizer.ExpectToken(ZScriptTokenType.Semicolon, ZScriptTokenType.Comma);
            if (token is not { IsValid: true }) { _parser.ReportError("Expected comma or ;"); return false; }
            if (token.Type == ZScriptTokenType.Semicolon) break;
        }

        return true;
    }

    private string? ParseVersion(bool required)
    {
        _tokenizer.SkipWhitespace();
        var token = _tokenizer.ExpectToken(ZScriptTokenType.OpenParen);
        if (token is not { IsValid: true })
        {
            if (required) _parser.ReportError($"Expected (, got {(object?)token ?? "<null>"}");
            return null;
        }

        _tokenizer.SkipWhitespace();
        token = _tokenizer.ExpectToken(ZScriptTokenType.String);
        if (token is not { IsValid: true }) { _parser.ReportError("Expected version"); return null; }

        var version = token.Value.Trim();
        _tokenizer.SkipWhitespace();

        token = _tokenizer.ExpectToken(ZScriptTokenType.CloseParen, ZScriptTokenType.Comma);
        if (token is not { IsValid: true }) { _parser.ReportError("Expected ) or comma"); return null; }
        if (token.Type == ZScriptTokenType.CloseParen) return version;

        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.String) is not { IsValid: true }) { _parser.ReportError("Expected helper message string"); return null; }

        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.CloseParen) is not { IsValid: true }) { _parser.ReportError("Expected )"); return null; }

        return version;
    }

    private string? ParseAction()
    {
        string[] contexts = { "actor", "overlay", "weapon", "item" };
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.OpenParen) is not { IsValid: true }) return "default";

        _tokenizer.SkipWhitespace();
        var token = _tokenizer.ExpectToken(ZScriptTokenType.Identifier);
        if (token is not { IsValid: true } || !contexts.Contains(token.Value.ToLowerInvariant()))
        {
            _parser.ReportError($"Expected actor, overlay, weapon, or item, got {(object?)token ?? "<null>"}");
            return null;
        }

        var context = token.Value.Trim();
        _tokenizer.SkipWhitespace();
        if (_tokenizer.ExpectToken(ZScriptTokenType.CloseParen) is not { IsValid: true }) { _parser.ReportError("Expected )"); return null; }
        return context;
    }
}
