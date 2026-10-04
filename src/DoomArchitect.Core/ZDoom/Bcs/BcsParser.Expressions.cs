namespace DoomArchitect.Core.ZDoom.Bcs;

/// <summary>
/// A real, if deliberately bounded, expression grammar - confirmed
/// against <c>zt-bcc</c>'s own <c>src/parse/expr.c</c> (not guessed),
/// used only for variable declaration initializers/array sizes
/// (<see cref="BcsParser.ParseDeclarator"/>, both top-level and
/// body-local). Nothing downstream needs an expression's *structure* -
/// completion/hover/go-to-definition are built entirely on
/// <c>DeclarationScanner</c>'s declaration-shaped pattern matching and
/// <c>BcsWordScanner</c>'s word-under-cursor matching, neither of which
/// cares what's inside an expression - so every method here is purely
/// a *validator*: it consumes tokens and reports a diagnostic on a real
/// structural problem, returning <c>false</c> once it has (so a caller
/// doesn't cascade further diagnostics for the same failure), but never
/// builds any expression-AST node. This is what lets `int x = ;` (and
/// similar) finally get flagged - the entire motivation for this file -
/// without needing a tree nothing would ever read.
///
/// Real precedence chain, low to high (confirmed from <c>read_op</c>'s
/// own goto-chain in <c>expr.c</c>, read line by line, not assumed):
/// assignment (right-assoc) → ternary <c>?:</c> (right-assoc; the
/// middle operand is optional - <c>a ?: b</c> is real, confirmed Elvis-
/// style grammar) → <c>||</c> → <c>&amp;&amp;</c> → <c>|</c> → <c>^</c>
/// → <c>&amp;</c> → <c>==</c>/<c>!=</c> → <c>&lt;</c>/<c>&lt;=</c>/
/// <c>&gt;</c>/<c>&gt;=</c> → <c>&lt;&lt;</c>/<c>&gt;&gt;</c> →
/// <c>+</c>/<c>-</c> → <c>*</c>/<c>/</c>/<c>%</c> → prefix unary
/// (<c>-</c> <c>+</c> <c>!</c> <c>~</c> <c>++</c> <c>--</c>) → postfix
/// (<c>[expr]</c> subscript, <c>.name</c> access, <c>(args)</c> call,
/// postfix <c>++</c>/<c>--</c>) → primary (identifier, every literal
/// kind the tokenizer already produces, <c>true</c>/<c>false</c>/
/// <c>null</c>, <c>( expr )</c>, and a <c>TYPE ( expr )</c> conversion
/// call - confirmed from <c>expr.c</c>'s own <c>read_conversion</c>,
/// common enough in real code - e.g. <c>int(fixedValue)</c> - to
/// support rather than false-positive on).
///
/// Deliberately excluded, documented not hidden, each confirmed real
/// but narrow/rare (supporting them would mean chasing most of the
/// remaining ~1000 lines of <c>expr.c</c> for very little real payoff):
/// <c>lengthof</c>/<c>strcpy</c>/<c>memcpy</c> builtins; <c>::</c>-
/// qualified names, <c>upmost</c>, <c>namespace</c> as primaries;
/// compound literals/func literals; postfix <c>!</c> (the real "sure"
/// operator - distinct from prefix logical-not, which is supported);
/// the parenthesized 3-argument array-field form of the
/// <c>a:</c> format cast (<c>a:(array, offset, length)</c> - the plain
/// <c>a:array</c> shape works fine, only this one nested sub-form
/// doesn't).
///
/// One real ambiguity that *is* handled: a call argument can start
/// <c>identifier ':'</c> - confirmed from <c>expr.c</c>'s own
/// <c>peek_format_cast</c> (<c>tk == TK_ID &amp;&amp; peek == TK_COLON</c>),
/// used by every <c>print</c>/<c>log</c>/<c>hudmessage</c>-style call
/// (<c>print(s:"text", d:value)</c>) - common, valid code a naive
/// parser would choke on at the bare <c>:</c>. <see cref="BcsTokenizer"/>
/// never supports forward lookahead (confirmed elsewhere in this
/// parser), so this is handled as a post-hoc *reinterpretation* instead
/// of a peek: the tag identifier is parsed as an ordinary expression
/// first (harmless - a bare identifier bottoms out immediately,
/// consuming exactly one token either way), and only if a <c>:</c>
/// is left over right after is it treated as "that was actually a
/// format-cast tag, not the real value" and a second, real expression
/// parsed after it. A bare <c>:</c> can only ever appear there in two
/// shapes - a ternary's own (fully consumed internally by
/// <see cref="ParseTernary"/> before ever returning) or this tag - so
/// there's no real ambiguity to resolve, just this reordering.
/// </summary>
public sealed partial class BcsParser
{
    private bool ParseExpression() => ParseAssignment();

    private static readonly BcsTokenType[] AssignmentOperators =
    {
        BcsTokenType.OpAssign, BcsTokenType.OpAssignAdd, BcsTokenType.OpAssignSubtract, BcsTokenType.OpAssignMultiply,
        BcsTokenType.OpAssignDivide, BcsTokenType.OpAssignMod, BcsTokenType.OpAssignLeftShift, BcsTokenType.OpAssignRightShift,
        BcsTokenType.OpAssignBitAnd, BcsTokenType.OpAssignBitXor, BcsTokenType.OpAssignBitOr,
    };

    /// <summary>Lowest precedence, right-associative - <c>a = b = c</c> is <c>a = (b = c)</c>.</summary>
    private bool ParseAssignment()
    {
        if (!ParseTernary()) return false;
        if (Array.IndexOf(AssignmentOperators, _current.Type) < 0) return true;

        Advance();
        return ParseAssignment();
    }

    /// <summary>
    /// <c>a ? b : c</c>, right-associative, with the middle operand
    /// optional (<c>a ?: b</c> - confirmed real "Elvis" grammar from
    /// <c>expr.c</c>'s own <c>read_op</c>, not a typo in my reading of
    /// it).
    /// </summary>
    private bool ParseTernary()
    {
        if (!ParseLogicalOr()) return false;
        if (_current.Type != BcsTokenType.Questionmark) return true;

        Advance();
        if (_current.Type != BcsTokenType.Colon && !ParseExpression()) return false;

        if (_current.Type != BcsTokenType.Colon)
        {
            AddDiagnostic("expected ':'", _current);
            return false;
        }

        Advance();
        return ParseExpression();
    }

    private bool ParseLogicalOr() => ParseLeftAssociative(ParseLogicalAnd, BcsTokenType.OpLogicalOr);
    private bool ParseLogicalAnd() => ParseLeftAssociative(ParseBitwiseOr, BcsTokenType.OpLogicalAnd);
    private bool ParseBitwiseOr() => ParseLeftAssociative(ParseBitwiseXor, BcsTokenType.OpBitOr);
    private bool ParseBitwiseXor() => ParseLeftAssociative(ParseBitwiseAnd, BcsTokenType.OpBitXor);
    private bool ParseBitwiseAnd() => ParseLeftAssociative(ParseEquality, BcsTokenType.OpBitAnd);
    private bool ParseEquality() => ParseLeftAssociative(ParseRelational, BcsTokenType.OpEquals, BcsTokenType.OpNotEquals);

    private bool ParseRelational() => ParseLeftAssociative(ParseShift,
        BcsTokenType.OpLessThan, BcsTokenType.OpLessOrEqual, BcsTokenType.OpGreaterThan, BcsTokenType.OpGreaterOrEqual);

    private bool ParseShift() => ParseLeftAssociative(ParseAdditive, BcsTokenType.OpLeftShift, BcsTokenType.OpRightShift);
    private bool ParseAdditive() => ParseLeftAssociative(ParseMultiplicative, BcsTokenType.OpAdd, BcsTokenType.OpSubtract);
    private bool ParseMultiplicative() => ParseLeftAssociative(ParseUnary, BcsTokenType.OpMultiply, BcsTokenType.OpDivide, BcsTokenType.OpMod);

    /// <summary>Shared shape for every left-associative binary precedence level: one operand at the next-higher level, then zero or more <c>(operator, operand)</c> pairs at this one.</summary>
    private bool ParseLeftAssociative(Func<bool> nextLevel, params BcsTokenType[] operators)
    {
        if (!nextLevel()) return false;

        while (Array.IndexOf(operators, _current.Type) >= 0)
        {
            Advance();
            if (!nextLevel()) return false;
        }

        return true;
    }

    private static readonly BcsTokenType[] PrefixOperators =
    {
        BcsTokenType.OpSubtract, BcsTokenType.OpAdd, BcsTokenType.OpLogicalNot, BcsTokenType.OpBitNot,
        BcsTokenType.Increment, BcsTokenType.Decrement,
    };

    /// <summary>Right-recursive so stacked prefixes (<c>--a</c>, <c>!!a</c>) work the same as the real grammar's own recursive <c>read_prefix</c>.</summary>
    private bool ParseUnary()
    {
        if (Array.IndexOf(PrefixOperators, _current.Type) < 0) return ParsePostfix();

        Advance();
        return ParseUnary();
    }

    private bool ParsePostfix()
    {
        if (!ParsePrimary()) return false;

        while (true)
        {
            switch (_current.Type)
            {
                case BcsTokenType.OpenSquare:
                    Advance();
                    if (!ParseExpression()) return false;
                    if (_current.Type != BcsTokenType.CloseSquare) { AddDiagnostic("expected ']'", _current); return false; }
                    Advance();
                    break;

                case BcsTokenType.Dot:
                    Advance();
                    if (_current.Type != BcsTokenType.Identifier) { AddDiagnostic("expected a member name after '.'", _current); return false; }
                    Advance();
                    break;

                case BcsTokenType.OpenParen:
                    if (!ParseCallArguments()) return false;
                    break;

                case BcsTokenType.Increment:
                case BcsTokenType.Decrement:
                    Advance();
                    break;

                default:
                    return true;
            }
        }
    }

    /// <summary>Consumes the balanced <c>( ... )</c> of a call, comma-separated arguments - each one optionally tagged <c>identifier ':'</c> first (see this class's own remarks on the format-cast ambiguity).</summary>
    private bool ParseCallArguments()
    {
        Advance(); // '('
        if (_current.Type == BcsTokenType.CloseParen) { Advance(); return true; }

        while (true)
        {
            if (!ParseExpression()) return false;

            if (_current.Type == BcsTokenType.Colon) // the expression just parsed was actually a format-cast tag, not the real value
            {
                Advance();
                if (!ParseExpression()) return false;
            }

            if (_current.Type == BcsTokenType.Comma) { Advance(); continue; }
            break;
        }

        if (_current.Type != BcsTokenType.CloseParen)
        {
            AddDiagnostic("expected ')'", _current);
            return false;
        }

        Advance();
        return true;
    }

    private static readonly BcsTokenType[] NumericLiteralTypes =
    {
        BcsTokenType.LitDecimal, BcsTokenType.LitOctal, BcsTokenType.LitHex,
        BcsTokenType.LitBinary, BcsTokenType.LitFixed, BcsTokenType.LitRadix,
    };

    private bool ParsePrimary()
    {
        if (_current.Type is BcsTokenType.Identifier or BcsTokenType.TypeName)
        {
            Advance();
            return true;
        }

        if (Array.IndexOf(NumericLiteralTypes, _current.Type) >= 0 || _current.Type is BcsTokenType.LitChar or BcsTokenType.LitString)
        {
            Advance();
            return true;
        }

        if (_current.Type is BcsTokenType.True or BcsTokenType.False or BcsTokenType.Null)
        {
            Advance();
            return true;
        }

        if (_current.Type == BcsTokenType.OpenParen)
        {
            Advance();
            if (!ParseExpression()) return false;
            if (_current.Type != BcsTokenType.CloseParen) { AddDiagnostic("expected ')'", _current); return false; }
            Advance();
            return true;
        }

        // TYPE ( expr ) conversion call - e.g. int(fixedValue) - confirmed from expr.c's own read_conversion.
        if (_current.Type is BcsTokenType.Int or BcsTokenType.Str or BcsTokenType.Bool or BcsTokenType.Fixed or BcsTokenType.Char)
        {
            Advance();
            if (_current.Type != BcsTokenType.OpenParen) { AddDiagnostic("expected '(' after a type conversion", _current); return false; }
            return ParseCallArguments();
        }

        AddDiagnostic($"expected an expression, got '{_current.Value}'", _current);
        return false;
    }

    /// <summary>
    /// After a failed <see cref="ParseExpression"/> (or a declarator with
    /// no name at all), skips forward to the next top-level <c>,</c> or
    /// <c>;</c> - not consumed, left for the caller (a declarator loop)
    /// to see and act on - or a <c>}</c>/end of input, same "leave it
    /// for the enclosing block to see" philosophy <see cref="Recover"/>
    /// already uses at the top level. This is what keeps one malformed
    /// initializer from cascading into bogus diagnostics for every
    /// declarator/statement after it.
    /// </summary>
    private void RecoverExpression()
    {
        var depth = 0;
        while (_current.Type != BcsTokenType.EndOfInput)
        {
            if (depth == 0 && _current.Type is BcsTokenType.Comma or BcsTokenType.Semicolon or BcsTokenType.CloseCurly) return;

            if (_current.Type is BcsTokenType.OpenCurly or BcsTokenType.OpenParen or BcsTokenType.OpenSquare) depth++;
            else if (_current.Type is BcsTokenType.CloseCurly or BcsTokenType.CloseParen or BcsTokenType.CloseSquare) depth--;

            Advance();
        }
    }
}
