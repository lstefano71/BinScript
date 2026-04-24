namespace BinScript.Tests.Compiler;

using BinScript.Core.Compiler;
using BinScript.Core.Compiler.Ast;
using BinScript.Core.Model;

public class ParserTests
{
    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private static ScriptFile Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens);
        var (file, diagnostics) = parser.Parse();
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return file;
    }

    private static (ScriptFile File, IReadOnlyList<Diagnostic> Diagnostics) ParseWithDiagnostics(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    /// <summary>Helper to parse a let-binding expression inside a struct.</summary>
    private static Expression ParseExpr(string expr)
    {
        var file = Parse($"struct T {{ @let x = {expr} }}");
        var let_ = Assert.IsType<LetBinding>(Assert.Single(file.Structs).Members[0]);
        return let_.Value;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Top-level directives
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TopLevel_DefaultEndian_Little()
    {
        var file = Parse("@default_endian(little)");
        Assert.Equal(Endianness.Little, file.DefaultEndian);
    }

    [Fact]
    public void TopLevel_DefaultEndian_Big()
    {
        var file = Parse("@default_endian(big)");
        Assert.Equal(Endianness.Big, file.DefaultEndian);
    }

    [Fact]
    public void TopLevel_DefaultEncoding()
    {
        var file = Parse("@default_encoding(utf8)");
        Assert.Equal("utf8", file.DefaultEncoding);
    }

    [Fact]
    public void TopLevel_Import()
    {
        var file = Parse("""@import "pe/dos_header" """);
        var imp = Assert.Single(file.Imports);
        Assert.Equal("pe/dos_header", imp.Path);
    }

    [Fact]
    public void TopLevel_Param()
    {
        var file = Parse("@param version: u32");
        var p = Assert.Single(file.Params);
        Assert.Equal("version", p.Name);
        var pt = Assert.IsType<PrimitiveTypeRef>(p.Type);
        Assert.Equal(TokenType.U32, pt.PrimitiveToken);
    }

    [Fact]
    public void TopLevel_Const_Hex()
    {
        var file = Parse("const PE_MAGIC = 0x00004550;");
        var c = Assert.Single(file.Constants);
        Assert.Equal("PE_MAGIC", c.Name);
        var lit = Assert.IsType<IntLiteralExpr>(c.Value);
        Assert.Equal(0x00004550L, lit.Value);
    }

    [Fact]
    public void TopLevel_Const_Decimal()
    {
        var file = Parse("const MAX = 255;");
        var c = Assert.Single(file.Constants);
        Assert.Equal("MAX", c.Name);
        Assert.Equal(255L, Assert.IsType<IntLiteralExpr>(c.Value).Value);
    }

    [Fact]
    public void TopLevel_MultipleImports()
    {
        var file = Parse("""
            @import "a"
            @import "b"
        """);
        Assert.Equal(2, file.Imports.Count);
        Assert.Equal("a", file.Imports[0].Path);
        Assert.Equal("b", file.Imports[1].Path);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Struct parsing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Struct_SimplePrimitiveFields()
    {
        var file = Parse("struct Foo { x: u32, y: u16 }");
        var s = Assert.Single(file.Structs);
        Assert.Equal("Foo", s.Name);
        Assert.False(s.IsRoot);
        Assert.Null(s.Coverage);
        Assert.Equal(2, s.Members.Count);

        var f0 = Assert.IsType<FieldDecl>(s.Members[0]);
        Assert.Equal("x", f0.Name);
        Assert.IsType<PrimitiveTypeRef>(f0.Type);

        var f1 = Assert.IsType<FieldDecl>(s.Members[1]);
        Assert.Equal("y", f1.Name);
    }

    [Fact]
    public void Struct_Root()
    {
        var file = Parse("@root struct Main { x: u8 }");
        var s = Assert.Single(file.Structs);
        Assert.True(s.IsRoot);
    }

    [Fact]
    public void Struct_WithParameters()
    {
        var file = Parse("struct Foo(bar, baz) { x: u8 }");
        var s = Assert.Single(file.Structs);
        Assert.Equal(["bar", "baz"], s.Parameters);
    }

    [Fact]
    public void Struct_CoveragePartial()
    {
        var file = Parse("@coverage(partial) struct Foo { x: u8 }");
        var s = Assert.Single(file.Structs);
        Assert.Equal(CoverageMode.Partial, s.Coverage);
    }

    [Fact]
    public void Struct_RootAndCoverage()
    {
        var file = Parse("@coverage(partial) @root struct Foo { x: u8 }");
        var s = Assert.Single(file.Structs);
        Assert.True(s.IsRoot);
        Assert.Equal(CoverageMode.Partial, s.Coverage);
    }

    [Fact]
    public void Struct_TrailingComma()
    {
        var file = Parse("struct T { x: u8, y: u16, }");
        Assert.Equal(2, Assert.Single(file.Structs).Members.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Field parsing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Field_Simple()
    {
        var file = Parse("struct T { name: u32 }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal("name", f.Name);
        var pt = Assert.IsType<PrimitiveTypeRef>(f.Type);
        Assert.Equal(TokenType.U32, pt.PrimitiveToken);
        Assert.Null(f.MagicValue);
        Assert.Null(f.Array);
    }

    [Fact]
    public void Field_MagicValue()
    {
        var file = Parse("struct T { sig: u32 = 0x5A4D }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal("sig", f.Name);
        var lit = Assert.IsType<IntLiteralExpr>(f.MagicValue);
        Assert.Equal(0x5A4DL, lit.Value);
    }

    [Fact]
    public void Field_NamedType()
    {
        var file = Parse("struct T { coff: CoffHeader }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var nt = Assert.IsType<NamedTypeRef>(f.Type);
        Assert.Equal("CoffHeader", nt.Name);
        Assert.Empty(nt.Arguments);
    }

    [Fact]
    public void Field_ParameterisedType()
    {
        var file = Parse("struct T { opt: OptionalHeader(arch) }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var nt = Assert.IsType<NamedTypeRef>(f.Type);
        Assert.Equal("OptionalHeader", nt.Name);
        Assert.Single(nt.Arguments);
        Assert.IsType<IdentifierExpr>(nt.Arguments[0]);
    }

    [Fact]
    public void Field_CString()
    {
        var file = Parse("struct T { name: cstring }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        Assert.IsType<CStringTypeRef>(f.Type);
    }

    [Fact]
    public void Field_StringWithLength()
    {
        var file = Parse("struct T { val: string[length] }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var st = Assert.IsType<StringTypeRef>(f.Type);
        Assert.IsType<IdentifierExpr>(st.Length);
    }

    [Fact]
    public void Field_FixedString()
    {
        var file = Parse("struct T { tag: fixed_string[4] }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var ft = Assert.IsType<FixedStringTypeRef>(f.Type);
        Assert.Equal(4L, Assert.IsType<IntLiteralExpr>(ft.Length).Value);
    }

    [Fact]
    public void Field_BytesExpr()
    {
        var file = Parse("struct T { data: bytes[size] }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var bt = Assert.IsType<BytesTypeRef>(f.Type);
        Assert.IsType<IdentifierExpr>(bt.Length);
    }

    [Fact]
    public void Field_BytesRemaining()
    {
        var file = Parse("struct T { rest: bytes[@remaining] }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var bt = Assert.IsType<BytesTypeRef>(f.Type);
        var fn = Assert.IsType<FunctionCallExpr>(bt.Length);
        Assert.Equal("remaining", fn.FunctionName);
        Assert.Empty(fn.Args);
    }

    [Fact]
    public void Field_EndiannessPrimitive()
    {
        var file = Parse("struct T { x: u32le, y: u16be }");
        var f0 = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal(TokenType.U32Le, Assert.IsType<PrimitiveTypeRef>(f0.Type).PrimitiveToken);
        var f1 = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[1]);
        Assert.Equal(TokenType.U16Be, Assert.IsType<PrimitiveTypeRef>(f1.Type).PrimitiveToken);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Array fields
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Array_CountBased()
    {
        var file = Parse("struct T { sections: Header[count] }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var arr = Assert.IsType<CountArraySpec>(f.Array);
        Assert.IsType<IdentifierExpr>(arr.Count);
    }

    [Fact]
    public void Array_Until()
    {
        var file = Parse("struct T { chunks: Chunk[] @until(@remaining == 0) }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var arr = Assert.IsType<UntilArraySpec>(f.Array);
        var cond = Assert.IsType<BinaryExpr>(arr.Condition);
        Assert.Equal(BinaryOp.Eq, cond.Op);
    }

    [Fact]
    public void Array_UntilSentinel()
    {
        var file = Parse("struct T { entries: Entry[] @until_sentinel(e => e.type == 0) }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var arr = Assert.IsType<SentinelArraySpec>(f.Array);
        Assert.Equal("e", arr.ParamName);
        var pred = Assert.IsType<BinaryExpr>(arr.Predicate);
        Assert.Equal(BinaryOp.Eq, pred.Op);
    }

    [Fact]
    public void Array_Greedy()
    {
        var file = Parse("struct T { blocks: Block[] @greedy }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        Assert.IsType<GreedyArraySpec>(f.Array);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Directives inside structs
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Directive_Seek()
    {
        var file = Parse("struct T { @seek(dos.e_lfanew) x: u32 }");
        var members = Assert.Single(file.Structs).Members;
        Assert.Equal(2, members.Count);
        var seek = Assert.IsType<SeekDirective>(members[0]);
        Assert.IsType<FieldAccessExpr>(seek.Offset);
    }

    [Fact]
    public void Directive_AtBlock()
    {
        var file = Parse("""
            struct T {
                @at(@input_size - 22) {
                    eocd: EndOfCentralDir,
                }
            }
        """);
        var at = Assert.IsType<AtBlock>(Assert.Single(file.Structs).Members[0]);
        Assert.IsType<BinaryExpr>(at.Offset);
        Assert.Single(at.Members);
        var inner = Assert.IsType<FieldDecl>(at.Members[0]);
        Assert.Equal("eocd", inner.Name);
    }

    [Fact]
    public void Directive_Let()
    {
        var file = Parse("struct T { @let arch = coff.machine }");
        var let_ = Assert.IsType<LetBinding>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal("arch", let_.Name);
        Assert.IsType<FieldAccessExpr>(let_.Value);
    }

    [Fact]
    public void Directive_Align()
    {
        var file = Parse("struct T { @align(4) }");
        var al = Assert.IsType<AlignDirective>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal(4L, Assert.IsType<IntLiteralExpr>(al.Alignment).Value);
    }

    [Fact]
    public void Directive_Skip()
    {
        var file = Parse("struct T { @skip(8) }");
        var sk = Assert.IsType<SkipDirective>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal(8, sk.ByteCount);
    }

    [Fact]
    public void Directive_Assert()
    {
        var file = Parse("""struct T { @assert(x > 0, "x must be positive") }""");
        var a = Assert.IsType<AssertDirective>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal("x must be positive", a.Message);
        var cond = Assert.IsType<BinaryExpr>(a.Condition);
        Assert.Equal(BinaryOp.Gt, cond.Op);
    }

    [Fact]
    public void Directive_DerivedField()
    {
        var file = Parse("struct T { @derived length: u32 = @sizeof(data) }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal("length", f.Name);
        Assert.True(f.Modifiers.IsDerived);
        var fn = Assert.IsType<FunctionCallExpr>(f.Modifiers.DerivedExpression);
        Assert.Equal("sizeof", fn.FunctionName);
        Assert.Null(f.MagicValue);
    }

    [Fact]
    public void Directive_DerivedCrc32()
    {
        var file = Parse("struct T { @derived crc: u32 = @crc32(chunk_type, data) }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var fn = Assert.IsType<FunctionCallExpr>(f.Modifiers.DerivedExpression);
        Assert.Equal("crc32", fn.FunctionName);
        Assert.Equal(2, fn.Args.Count);
    }

    [Fact]
    public void Modifier_Hidden_Trailing()
    {
        var file = Parse("struct T { reserved: bytes[8] @hidden }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal("reserved", f.Name);
        Assert.True(f.Modifiers.IsHidden);
    }

    [Fact]
    public void Modifier_Hidden_Prefix()
    {
        var file = Parse("struct T { @hidden reserved: bytes[8] }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        Assert.True(f.Modifiers.IsHidden);
    }

    [Fact]
    public void Modifier_Encoding()
    {
        var file = Parse("struct T { value: string[length] @encoding(utf16le) }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal("utf16le", f.Modifiers.Encoding);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Expression parsing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Expr_IntLiteral_Decimal()
    {
        var e = ParseExpr("42");
        Assert.Equal(42L, Assert.IsType<IntLiteralExpr>(e).Value);
    }

    [Fact]
    public void Expr_IntLiteral_Hex()
    {
        var e = ParseExpr("0xFF");
        Assert.Equal(255L, Assert.IsType<IntLiteralExpr>(e).Value);
    }

    [Fact]
    public void Expr_IntLiteral_Binary()
    {
        var e = ParseExpr("0b1010");
        Assert.Equal(10L, Assert.IsType<IntLiteralExpr>(e).Value);
    }

    [Fact]
    public void Expr_IntLiteral_Octal()
    {
        var e = ParseExpr("0o77");
        Assert.Equal(63L, Assert.IsType<IntLiteralExpr>(e).Value);
    }

    [Fact]
    public void Expr_StringLiteral()
    {
        var e = ParseExpr("\"hello\"");
        Assert.Equal("hello", Assert.IsType<StringLiteralExpr>(e).Value);
    }

    [Fact]
    public void Expr_BoolLiteral_True()
    {
        var e = ParseExpr("true");
        Assert.True(Assert.IsType<BoolLiteralExpr>(e).Value);
    }

    [Fact]
    public void Expr_BoolLiteral_False()
    {
        var e = ParseExpr("false");
        Assert.False(Assert.IsType<BoolLiteralExpr>(e).Value);
    }

    [Fact]
    public void Expr_Identifier()
    {
        var e = ParseExpr("foo");
        Assert.Equal("foo", Assert.IsType<IdentifierExpr>(e).Name);
    }

    [Fact]
    public void Expr_DotAccess()
    {
        var e = ParseExpr("a.b");
        var fa = Assert.IsType<FieldAccessExpr>(e);
        Assert.Equal("b", fa.FieldName);
        Assert.Equal("a", Assert.IsType<IdentifierExpr>(fa.Object).Name);
    }

    [Fact]
    public void Expr_DotChain()
    {
        var e = ParseExpr("a.b.c");
        var outer = Assert.IsType<FieldAccessExpr>(e);
        Assert.Equal("c", outer.FieldName);
        var inner = Assert.IsType<FieldAccessExpr>(outer.Object);
        Assert.Equal("b", inner.FieldName);
        Assert.Equal("a", Assert.IsType<IdentifierExpr>(inner.Object).Name);
    }

    [Fact]
    public void Expr_IndexAccess()
    {
        var e = ParseExpr("arr[0]");
        var ia = Assert.IsType<IndexAccessExpr>(e);
        Assert.Equal("arr", Assert.IsType<IdentifierExpr>(ia.Object).Name);
        Assert.Equal(0L, Assert.IsType<IntLiteralExpr>(ia.Index).Value);
    }

    [Fact]
    public void Expr_BinaryAdd()
    {
        var e = ParseExpr("a + b");
        var bin = Assert.IsType<BinaryExpr>(e);
        Assert.Equal(BinaryOp.Add, bin.Op);
    }

    [Fact]
    public void Expr_BinaryPrecedence_MulOverAdd()
    {
        // a + b * c  →  a + (b * c)
        var e = ParseExpr("a + b * c");
        var add = Assert.IsType<BinaryExpr>(e);
        Assert.Equal(BinaryOp.Add, add.Op);
        Assert.IsType<IdentifierExpr>(add.Left);
        var mul = Assert.IsType<BinaryExpr>(add.Right);
        Assert.Equal(BinaryOp.Mul, mul.Op);
    }

    [Fact]
    public void Expr_LeftAssociativity()
    {
        // a - b - c  →  (a - b) - c
        var e = ParseExpr("a - b - c");
        var outer = Assert.IsType<BinaryExpr>(e);
        Assert.Equal(BinaryOp.Sub, outer.Op);
        Assert.IsType<IdentifierExpr>(outer.Right); // c
        var inner = Assert.IsType<BinaryExpr>(outer.Left);
        Assert.Equal(BinaryOp.Sub, inner.Op);
    }

    [Fact]
    public void Expr_Parenthesised()
    {
        // (a + b) * c
        var e = ParseExpr("(a + b) * c");
        var mul = Assert.IsType<BinaryExpr>(e);
        Assert.Equal(BinaryOp.Mul, mul.Op);
        var add = Assert.IsType<BinaryExpr>(mul.Left);
        Assert.Equal(BinaryOp.Add, add.Op);
    }

    [Theory]
    [InlineData("a == b", BinaryOp.Eq)]
    [InlineData("a != b", BinaryOp.Ne)]
    [InlineData("a < b", BinaryOp.Lt)]
    [InlineData("a > b", BinaryOp.Gt)]
    [InlineData("a <= b", BinaryOp.Le)]
    [InlineData("a >= b", BinaryOp.Ge)]
    [InlineData("a & b", BinaryOp.BitAnd)]
    [InlineData("a | b", BinaryOp.BitOr)]
    [InlineData("a ^ b", BinaryOp.BitXor)]
    [InlineData("a << b", BinaryOp.Shl)]
    [InlineData("a >> b", BinaryOp.Shr)]
    [InlineData("a && b", BinaryOp.LogicAnd)]
    [InlineData("a || b", BinaryOp.LogicOr)]
    [InlineData("a / b", BinaryOp.Div)]
    [InlineData("a % b", BinaryOp.Mod)]
    public void Expr_BinaryOperators(string expr, BinaryOp expectedOp)
    {
        var bin = Assert.IsType<BinaryExpr>(ParseExpr(expr));
        Assert.Equal(expectedOp, bin.Op);
    }

    [Fact]
    public void Expr_UnaryNegate()
    {
        var e = ParseExpr("-42");
        var u = Assert.IsType<UnaryExpr>(e);
        Assert.Equal(UnaryOp.Negate, u.Op);
        Assert.Equal(42L, Assert.IsType<IntLiteralExpr>(u.Operand).Value);
    }

    [Fact]
    public void Expr_UnaryLogicNot()
    {
        var e = ParseExpr("!flag");
        var u = Assert.IsType<UnaryExpr>(e);
        Assert.Equal(UnaryOp.LogicNot, u.Op);
    }

    [Fact]
    public void Expr_UnaryBitNot()
    {
        var e = ParseExpr("~mask");
        var u = Assert.IsType<UnaryExpr>(e);
        Assert.Equal(UnaryOp.BitNot, u.Op);
    }

    [Fact]
    public void Expr_BuiltinFunctionCall_SizeOf()
    {
        var e = ParseExpr("@sizeof(data)");
        var fn = Assert.IsType<FunctionCallExpr>(e);
        Assert.Equal("sizeof", fn.FunctionName);
        Assert.Single(fn.Args);
    }

    [Fact]
    public void Expr_BuiltinFunctionCall_Crc32()
    {
        var e = ParseExpr("@crc32(a, b)");
        var fn = Assert.IsType<FunctionCallExpr>(e);
        Assert.Equal("crc32", fn.FunctionName);
        Assert.Equal(2, fn.Args.Count);
    }

    [Fact]
    public void Expr_Remaining()
    {
        var e = ParseExpr("@remaining");
        var fn = Assert.IsType<FunctionCallExpr>(e);
        Assert.Equal("remaining", fn.FunctionName);
        Assert.Empty(fn.Args);
    }

    [Fact]
    public void Expr_InputSize()
    {
        var e = ParseExpr("@input_size");
        var fn = Assert.IsType<FunctionCallExpr>(e);
        Assert.Equal("input_size", fn.FunctionName);
        Assert.Empty(fn.Args);
    }

    [Fact]
    public void Expr_Offset()
    {
        var e = ParseExpr("@offset");
        var fn = Assert.IsType<FunctionCallExpr>(e);
        Assert.Equal("offset", fn.FunctionName);
    }

    [Fact]
    public void Expr_MethodCall()
    {
        var e = ParseExpr("s.starts_with(\"text\")");
        var mc = Assert.IsType<MethodCallExpr>(e);
        Assert.Equal("starts_with", mc.MethodName);
        Assert.Equal("s", Assert.IsType<IdentifierExpr>(mc.Object).Name);
        Assert.Single(mc.Args);
        Assert.Equal("text", Assert.IsType<StringLiteralExpr>(mc.Args[0]).Value);
    }

    [Fact]
    public void Expr_Complex_InputSizeMinusInt()
    {
        var e = ParseExpr("@input_size - 22");
        var bin = Assert.IsType<BinaryExpr>(e);
        Assert.Equal(BinaryOp.Sub, bin.Op);
        Assert.IsType<FunctionCallExpr>(bin.Left);
        Assert.Equal(22L, Assert.IsType<IntLiteralExpr>(bin.Right).Value);
    }

    [Fact]
    public void Expr_Complex_DotAccessPlusFour()
    {
        var e = ParseExpr("dos.e_lfanew + 4");
        var bin = Assert.IsType<BinaryExpr>(e);
        Assert.Equal(BinaryOp.Add, bin.Op);
        Assert.IsType<FieldAccessExpr>(bin.Left);
        Assert.Equal(4L, Assert.IsType<IntLiteralExpr>(bin.Right).Value);
    }

    [Fact]
    public void Expr_Complex_IndexedDotAccess()
    {
        var e = ParseExpr("central_dir[idx].local_header_offset");
        var fa = Assert.IsType<FieldAccessExpr>(e);
        Assert.Equal("local_header_offset", fa.FieldName);
        var ia = Assert.IsType<IndexAccessExpr>(fa.Object);
        Assert.Equal("central_dir", Assert.IsType<IdentifierExpr>(ia.Object).Name);
    }

    [Fact]
    public void Expr_RemainingEqualsZero()
    {
        var e = ParseExpr("@remaining == 0");
        var bin = Assert.IsType<BinaryExpr>(e);
        Assert.Equal(BinaryOp.Eq, bin.Op);
        Assert.IsType<FunctionCallExpr>(bin.Left);
        Assert.Equal(0L, Assert.IsType<IntLiteralExpr>(bin.Right).Value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Match expressions (in type position)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Match_SimpleValueArms()
    {
        var file = Parse("""
            struct T {
                body: match(chunk_type) {
                    0x01 => TypeA,
                    0x02 => TypeB,
                }
            }
        """);
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var mt = Assert.IsType<MatchTypeRef>(f.Type);
        Assert.Equal(2, mt.Match.Arms.Count);
        Assert.IsType<ValuePattern>(mt.Match.Arms[0].Pattern);
        Assert.IsType<ValuePattern>(mt.Match.Arms[1].Pattern);
    }

    [Fact]
    public void Match_DotAccessAsValuePattern()
    {
        var file = Parse("""
            struct T {
                body: match(ct) {
                    PngChunkType.IHDR => IhdrBody,
                    PngChunkType.PLTE => PlteBody,
                }
            }
        """);
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        var mt = Assert.IsType<MatchTypeRef>(f.Type);
        var pat = Assert.IsType<ValuePattern>(mt.Match.Arms[0].Pattern);
        Assert.IsType<FieldAccessExpr>(pat.Value);
    }

    [Fact]
    public void Match_RangeArm()
    {
        var file = Parse("""
            struct T {
                body: match(tag) {
                    0x00..0x1F => SmallTag,
                    _ => bytes[len],
                }
            }
        """);
        var mt = Assert.IsType<MatchTypeRef>(
            Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]).Type);

        var range = Assert.IsType<RangePattern>(mt.Match.Arms[0].Pattern);
        Assert.Equal(0x00L, Assert.IsType<IntLiteralExpr>(range.Low).Value);
        Assert.Equal(0x1FL, Assert.IsType<IntLiteralExpr>(range.High).Value);
    }

    [Fact]
    public void Match_WildcardArm()
    {
        var file = Parse("""
            struct T {
                body: match(t) {
                    _ => bytes[length],
                }
            }
        """);
        var mt = Assert.IsType<MatchTypeRef>(
            Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]).Type);
        Assert.IsType<WildcardPattern>(mt.Match.Arms[0].Pattern);
        Assert.IsType<BytesTypeRef>(mt.Match.Arms[0].Type);
    }

    [Fact]
    public void Match_GuardOnValuePattern()
    {
        var file = Parse("""
            struct T {
                body: match(ct) {
                    "text" when arch == 0x8664 => TextBody,
                }
            }
        """);
        var mt = Assert.IsType<MatchTypeRef>(
            Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]).Type);
        var arm = mt.Match.Arms[0];
        Assert.IsType<ValuePattern>(arm.Pattern);
        Assert.NotNull(arm.Guard);
        Assert.IsType<BinaryExpr>(arm.Guard);
    }

    [Fact]
    public void Match_IdentifierPatternWithGuard()
    {
        var file = Parse("""
            struct T {
                body: match(name) {
                    s when s.starts_with(".debug") => DebugSection,
                    _ => bytes[size],
                }
            }
        """);
        var mt = Assert.IsType<MatchTypeRef>(
            Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]).Type);

        var arm0 = mt.Match.Arms[0];
        var idPat = Assert.IsType<IdentifierPattern>(arm0.Pattern);
        Assert.Equal("s", idPat.Name);
        var guard = Assert.IsType<MethodCallExpr>(arm0.Guard);
        Assert.Equal("starts_with", guard.MethodName);

        Assert.IsType<WildcardPattern>(mt.Match.Arms[1].Pattern);
    }

    [Fact]
    public void Match_MixedPatternTypes()
    {
        var file = Parse("""
            struct T {
                body: match(tag) {
                    0x01 => TypeA,
                    0x10..0x1F => RangeType,
                    s when s > 0xFF => BigType,
                    _ => bytes[1],
                }
            }
        """);
        var mt = Assert.IsType<MatchTypeRef>(
            Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]).Type);
        Assert.Equal(4, mt.Match.Arms.Count);
        Assert.IsType<ValuePattern>(mt.Match.Arms[0].Pattern);
        Assert.IsType<RangePattern>(mt.Match.Arms[1].Pattern);
        Assert.IsType<IdentifierPattern>(mt.Match.Arms[2].Pattern);
        Assert.IsType<WildcardPattern>(mt.Match.Arms[3].Pattern);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Enum parsing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Enum_IntegerBaseType()
    {
        var file = Parse("""
            enum Machine : u16 {
                I386  = 0x014C,
                AMD64 = 0x8664,
                ARM64 = 0xAA64,
            }
        """);
        var e = Assert.Single(file.Enums);
        Assert.Equal("Machine", e.Name);
        Assert.Equal(TokenType.U16, Assert.IsType<PrimitiveTypeRef>(e.BaseType).PrimitiveToken);
        Assert.Equal(3, e.Variants.Count);
        Assert.Equal("I386", e.Variants[0].Name);
        Assert.Equal(0x014CL, Assert.IsType<IntLiteralExpr>(e.Variants[0].Value).Value);
    }

    [Fact]
    public void Enum_FixedStringBaseType()
    {
        var file = Parse("""
            enum PngChunkType : fixed_string[4] {
                IHDR = "IHDR",
                PLTE = "PLTE",
            }
        """);
        var e = Assert.Single(file.Enums);
        Assert.IsType<FixedStringTypeRef>(e.BaseType);
        Assert.Equal(2, e.Variants.Count);
        Assert.Equal("IHDR", Assert.IsType<StringLiteralExpr>(e.Variants[0].Value).Value);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Bits struct parsing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BitsStruct_BitAndBitsFields()
    {
        var file = Parse("""
            bits struct Flags : u16 {
                flag_a: bit,
                flag_b: bit,
                _reserved: bits[3],
                flag_c: bit,
            }
        """);
        var bs = Assert.Single(file.BitsStructs);
        Assert.Equal("Flags", bs.Name);
        Assert.Equal(TokenType.U16, Assert.IsType<PrimitiveTypeRef>(bs.BaseType).PrimitiveToken);
        Assert.Equal(4, bs.Fields.Count);

        Assert.Equal("flag_a", bs.Fields[0].Name);
        Assert.IsType<SingleBit>(bs.Fields[0].Type);

        Assert.Equal("_reserved", bs.Fields[2].Name);
        var mb = Assert.IsType<MultiBits>(bs.Fields[2].Type);
        Assert.Equal(3, mb.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Error recovery
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ErrorRecovery_SkipsToNextDeclaration()
    {
        var (file, diags) = ParseWithDiagnostics("""
            !!!invalid!!!
            struct Valid { x: u8 }
        """);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Error);
        // The valid struct should still be parsed
        Assert.Single(file.Structs);
        Assert.Equal("Valid", file.Structs[0].Name);
    }

    [Fact]
    public void ErrorRecovery_MalformedExpression()
    {
        var (file, diags) = ParseWithDiagnostics("""
            struct Bad { x: u32 = }
            struct Good { y: u16 }
        """);
        Assert.Contains(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(file.Structs, s => s.Name == "Good");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Integration test — full PE header example
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Integration_PeQuickHeader()
    {
        var file = Parse("""
            @default_endian(little)
            @coverage(partial)

            @root struct PeQuickHeader {
                dos: DosHeader,
                @seek(dos.e_lfanew)
                pe_sig: u32 = 0x00004550,
                coff: CoffHeader,
            }

            struct DosHeader {
                e_magic: u16 = 0x5A4D,
                e_cblp: u16,
                e_cp: u16,
                e_lfanew: u32,
            }

            struct CoffHeader {
                machine: Machine,
                number_of_sections: u16,
                time_date_stamp: u32,
                pointer_to_symbol_table: u32,
                number_of_symbols: u32,
                size_of_optional_header: u16,
                characteristics: Characteristics,
            }

            enum Machine : u16 {
                I386  = 0x014C,
                AMD64 = 0x8664,
                ARM64 = 0xAA64,
            }

            bits struct Characteristics : u16 {
                relocs_stripped: bit,
                executable_image: bit,
                line_nums_stripped: bit,
                local_syms_stripped: bit,
                aggressive_ws_trim: bit,
                large_address_aware: bit,
                _reserved: bit,
                bytes_reversed_lo: bit,
                _32bit_machine: bit,
                debug_stripped: bit,
                removable_run_from_swap: bit,
                net_run_from_swap: bit,
                system: bit,
                dll: bit,
                up_system_only: bit,
                bytes_reversed_hi: bit,
            }
        """);

        // ── file-level directives ──
        Assert.Equal(Endianness.Little, file.DefaultEndian);

        // ── 3 structs ──
        Assert.Equal(3, file.Structs.Count);

        // PeQuickHeader
        var pe = file.Structs[0];
        Assert.Equal("PeQuickHeader", pe.Name);
        Assert.True(pe.IsRoot);
        Assert.Equal(CoverageMode.Partial, pe.Coverage);
        Assert.Equal(4, pe.Members.Count); // dos, @seek, pe_sig, coff

        var dos = Assert.IsType<FieldDecl>(pe.Members[0]);
        Assert.Equal("dos", dos.Name);
        Assert.IsType<NamedTypeRef>(dos.Type);

        Assert.IsType<SeekDirective>(pe.Members[1]);

        var peSig = Assert.IsType<FieldDecl>(pe.Members[2]);
        Assert.Equal("pe_sig", peSig.Name);
        Assert.Equal(0x00004550L, Assert.IsType<IntLiteralExpr>(peSig.MagicValue).Value);

        // DosHeader
        var dosH = file.Structs[1];
        Assert.Equal("DosHeader", dosH.Name);
        Assert.Equal(4, dosH.Members.Count);
        var magic = Assert.IsType<FieldDecl>(dosH.Members[0]);
        Assert.Equal(0x5A4DL, Assert.IsType<IntLiteralExpr>(magic.MagicValue).Value);

        // CoffHeader
        var coffH = file.Structs[2];
        Assert.Equal("CoffHeader", coffH.Name);
        Assert.Equal(7, coffH.Members.Count);

        // ── 1 enum ──
        var machine = Assert.Single(file.Enums);
        Assert.Equal("Machine", machine.Name);
        Assert.Equal(3, machine.Variants.Count);

        // ── 1 bits struct ──
        var chars = Assert.Single(file.BitsStructs);
        Assert.Equal("Characteristics", chars.Name);
        Assert.Equal(16, chars.Fields.Count);
        Assert.IsType<SingleBit>(chars.Fields[0].Type);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Regression / edge-case tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Empty_Source_ProducesEmptyFile()
    {
        var file = Parse("");
        Assert.Empty(file.Structs);
        Assert.Empty(file.Enums);
        Assert.Empty(file.BitsStructs);
        Assert.Empty(file.Constants);
    }

    [Fact]
    public void Struct_Empty_Body()
    {
        var file = Parse("struct Empty { }");
        Assert.Empty(Assert.Single(file.Structs).Members);
    }

    [Fact]
    public void Field_BoolPrimitive()
    {
        var file = Parse("struct T { active: bool }");
        var f = Assert.IsType<FieldDecl>(Assert.Single(file.Structs).Members[0]);
        Assert.Equal(TokenType.Bool, Assert.IsType<PrimitiveTypeRef>(f.Type).PrimitiveToken);
    }

    [Fact]
    public void Expr_Precedence_LogicOrLowest()
    {
        // a || b && c  →  a || (b && c)
        var e = ParseExpr("a || b && c");
        var or = Assert.IsType<BinaryExpr>(e);
        Assert.Equal(BinaryOp.LogicOr, or.Op);
        Assert.IsType<BinaryExpr>(or.Right); // (b && c)
    }

    [Fact]
    public void Expr_Precedence_BitOpsBetweenLogicAndComparison()
    {
        // a == b & c  →  a == (b & c)
        var e = ParseExpr("a == b & c");
        // BitAnd (5) > Eq (6)? No! Eq is 6, BitAnd is 5.
        // Lower precedence number means lower precedence (binds less tightly).
        // So BitAnd(5) binds LESS tightly than Eq(6).
        // a == b & c  →  (a == b) & c
        var bitAnd = Assert.IsType<BinaryExpr>(e);
        Assert.Equal(BinaryOp.BitAnd, bitAnd.Op);
        var eq = Assert.IsType<BinaryExpr>(bitAnd.Left);
        Assert.Equal(BinaryOp.Eq, eq.Op);
    }

    [Fact]
    public void MultipleDeclarationTypes()
    {
        var file = Parse("""
            @import "a"
            @param ver: u32
            const MAGIC = 42;
            struct Foo { x: u8 }
            enum Bar : u8 { A = 1 }
            bits struct Baz : u8 { f: bit }
        """);
        Assert.Single(file.Imports);
        Assert.Single(file.Params);
        Assert.Single(file.Constants);
        Assert.Single(file.Structs);
        Assert.Single(file.Enums);
        Assert.Single(file.BitsStructs);
    }
}
