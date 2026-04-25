using BinScript.Core.Compiler.Ast;
using BinScript.Core.Model;

namespace BinScript.Core.Compiler;

/// <summary>
/// Recursive-descent parser that consumes a token list from <see cref="Lexer"/>
/// and produces a <see cref="ScriptFile"/> AST together with diagnostics.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly string _filePath;

    public Parser(List<Token> tokens, string filePath = "")
    {
        _tokens = tokens;
        _filePath = filePath;
    }

    // ════════════════════════════════════════════════════════════════
    //  Public API
    // ════════════════════════════════════════════════════════════════

    public (ScriptFile File, IReadOnlyList<Diagnostic> Diagnostics) Parse()
    {
        Endianness? defaultEndian = null;
        string? defaultEncoding = null;
        var imports = new List<ImportDecl>();
        var parameters = new List<ParamDecl>();
        var structs = new List<StructDecl>();
        var bitsStructs = new List<BitsStructDecl>();
        var enums = new List<EnumDecl>();
        var constants = new List<ConstDecl>();
        var maps = new List<MapDecl>();

        var startSpan = Current().Span;
        bool isRoot = false;
        CoverageMode? coverage = null;

        while (Current().Type != TokenType.Eof)
        {
            try
            {
                switch (Current().Type)
                {
                    case TokenType.DefaultEndian:
                        defaultEndian = ParseDefaultEndian();
                        break;

                    case TokenType.DefaultEncoding:
                        defaultEncoding = ParseDefaultEncoding();
                        break;

                    case TokenType.Import:
                        imports.Add(ParseImport());
                        break;

                    case TokenType.Param:
                        parameters.Add(ParseParam());
                        break;

                    case TokenType.Root:
                        Advance();
                        isRoot = true;
                        continue; // skip flag reset

                    case TokenType.Coverage:
                        coverage = ParseCoverage();
                        continue; // skip flag reset

                    case TokenType.Struct:
                        structs.Add(ParseStruct(isRoot, coverage));
                        break;

                    case TokenType.Bits:
                        bitsStructs.Add(ParseBitsStruct());
                        break;

                    case TokenType.Enum:
                        enums.Add(ParseEnum());
                        break;

                    case TokenType.Const:
                        constants.Add(ParseConst());
                        break;

                    case TokenType.Map:
                        maps.Add(ParseMapDecl());
                        break;

                    default:
                        Error($"Unexpected token '{Current().Text}' at top level");
                        SkipToNextTopLevel();
                        break;
                }
            }
            catch (ParseException)
            {
                SkipToNextTopLevel();
            }

            isRoot = false;
            coverage = null;
        }

        var endSpan = Current().Span;
        var file = new ScriptFile(
            _filePath, defaultEndian, defaultEncoding,
            imports, parameters, structs, bitsStructs, enums, constants, maps,
            SourceSpan.Merge(startSpan, endSpan));

        return (file, _diagnostics);
    }

    // ════════════════════════════════════════════════════════════════
    //  Token navigation
    // ════════════════════════════════════════════════════════════════

    private Token Current() =>
        _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1];

    private Token Peek(int offset = 1)
    {
        int idx = _pos + offset;
        return idx < _tokens.Count ? _tokens[idx] : _tokens[^1];
    }

    private Token Advance()
    {
        var tok = Current();
        if (_pos < _tokens.Count - 1)
            _pos++;
        return tok;
    }

    private Token Expect(TokenType type)
    {
        if (Current().Type == type)
            return Advance();

        throw Error($"Expected {type} but found {Current().Type} ('{Current().Text}')");
    }

    private bool Match(TokenType type)
    {
        if (Current().Type != type) return false;
        Advance();
        return true;
    }

    /// <summary>Accepts an identifier or bare underscore as a name.</summary>
    private Token ExpectName()
    {
        if (Current().Type is TokenType.Identifier or TokenType.Underscore)
            return Advance();

        throw Error($"Expected identifier but found {Current().Type} ('{Current().Text}')");
    }

    // ════════════════════════════════════════════════════════════════
    //  Diagnostics & error recovery
    // ════════════════════════════════════════════════════════════════

    private ParseException Error(string message)
    {
        _diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Error, "BSC001", message, Current().Span));
        return new ParseException();
    }

    private void SkipToNextTopLevel()
    {
        while (Current().Type != TokenType.Eof)
        {
            if (Current().Type is TokenType.Struct or TokenType.Bits
                or TokenType.Enum or TokenType.Const
                or TokenType.Import or TokenType.Param
                or TokenType.Root or TokenType.Coverage
                or TokenType.DefaultEndian or TokenType.DefaultEncoding
                or TokenType.Map)
                return;

            Advance();
        }
    }

    private sealed class ParseException : Exception;

    // ════════════════════════════════════════════════════════════════
    //  Top-level declaration parsers
    // ════════════════════════════════════════════════════════════════

    private Endianness ParseDefaultEndian()
    {
        Advance(); // @default_endian
        Expect(TokenType.LeftParen);
        var name = Advance();
        Expect(TokenType.RightParen);
        return name.Text switch
        {
            "little" => Endianness.Little,
            "big" => Endianness.Big,
            _ => throw Error($"Expected 'little' or 'big' but found '{name.Text}'"),
        };
    }

    private string ParseDefaultEncoding()
    {
        Advance(); // @default_encoding
        Expect(TokenType.LeftParen);
        var name = Advance();
        Expect(TokenType.RightParen);
        return name.Text;
    }

    private ImportDecl ParseImport()
    {
        var start = Advance(); // @import
        var path = Expect(TokenType.StringLiteral);
        return new ImportDecl(path.Text, SourceSpan.Merge(start.Span, path.Span));
    }

    private ParamDecl ParseParam()
    {
        var start = Advance(); // @param
        var name = Expect(TokenType.Identifier);
        Expect(TokenType.Colon);
        var type = ParseTypeReference();
        return new ParamDecl(name.Text, type, SourceSpan.Merge(start.Span, type.Span));
    }

    private CoverageMode ParseCoverage()
    {
        Advance(); // @coverage
        Expect(TokenType.LeftParen);
        var name = Advance();
        Expect(TokenType.RightParen);
        return name.Text switch
        {
            "partial" => CoverageMode.Partial,
            _ => throw Error($"Expected 'partial' but found '{name.Text}'"),
        };
    }

    private ConstDecl ParseConst()
    {
        var start = Advance(); // const
        var name = Expect(TokenType.Identifier);
        Expect(TokenType.Equal);
        var value = ParseExpression();
        var end = Expect(TokenType.Semicolon);
        return new ConstDecl(name.Text, value, SourceSpan.Merge(start.Span, end.Span));
    }

    /// <summary>Parses <c>@map name(param: type, ...): return_type = expression</c>.</summary>
    private MapDecl ParseMapDecl()
    {
        var start = Advance(); // @map
        var name = Expect(TokenType.Identifier);
        Expect(TokenType.LeftParen);

        var mapParams = new List<MapParam>();
        if (Current().Type != TokenType.RightParen)
        {
            do
            {
                var pStart = Current();
                var pName = Expect(TokenType.Identifier);
                Expect(TokenType.Colon);
                var pType = ParseAnnotationTypeReference();
                mapParams.Add(new MapParam(pName.Text, pType,
                    SourceSpan.Merge(pName.Span, pType.Span)));
            }
            while (Match(TokenType.Comma));
        }
        Expect(TokenType.RightParen);

        Expect(TokenType.Colon);
        var returnType = ParseAnnotationTypeReference();
        Expect(TokenType.Equal);
        var body = ParseExpression();

        return new MapDecl(name.Text, mapParams, returnType, body,
            SourceSpan.Merge(start.Span, body.Span));
    }

    // ════════════════════════════════════════════════════════════════
    //  Struct
    // ════════════════════════════════════════════════════════════════

    private StructDecl ParseStruct(bool isRoot, CoverageMode? coverage)
    {
        var start = Advance(); // struct
        var name = Expect(TokenType.Identifier);

        var parameters = new List<string>();
        if (Match(TokenType.LeftParen))
        {
            if (Current().Type != TokenType.RightParen)
            {
                do { parameters.Add(Expect(TokenType.Identifier).Text); }
                while (Match(TokenType.Comma));
            }
            Expect(TokenType.RightParen);
        }

        // Optional @max_depth(N) after name/params, before '{'
        int? maxDepth = null;
        if (Current().Type == TokenType.MaxDepth)
        {
            Advance(); // @max_depth
            Expect(TokenType.LeftParen);
            var depthTok = Expect(TokenType.IntLiteral);
            Expect(TokenType.RightParen);
            maxDepth = (int)ParseLongText(depthTok.Text);
        }

        Expect(TokenType.LeftBrace);
        var members = ParseMembers();
        var end = Expect(TokenType.RightBrace);

        return new StructDecl(
            name.Text, parameters, members, isRoot, coverage, maxDepth,
            SourceSpan.Merge(start.Span, end.Span));
    }

    private List<MemberDecl> ParseMembers()
    {
        var members = new List<MemberDecl>();
        while (Current().Type is not TokenType.RightBrace and not TokenType.Eof)
        {
            members.Add(ParseMember());
            Match(TokenType.Comma); // optional trailing comma
        }
        return members;
    }

    private MemberDecl ParseMember() => Current().Type switch
    {
        TokenType.Seek => ParseSeek(),
        TokenType.At => ParseAtBlock(),
        TokenType.Let => ParseLet(),
        TokenType.Align => ParseAlign(),
        TokenType.Skip => ParseSkip(),
        TokenType.Assert => ParseAssert(),
        TokenType.Derived => ParseDerivedField(),
        TokenType.Hidden => ParseHiddenField(),
        _ => ParseField(new FieldModifiers(), Current().Span),
    };

    // ── member-level directive parsers ──

    private SeekDirective ParseSeek()
    {
        var start = Advance(); // @seek
        Expect(TokenType.LeftParen);
        var expr = ParseExpression();
        var end = Expect(TokenType.RightParen);
        return new SeekDirective(expr, SourceSpan.Merge(start.Span, end.Span));
    }

    private AtBlock ParseAtBlock()
    {
        var start = Advance(); // @at
        Expect(TokenType.LeftParen);
        var expr = ParseExpression();
        Expect(TokenType.RightParen);

        // Optional 'when' guard (spec §4.1)
        Expression? guard = null;
        if (Current().Type == TokenType.When)
        {
            Advance(); // when
            guard = ParseExpression();
        }

        Expect(TokenType.LeftBrace);
        var members = ParseMembers();
        var end = Expect(TokenType.RightBrace);
        return new AtBlock(expr, guard, members, SourceSpan.Merge(start.Span, end.Span));
    }

    private LetBinding ParseLet()
    {
        var start = Advance(); // @let
        var name = Expect(TokenType.Identifier);
        Expect(TokenType.Equal);
        var expr = ParseExpression();
        return new LetBinding(name.Text, expr, SourceSpan.Merge(start.Span, expr.Span));
    }

    private AlignDirective ParseAlign()
    {
        var start = Advance(); // @align
        Expect(TokenType.LeftParen);
        var expr = ParseExpression();
        var end = Expect(TokenType.RightParen);
        return new AlignDirective(expr, SourceSpan.Merge(start.Span, end.Span));
    }

    private SkipDirective ParseSkip()
    {
        var start = Advance(); // @skip
        Expect(TokenType.LeftParen);
        var numTok = Expect(TokenType.IntLiteral);
        var end = Expect(TokenType.RightParen);
        return new SkipDirective(
            (int)ParseLongText(numTok.Text),
            SourceSpan.Merge(start.Span, end.Span));
    }

    private AssertDirective ParseAssert()
    {
        var start = Advance(); // @assert
        Expect(TokenType.LeftParen);
        var cond = ParseExpression();
        Expect(TokenType.Comma);
        var msg = Expect(TokenType.StringLiteral);
        var end = Expect(TokenType.RightParen);
        return new AssertDirective(cond, msg.Text, SourceSpan.Merge(start.Span, end.Span));
    }

    private FieldDecl ParseDerivedField()
    {
        var start = Advance(); // @derived
        var name = ExpectName();
        Expect(TokenType.Colon);
        var type = ParseTypeReference();
        Expect(TokenType.Equal);
        var derivedExpr = ParseExpression();

        var modifiers = new FieldModifiers
        {
            IsDerived = true,
            DerivedExpression = derivedExpr,
        };
        return new FieldDecl(
            name.Text, type, null, null, modifiers,
            SourceSpan.Merge(start.Span, derivedExpr.Span));
    }

    private FieldDecl ParseHiddenField()
    {
        var start = Advance(); // @hidden
        return ParseField(new FieldModifiers { IsHidden = true }, start.Span);
    }

    // ── field declaration ──

    private FieldDecl ParseField(FieldModifiers modifiers, SourceSpan startSpan)
    {
        var name = ExpectName();
        Expect(TokenType.Colon);
        var type = ParseTypeReference();

        Expression? magicValue = null;
        if (Match(TokenType.Equal))
            magicValue = ParseExpression();

        var arraySpec = ParseArraySpec();
        modifiers = ParseFieldModifiers(modifiers);

        var endSpan = arraySpec?.Span ?? magicValue?.Span ?? type.Span;
        return new FieldDecl(
            name.Text, type, magicValue, arraySpec, modifiers,
            SourceSpan.Merge(startSpan, endSpan));
    }

    private FieldModifiers ParseFieldModifiers(FieldModifiers current)
    {
        while (true)
        {
            switch (Current().Type)
            {
                case TokenType.Hidden:
                    Advance();
                    current = current with { IsHidden = true };
                    break;

                case TokenType.Encoding:
                    Advance();
                    Expect(TokenType.LeftParen);
                    var enc = Advance();
                    Expect(TokenType.RightParen);
                    current = current with { Encoding = enc.Text };
                    break;

                case TokenType.Assert:
                    Advance();
                    Expect(TokenType.LeftParen);
                    var assertExpr = ParseExpression();
                    Expect(TokenType.Comma);
                    var assertMsg = Expect(TokenType.StringLiteral);
                    Expect(TokenType.RightParen);
                    current = current with
                    {
                        AssertExpression = assertExpr,
                        AssertMessage = assertMsg.Text,
                    };
                    break;

                case TokenType.Inline:
                    Advance();
                    current = current with { IsInline = true };
                    break;

                case TokenType.ShowPtr:
                    Advance();
                    current = current with { ShowPtr = true };
                    break;

                default:
                    return current;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Array specifications
    // ════════════════════════════════════════════════════════════════

    private ArraySpec? ParseArraySpec()
    {
        if (Current().Type != TokenType.LeftBracket)
            return null;

        var start = Advance(); // [

        // Empty brackets → @until / @until_sentinel / @greedy
        if (Match(TokenType.RightBracket))
        {
            switch (Current().Type)
            {
                case TokenType.Until:
                {
                    Advance();
                    Expect(TokenType.LeftParen);
                    var cond = ParseExpression();
                    var end = Expect(TokenType.RightParen);
                    return new UntilArraySpec(cond, SourceSpan.Merge(start.Span, end.Span));
                }
                case TokenType.UntilSentinel:
                {
                    Advance();
                    Expect(TokenType.LeftParen);
                    var paramName = Expect(TokenType.Identifier);
                    Expect(TokenType.Arrow);
                    var pred = ParseExpression();
                    var end = Expect(TokenType.RightParen);
                    return new SentinelArraySpec(
                        paramName.Text, pred,
                        SourceSpan.Merge(start.Span, end.Span));
                }
                case TokenType.Greedy:
                {
                    var end = Advance();
                    return new GreedyArraySpec(SourceSpan.Merge(start.Span, end.Span));
                }
                default:
                    throw Error("Expected @until, @until_sentinel, or @greedy after []");
            }
        }

        // [count_expr]
        var count = ParseExpression();
        var bracket = Expect(TokenType.RightBracket);
        return new CountArraySpec(count, SourceSpan.Merge(start.Span, bracket.Span));
    }

    // ════════════════════════════════════════════════════════════════
    //  Type references
    // ════════════════════════════════════════════════════════════════

    private TypeReference ParseTypeReference()
    {
        var type = ParseBaseTypeReference();

        // Nullable suffix: T?
        if (Current().Type == TokenType.QuestionMark)
        {
            var q = Advance();
            type = new NullableTypeRef(type, SourceSpan.Merge(type.Span, q.Span));
        }

        return type;
    }

    private TypeReference ParseBaseTypeReference()
    {
        var tok = Current();

        switch (tok.Type)
        {
            // ── pointer types (spec §3.4) ──

            case TokenType.Ptr:
            case TokenType.RelPtr:
                return ParsePtrType();

            // ── special compound types ──

            case TokenType.CString:
                Advance();
                return new CStringTypeRef(tok.Span);

            case TokenType.String:
            {
                Advance();
                Expect(TokenType.LeftBracket);
                var len = ParseExpression();
                var end = Expect(TokenType.RightBracket);
                return new StringTypeRef(len, SourceSpan.Merge(tok.Span, end.Span));
            }

            case TokenType.FixedString:
            {
                Advance();
                Expect(TokenType.LeftBracket);
                var len = ParseExpression();
                var end = Expect(TokenType.RightBracket);
                return new FixedStringTypeRef(len, SourceSpan.Merge(tok.Span, end.Span));
            }

            case TokenType.Bytes:
            {
                Advance();
                Expect(TokenType.LeftBracket);
                var len = ParseExpression();
                var end = Expect(TokenType.RightBracket);
                return new BytesTypeRef(len, SourceSpan.Merge(tok.Span, end.Span));
            }

            case TokenType.Bit:
                Advance();
                return new BitTypeRef(tok.Span);

            case TokenType.Bits:
            {
                Advance();
                Expect(TokenType.LeftBracket);
                var countTok = Expect(TokenType.IntLiteral);
                var end = Expect(TokenType.RightBracket);
                return new BitsTypeRef(
                    (int)ParseLongText(countTok.Text),
                    SourceSpan.Merge(tok.Span, end.Span));
            }

            // ── match-as-type ──

            case TokenType.Match:
            {
                var matchExpr = ParseMatchExpression();
                return new MatchTypeRef(matchExpr, matchExpr.Span);
            }

            // ── named / parameterised type ──

            case TokenType.Identifier:
            {
                var name = Advance();
                var args = new List<Expression>();
                if (Match(TokenType.LeftParen))
                {
                    if (Current().Type != TokenType.RightParen)
                    {
                        do { args.Add(ParseExpression()); }
                        while (Match(TokenType.Comma));
                    }
                    var end = Expect(TokenType.RightParen);
                    return new NamedTypeRef(
                        name.Text, args,
                        SourceSpan.Merge(name.Span, end.Span));
                }
                return new NamedTypeRef(name.Text, args, name.Span);
            }

            // ── primitives ──

            default:
            {
                if (TokenTypeInfo.IsPrimitiveType(tok.Type))
                {
                    Advance();
                    return new PrimitiveTypeRef(tok.Type, tok.Span);
                }
                throw Error($"Expected type but found {tok.Type} ('{tok.Text}')");
            }
        }
    }

    /// <summary>Parses <c>ptr&lt;T, width&gt;</c> or <c>relptr&lt;T, width&gt;</c>.</summary>
    private PtrTypeRef ParsePtrType()
    {
        var start = Advance(); // ptr or relptr
        bool isRelative = start.Type == TokenType.RelPtr;

        Expect(TokenType.Less); // <

        var innerType = ParseBaseTypeReference();

        // Optional inner field modifiers (e.g., @encoding(utf16le))
        FieldModifiers? innerMods = null;
        if (Current().Type is TokenType.Encoding or TokenType.Hidden)
            innerMods = ParseFieldModifiers(new FieldModifiers());

        // Optional width type after comma; defaults to u64 if omitted
        PrimitiveTypeRef? width = null;
        if (Match(TokenType.Comma))
        {
            var widthTok = Current();
            if (!TokenTypeInfo.IsPrimitiveType(widthTok.Type))
                throw Error($"Expected primitive type for pointer width but found {widthTok.Type}");
            Advance();
            width = new PrimitiveTypeRef(widthTok.Type, widthTok.Span);
        }

        var end = Expect(TokenType.Greater); // >

        return new PtrTypeRef(innerType, width, isRelative, innerMods,
            SourceSpan.Merge(start.Span, end.Span));
    }

    /// <summary>Parses a type reference with optional <c>[]</c> array suffix, used in <c>@map</c> parameter types.</summary>
    private TypeReference ParseAnnotationTypeReference()
    {
        var type = ParseTypeReference();

        // Array suffix: SectionHeader[]
        if (Current().Type == TokenType.LeftBracket && Peek().Type == TokenType.RightBracket)
        {
            var start = Advance(); // [
            var end = Advance(); // ]
            type = new ArrayTypeRef(type, null, SourceSpan.Merge(type.Span, end.Span));
        }

        return type;
    }

    private Expression ParseExpression(int minPrec = 0)
    {
        var left = ParseUnary();

        while (true)
        {
            var (op, prec) = GetBinaryOp(Current().Type);
            if (op is null || prec < minPrec) break;

            Advance(); // consume operator
            var right = ParseExpression(prec + 1); // left-associative
            left = new BinaryExpr(left, op.Value, right,
                SourceSpan.Merge(left.Span, right.Span));
        }

        return left;
    }

    private Expression ParseUnary()
    {
        var tok = Current();
        switch (tok.Type)
        {
            case TokenType.Bang:
                Advance();
                var notOp = ParseUnary();
                return new UnaryExpr(UnaryOp.LogicNot, notOp,
                    SourceSpan.Merge(tok.Span, notOp.Span));

            case TokenType.Tilde:
                Advance();
                var bitNotOp = ParseUnary();
                return new UnaryExpr(UnaryOp.BitNot, bitNotOp,
                    SourceSpan.Merge(tok.Span, bitNotOp.Span));

            case TokenType.Minus:
                Advance();
                var negOp = ParseUnary();
                return new UnaryExpr(UnaryOp.Negate, negOp,
                    SourceSpan.Merge(tok.Span, negOp.Span));

            default:
                return ParsePostfix();
        }
    }

    private Expression ParsePostfix()
    {
        var expr = ParsePrimary();

        while (true)
        {
            if (Current().Type == TokenType.Dot)
            {
                Advance(); // .
                var field = Expect(TokenType.Identifier);

                if (Match(TokenType.LeftParen))
                {
                    // method call: obj.method(args)
                    var args = ParseArgList();
                    var end = Expect(TokenType.RightParen);
                    expr = new MethodCallExpr(expr, field.Text, args,
                        SourceSpan.Merge(expr.Span, end.Span));
                }
                else
                {
                    expr = new FieldAccessExpr(expr, field.Text,
                        SourceSpan.Merge(expr.Span, field.Span));
                }
            }
            else if (Current().Type == TokenType.LeftBracket)
            {
                Advance(); // [
                var index = ParseExpression();
                var end = Expect(TokenType.RightBracket);
                expr = new IndexAccessExpr(expr, index,
                    SourceSpan.Merge(expr.Span, end.Span));
            }
            else break;
        }
        return expr;
    }

    private Expression ParsePrimary()
    {
        var tok = Current();
        switch (tok.Type)
        {
            case TokenType.IntLiteral:
                Advance();
                return new IntLiteralExpr(ParseLongText(tok.Text), tok.Span);

            case TokenType.StringLiteral:
                Advance();
                return new StringLiteralExpr(tok.Text, tok.Span);

            case TokenType.TrueLiteral:
                Advance();
                return new BoolLiteralExpr(true, tok.Span);

            case TokenType.FalseLiteral:
                Advance();
                return new BoolLiteralExpr(false, tok.Span);

            case TokenType.NullLiteral:
                Advance();
                return new NullLiteralExpr(tok.Span);

            case TokenType.Identifier:
            {
                Advance();
                // Lambda: ident => expr
                if (Current().Type == TokenType.Arrow)
                {
                    Advance(); // =>
                    var body = ParseExpression();
                    return new LambdaExpr(tok.Text, body,
                        SourceSpan.Merge(tok.Span, body.Span));
                }
                // Generic call: ident(args) — for @map invocations
                if (Current().Type == TokenType.LeftParen)
                {
                    Advance(); // (
                    var args = ParseArgList();
                    var end = Expect(TokenType.RightParen);
                    return new FunctionCallExpr(tok.Text, args,
                        SourceSpan.Merge(tok.Span, end.Span));
                }
                return new IdentifierExpr(tok.Text, tok.Span);
            }

            case TokenType.LeftParen:
                Advance();
                var inner = ParseExpression();
                Expect(TokenType.RightParen);
                return inner;

            // Block expression: { @let bindings, result_expr }
            case TokenType.LeftBrace:
                return ParseBlockExpr();

            // ── directives used as zero-arg built-in functions ──
            case TokenType.Remaining:
                Advance();
                return new FunctionCallExpr("remaining", [], tok.Span);

            case TokenType.Offset:
                Advance();
                return new FunctionCallExpr("offset", [], tok.Span);

            case TokenType.InputSize:
                Advance();
                return new FunctionCallExpr("input_size", [], tok.Span);

            // ── directives used as built-in function calls ──
            case TokenType.SizeOf:
            case TokenType.OffsetOf:
            case TokenType.Count:
            case TokenType.StrLen:
            case TokenType.Crc32:
            case TokenType.Adler32:
                return ParseDirectiveCall();

            default:
                throw Error($"Expected expression but found {tok.Type} ('{tok.Text}')");
        }
    }

    private FunctionCallExpr ParseDirectiveCall()
    {
        var tok = Advance(); // directive token (e.g. @sizeof)
        // strip leading '@' for the function name
        var name = tok.Text.StartsWith('@') ? tok.Text[1..] : tok.Text;

        Expect(TokenType.LeftParen);
        var args = ParseArgList();
        var end = Expect(TokenType.RightParen);
        return new FunctionCallExpr(name, args, SourceSpan.Merge(tok.Span, end.Span));
    }

    /// <summary>Parses a block expression: <c>{ @let x = e, ..., result }</c>.</summary>
    private BlockExpr ParseBlockExpr()
    {
        var start = Advance(); // {
        var bindings = new List<MapLetBinding>();

        while (Current().Type == TokenType.Let)
        {
            var letTok = Advance(); // @let
            var name = Expect(TokenType.Identifier);
            Expect(TokenType.Equal);
            var val = ParseExpression();
            var binding = new MapLetBinding(name.Text, val,
                SourceSpan.Merge(letTok.Span, val.Span));
            bindings.Add(binding);
            Expect(TokenType.Comma);
        }

        var result = ParseExpression();
        var end = Expect(TokenType.RightBrace);
        return new BlockExpr(bindings, result,
            SourceSpan.Merge(start.Span, end.Span));
    }

    private List<Expression> ParseArgList()
    {
        var args = new List<Expression>();
        if (Current().Type != TokenType.RightParen)
        {
            do { args.Add(ParseExpression()); }
            while (Match(TokenType.Comma));
        }
        return args;
    }

    // ════════════════════════════════════════════════════════════════
    //  Match expression (used in type position via MatchTypeRef)
    // ════════════════════════════════════════════════════════════════

    private MatchExpr ParseMatchExpression()
    {
        var start = Expect(TokenType.Match);
        Expect(TokenType.LeftParen);
        var discriminant = ParseExpression();
        Expect(TokenType.RightParen);
        Expect(TokenType.LeftBrace);

        var arms = new List<MatchArm>();
        while (Current().Type is not TokenType.RightBrace and not TokenType.Eof)
        {
            arms.Add(ParseMatchArm());
            Match(TokenType.Comma);
        }

        var end = Expect(TokenType.RightBrace);
        return new MatchExpr(discriminant, arms, SourceSpan.Merge(start.Span, end.Span));
    }

    private MatchArm ParseMatchArm()
    {
        var startSpan = Current().Span;
        var pattern = ParseMatchPattern();

        Expression? guard = null;
        if (Match(TokenType.When))
            guard = ParseExpression();

        Expect(TokenType.Arrow);
        var type = ParseTypeReference();
        return new MatchArm(pattern, guard, type,
            SourceSpan.Merge(startSpan, type.Span));
    }

    private MatchPattern ParseMatchPattern()
    {
        if (Current().Type == TokenType.Underscore)
        {
            var tok = Advance();
            return new WildcardPattern(tok.Span);
        }

        var expr = ParseExpression();

        // range pattern: low..high
        if (Current().Type == TokenType.DotDot)
        {
            Advance();
            var high = ParseExpression();
            return new RangePattern(expr, high,
                SourceSpan.Merge(expr.Span, high.Span));
        }

        // identifier pattern (simple identifier followed by 'when')
        if (expr is IdentifierExpr idExpr && Current().Type == TokenType.When)
            return new IdentifierPattern(idExpr.Name, idExpr.Span);

        return new ValuePattern(expr, expr.Span);
    }

    // ════════════════════════════════════════════════════════════════
    //  Bits struct
    // ════════════════════════════════════════════════════════════════

    private BitsStructDecl ParseBitsStruct()
    {
        var start = Advance(); // bits
        Expect(TokenType.Struct);
        var name = Expect(TokenType.Identifier);
        Expect(TokenType.Colon);
        var baseType = ParseTypeReference();
        Expect(TokenType.LeftBrace);

        var fields = new List<BitFieldDecl>();
        while (Current().Type is not TokenType.RightBrace and not TokenType.Eof)
        {
            fields.Add(ParseBitField());
            Match(TokenType.Comma);
        }

        var end = Expect(TokenType.RightBrace);
        return new BitsStructDecl(
            name.Text, baseType, fields,
            SourceSpan.Merge(start.Span, end.Span));
    }

    private BitFieldDecl ParseBitField()
    {
        var name = ExpectName();
        Expect(TokenType.Colon);

        BitFieldType fieldType;
        SourceSpan endSpan;

        if (Current().Type == TokenType.Bit)
        {
            endSpan = Advance().Span;
            fieldType = new SingleBit();
        }
        else if (Current().Type == TokenType.Bits)
        {
            Advance();
            Expect(TokenType.LeftBracket);
            var countTok = Expect(TokenType.IntLiteral);
            endSpan = Expect(TokenType.RightBracket).Span;
            fieldType = new MultiBits((int)ParseLongText(countTok.Text));
        }
        else
        {
            throw Error($"Expected 'bit' or 'bits[N]' but found {Current().Type}");
        }

        return new BitFieldDecl(name.Text, fieldType,
            SourceSpan.Merge(name.Span, endSpan));
    }

    // ════════════════════════════════════════════════════════════════
    //  Enum
    // ════════════════════════════════════════════════════════════════

    private EnumDecl ParseEnum()
    {
        var start = Advance(); // enum
        var name = Expect(TokenType.Identifier);
        Expect(TokenType.Colon);
        var baseType = ParseTypeReference();
        Expect(TokenType.LeftBrace);

        var variants = new List<EnumVariant>();
        while (Current().Type is not TokenType.RightBrace and not TokenType.Eof)
        {
            var vName = Expect(TokenType.Identifier);
            Expect(TokenType.Equal);
            var value = ParseExpression();
            variants.Add(new EnumVariant(
                vName.Text, value,
                SourceSpan.Merge(vName.Span, value.Span)));
            Match(TokenType.Comma);
        }

        var end = Expect(TokenType.RightBrace);
        return new EnumDecl(name.Text, baseType, variants,
            SourceSpan.Merge(start.Span, end.Span));
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    private static long ParseLongText(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt64(text[2..], 16);
        if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt64(text[2..], 2);
        if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt64(text[2..], 8);
        return long.Parse(text);
    }

    private static (BinaryOp? Op, int Precedence) GetBinaryOp(TokenType type) => type switch
    {
        TokenType.PipePipe            => (BinaryOp.LogicOr,  1),
        TokenType.AmpersandAmpersand  => (BinaryOp.LogicAnd, 2),
        TokenType.Pipe                => (BinaryOp.BitOr,    3),
        TokenType.Caret               => (BinaryOp.BitXor,   4),
        TokenType.Ampersand           => (BinaryOp.BitAnd,   5),
        TokenType.EqualEqual          => (BinaryOp.Eq,       6),
        TokenType.BangEqual           => (BinaryOp.Ne,       6),
        TokenType.Less                => (BinaryOp.Lt,       7),
        TokenType.Greater             => (BinaryOp.Gt,       7),
        TokenType.LessEqual           => (BinaryOp.Le,       7),
        TokenType.GreaterEqual        => (BinaryOp.Ge,       7),
        TokenType.LeftShift           => (BinaryOp.Shl,      8),
        TokenType.RightShift          => (BinaryOp.Shr,      8),
        TokenType.Plus                => (BinaryOp.Add,      9),
        TokenType.Minus               => (BinaryOp.Sub,      9),
        TokenType.Star                => (BinaryOp.Mul,     10),
        TokenType.Slash               => (BinaryOp.Div,     10),
        TokenType.Percent             => (BinaryOp.Mod,     10),
        _                             => (null, -1),
    };
}
