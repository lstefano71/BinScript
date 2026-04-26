using BinScript.NALight;

namespace BinScript.Tests.NALight;

public class NAParserTests
{
    // ── Basic structure ─────────────────────────────────────────────────────

    [Fact]
    public void Empty_NoLibrary_NoArgs()
    {
        var spec = Parse("");
        Assert.Null(spec.ReturnType);
        Assert.Null(spec.LibraryPath);
        Assert.Null(spec.FunctionName);
        Assert.Empty(spec.Arguments);
    }

    [Fact]
    public void SimpleFunction_ReturnAndOneArg()
    {
        var spec = Parse("I4 user32|MessageBoxW P");
        Assert.IsType<NAPrimitiveDesc>(spec.ReturnType);
        var ret = (NAPrimitiveDesc)spec.ReturnType!;
        Assert.Equal('I', ret.TypeLetter);
        Assert.Equal(4, ret.Width);
        Assert.Equal("user32", spec.LibraryPath);
        Assert.Equal("MessageBoxW", spec.FunctionName);
        Assert.False(spec.PassByPointer);
        Assert.False(spec.ThreadSafe);
        Assert.Single(spec.Arguments);
        Assert.IsType<NAPointerDesc>(spec.Arguments[0].Type);
    }

    [Fact]
    public void FunctionWithStar_PassByPointer()
    {
        var spec = Parse("I4 shell32|SHFileOp* P");
        Assert.True(spec.PassByPointer);
        Assert.False(spec.ThreadSafe);
    }

    [Fact]
    public void FunctionWithAmpersand_ThreadSafe()
    {
        var spec = Parse("I4 lib|fn& I4");
        Assert.False(spec.PassByPointer);
        Assert.True(spec.ThreadSafe);
    }

    [Fact]
    public void FunctionWithStarAndAmpersand()
    {
        var spec = Parse("I4 lib|fn*& I4");
        Assert.True(spec.PassByPointer);
        Assert.True(spec.ThreadSafe);
    }

    [Fact]
    public void NoReturnType()
    {
        var spec = Parse("user32|MessageBoxW P");
        Assert.Null(spec.ReturnType);
        Assert.Equal("user32", spec.LibraryPath);
        Assert.Equal("MessageBoxW", spec.FunctionName);
        Assert.Single(spec.Arguments);
    }

    // ── Primitive types ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("I1", 'I', 1)]
    [InlineData("I2", 'I', 2)]
    [InlineData("I4", 'I', 4)]
    [InlineData("I8", 'I', 8)]
    [InlineData("U1", 'U', 1)]
    [InlineData("U2", 'U', 2)]
    [InlineData("U4", 'U', 4)]
    [InlineData("U8", 'U', 8)]
    [InlineData("C1", 'C', 1)]
    [InlineData("C2", 'C', 2)]
    [InlineData("C4", 'C', 4)]
    [InlineData("T1", 'T', 1)]
    [InlineData("T2", 'T', 2)]
    [InlineData("T4", 'T', 4)]
    [InlineData("F4", 'F', 4)]
    [InlineData("F8", 'F', 8)]
    public void PrimitiveTypes(string input, char expectedLetter, int expectedWidth)
    {
        var spec = Parse($"lib|fn {input}");
        Assert.Single(spec.Arguments);
        var prim = Assert.IsType<NAPrimitiveDesc>(spec.Arguments[0].Type);
        Assert.Equal(expectedLetter, prim.TypeLetter);
        Assert.Equal(expectedWidth, prim.Width);
    }

    [Theory]
    [InlineData("I", 'I', 4)]   // default width
    [InlineData("U", 'U', 4)]
    [InlineData("C", 'C', 1)]
    [InlineData("T", 'T', 2)]
    [InlineData("F", 'F', 8)]
    public void PrimitiveTypes_DefaultWidth(string input, char expectedLetter, int expectedWidth)
    {
        var spec = Parse($"lib|fn {input}");
        var prim = Assert.IsType<NAPrimitiveDesc>(spec.Arguments[0].Type);
        Assert.Equal(expectedLetter, prim.TypeLetter);
        Assert.Equal(expectedWidth, prim.Width);
    }

    [Fact]
    public void SpecialTypes_Pointer()
    {
        var spec = Parse("lib|fn P");
        Assert.IsType<NAPointerDesc>(spec.Arguments[0].Type);
    }

    [Fact]
    public void SpecialTypes_Decimal()
    {
        var spec = Parse("lib|fn D");
        Assert.IsType<NADecimalDesc>(spec.Arguments[0].Type);
    }

    [Fact]
    public void SpecialTypes_Complex()
    {
        var spec = Parse("lib|fn J");
        Assert.IsType<NAComplexDesc>(spec.Arguments[0].Type);
    }

    [Fact]
    public void UnsupportedType_A_Throws()
    {
        Assert.Throws<NAParseException>(() => Parse("lib|fn A"));
    }

    [Fact]
    public void UnsupportedType_Z_Throws()
    {
        Assert.Throws<NAParseException>(() => Parse("lib|fn Z"));
    }

    // ── Direction markers ───────────────────────────────────────────────────

    [Fact]
    public void Direction_In()
    {
        var spec = Parse("lib|fn <I4");
        Assert.Equal(NADirection.In, spec.Arguments[0].Direction);
    }

    [Fact]
    public void Direction_Out()
    {
        var spec = Parse("lib|fn >I4");
        Assert.Equal(NADirection.Out, spec.Arguments[0].Direction);
    }

    [Fact]
    public void Direction_InOut()
    {
        var spec = Parse("lib|fn =I4");
        Assert.Equal(NADirection.InOut, spec.Arguments[0].Direction);
    }

    [Fact]
    public void Direction_None()
    {
        var spec = Parse("lib|fn I4");
        Assert.Equal(NADirection.None, spec.Arguments[0].Direction);
    }

    // ── Null-terminated types ───────────────────────────────────────────────

    [Fact]
    public void NullTerm_0T()
    {
        var spec = Parse("lib|fn 0T");
        var nt = Assert.IsType<NANullTermDesc>(spec.Arguments[0].Type);
        Assert.Equal('T', nt.TypeLetter);
        Assert.Equal(2, nt.Width); // default T width
    }

    [Fact]
    public void NullTerm_0T1()
    {
        var spec = Parse("lib|fn 0T1");
        var nt = Assert.IsType<NANullTermDesc>(spec.Arguments[0].Type);
        Assert.Equal('T', nt.TypeLetter);
        Assert.Equal(1, nt.Width);
    }

    [Fact]
    public void NullTerm_0C()
    {
        var spec = Parse("lib|fn 0C");
        var nt = Assert.IsType<NANullTermDesc>(spec.Arguments[0].Type);
        Assert.Equal('C', nt.TypeLetter);
        Assert.Equal(1, nt.Width); // default C width
    }

    // ── Double-null types ───────────────────────────────────────────────────

    [Fact]
    public void DoubleNull_00T()
    {
        var spec = Parse("lib|fn 00T");
        var dn = Assert.IsType<NADoubleNullDesc>(spec.Arguments[0].Type);
        Assert.Equal('T', dn.TypeLetter);
        Assert.Equal(2, dn.Width);
    }

    [Fact]
    public void DoubleNull_00T1()
    {
        var spec = Parse("lib|fn 00T1");
        var dn = Assert.IsType<NADoubleNullDesc>(spec.Arguments[0].Type);
        Assert.Equal(1, dn.Width);
    }

    // ── Structs ─────────────────────────────────────────────────────────────

    [Fact]
    public void Struct_Simple()
    {
        var spec = Parse("lib|fn {I4 U2}");
        var s = Assert.IsType<NAStructDesc>(spec.Arguments[0].Type);
        Assert.Equal(2, s.Fields.Count);
        Assert.IsType<NAPrimitiveDesc>(s.Fields[0].Type);
        Assert.IsType<NAPrimitiveDesc>(s.Fields[1].Type);
    }

    [Fact]
    public void Struct_WithPointer()
    {
        var spec = Parse("lib|fn {P U4 P}");
        var s = Assert.IsType<NAStructDesc>(spec.Arguments[0].Type);
        Assert.Equal(3, s.Fields.Count);
        Assert.IsType<NAPointerDesc>(s.Fields[0].Type);
        Assert.IsType<NAPrimitiveDesc>(s.Fields[1].Type);
        Assert.IsType<NAPointerDesc>(s.Fields[2].Type);
    }

    [Fact]
    public void Struct_Nested()
    {
        var spec = Parse("lib|fn {I4 {U2 U2} I4}");
        var outer = Assert.IsType<NAStructDesc>(spec.Arguments[0].Type);
        Assert.Equal(3, outer.Fields.Count);
        var inner = Assert.IsType<NAStructDesc>(outer.Fields[1].Type);
        Assert.Equal(2, inner.Fields.Count);
    }

    [Fact]
    public void Struct_DefaultAlignment_Packed()
    {
        var spec = Parse("lib|fn {I4 U2}");
        var s = Assert.IsType<NAStructDesc>(spec.Arguments[0].Type);
        Assert.Equal(NAAlignMode.Packed, s.AlignMode);
        Assert.Null(s.PackSize);
    }

    // ── BSX-Light extensions in structs ─────────────────────────────────────

    [Fact]
    public void Struct_DirectionMarkers()
    {
        var spec = Parse("lib|fn ={P U4 <0T >0T}");
        Assert.Equal(NADirection.InOut, spec.Arguments[0].Direction);
        var s = Assert.IsType<NAStructDesc>(spec.Arguments[0].Type);
        Assert.Equal(4, s.Fields.Count);
        Assert.Equal(NADirection.None, s.Fields[0].Direction);  // P
        Assert.Equal(NADirection.None, s.Fields[1].Direction);  // U4
        Assert.Equal(NADirection.In, s.Fields[2].Direction);    // <0T
        Assert.Equal(NADirection.Out, s.Fields[3].Direction);   // >0T
    }

    [Fact]
    public void Struct_Aligned()
    {
        var spec = Parse("lib|fn @aligned{I4 C1}");
        var s = Assert.IsType<NAStructDesc>(spec.Arguments[0].Type);
        Assert.Equal(NAAlignMode.Natural, s.AlignMode);
    }

    [Fact]
    public void Struct_Pack()
    {
        var spec = Parse("lib|fn @pack(4){I4 C1}");
        var s = Assert.IsType<NAStructDesc>(spec.Arguments[0].Type);
        Assert.Equal(4, s.PackSize);
    }

    // ── Arrays ──────────────────────────────────────────────────────────────

    [Fact]
    public void Array_Fixed()
    {
        var spec = Parse("lib|fn I4[10]");
        var a = Assert.IsType<NAArrayedDesc>(spec.Arguments[0].Type);
        Assert.IsType<NAPrimitiveDesc>(a.Element);
        var kind = Assert.IsType<FixedArrayKind>(a.ArrayKind);
        Assert.Equal(10, kind.Count);
    }

    [Fact]
    public void Array_Variable()
    {
        var spec = Parse("lib|fn I4[]");
        var a = Assert.IsType<NAArrayedDesc>(spec.Arguments[0].Type);
        Assert.IsType<VariableArrayKind>(a.ArrayKind);
    }

    [Fact]
    public void Array_Counted()
    {
        var spec = Parse("lib|fn I4[*]");
        var a = Assert.IsType<NAArrayedDesc>(spec.Arguments[0].Type);
        var kind = Assert.IsType<CountedArrayKind>(a.ArrayKind);
        Assert.Null(kind.MaxCount);
    }

    [Fact]
    public void Array_CountedWithMax()
    {
        var spec = Parse("lib|fn I4[*:256]");
        var a = Assert.IsType<NAArrayedDesc>(spec.Arguments[0].Type);
        var kind = Assert.IsType<CountedArrayKind>(a.ArrayKind);
        Assert.Equal(256, kind.MaxCount);
    }

    // ── Full examples ───────────────────────────────────────────────────────

    [Fact]
    public void FullSpec_SHFileOp()
    {
        var spec = Parse("I4 shell32|SHFileOperationW* ={P U4 <00T <00T U2 I4 P <0T}");
        // Return type
        var ret = Assert.IsType<NAPrimitiveDesc>(spec.ReturnType);
        Assert.Equal('I', ret.TypeLetter);
        Assert.Equal(4, ret.Width);
        // Function
        Assert.Equal("shell32", spec.LibraryPath);
        Assert.Equal("SHFileOperationW", spec.FunctionName);
        Assert.True(spec.PassByPointer);
        // One arg: in/out struct
        Assert.Single(spec.Arguments);
        Assert.Equal(NADirection.InOut, spec.Arguments[0].Direction);
        var s = Assert.IsType<NAStructDesc>(spec.Arguments[0].Type);
        Assert.Equal(8, s.Fields.Count);
    }

    [Fact]
    public void FullSpec_MessageBox()
    {
        var spec = Parse("I4 user32|MessageBoxW P <0T <0T U4");
        Assert.Equal("user32", spec.LibraryPath);
        Assert.Equal("MessageBoxW", spec.FunctionName);
        Assert.Equal(4, spec.Arguments.Count);
        Assert.IsType<NAPointerDesc>(spec.Arguments[0].Type);
        Assert.IsType<NANullTermDesc>(spec.Arguments[1].Type);
        Assert.IsType<NANullTermDesc>(spec.Arguments[2].Type);
        Assert.IsType<NAPrimitiveDesc>(spec.Arguments[3].Type);
    }

    [Fact]
    public void FullSpec_WithComments()
    {
        // Comments are stripped by the lexer, parser shouldn't see them
        var spec = Parse("I4 lib|fn /* hwnd */ P /* wFunc */ U4");
        Assert.Equal(2, spec.Arguments.Count);
    }

    [Fact]
    public void MultipleArgs_MixedTypes()
    {
        var spec = Parse("lib|fn I4 U2 F8 P 0T {I4 U2}");
        Assert.Equal(6, spec.Arguments.Count);
        Assert.IsType<NAPrimitiveDesc>(spec.Arguments[0].Type);   // I4
        Assert.IsType<NAPrimitiveDesc>(spec.Arguments[1].Type);   // U2
        Assert.IsType<NAPrimitiveDesc>(spec.Arguments[2].Type);   // F8
        Assert.IsType<NAPointerDesc>(spec.Arguments[3].Type);     // P
        Assert.IsType<NANullTermDesc>(spec.Arguments[4].Type);    // 0T
        Assert.IsType<NAStructDesc>(spec.Arguments[5].Type);      // {I4 U2}
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    private static NASpec Parse(string input)
    {
        var lexer = new NALexer(input);
        var tokens = lexer.Tokenize();
        var parser = new NAParser(tokens);
        return parser.Parse();
    }
}
