namespace BinScript.Core.Compiler;

/// <summary>
/// Scans BinScript source text into a stream of <see cref="Token"/>s.
/// Designed for span-based, low-allocation processing on .NET 10.
/// </summary>
public sealed class Lexer
{
    private readonly string _source;
    private readonly string _filePath;
    private int _pos;
    private int _line;
    private int _column;

    public Lexer(string source, string filePath = "")
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _filePath = filePath;
        _pos = 0;
        _line = 1;
        _column = 1;
    }

    /// <summary>Returns the next token from the source. Returns <see cref="TokenType.Eof"/> at end.</summary>
    public Token NextToken()
    {
        SkipWhitespaceAndComments();

        if (_pos >= _source.Length)
            return new Token(TokenType.Eof, "", MakeSpan(_pos, 0));

        char c = _source[_pos];

        if (c == '"')
            return ReadString();

        if (c == '@')
            return ReadDirective();

        if (char.IsAsciiDigit(c))
            return ReadNumber();

        if (c == '_' || char.IsAsciiLetter(c))
            return ReadIdentifierOrKeyword();

        return ReadPunctuation();
    }

    /// <summary>Tokenizes the entire source, including the trailing <see cref="TokenType.Eof"/>.</summary>
    public List<Token> TokenizeAll()
    {
        var tokens = new List<Token>();
        Token tok;
        do
        {
            tok = NextToken();
            tokens.Add(tok);
        }
        while (tok.Type != TokenType.Eof);
        return tokens;
    }

    // ────────────────────────── Whitespace / Comments ──────────────────────────

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _source.Length)
        {
            char c = _source[_pos];

            if (c is ' ' or '\t' or '\r' or '\n')
            {
                Advance();
                continue;
            }

            if (c == '/' && _pos + 1 < _source.Length)
            {
                char next = _source[_pos + 1];
                if (next == '/')
                {
                    SkipLineComment();
                    continue;
                }
                if (next == '*')
                {
                    SkipBlockComment();
                    continue;
                }
            }

            break;
        }
    }

    private void SkipLineComment()
    {
        Advance(); // '/'
        Advance(); // '/'
        while (_pos < _source.Length && _source[_pos] != '\n')
            Advance();
    }

    private void SkipBlockComment()
    {
        Advance(); // '/'
        Advance(); // '*'
        while (_pos < _source.Length)
        {
            if (_source[_pos] == '*' && _pos + 1 < _source.Length && _source[_pos + 1] == '/')
            {
                Advance(); // '*'
                Advance(); // '/'
                return;
            }
            Advance();
        }
        // Unterminated block comment — silently stop (error recovery).
    }

    // ────────────────────────── Numeric Literals ──────────────────────────

    private Token ReadNumber()
    {
        int start = _pos;
        int startLine = _line;
        int startCol = _column;

        if (_source[_pos] == '0' && _pos + 1 < _source.Length)
        {
            char prefix = _source[_pos + 1];

            if (prefix is 'x' or 'X')
            {
                Advance(); // '0'
                Advance(); // 'x'
                while (_pos < _source.Length && IsHexDigit(_source[_pos]))
                    Advance();
                return MakeTokenFromRange(TokenType.IntLiteral, start, startLine, startCol);
            }

            if (prefix is 'b' or 'B')
            {
                Advance(); // '0'
                Advance(); // 'b'
                while (_pos < _source.Length && _source[_pos] is '0' or '1')
                    Advance();
                return MakeTokenFromRange(TokenType.IntLiteral, start, startLine, startCol);
            }

            if (prefix is 'o' or 'O')
            {
                Advance(); // '0'
                Advance(); // 'o'
                while (_pos < _source.Length && _source[_pos] >= '0' && _source[_pos] <= '7')
                    Advance();
                return MakeTokenFromRange(TokenType.IntLiteral, start, startLine, startCol);
            }
        }

        // Decimal
        while (_pos < _source.Length && char.IsAsciiDigit(_source[_pos]))
            Advance();

        return MakeTokenFromRange(TokenType.IntLiteral, start, startLine, startCol);
    }

    // ────────────────────────── String Literals ──────────────────────────

    private Token ReadString()
    {
        int start = _pos;
        int startLine = _line;
        int startCol = _column;

        Advance(); // consume opening '"'

        var sb = new System.Text.StringBuilder();

        while (_pos < _source.Length)
        {
            char c = _source[_pos];

            if (c == '"')
            {
                Advance(); // consume closing '"'
                return new Token(
                    TokenType.StringLiteral,
                    sb.ToString(),
                    new SourceSpan(_filePath, start, _pos - start, startLine, startCol));
            }

            if (c is '\n' or '\r')
                break; // unterminated — hit raw newline

            if (c == '\\')
            {
                Advance(); // consume '\'

                if (_pos >= _source.Length)
                    break; // unterminated

                char esc = Advance(); // consume escape character
                switch (esc)
                {
                    case '\\': sb.Append('\\'); break;
                    case '"':  sb.Append('"');  break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case '0':  sb.Append('\0'); break;
                    case 'x':
                        if (_pos + 1 < _source.Length
                            && IsHexDigit(_source[_pos])
                            && IsHexDigit(_source[_pos + 1]))
                        {
                            int val = HexValue(_source[_pos]) * 16 + HexValue(_source[_pos + 1]);
                            sb.Append((char)val);
                            Advance(); // first hex digit
                            Advance(); // second hex digit
                        }
                        else
                        {
                            return new Token(
                                TokenType.Error,
                                "Invalid hex escape",
                                new SourceSpan(_filePath, start, _pos - start, startLine, startCol));
                        }
                        break;
                    default:
                        return new Token(
                            TokenType.Error,
                            $"Unknown escape sequence: \\{esc}",
                            new SourceSpan(_filePath, start, _pos - start, startLine, startCol));
                }
            }
            else
            {
                sb.Append(c);
                Advance();
            }
        }

        // Unterminated string
        return new Token(
            TokenType.Error,
            "Unterminated string",
            new SourceSpan(_filePath, start, _pos - start, startLine, startCol));
    }

    // ────────────────────────── Identifiers / Keywords ──────────────────────────

    private Token ReadIdentifierOrKeyword()
    {
        int start = _pos;
        int startLine = _line;
        int startCol = _column;

        while (_pos < _source.Length && IsIdentChar(_source[_pos]))
            Advance();

        string text = _source[start.._pos];
        var span = new SourceSpan(_filePath, start, _pos - start, startLine, startCol);

        if (text == "_")
            return new Token(TokenType.Underscore, text, span);

        if (TokenTypeInfo.Keywords.TryGetValue(text, out var kwType))
            return new Token(kwType, text, span);

        return new Token(TokenType.Identifier, text, span);
    }

    // ────────────────────────── Directives ──────────────────────────

    private Token ReadDirective()
    {
        int start = _pos;
        int startLine = _line;
        int startCol = _column;

        Advance(); // consume '@'

        int identStart = _pos;
        while (_pos < _source.Length && IsIdentChar(_source[_pos]))
            Advance();

        if (_pos == identStart)
        {
            // Bare '@' with no identifier
            return new Token(TokenType.Error, "@",
                new SourceSpan(_filePath, start, 1, startLine, startCol));
        }

        string name = _source[identStart.._pos];
        string fullText = _source[start.._pos]; // "@name"
        var span = new SourceSpan(_filePath, start, _pos - start, startLine, startCol);

        if (TokenTypeInfo.Directives.TryGetValue(name, out var dirType))
            return new Token(dirType, fullText, span);

        return new Token(TokenType.Error, fullText, span);
    }

    // ────────────────────────── Punctuation / Operators ──────────────────────────

    private Token ReadPunctuation()
    {
        int start = _pos;
        int startLine = _line;
        int startCol = _column;
        char c = _source[_pos];
        char next = _pos + 1 < _source.Length ? _source[_pos + 1] : '\0';

        // Two-character tokens (checked first)
        TokenType? twoChar = (c, next) switch
        {
            ('=', '>') => TokenType.Arrow,
            ('=', '=') => TokenType.EqualEqual,
            ('!', '=') => TokenType.BangEqual,
            ('<', '=') => TokenType.LessEqual,
            ('<', '<') => TokenType.LeftShift,
            ('>', '=') => TokenType.GreaterEqual,
            ('>', '>') => TokenType.RightShift,
            ('&', '&') => TokenType.AmpersandAmpersand,
            ('|', '|') => TokenType.PipePipe,
            ('.', '.') => TokenType.DotDot,
            _ => null
        };

        if (twoChar is { } tt)
        {
            Advance();
            Advance();
            return new Token(tt, _source[start.._pos],
                new SourceSpan(_filePath, start, 2, startLine, startCol));
        }

        // Single-character tokens
        Advance();
        var span = new SourceSpan(_filePath, start, 1, startLine, startCol);

        var type = c switch
        {
            '{' => TokenType.LeftBrace,
            '}' => TokenType.RightBrace,
            '(' => TokenType.LeftParen,
            ')' => TokenType.RightParen,
            '[' => TokenType.LeftBracket,
            ']' => TokenType.RightBracket,
            ',' => TokenType.Comma,
            ':' => TokenType.Colon,
            ';' => TokenType.Semicolon,
            '.' => TokenType.Dot,
            '+' => TokenType.Plus,
            '-' => TokenType.Minus,
            '*' => TokenType.Star,
            '/' => TokenType.Slash,
            '%' => TokenType.Percent,
            '&' => TokenType.Ampersand,
            '|' => TokenType.Pipe,
            '^' => TokenType.Caret,
            '~' => TokenType.Tilde,
            '!' => TokenType.Bang,
            '?' => TokenType.QuestionMark,
            '=' => TokenType.Equal,
            '<' => TokenType.Less,
            '>' => TokenType.Greater,
            _ => TokenType.Error,
        };

        return new Token(type, c.ToString(), span);
    }

    // ────────────────────────── Helpers ──────────────────────────

    private char Advance()
    {
        char c = _source[_pos];
        _pos++;
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        return c;
    }

    private SourceSpan MakeSpan(int start, int length)
        => new(_filePath, start, length, _line, _column);

    private Token MakeTokenFromRange(TokenType type, int start, int startLine, int startCol)
        => new(type, _source[start.._pos],
            new SourceSpan(_filePath, start, _pos - start, startLine, startCol));

    private static bool IsIdentChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    private static bool IsHexDigit(char c) =>
        char.IsAsciiDigit(c) || (c is >= 'a' and <= 'f') || (c is >= 'A' and <= 'F');

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };
}
