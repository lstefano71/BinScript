namespace BinScript.NALight;

/// <summary>Token kinds for the BSX-Light (NA superset) lexer.</summary>
public enum NATokenKind
{
    // ── Primitive type letters ──
    TypeI,      // I (signed integer)
    TypeU,      // U (unsigned integer)
    TypeC,      // C (character, untranslated)
    TypeT,      // T (translated character)
    TypeF,      // F (float)
    TypeD,      // D (decimal128)
    TypeJ,      // J (complex)
    TypeP,      // P (pointer)
    TypeA,      // A (APL array)
    TypeZ,      // Z (APL array + header)
    TypeUTF,    // UTF (followed by width 8 or 16)

    // ── Width ──
    Width,      // numeric width: 1, 2, 4, 8, 16

    // ── Direction markers ──
    DirIn,      // <
    DirOut,     // >
    DirInOut,   // =

    // ── Special qualifiers ──
    NullTerm,   // 0 (single null-terminated)
    DoubleNull, // 00 (double-null-terminated, BSX-Light extension)
    ByteCounted,// #

    // ── Structural ──
    LBrace,     // {
    RBrace,     // }
    LBracket,   // [
    RBracket,   // ]
    Pipe,       // |
    Star,       // * (counted array back-reference)
    Colon,      // : (max count separator in [*:N])
    Ampersand,  // & (threaded call suffix)

    // ── Modifiers (BSX-Light extensions) ──
    Aligned,    // @aligned
    Pack,       // @pack
    LParen,     // (
    RParen,     // )

    // ── Literals ──
    Number,     // integer literal (width, array count, etc.)
    Identifier, // dll/function name segment

    // ── Meta ──
    End,        // end of input
}

/// <summary>A single token from BSX-Light lexing.</summary>
public readonly record struct NAToken(NATokenKind Kind, string Value, int Position);

/// <summary>
/// Lexer for BSX-Light (NA superset) syntax.
/// Tokenizes a compact type-declaration string into a stream of <see cref="NAToken"/>s.
/// Handles /* */ comments and linefeeds as insignificant whitespace.
/// </summary>
public sealed class NALexer
{
    private readonly string _source;
    private int _pos;

    public NALexer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pos = 0;
    }

    /// <summary>Tokenize the entire input into a list.</summary>
    public List<NAToken> Tokenize()
    {
        var tokens = new List<NAToken>();
        while (true)
        {
            var tok = Next();
            tokens.Add(tok);
            if (tok.Kind == NATokenKind.End) break;
        }
        return tokens;
    }

    /// <summary>Return the next token, advancing the position.</summary>
    public NAToken Next()
    {
        SkipWhitespaceAndComments();

        if (_pos >= _source.Length)
            return new NAToken(NATokenKind.End, "", _pos);

        int start = _pos;
        char c = _source[_pos];

        // Single-character tokens
        switch (c)
        {
            case '{': _pos++; return new NAToken(NATokenKind.LBrace, "{", start);
            case '}': _pos++; return new NAToken(NATokenKind.RBrace, "}", start);
            case '[': _pos++; return new NAToken(NATokenKind.LBracket, "[", start);
            case ']': _pos++; return new NAToken(NATokenKind.RBracket, "]", start);
            case '(': _pos++; return new NAToken(NATokenKind.LParen, "(", start);
            case ')': _pos++; return new NAToken(NATokenKind.RParen, ")", start);
            case '|': _pos++; return new NAToken(NATokenKind.Pipe, "|", start);
            case '*': _pos++; return new NAToken(NATokenKind.Star, "*", start);
            case ':': _pos++; return new NAToken(NATokenKind.Colon, ":", start);
            case '&': _pos++; return new NAToken(NATokenKind.Ampersand, "&", start);
            case '#': _pos++; return new NAToken(NATokenKind.ByteCounted, "#", start);
        }

        // Direction markers (< > =) — but only if not followed by something
        // that makes it a different token
        if (c == '<') { _pos++; return new NAToken(NATokenKind.DirIn, "<", start); }
        if (c == '>') { _pos++; return new NAToken(NATokenKind.DirOut, ">", start); }
        if (c == '=') { _pos++; return new NAToken(NATokenKind.DirInOut, "=", start); }

        // @ modifiers
        if (c == '@')
        {
            _pos++;
            string word = ReadWhile(char.IsLetterOrDigit);
            return word.ToLowerInvariant() switch
            {
                "aligned" => new NAToken(NATokenKind.Aligned, "@aligned", start),
                "pack" => new NAToken(NATokenKind.Pack, "@pack", start),
                _ => throw new NALexerException($"Unknown modifier '@{word}' at position {start}", start),
            };
        }

        // Numbers and special qualifiers (0, 00)
        if (char.IsDigit(c))
        {
            // Check for null-terminated qualifiers: 0 or 00 before a type letter
            if (c == '0')
            {
                char next = Peek(1);
                if (next == '0')
                {
                    // Could be 00 (double-null) if followed by a type letter
                    char afterTwo = Peek(2);
                    if (afterTwo != '\0' && IsTypeChar(afterTwo) && char.IsUpper(afterTwo))
                    {
                        _pos += 2;
                        return new NAToken(NATokenKind.DoubleNull, "00", start);
                    }
                }
                // Single 0 before a type letter = null-terminated
                if (next != '\0' && IsTypeChar(next) && char.IsUpper(next))
                {
                    _pos++;
                    return new NAToken(NATokenKind.NullTerm, "0", start);
                }
            }
            string num = ReadWhile(char.IsDigit);
            return new NAToken(NATokenKind.Number, num, start);
        }

        // Type letters and identifiers
        if (char.IsLetter(c) || c == '_')
        {
            return ReadTypeOrIdentifier(start);
        }

        throw new NALexerException($"Unexpected character '{c}' at position {start}", start);
    }

    private NAToken ReadTypeOrIdentifier(int start)
    {
        // Try to match type letters first.
        // Type letters are single uppercase letters (I, U, C, T, F, D, J, P, A, Z)
        // or the special "UTF" prefix.
        char c = _source[_pos];

        // Check for UTF prefix
        if (c == 'U' && Peek(1) == 'T' && Peek(2) == 'F')
        {
            // But make sure it's not part of a longer identifier like "UTFStuff"
            char after = Peek(3);
            if (after == '\0' || char.IsDigit(after) || !char.IsLetterOrDigit(after))
            {
                _pos += 3;
                return new NAToken(NATokenKind.TypeUTF, "UTF", start);
            }
        }

        // Single uppercase type letters — only if followed by a non-letter
        // (to distinguish 'I' the type from 'ImportTable' the identifier)
        // Exception: don't match as type if followed by ':' (Windows drive path like C:\)
        if (char.IsUpper(c) && IsTypeChar(c))
        {
            char next = Peek(1);
            if (next != ':' && (!char.IsLetter(next) || (char.IsUpper(next) && IsTypeChar(next))))
            {
                _pos++;
                var kind = c switch
                {
                    'I' => NATokenKind.TypeI,
                    'U' => NATokenKind.TypeU,
                    'C' => NATokenKind.TypeC,
                    'T' => NATokenKind.TypeT,
                    'F' => NATokenKind.TypeF,
                    'D' => NATokenKind.TypeD,
                    'J' => NATokenKind.TypeJ,
                    'P' => NATokenKind.TypeP,
                    'A' => NATokenKind.TypeA,
                    'Z' => NATokenKind.TypeZ,
                    _ => throw new NALexerException($"Unexpected type char '{c}' at {start}", start),
                };
                return new NAToken(kind, c.ToString(), start);
            }
        }

        // It's an identifier (dll name, function name, etc.)
        string ident = ReadWhile(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '\\' || c == '/' || c == ':');
        return new NAToken(NATokenKind.Identifier, ident, start);
    }

    private static bool IsTypeChar(char c) =>
        c is 'I' or 'U' or 'C' or 'T' or 'F' or 'D' or 'J' or 'P' or 'A' or 'Z';

    private char Peek(int offset = 1)
    {
        int idx = _pos + offset;
        return idx < _source.Length ? _source[idx] : '\0';
    }

    private string ReadWhile(Func<char, bool> predicate)
    {
        int start = _pos;
        while (_pos < _source.Length && predicate(_source[_pos]))
            _pos++;
        return _source[start.._pos];
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _source.Length)
        {
            char c = _source[_pos];

            // Whitespace (space, tab, CR, LF — linefeeds are insignificant whitespace)
            if (char.IsWhiteSpace(c))
            {
                _pos++;
                continue;
            }

            // Block comment: /* ... */
            if (c == '/' && Peek() == '*')
            {
                _pos += 2; // skip /*
                while (_pos < _source.Length)
                {
                    if (_source[_pos] == '*' && Peek() == '/')
                    {
                        _pos += 2; // skip */
                        break;
                    }
                    _pos++;
                }
                continue;
            }

            break;
        }
    }
}

/// <summary>Error during BSX-Light lexing.</summary>
public sealed class NALexerException : Exception
{
    public int Position { get; }

    public NALexerException(string message, int position)
        : base(message)
    {
        Position = position;
    }
}
