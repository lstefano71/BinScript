namespace BinScript.Tests.Compiler;

using BinScript.Core.Compiler;

public class LexerTests
{
    // ─────────────────────── Helpers ───────────────────────

    private static Token Single(string source)
    {
        var lexer = new Lexer(source);
        return lexer.NextToken();
    }

    private static List<Token> All(string source) => new Lexer(source).TokenizeAll();

    private static void AssertToken(Token tok, TokenType type, string text)
    {
        Assert.Equal(type, tok.Type);
        Assert.Equal(text, tok.Text);
    }

    // ─────────────────────── Integer Literals ───────────────────────

    [Theory]
    [InlineData("0", "0")]
    [InlineData("42", "42")]
    [InlineData("12345", "12345")]
    public void Lex_IntLiteral_Decimal(string source, string expected)
    {
        var tok = Single(source);
        AssertToken(tok, TokenType.IntLiteral, expected);
        Assert.Equal(0, tok.Span.Start);
        Assert.Equal(expected.Length, tok.Span.Length);
    }

    [Theory]
    [InlineData("0xFF", "0xFF")]
    [InlineData("0x00004550", "0x00004550")]
    [InlineData("0xAA64", "0xAA64")]
    [InlineData("0X1a", "0X1a")]
    public void Lex_IntLiteral_Hex(string source, string expected)
    {
        var tok = Single(source);
        AssertToken(tok, TokenType.IntLiteral, expected);
    }

    [Theory]
    [InlineData("0b10110", "0b10110")]
    [InlineData("0b0", "0b0")]
    [InlineData("0B1", "0B1")]
    public void Lex_IntLiteral_Binary(string source, string expected)
    {
        var tok = Single(source);
        AssertToken(tok, TokenType.IntLiteral, expected);
    }

    [Theory]
    [InlineData("0o755", "0o755")]
    [InlineData("0o0", "0o0")]
    [InlineData("0O17", "0O17")]
    public void Lex_IntLiteral_Octal(string source, string expected)
    {
        var tok = Single(source);
        AssertToken(tok, TokenType.IntLiteral, expected);
    }

    // ─────────────────────── String Literals ───────────────────────

    [Fact]
    public void Lex_StringLiteral_Simple()
    {
        var tok = Single("\"hello\"");
        AssertToken(tok, TokenType.StringLiteral, "hello");
    }

    [Fact]
    public void Lex_StringLiteral_Empty()
    {
        var tok = Single("\"\"");
        AssertToken(tok, TokenType.StringLiteral, "");
    }

    [Fact]
    public void Lex_StringLiteral_Escapes()
    {
        // \n \t \0 \\ \" \r \xff
        var tok = Single("\"\\n\\t\\0\\\\\\\"\\r\\xff\"");
        AssertToken(tok, TokenType.StringLiteral, "\n\t\0\\\"\r\xff");
    }

    [Fact]
    public void Lex_StringLiteral_HexEscape()
    {
        var tok = Single("\"\\x41\""); // 0x41 == 'A'
        AssertToken(tok, TokenType.StringLiteral, "A");
    }

    [Fact]
    public void Lex_StringLiteral_Unterminated()
    {
        var tok = Single("\"hello");
        Assert.Equal(TokenType.Error, tok.Type);
        Assert.Equal("Unterminated string", tok.Text);
    }

    [Fact]
    public void Lex_StringLiteral_UnterminatedAtNewline()
    {
        var tok = Single("\"hello\nworld\"");
        Assert.Equal(TokenType.Error, tok.Type);
    }

    [Fact]
    public void Lex_StringLiteral_InvalidHexEscape()
    {
        var tok = Single("\"\\xZZ\"");
        Assert.Equal(TokenType.Error, tok.Type);
    }

    [Fact]
    public void Lex_StringLiteral_UnknownEscape()
    {
        var tok = Single("\"\\q\"");
        Assert.Equal(TokenType.Error, tok.Type);
        Assert.Contains("Unknown escape", tok.Text);
    }

    // ─────────────────────── Keywords ───────────────────────

    [Theory]
    [InlineData("struct", TokenType.Struct)]
    [InlineData("bits", TokenType.Bits)]
    [InlineData("enum", TokenType.Enum)]
    [InlineData("const", TokenType.Const)]
    [InlineData("match", TokenType.Match)]
    [InlineData("when", TokenType.When)]
    [InlineData("if", TokenType.If)]
    [InlineData("else", TokenType.Else)]
    [InlineData("bit", TokenType.Bit)]
    [InlineData("bytes", TokenType.Bytes)]
    [InlineData("cstring", TokenType.CString)]
    [InlineData("string", TokenType.String)]
    [InlineData("fixed_string", TokenType.FixedString)]
    [InlineData("bool", TokenType.Bool)]
    public void Lex_Keywords(string source, TokenType expected)
    {
        var tok = Single(source);
        Assert.Equal(expected, tok.Type);
        Assert.Equal(source, tok.Text);
    }

    [Theory]
    [InlineData("true", TokenType.TrueLiteral)]
    [InlineData("false", TokenType.FalseLiteral)]
    public void Lex_BoolLiterals(string source, TokenType expected)
    {
        var tok = Single(source);
        Assert.Equal(expected, tok.Type);
        Assert.Equal(source, tok.Text);
    }

    // ─────────────────────── Primitive Types ───────────────────────

    [Theory]
    [InlineData("u8", TokenType.U8)]
    [InlineData("u16", TokenType.U16)]
    [InlineData("u32", TokenType.U32)]
    [InlineData("u64", TokenType.U64)]
    [InlineData("i8", TokenType.I8)]
    [InlineData("i16", TokenType.I16)]
    [InlineData("i32", TokenType.I32)]
    [InlineData("i64", TokenType.I64)]
    [InlineData("f32", TokenType.F32)]
    [InlineData("f64", TokenType.F64)]
    [InlineData("u16le", TokenType.U16Le)]
    [InlineData("u16be", TokenType.U16Be)]
    [InlineData("u32le", TokenType.U32Le)]
    [InlineData("u32be", TokenType.U32Be)]
    [InlineData("i32le", TokenType.I32Le)]
    [InlineData("i32be", TokenType.I32Be)]
    [InlineData("f64le", TokenType.F64Le)]
    [InlineData("f64be", TokenType.F64Be)]
    public void Lex_PrimitiveTypes(string source, TokenType expected)
    {
        var tok = Single(source);
        Assert.Equal(expected, tok.Type);
        Assert.Equal(source, tok.Text);
    }

    // ─────────────────────── Directives ───────────────────────

    [Theory]
    [InlineData("@root", TokenType.Root)]
    [InlineData("@default_endian", TokenType.DefaultEndian)]
    [InlineData("@default_encoding", TokenType.DefaultEncoding)]
    [InlineData("@param", TokenType.Param)]
    [InlineData("@import", TokenType.Import)]
    [InlineData("@coverage", TokenType.Coverage)]
    [InlineData("@let", TokenType.Let)]
    [InlineData("@seek", TokenType.Seek)]
    [InlineData("@at", TokenType.At)]
    [InlineData("@align", TokenType.Align)]
    [InlineData("@skip", TokenType.Skip)]
    [InlineData("@hidden", TokenType.Hidden)]
    [InlineData("@derived", TokenType.Derived)]
    [InlineData("@assert", TokenType.Assert)]
    [InlineData("@encoding", TokenType.Encoding)]
    [InlineData("@until", TokenType.Until)]
    [InlineData("@until_sentinel", TokenType.UntilSentinel)]
    [InlineData("@greedy", TokenType.Greedy)]
    [InlineData("@input_size", TokenType.InputSize)]
    [InlineData("@offset", TokenType.Offset)]
    [InlineData("@remaining", TokenType.Remaining)]
    [InlineData("@sizeof", TokenType.SizeOf)]
    [InlineData("@offsetof", TokenType.OffsetOf)]
    [InlineData("@count", TokenType.Count)]
    [InlineData("@strlen", TokenType.StrLen)]
    [InlineData("@crc32", TokenType.Crc32)]
    [InlineData("@adler32", TokenType.Adler32)]
    public void Lex_Directives(string source, TokenType expected)
    {
        var tok = Single(source);
        Assert.Equal(expected, tok.Type);
        Assert.Equal(source, tok.Text);
    }

    [Theory]
    [InlineData("@foobar")]
    [InlineData("@notreal")]
    public void Lex_UnknownDirective(string source)
    {
        var tok = Single(source);
        Assert.Equal(TokenType.Error, tok.Type);
        Assert.Equal(source, tok.Text);
    }

    [Fact]
    public void Lex_BareAtSign()
    {
        var tok = Single("@");
        Assert.Equal(TokenType.Error, tok.Type);
        Assert.Equal("@", tok.Text);
    }

    // ─────────────────────── Operators ───────────────────────

    [Theory]
    [InlineData("+", TokenType.Plus)]
    [InlineData("-", TokenType.Minus)]
    [InlineData("*", TokenType.Star)]
    [InlineData("/", TokenType.Slash)]
    [InlineData("%", TokenType.Percent)]
    [InlineData("&", TokenType.Ampersand)]
    [InlineData("|", TokenType.Pipe)]
    [InlineData("^", TokenType.Caret)]
    [InlineData("~", TokenType.Tilde)]
    [InlineData("!", TokenType.Bang)]
    [InlineData("=", TokenType.Equal)]
    [InlineData("<", TokenType.Less)]
    [InlineData(">", TokenType.Greater)]
    public void Lex_SingleCharOperators(string source, TokenType expected)
    {
        var tok = Single(source);
        Assert.Equal(expected, tok.Type);
        Assert.Equal(source, tok.Text);
    }

    [Theory]
    [InlineData("=>", TokenType.Arrow)]
    [InlineData("==", TokenType.EqualEqual)]
    [InlineData("!=", TokenType.BangEqual)]
    [InlineData("<=", TokenType.LessEqual)]
    [InlineData(">=", TokenType.GreaterEqual)]
    [InlineData("<<", TokenType.LeftShift)]
    [InlineData(">>", TokenType.RightShift)]
    [InlineData("&&", TokenType.AmpersandAmpersand)]
    [InlineData("||", TokenType.PipePipe)]
    [InlineData("..", TokenType.DotDot)]
    public void Lex_TwoCharOperators(string source, TokenType expected)
    {
        var tok = Single(source);
        Assert.Equal(expected, tok.Type);
        Assert.Equal(source, tok.Text);
    }

    // ─────────────────────── Punctuation ───────────────────────

    [Theory]
    [InlineData("{", TokenType.LeftBrace)]
    [InlineData("}", TokenType.RightBrace)]
    [InlineData("(", TokenType.LeftParen)]
    [InlineData(")", TokenType.RightParen)]
    [InlineData("[", TokenType.LeftBracket)]
    [InlineData("]", TokenType.RightBracket)]
    [InlineData(",", TokenType.Comma)]
    [InlineData(":", TokenType.Colon)]
    [InlineData(";", TokenType.Semicolon)]
    [InlineData(".", TokenType.Dot)]
    public void Lex_Punctuation(string source, TokenType expected)
    {
        var tok = Single(source);
        Assert.Equal(expected, tok.Type);
        Assert.Equal(source, tok.Text);
    }

    // ─────────────────────── Identifiers ───────────────────────

    [Fact]
    public void Lex_Identifier()
    {
        var tok = Single("my_field");
        AssertToken(tok, TokenType.Identifier, "my_field");
    }

    [Fact]
    public void Lex_Identifier_StartingWithUnderscore()
    {
        var tok = Single("_reserved");
        AssertToken(tok, TokenType.Identifier, "_reserved");
    }

    [Fact]
    public void Lex_Identifier_WithDigits()
    {
        var tok = Single("field2");
        AssertToken(tok, TokenType.Identifier, "field2");
    }

    [Fact]
    public void Lex_Underscore_Wildcard()
    {
        var tok = Single("_");
        Assert.Equal(TokenType.Underscore, tok.Type);
        Assert.Equal("_", tok.Text);
    }

    // ─────────────────────── Comments ───────────────────────

    [Fact]
    public void Lex_LineComment()
    {
        var tokens = All("// this is a comment\nstruct");
        Assert.Equal(2, tokens.Count); // struct + Eof
        AssertToken(tokens[0], TokenType.Struct, "struct");
        Assert.Equal(TokenType.Eof, tokens[1].Type);
    }

    [Fact]
    public void Lex_LineComment_AtEof()
    {
        var tokens = All("42 // trailing");
        Assert.Equal(2, tokens.Count);
        AssertToken(tokens[0], TokenType.IntLiteral, "42");
    }

    [Fact]
    public void Lex_BlockComment()
    {
        var tokens = All("/* comment */ struct");
        Assert.Equal(2, tokens.Count);
        AssertToken(tokens[0], TokenType.Struct, "struct");
    }

    [Fact]
    public void Lex_BlockComment_Multiline()
    {
        var tokens = All("/* line1\nline2\nline3 */ 42");
        Assert.Equal(2, tokens.Count);
        AssertToken(tokens[0], TokenType.IntLiteral, "42");
    }

    [Fact]
    public void Lex_BlockComment_Inline()
    {
        var tokens = All("struct /* inline */ MyHeader");
        Assert.Equal(3, tokens.Count);
        AssertToken(tokens[0], TokenType.Struct, "struct");
        AssertToken(tokens[1], TokenType.Identifier, "MyHeader");
    }

    // ─────────────────────── Whitespace Handling ───────────────────────

    [Fact]
    public void Lex_EmptySource()
    {
        var tok = Single("");
        Assert.Equal(TokenType.Eof, tok.Type);
    }

    [Fact]
    public void Lex_OnlyWhitespace()
    {
        var tok = Single("   \t\r\n  ");
        Assert.Equal(TokenType.Eof, tok.Type);
    }

    // ─────────────────────── Source Positions ───────────────────────

    [Fact]
    public void Lex_SourcePositions_FirstToken()
    {
        var tok = Single("struct");
        Assert.Equal(0, tok.Span.Start);
        Assert.Equal(6, tok.Span.Length);
        Assert.Equal(1, tok.Span.Line);
        Assert.Equal(1, tok.Span.Column);
    }

    [Fact]
    public void Lex_SourcePositions_AfterWhitespace()
    {
        var tokens = All("  struct");
        Assert.Equal(2, tokens[0].Span.Start);
        Assert.Equal(1, tokens[0].Span.Line);
        Assert.Equal(3, tokens[0].Span.Column);
    }

    [Fact]
    public void Lex_SourcePositions_MultiLine()
    {
        var source = "struct\nMyHeader";
        var tokens = All(source);

        // "struct" at line 1, col 1
        Assert.Equal(1, tokens[0].Span.Line);
        Assert.Equal(1, tokens[0].Span.Column);

        // "MyHeader" at line 2, col 1
        Assert.Equal(2, tokens[1].Span.Line);
        Assert.Equal(1, tokens[1].Span.Column);
    }

    [Fact]
    public void Lex_SourcePositions_SameLine()
    {
        var source = "u16 field";
        var tokens = All(source);

        Assert.Equal(1, tokens[0].Span.Column); // "u16"
        Assert.Equal(5, tokens[1].Span.Column); // "field"
    }

    [Fact]
    public void Lex_SourcePositions_FilePath()
    {
        var lexer = new Lexer("42", "test.bs");
        var tok = lexer.NextToken();
        Assert.Equal("test.bs", tok.Span.FilePath);
    }

    // ─────────────────────── Edge Cases ───────────────────────

    [Fact]
    public void Lex_UnknownCharacter()
    {
        var tok = Single("#");
        Assert.Equal(TokenType.Error, tok.Type);
    }

    [Fact]
    public void Lex_DotVsDotDot()
    {
        var twoSep = All(". .");
        Assert.Equal(TokenType.Dot, twoSep[0].Type);
        Assert.Equal(TokenType.Dot, twoSep[1].Type);

        var range = All("..");
        Assert.Equal(TokenType.DotDot, range[0].Type);
    }

    [Fact]
    public void Lex_EqualVsEqualEqual()
    {
        var eq = All("= ==");
        Assert.Equal(TokenType.Equal, eq[0].Type);
        Assert.Equal(TokenType.EqualEqual, eq[1].Type);
    }

    [Fact]
    public void Lex_LessVariants()
    {
        var tokens = All("< <= <<");
        Assert.Equal(TokenType.Less, tokens[0].Type);
        Assert.Equal(TokenType.LessEqual, tokens[1].Type);
        Assert.Equal(TokenType.LeftShift, tokens[2].Type);
    }

    [Fact]
    public void Lex_GreaterVariants()
    {
        var tokens = All("> >= >>");
        Assert.Equal(TokenType.Greater, tokens[0].Type);
        Assert.Equal(TokenType.GreaterEqual, tokens[1].Type);
        Assert.Equal(TokenType.RightShift, tokens[2].Type);
    }

    [Fact]
    public void Lex_AmpersandVariants()
    {
        var tokens = All("& &&");
        Assert.Equal(TokenType.Ampersand, tokens[0].Type);
        Assert.Equal(TokenType.AmpersandAmpersand, tokens[1].Type);
    }

    [Fact]
    public void Lex_PipeVariants()
    {
        var tokens = All("| ||");
        Assert.Equal(TokenType.Pipe, tokens[0].Type);
        Assert.Equal(TokenType.PipePipe, tokens[1].Type);
    }

    [Fact]
    public void Lex_BangVariants()
    {
        var tokens = All("! !=");
        Assert.Equal(TokenType.Bang, tokens[0].Type);
        Assert.Equal(TokenType.BangEqual, tokens[1].Type);
    }

    [Fact]
    public void Lex_Arrow()
    {
        var tok = Single("=>");
        AssertToken(tok, TokenType.Arrow, "=>");
    }

    // ─────────────────────── Complete Script ───────────────────────

    [Fact]
    public void Lex_CompleteScript()
    {
        const string script = """
            @default_endian(little)
            @root struct DosHeader {
                e_magic: u16 = 0x5A4D,
            }
            """;

        var tokens = All(script);

        // Strip Eof for easier assertion
        var toks = tokens.Where(t => t.Type != TokenType.Eof).ToList();

        // @default_endian ( little )
        AssertToken(toks[0], TokenType.DefaultEndian, "@default_endian");
        AssertToken(toks[1], TokenType.LeftParen, "(");
        AssertToken(toks[2], TokenType.Identifier, "little");
        AssertToken(toks[3], TokenType.RightParen, ")");

        // @root struct DosHeader {
        AssertToken(toks[4], TokenType.Root, "@root");
        AssertToken(toks[5], TokenType.Struct, "struct");
        AssertToken(toks[6], TokenType.Identifier, "DosHeader");
        AssertToken(toks[7], TokenType.LeftBrace, "{");

        // e_magic : u16 = 0x5A4D ,
        AssertToken(toks[8], TokenType.Identifier, "e_magic");
        AssertToken(toks[9], TokenType.Colon, ":");
        AssertToken(toks[10], TokenType.U16, "u16");
        AssertToken(toks[11], TokenType.Equal, "=");
        AssertToken(toks[12], TokenType.IntLiteral, "0x5A4D");
        AssertToken(toks[13], TokenType.Comma, ",");

        // }
        AssertToken(toks[14], TokenType.RightBrace, "}");

        Assert.Equal(15, toks.Count);
    }

    [Fact]
    public void Lex_MatchExpression()
    {
        const string script = "match e_machine { 0x014C => when _ => @skip(4) }";
        var tokens = All(script);
        var toks = tokens.Where(t => t.Type != TokenType.Eof).ToList();

        AssertToken(toks[0], TokenType.Match, "match");
        AssertToken(toks[1], TokenType.Identifier, "e_machine");
        AssertToken(toks[2], TokenType.LeftBrace, "{");
        AssertToken(toks[3], TokenType.IntLiteral, "0x014C");
        AssertToken(toks[4], TokenType.Arrow, "=>");
        AssertToken(toks[5], TokenType.When, "when");
        AssertToken(toks[6], TokenType.Underscore, "_");
        AssertToken(toks[7], TokenType.Arrow, "=>");
        AssertToken(toks[8], TokenType.Skip, "@skip");
        AssertToken(toks[9], TokenType.LeftParen, "(");
        AssertToken(toks[10], TokenType.IntLiteral, "4");
        AssertToken(toks[11], TokenType.RightParen, ")");
        AssertToken(toks[12], TokenType.RightBrace, "}");
    }

    [Fact]
    public void Lex_ArrayWithRange()
    {
        const string script = "data: u8[0..16]";
        var tokens = All(script);
        var toks = tokens.Where(t => t.Type != TokenType.Eof).ToList();

        AssertToken(toks[0], TokenType.Identifier, "data");
        AssertToken(toks[1], TokenType.Colon, ":");
        AssertToken(toks[2], TokenType.U8, "u8");
        AssertToken(toks[3], TokenType.LeftBracket, "[");
        AssertToken(toks[4], TokenType.IntLiteral, "0");
        AssertToken(toks[5], TokenType.DotDot, "..");
        AssertToken(toks[6], TokenType.IntLiteral, "16");
        AssertToken(toks[7], TokenType.RightBracket, "]");
    }

    [Fact]
    public void Lex_ExpressionWithOperators()
    {
        const string script = "offset + size * 2 >> 3";
        var tokens = All(script);
        var toks = tokens.Where(t => t.Type != TokenType.Eof).ToList();

        AssertToken(toks[0], TokenType.Identifier, "offset");
        AssertToken(toks[1], TokenType.Plus, "+");
        AssertToken(toks[2], TokenType.Identifier, "size");
        AssertToken(toks[3], TokenType.Star, "*");
        AssertToken(toks[4], TokenType.IntLiteral, "2");
        AssertToken(toks[5], TokenType.RightShift, ">>");
        AssertToken(toks[6], TokenType.IntLiteral, "3");
    }

    [Fact]
    public void Lex_TokenizeAll_EndsWithEof()
    {
        var tokens = All("42");
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenType.IntLiteral, tokens[0].Type);
        Assert.Equal(TokenType.Eof, tokens[1].Type);
    }

    [Fact]
    public void Lex_MultipleCallsToNextToken()
    {
        var lexer = new Lexer("a b");
        var t1 = lexer.NextToken();
        var t2 = lexer.NextToken();
        var t3 = lexer.NextToken();

        AssertToken(t1, TokenType.Identifier, "a");
        AssertToken(t2, TokenType.Identifier, "b");
        Assert.Equal(TokenType.Eof, t3.Type);
    }

    [Fact]
    public void Lex_RepeatedEof()
    {
        var lexer = new Lexer("");
        Assert.Equal(TokenType.Eof, lexer.NextToken().Type);
        Assert.Equal(TokenType.Eof, lexer.NextToken().Type);
    }
}
