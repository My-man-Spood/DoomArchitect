using System.Text;
using DoomArchitect.Core.ZDoom.Bcs;

namespace DoomArchitect.Core.Tests.ZDoom.Bcs;

public class BcsTokenizerTests
{
    private static (BcsTokenizer Tokenizer, List<BcsDiagnostic> Diagnostics) Create(string source)
    {
        var diagnostics = new List<BcsDiagnostic>();
        var tokenizer = new BcsTokenizer(new BinaryReader(new MemoryStream(Encoding.UTF8.GetBytes(source))), diagnostics);
        return (tokenizer, diagnostics);
    }

    private static List<BcsToken> ReadAll(BcsTokenizer tokenizer)
    {
        var tokens = new List<BcsToken>();
        while (true)
        {
            var tok = tokenizer.ReadToken();
            if (tok.Type == BcsTokenType.EndOfInput) break;
            tokens.Add(tok);
        }
        return tokens;
    }

    private static List<BcsToken> ReadAllSignificant(BcsTokenizer tokenizer, bool includeNewlines = false)
    {
        var tokens = new List<BcsToken>();
        while (true)
        {
            var tok = tokenizer.NextSignificantToken(includeNewlines);
            if (tok.Type == BcsTokenType.EndOfInput) break;
            tokens.Add(tok);
        }
        return tokens;
    }

    [Fact]
    public void ReadToken_TokenizesAScriptDeclaration()
    {
        var (tokenizer, _) = Create("script 1 open\n{\n}\n");
        var tokens = ReadAllSignificant(tokenizer, includeNewlines: true);

        Assert.Equal(BcsTokenType.Script, tokens[0].Type);
        Assert.Equal(BcsTokenType.LitDecimal, tokens[1].Type);
        Assert.Equal(1, tokens[1].IntValue);
        Assert.Equal(BcsTokenType.Identifier, tokens[2].Type);
        Assert.Equal("open", tokens[2].Value);
        Assert.Equal(BcsTokenType.Newline, tokens[3].Type);
        Assert.Equal(BcsTokenType.OpenCurly, tokens[4].Type);
        Assert.Equal(BcsTokenType.Newline, tokens[5].Type);
        Assert.Equal(BcsTokenType.CloseCurly, tokens[6].Type);
    }

    [Fact]
    public void ReadToken_BlockComment_DoesNotNest()
    {
        // Matches this project's own ZScriptTokenizer quirk, confirmed separately for real in zt-bcc's source: "/*" inside a block comment does nothing special - the first "*/" always closes the outermost comment.
        var (tokenizer, diagnostics) = Create("/* outer /* inner */ after */");
        var tokens = ReadAll(tokenizer);

        Assert.Equal(BcsTokenType.BlockComment, tokens[0].Type);
        Assert.Equal(" outer /* inner ", tokens[0].Value);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ReadToken_UnterminatedBlockComment_ReportsAFatalDiagnostic()
    {
        var (tokenizer, diagnostics) = Create("/* never closes");
        var token = tokenizer.ReadToken();

        Assert.False(token.IsValid);
        Assert.Contains(diagnostics, d => d.Message == "unterminated comment" && d.Severity == BcsDiagnosticSeverity.Error);
    }

    [Fact]
    public void ReadToken_UnterminatedString_ReportsAFatalDiagnostic()
    {
        var (tokenizer, diagnostics) = Create("\"never closes");
        var token = tokenizer.ReadToken();

        Assert.False(token.IsValid);
        Assert.Contains(diagnostics, d => d.Message == "unterminated string" && d.Severity == BcsDiagnosticSeverity.Error);
    }

    [Fact]
    public void ReadToken_LineComment_DoesNotConsumeTrailingNewline()
    {
        var (tokenizer, _) = Create("// a comment\n");

        Assert.Equal(BcsTokenType.LineComment, tokenizer.ReadToken().Type);
        Assert.Equal(BcsTokenType.Newline, tokenizer.ReadToken().Type);
    }

    [Theory]
    [InlineData("010", 10)] // a bare leading zero is decimal, NOT octal - a real, confirmed divergence from both C and this project's own ZScriptTokenizer
    [InlineData("0", 0)]
    [InlineData("12345", 12345)]
    public void ReadToken_LeadingZeroDecimal_IsNotOctal(string source, int expected)
    {
        var (tokenizer, _) = Create(source);
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitDecimal, token.Type);
        Assert.Equal(expected, token.IntValue);
    }

    [Fact]
    public void ReadToken_ExplicitOctalPrefix_ParsesAsOctal()
    {
        var (tokenizer, _) = Create("0o17");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitOctal, token.Type);
        Assert.Equal(15, token.IntValue);
    }

    [Fact]
    public void ReadToken_BinaryLiteral_ParsesCorrectly()
    {
        var (tokenizer, _) = Create("0b101");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitBinary, token.Type);
        Assert.Equal(5, token.IntValue);
    }

    [Fact]
    public void ReadToken_HexLiteral_ParsesCorrectly()
    {
        var (tokenizer, _) = Create("0x1F");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitHex, token.Type);
        Assert.Equal(31, token.IntValue);
    }

    [Theory]
    [InlineData("0x1F", 4)] // Value is "1F" (2 chars) - Length must still cover the "0x" prefix
    [InlineData("1'000", 5)] // Value is "1000" (4 chars) - Length must still cover the digit separator
    [InlineData("myvar", 5)]
    [InlineData("SCRIPT", 6)] // Value is lowercased to "script" - same length, but confirms Length isn't accidentally always right by coincidence of case-folding not changing length
    public void ReadToken_Length_ReflectsTheRealSourceSpanNotValueLength(string source, int expectedLength)
    {
        var (tokenizer, _) = Create(source);
        var token = tokenizer.ReadToken();

        Assert.Equal(expectedLength, token.Length);
    }

    [Theory]
    [InlineData("1'000", 1000)]
    [InlineData("1'000'000", 1000000)]
    public void ReadToken_DigitSeparator_IsIgnored(string source, int expected)
    {
        var (tokenizer, diagnostics) = Create(source);
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitDecimal, token.Type);
        Assert.Equal(expected, token.IntValue);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ReadToken_EmptyHexLiteral_WarnsAndDefaultsToZero()
    {
        var (tokenizer, diagnostics) = Create("0x");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitHex, token.Type);
        Assert.Equal(0, token.IntValue);
        Assert.Contains(diagnostics, d => d.Severity == BcsDiagnosticSeverity.Warning);
    }

    [Fact]
    public void ReadToken_EmptyBinaryLiteral_IsFatal()
    {
        var (tokenizer, diagnostics) = Create("0b");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitBinary, token.Type);
        Assert.Contains(diagnostics, d => d.Message == "binary literal has no digits" && d.Severity == BcsDiagnosticSeverity.Error);
    }

    [Fact]
    public void ReadToken_FixedPointLiteral_ParsesCorrectly()
    {
        var (tokenizer, _) = Create("1.5");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitFixed, token.Type);
        Assert.Equal(1.5, token.DoubleValue);
    }

    [Fact]
    public void ReadToken_RadixLiteral_ParsesWithEitherSeparator()
    {
        var (tokenizer1, _) = Create("16_ff");
        Assert.Equal(BcsTokenType.LitRadix, tokenizer1.ReadToken().Type);

        var (tokenizer2, _) = Create("16rff");
        Assert.Equal(BcsTokenType.LitRadix, tokenizer2.ReadToken().Type);
    }

    [Fact]
    public void ReadToken_Identifier_IsCaseFolded()
    {
        // Real bcc lowercases identifiers in place before keyword lookup - BCS identifiers, keywords included, are case-insensitive, unlike this project's own ZScriptTokenizer.
        var (tokenizer, _) = Create("MyVar");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.Identifier, token.Type);
        Assert.Equal("myvar", token.Value);
    }

    [Fact]
    public void ReadToken_Identifier_RawValuePreservesOriginalCasing()
    {
        // Value is case-folded for grammar/lookup purposes, but a completion provider offering a user's own declared name back to them needs their actual spelling, not a re-cased one.
        var (tokenizer, _) = Create("MyVar");
        var token = tokenizer.ReadToken();

        Assert.Equal("MyVar", token.RawValue);
    }

    [Fact]
    public void ReadToken_Keyword_RawValueIsEmpty()
    {
        // Keywords always display canonically lowercase - RawValue is only meaningful for Identifier/TypeName.
        var (tokenizer, _) = Create("SCRIPT");
        var token = tokenizer.ReadToken();

        Assert.Equal(string.Empty, token.RawValue);
    }

    [Fact]
    public void ReadToken_UppercaseKeyword_IsRecognizedAndLowercased()
    {
        var (tokenizer, _) = Create("SCRIPT");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.Script, token.Type);
        Assert.Equal("script", token.Value);
    }

    [Theory]
    [InlineData("xT")]
    [InlineData("foo_T")]
    public void ReadToken_TypeNameSuffix_LowercaseLetterOrUnderscorePlusT_IsRecognized(string source)
    {
        var (tokenizer, _) = Create(source);
        Assert.Equal(BcsTokenType.TypeName, tokenizer.ReadToken().Type);
    }

    [Fact]
    public void ReadToken_TypeNameLoneT_IsRecognized()
    {
        var (tokenizer, _) = Create("T");
        Assert.Equal(BcsTokenType.TypeName, tokenizer.ReadToken().Type);
    }

    [Fact]
    public void ReadToken_UppercaseTAfterUppercaseLetter_IsNotATypeName()
    {
        // The real rule requires the second-to-last character to be lowercase or '_' - "XT" (uppercase before T) doesn't qualify.
        var (tokenizer, _) = Create("XT");
        Assert.Equal(BcsTokenType.Identifier, tokenizer.ReadToken().Type);
    }

    [Fact]
    public void ReadToken_StringLiteral_KeepsEscapeSequencesVerbatim()
    {
        var (tokenizer, _) = Create("\"a\\nb\"");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitString, token.Type);
        Assert.Equal("a\\nb", token.Value);
    }

    [Fact]
    public void ReadToken_CharLiteral_InterpretsEscapeSequences()
    {
        var (tokenizer, _) = Create("'\\n'");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitChar, token.Type);
        Assert.Equal('\n', token.IntValue);
    }

    [Fact]
    public void ReadToken_CharLiteral_InterpretsOctalEscape()
    {
        var (tokenizer, _) = Create("'\\101'"); // octal 101 = 65 = 'A'
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitChar, token.Type);
        Assert.Equal('A', token.IntValue);
    }

    [Fact]
    public void ReadToken_CharLiteral_InterpretsHexEscape()
    {
        var (tokenizer, _) = Create("'\\x41'"); // hex 41 = 65 = 'A'
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.LitChar, token.Type);
        Assert.Equal('A', token.IntValue);
    }

    [Theory]
    [InlineData("<<=", BcsTokenType.OpAssignLeftShift)]
    [InlineData("<<", BcsTokenType.OpLeftShift)]
    [InlineData("<=", BcsTokenType.OpLessOrEqual)]
    [InlineData("<", BcsTokenType.OpLessThan)]
    public void ReadToken_LongestMatchFirst_DisambiguatesShiftAssignFromShiftFromLessThan(string source, BcsTokenType expected)
    {
        var (tokenizer, _) = Create(source);
        Assert.Equal(expected, tokenizer.ReadToken().Type);
    }

    [Fact]
    public void ReadToken_LogicalAndBitwiseAnd_AreDistinctTokens()
    {
        var (tokenizer, _) = Create("&& &");
        Assert.Equal(BcsTokenType.OpLogicalAnd, tokenizer.ReadToken().Type);
        tokenizer.ReadToken(); // whitespace
        Assert.Equal(BcsTokenType.OpBitAnd, tokenizer.ReadToken().Type);
    }

    [Fact]
    public void ReadToken_Ellipsis_IsRecognizedAsOneToken()
    {
        var (tokenizer, _) = Create("...");
        var token = tokenizer.ReadToken();

        Assert.Equal(BcsTokenType.Ellipsis, token.Type);
        Assert.Equal("...", token.Value);
    }

    [Fact]
    public void ReadToken_InvalidCharacter_ReportsADiagnosticAndContinues()
    {
        var (tokenizer, diagnostics) = Create("`");
        var token = tokenizer.ReadToken();

        Assert.False(token.IsValid);
        Assert.Contains(diagnostics, d => d.Message == "invalid character");
        Assert.Equal(BcsTokenType.EndOfInput, tokenizer.ReadToken().Type);
    }

    [Fact]
    public void ReservedWordTexts_HasExactlyTheRealCompilersFiftyThreeEntries()
    {
        Assert.Equal(53, BcsTokenizer.ReservedWordTexts.Count);
    }

    [Theory]
    [InlineData("script")]
    [InlineData("foreach")]
    [InlineData("createtranslation")] // the real spelling for BcsTokenType.PalTrans
    public void ReservedWordTexts_ContainsTheRealKeywordSpelling(string keyword)
    {
        Assert.Contains(keyword, BcsTokenizer.ReservedWordTexts);
    }

    [Fact]
    public void ReservedWordTexts_MatchesReservedWordTypesOneForOne()
    {
        // Built together in the same reflection pass - a mismatch here would mean the two lists drifted apart.
        Assert.Equal(BcsTokenizer.ReservedWordTypes.Count, BcsTokenizer.ReservedWordTexts.Count);
    }
}
