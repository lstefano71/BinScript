namespace BinScript.NALight;

/// <summary>
/// Parses a stream of <see cref="NAToken"/>s (produced by <see cref="NALexer"/>)
/// into an <see cref="NASpec"/> intermediate representation.
/// </summary>
/// <remarks>
/// Grammar (simplified):
/// <code>
/// spec        := [return_type] library_fn args*
/// library_fn  := IDENTIFIER PIPE IDENTIFIER [STAR] [AMPERSAND]
/// args        := arg*
/// arg         := [direction] type_desc [array_spec]
/// direction   := LT | GT | EQ
/// type_desc   := primitive | pointer | null_term | double_null | struct_desc
/// primitive   := type_letter [width]
/// null_term   := NULLTERM type_letter [width]
/// double_null := DOUBLENULL type_letter [width]
/// struct_desc := [alignment] LBRACE struct_field* RBRACE
/// struct_field:= [direction] type_desc [array_spec]
/// array_spec  := LBRACKET (NUMBER | STAR [COLON NUMBER] | ) RBRACKET
/// alignment   := AT_ALIGNED | AT_PACK LPAREN NUMBER RPAREN
/// </code>
/// </remarks>
public sealed class NAParser
{
    private readonly IReadOnlyList<NAToken> _tokens;
    private int _pos;

    public NAParser(IReadOnlyList<NAToken> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    /// <summary>Parse the token stream into an <see cref="NASpec"/>.</summary>
    public NASpec Parse()
    {
        // Spec format: [ReturnType] [Library|Function[*][&]] ArgTypes...
        // We need to distinguish the return type from the first arg type.
        // The library|function separator is the Pipe token.

        NATypeDesc? returnType = null;
        string? libraryPath = null;
        string? functionName = null;
        bool passByPointer = false;
        bool threadSafe = false;

        // Look ahead to find the Pipe — everything before it is return type + lib|fn.
        int pipeIndex = FindToken(NATokenKind.Pipe);

        if (pipeIndex >= 0)
        {
            // There's a library|function. The token before Pipe is the library name.
            // The token after Pipe is the function name.
            // Anything before the library name is the return type.
            int libIndex = pipeIndex - 1;
            if (libIndex < 0 || _tokens[libIndex].Kind != NATokenKind.Identifier)
                throw new NAParseException("Expected library name before '|'", PosOf(pipeIndex));

            // Parse return type if there are tokens before the library name
            if (libIndex > 0)
            {
                returnType = ParseTypeDesc();
                // After parsing the return type, we should be at the library name
                if (_pos != libIndex)
                    throw new NAParseException(
                        $"Unexpected tokens before library name at position {PosOf(_pos)}",
                        PosOf(_pos));
            }

            libraryPath = Expect(NATokenKind.Identifier).Value;
            Expect(NATokenKind.Pipe);

            if (!IsAtEnd && Peek().Kind == NATokenKind.Identifier)
            {
                functionName = Expect(NATokenKind.Identifier).Value;

                // Optional trailing * (pass-by-pointer) and & (threaded)
                if (!IsAtEnd && Peek().Kind == NATokenKind.Star)
                {
                    Advance();
                    passByPointer = true;
                }
                if (!IsAtEnd && Peek().Kind == NATokenKind.Ampersand)
                {
                    Advance();
                    threadSafe = true;
                }
            }
            else
            {
                throw new NAParseException("Expected function name after '|'", PosOf(_pos));
            }
        }

        // Parse arguments
        var args = new List<NAArgDesc>();
        while (!IsAtEnd)
        {
            args.Add(ParseArg());
        }

        return new NASpec(returnType, libraryPath, functionName, passByPointer, threadSafe, args);
    }

    // ── Argument parsing ────────────────────────────────────────────────────

    private NAArgDesc ParseArg()
    {
        var direction = TryParseDirection();
        var type = ParseTypeDesc();
        var arrayed = TryParseArraySpec(type);
        return new NAArgDesc(direction, arrayed);
    }

    private NADirection TryParseDirection()
    {
        if (IsAtEnd) return NADirection.None;
        return Peek().Kind switch
        {
            NATokenKind.DirIn => AdvanceAnd(NADirection.In),
            NATokenKind.DirOut => AdvanceAnd(NADirection.Out),
            NATokenKind.DirInOut => AdvanceAnd(NADirection.InOut),
            _ => NADirection.None,
        };
    }

    // ── Type description parsing ────────────────────────────────────────────

    private NATypeDesc ParseTypeDesc()
    {
        if (IsAtEnd)
            throw new NAParseException("Unexpected end of input while parsing type", -1);

        var token = Peek();

        // Null-terminated (0 prefix)
        if (token.Kind == NATokenKind.NullTerm)
        {
            Advance();
            return ParseNullTermType();
        }

        // Double-null (00 prefix)
        if (token.Kind == NATokenKind.DoubleNull)
        {
            Advance();
            return ParseDoubleNullType();
        }

        // Struct
        if (token.Kind == NATokenKind.LBrace ||
            token.Kind == NATokenKind.Aligned ||
            token.Kind == NATokenKind.Pack)
        {
            return ParseStructDesc();
        }

        // Primitive type letters
        if (IsTypeLetter(token.Kind))
        {
            return ParsePrimitiveType();
        }

        throw new NAParseException($"Unexpected token {token.Kind} '{token.Value}' at position {token.Position}", token.Position);
    }

    private NATypeDesc ParsePrimitiveType()
    {
        var token = Advance();
        char letter = token.Kind switch
        {
            NATokenKind.TypeI => 'I',
            NATokenKind.TypeU => 'U',
            NATokenKind.TypeC => 'C',
            NATokenKind.TypeT => 'T',
            NATokenKind.TypeF => 'F',
            NATokenKind.TypeD => 'D',
            NATokenKind.TypeJ => 'J',
            NATokenKind.TypeP => 'P',
            NATokenKind.TypeA => 'A',
            NATokenKind.TypeZ => 'Z',
            NATokenKind.TypeUTF => throw new NAParseException("UTF type is not yet supported", token.Position),
            _ => throw new NAParseException($"Expected type letter, got {token.Kind}", token.Position),
        };

        // Special types with no width
        if (letter == 'P') return new NAPointerDesc();
        if (letter == 'D') return new NADecimalDesc();
        if (letter == 'J') return new NAComplexDesc();
        if (letter == 'A') throw new NAParseException("APL array type 'A' is not supported", token.Position);
        if (letter == 'Z') throw new NAParseException("Compressed type 'Z' is not supported", token.Position);

        // Read optional width
        int width = TryReadWidth(letter);
        return new NAPrimitiveDesc(letter, width);
    }

    private NANullTermDesc ParseNullTermType()
    {
        if (IsAtEnd || !IsTypeLetter(Peek().Kind))
            throw new NAParseException("Expected type letter after '0' modifier", PosOf(_pos));

        var token = Advance();
        char letter = TypeLetterChar(token);
        if (letter is not ('T' or 'C'))
            throw new NAParseException($"Null-terminated modifier only valid with T or C, got {letter}", token.Position);

        int width = TryReadWidth(letter);
        return new NANullTermDesc(letter, width);
    }

    private NADoubleNullDesc ParseDoubleNullType()
    {
        if (IsAtEnd || !IsTypeLetter(Peek().Kind))
            throw new NAParseException("Expected type letter after '00' modifier", PosOf(_pos));

        var token = Advance();
        char letter = TypeLetterChar(token);
        if (letter is not ('T' or 'C'))
            throw new NAParseException($"Double-null modifier only valid with T or C, got {letter}", token.Position);

        int width = TryReadWidth(letter);
        return new NADoubleNullDesc(letter, width);
    }

    private NAStructDesc ParseStructDesc()
    {
        // Optional alignment modifier before the brace
        var alignMode = NAAlignMode.Packed;
        int? packSize = null;

        if (!IsAtEnd && Peek().Kind == NATokenKind.Aligned)
        {
            Advance();
            alignMode = NAAlignMode.Natural;
        }
        else if (!IsAtEnd && Peek().Kind == NATokenKind.Pack)
        {
            Advance();
            Expect(NATokenKind.LParen);
            var numTok = Expect(NATokenKind.Number);
            packSize = int.Parse(numTok.Value);
            Expect(NATokenKind.RParen);
            alignMode = NAAlignMode.Packed; // pack(N) is still "packed" with explicit size
        }

        Expect(NATokenKind.LBrace);

        var fields = new List<NAStructField>();
        while (!IsAtEnd && Peek().Kind != NATokenKind.RBrace)
        {
            var direction = TryParseDirection();
            var type = ParseTypeDesc();
            var arrayed = TryParseArraySpec(type);
            fields.Add(new NAStructField(direction, arrayed));
        }

        Expect(NATokenKind.RBrace);
        return new NAStructDesc(fields, alignMode, packSize);
    }

    // ── Array spec parsing ──────────────────────────────────────────────────

    private NATypeDesc TryParseArraySpec(NATypeDesc element)
    {
        if (IsAtEnd || Peek().Kind != NATokenKind.LBracket)
            return element;

        Advance(); // consume [

        // Empty brackets: []
        if (!IsAtEnd && Peek().Kind == NATokenKind.RBracket)
        {
            Advance();
            return new NAArrayedDesc(element, new VariableArrayKind());
        }

        // Star: [*] or [*:N]
        if (!IsAtEnd && Peek().Kind == NATokenKind.Star)
        {
            Advance();
            int? maxCount = null;
            if (!IsAtEnd && Peek().Kind == NATokenKind.Colon)
            {
                Advance();
                var numTok = Expect(NATokenKind.Number);
                maxCount = int.Parse(numTok.Value);
            }
            Expect(NATokenKind.RBracket);
            return new NAArrayedDesc(element, new CountedArrayKind(maxCount));
        }

        // Fixed count: [N]
        var countTok = Expect(NATokenKind.Number);
        int count = int.Parse(countTok.Value);
        Expect(NATokenKind.RBracket);
        return new NAArrayedDesc(element, new FixedArrayKind(count));
    }

    // ── Width parsing ───────────────────────────────────────────────────────

    private int TryReadWidth(char typeLetter)
    {
        if (!IsAtEnd && Peek().Kind == NATokenKind.Number)
        {
            var numTok = Advance();
            return int.Parse(numTok.Value);
        }

        // Default widths per type letter
        return typeLetter switch
        {
            'I' or 'U' => 4,  // default 32-bit
            'C' => 1,         // default 8-bit char
            'T' => 2,         // default 16-bit wide char (Windows)
            'F' => 8,         // default 64-bit double
            _ => 4,
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private bool IsAtEnd => _pos >= _tokens.Count || _tokens[_pos].Kind == NATokenKind.End;
    private NAToken Peek() => _tokens[_pos];

    private NAToken Advance()
    {
        var tok = _tokens[_pos];
        _pos++;
        return tok;
    }

    private T AdvanceAnd<T>(T value)
    {
        _pos++;
        return value;
    }

    private NAToken Expect(NATokenKind kind)
    {
        if (IsAtEnd)
            throw new NAParseException($"Expected {kind} but reached end of input", -1);
        var tok = _tokens[_pos];
        if (tok.Kind != kind)
            throw new NAParseException($"Expected {kind}, got {tok.Kind} '{tok.Value}' at position {tok.Position}", tok.Position);
        _pos++;
        return tok;
    }

    private int FindToken(NATokenKind kind)
    {
        for (int i = _pos; i < _tokens.Count; i++)
            if (_tokens[i].Kind == kind)
                return i;
        return -1;
    }

    private int PosOf(int tokenIndex)
    {
        if (tokenIndex >= 0 && tokenIndex < _tokens.Count)
            return _tokens[tokenIndex].Position;
        return -1;
    }

    private static bool IsTypeLetter(NATokenKind kind) => kind is
        NATokenKind.TypeI or NATokenKind.TypeU or NATokenKind.TypeC or
        NATokenKind.TypeT or NATokenKind.TypeF or NATokenKind.TypeD or
        NATokenKind.TypeJ or NATokenKind.TypeP or NATokenKind.TypeA or
        NATokenKind.TypeZ or NATokenKind.TypeUTF;

    private static char TypeLetterChar(NAToken token) => token.Kind switch
    {
        NATokenKind.TypeI => 'I',
        NATokenKind.TypeU => 'U',
        NATokenKind.TypeC => 'C',
        NATokenKind.TypeT => 'T',
        NATokenKind.TypeF => 'F',
        NATokenKind.TypeD => 'D',
        NATokenKind.TypeJ => 'J',
        NATokenKind.TypeP => 'P',
        NATokenKind.TypeA => 'A',
        NATokenKind.TypeZ => 'Z',
        _ => throw new NAParseException($"Expected type letter, got {token.Kind}", token.Position),
    };
}

/// <summary>Exception thrown during NA spec parsing.</summary>
public class NAParseException : Exception
{
    public int Position { get; }
    public NAParseException(string message, int position) : base(message)
    {
        Position = position;
    }
}
