using System.Collections.Frozen;

namespace BinScript.Core.Compiler;

/// <summary>Represents a position and extent in source text.</summary>
public readonly record struct SourceSpan(string FilePath, int Start, int Length, int Line, int Column)
{
    public int End => Start + Length;

    public static SourceSpan None => new(string.Empty, 0, 0, 0, 0);

    /// <summary>Returns a span that covers both <paramref name="first"/> and <paramref name="last"/>.</summary>
    public static SourceSpan Merge(SourceSpan first, SourceSpan last)
    {
        var start = Math.Min(first.Start, last.Start);
        var end = Math.Max(first.End, last.End);
        return new SourceSpan(first.FilePath, start, end - start, first.Line, first.Column);
    }
}

/// <summary>A single lexical token produced by the scanner.</summary>
public readonly record struct Token(TokenType Type, string Text, SourceSpan Span)
{
    public override string ToString() => $"{Type}({Text}) at {Span.Line}:{Span.Column}";
}

public enum TokenType
{
    // ── Literals ──────────────────────────────────────────────
    IntLiteral,
    StringLiteral,
    TrueLiteral,
    FalseLiteral,

    // ── Identifiers ──────────────────────────────────────────
    Identifier,

    // ── Keywords ─────────────────────────────────────────────
    Struct,
    Bits,
    Enum,
    Const,
    Match,
    When,
    If,
    Else,
    Bit,
    Bytes,
    CString,
    String,
    FixedString,
    Bool,
    Ptr,
    RelPtr,
    NullLiteral,

    // ── Directives (@xxx) ────────────────────────────────────
    Root,
    DefaultEndian,
    DefaultEncoding,
    Param,
    Import,
    Coverage,
    Let,
    Seek,
    At,
    Align,
    Skip,
    Hidden,
    Derived,
    Assert,
    Encoding,
    Until,
    UntilSentinel,
    Greedy,
    InputSize,
    Offset,
    Remaining,
    SizeOf,
    OffsetOf,
    Count,
    StrLen,
    Crc32,
    Adler32,
    Map,
    MaxDepth,
    Inline,
    ShowPtr,

    // ── Primitive type keywords ──────────────────────────────
    U8, U16, U32, U64,
    I8, I16, I32, I64,
    F32, F64,

    // ── Endianness-qualified primitives ──────────────────────
    U16Le, U16Be,
    U32Le, U32Be,
    U64Le, U64Be,
    I16Le, I16Be,
    I32Le, I32Be,
    I64Le, I64Be,
    F32Le, F32Be,
    F64Le, F64Be,

    // ── Punctuation ──────────────────────────────────────────
    LeftBrace,      // {
    RightBrace,     // }
    LeftParen,      // (
    RightParen,     // )
    LeftBracket,    // [
    RightBracket,   // ]
    Comma,          // ,
    Colon,          // :
    Semicolon,      // ;
    Dot,            // .
    Arrow,          // =>
    DotDot,         // ..
    QuestionMark,   // ?

    // ── Operators ────────────────────────────────────────────
    Plus,               // +
    Minus,              // -
    Star,               // *
    Slash,              // /
    Percent,            // %
    Ampersand,          // &
    Pipe,               // |
    Caret,              // ^
    Tilde,              // ~
    LeftShift,          // <<
    RightShift,         // >>
    Equal,              // =
    EqualEqual,         // ==
    BangEqual,          // !=
    Less,               // <
    Greater,            // >
    LessEqual,          // <=
    GreaterEqual,       // >=
    AmpersandAmpersand, // &&
    PipePipe,           // ||
    Bang,               // !

    // ── Special ──────────────────────────────────────────────
    Eof,
    Error,
    Underscore,         // _
}

/// <summary>Static lookup tables and helpers for <see cref="TokenType"/>.</summary>
public static class TokenTypeInfo
{
    /// <summary>Maps keyword text to its <see cref="TokenType"/>.</summary>
    public static FrozenDictionary<string, TokenType> Keywords { get; } = new Dictionary<string, TokenType>
    {
        // Language keywords
        ["struct"] = TokenType.Struct,
        ["bits"] = TokenType.Bits,
        ["enum"] = TokenType.Enum,
        ["const"] = TokenType.Const,
        ["match"] = TokenType.Match,
        ["when"] = TokenType.When,
        ["if"] = TokenType.If,
        ["else"] = TokenType.Else,
        ["bit"] = TokenType.Bit,
        ["bytes"] = TokenType.Bytes,
        ["cstring"] = TokenType.CString,
        ["string"] = TokenType.String,
        ["fixed_string"] = TokenType.FixedString,
        ["bool"] = TokenType.Bool,
        ["ptr"] = TokenType.Ptr,
        ["relptr"] = TokenType.RelPtr,
        ["null"] = TokenType.NullLiteral,
        ["true"] = TokenType.TrueLiteral,
        ["false"] = TokenType.FalseLiteral,

        // Primitive types (no endianness)
        ["u8"] = TokenType.U8,
        ["u16"] = TokenType.U16,
        ["u32"] = TokenType.U32,
        ["u64"] = TokenType.U64,
        ["i8"] = TokenType.I8,
        ["i16"] = TokenType.I16,
        ["i32"] = TokenType.I32,
        ["i64"] = TokenType.I64,
        ["f32"] = TokenType.F32,
        ["f64"] = TokenType.F64,

        // Endianness-qualified primitives
        ["u16le"] = TokenType.U16Le,
        ["u16be"] = TokenType.U16Be,
        ["u32le"] = TokenType.U32Le,
        ["u32be"] = TokenType.U32Be,
        ["u64le"] = TokenType.U64Le,
        ["u64be"] = TokenType.U64Be,
        ["i16le"] = TokenType.I16Le,
        ["i16be"] = TokenType.I16Be,
        ["i32le"] = TokenType.I32Le,
        ["i32be"] = TokenType.I32Be,
        ["i64le"] = TokenType.I64Le,
        ["i64be"] = TokenType.I64Be,
        ["f32le"] = TokenType.F32Le,
        ["f32be"] = TokenType.F32Be,
        ["f64le"] = TokenType.F64Le,
        ["f64be"] = TokenType.F64Be,
    }.ToFrozenDictionary();

    /// <summary>Maps directive name (without the leading <c>@</c>) to its <see cref="TokenType"/>.</summary>
    public static FrozenDictionary<string, TokenType> Directives { get; } = new Dictionary<string, TokenType>
    {
        ["root"] = TokenType.Root,
        ["default_endian"] = TokenType.DefaultEndian,
        ["default_encoding"] = TokenType.DefaultEncoding,
        ["param"] = TokenType.Param,
        ["import"] = TokenType.Import,
        ["coverage"] = TokenType.Coverage,
        ["let"] = TokenType.Let,
        ["seek"] = TokenType.Seek,
        ["at"] = TokenType.At,
        ["align"] = TokenType.Align,
        ["skip"] = TokenType.Skip,
        ["hidden"] = TokenType.Hidden,
        ["derived"] = TokenType.Derived,
        ["assert"] = TokenType.Assert,
        ["encoding"] = TokenType.Encoding,
        ["until"] = TokenType.Until,
        ["until_sentinel"] = TokenType.UntilSentinel,
        ["greedy"] = TokenType.Greedy,
        ["input_size"] = TokenType.InputSize,
        ["offset"] = TokenType.Offset,
        ["remaining"] = TokenType.Remaining,
        ["sizeof"] = TokenType.SizeOf,
        ["offsetof"] = TokenType.OffsetOf,
        ["count"] = TokenType.Count,
        ["strlen"] = TokenType.StrLen,
        ["crc32"] = TokenType.Crc32,
        ["adler32"] = TokenType.Adler32,
        ["map"] = TokenType.Map,
        ["max_depth"] = TokenType.MaxDepth,
        ["inline"] = TokenType.Inline,
        ["show_ptr"] = TokenType.ShowPtr,
    }.ToFrozenDictionary();

    private static readonly FrozenSet<TokenType> s_primitiveTypes =
    [
        TokenType.U8,   TokenType.U16,   TokenType.U32,   TokenType.U64,
        TokenType.I8,   TokenType.I16,   TokenType.I32,   TokenType.I64,
        TokenType.F32,  TokenType.F64,
        TokenType.U16Le, TokenType.U16Be, TokenType.U32Le, TokenType.U32Be, TokenType.U64Le, TokenType.U64Be,
        TokenType.I16Le, TokenType.I16Be, TokenType.I32Le, TokenType.I32Be, TokenType.I64Le, TokenType.I64Be,
        TokenType.F32Le, TokenType.F32Be, TokenType.F64Le, TokenType.F64Be,
        TokenType.Bool,
    ];

    /// <summary>Returns <c>true</c> if <paramref name="type"/> represents a built-in primitive type.</summary>
    public static bool IsPrimitiveType(TokenType type) => s_primitiveTypes.Contains(type);
}
