namespace BinScript.Tests.NALight;

using BinScript.NALight;

public class NALexerTests
{
    private static List<NAToken> Lex(string input) => new NALexer(input).Tokenize();

    private static void AssertTokens(string input, params NATokenKind[] expected)
    {
        var tokens = Lex(input);
        // Last token is always End
        var kinds = tokens.Select(t => t.Kind).ToArray();
        Assert.Equal(expected.Append(NATokenKind.End).ToArray(), kinds);
    }

    // ── Basic ⎕NA strings ──

    [Fact]
    public void SimpleFunction_ReturnTypeAndArgs()
    {
        // F8 math|divide I4 I4
        AssertTokens("F8 math|divide I4 I4",
            NATokenKind.TypeF, NATokenKind.Number,          // F8
            NATokenKind.Identifier, NATokenKind.Pipe,       // math|
            NATokenKind.Identifier,                         // divide
            NATokenKind.TypeI, NATokenKind.Number,          // I4
            NATokenKind.TypeI, NATokenKind.Number);         // I4
    }

    [Fact]
    public void VoidFunction_NoReturn()
    {
        // mydll|DoSomething U4
        AssertTokens("mydll|DoSomething U4",
            NATokenKind.Identifier, NATokenKind.Pipe, NATokenKind.Identifier,
            NATokenKind.TypeU, NATokenKind.Number);
    }

    [Fact]
    public void PointerArgument_WithDirection()
    {
        // I4 user32|MessageBoxW P <0T <0T U4
        AssertTokens("I4 user32|MessageBoxW P <0T <0T U4",
            NATokenKind.TypeI, NATokenKind.Number,                          // I4
            NATokenKind.Identifier, NATokenKind.Pipe, NATokenKind.Identifier, // user32|MessageBoxW
            NATokenKind.TypeP,                                              // P
            NATokenKind.DirIn, NATokenKind.NullTerm, NATokenKind.TypeT,    // <0T
            NATokenKind.DirIn, NATokenKind.NullTerm, NATokenKind.TypeT,    // <0T
            NATokenKind.TypeU, NATokenKind.Number);                        // U4
    }

    [Fact]
    public void Struct_Simple()
    {
        // I4 mydll|foo <{I4 F8}
        AssertTokens("I4 mydll|foo <{I4 F8}",
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.Identifier, NATokenKind.Pipe, NATokenKind.Identifier,
            NATokenKind.DirIn, NATokenKind.LBrace,
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.TypeF, NATokenKind.Number,
            NATokenKind.RBrace);
    }

    [Fact]
    public void FixedArray()
    {
        // <I4[10]
        AssertTokens("<I4[10]",
            NATokenKind.DirIn,
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.LBracket, NATokenKind.Number, NATokenKind.RBracket);
    }

    [Fact]
    public void VariableArray()
    {
        // <I4[]
        AssertTokens("<I4[]",
            NATokenKind.DirIn,
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.LBracket, NATokenKind.RBracket);
    }

    [Fact]
    public void OutputDirection()
    {
        // >I4[100]
        AssertTokens(">I4[100]",
            NATokenKind.DirOut,
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.LBracket, NATokenKind.Number, NATokenKind.RBracket);
    }

    [Fact]
    public void InOutDirection()
    {
        // =T[]
        AssertTokens("=T[]",
            NATokenKind.DirInOut,
            NATokenKind.TypeT,
            NATokenKind.LBracket, NATokenKind.RBracket);
    }

    [Fact]
    public void ThreadedCall()
    {
        // I4 mydll|foo& I4
        AssertTokens("I4 mydll|foo& I4",
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.Identifier, NATokenKind.Pipe, NATokenKind.Identifier,
            NATokenKind.Ampersand,
            NATokenKind.TypeI, NATokenKind.Number);
    }

    [Fact]
    public void ByteCountedString()
    {
        // <#T[]
        AssertTokens("<#T[]",
            NATokenKind.DirIn, NATokenKind.ByteCounted, NATokenKind.TypeT,
            NATokenKind.LBracket, NATokenKind.RBracket);
    }

    [Fact]
    public void UTF_Type()
    {
        // >0UTF8[]
        AssertTokens(">0UTF8[]",
            NATokenKind.DirOut, NATokenKind.NullTerm,
            NATokenKind.TypeUTF, NATokenKind.Number,
            NATokenKind.LBracket, NATokenKind.RBracket);
    }

    // ── BSX-Light extensions ──

    [Fact]
    public void DoubleNull_00T()
    {
        // <00T
        AssertTokens("<00T",
            NATokenKind.DirIn, NATokenKind.DoubleNull, NATokenKind.TypeT);
    }

    [Fact]
    public void CountedArray_Star()
    {
        // {U4 I4[*]}
        AssertTokens("{U4 I4[*]}",
            NATokenKind.LBrace,
            NATokenKind.TypeU, NATokenKind.Number,
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.LBracket, NATokenKind.Star, NATokenKind.RBracket,
            NATokenKind.RBrace);
    }

    [Fact]
    public void CountedArray_StarWithMax()
    {
        // {U4 I4[*:256]}
        AssertTokens("{U4 I4[*:256]}",
            NATokenKind.LBrace,
            NATokenKind.TypeU, NATokenKind.Number,
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.LBracket, NATokenKind.Star, NATokenKind.Colon, NATokenKind.Number, NATokenKind.RBracket,
            NATokenKind.RBrace);
    }

    [Fact]
    public void Aligned_Modifier()
    {
        // =@aligned{C1 I4 C1 F8}
        AssertTokens("=@aligned{C1 I4 C1 F8}",
            NATokenKind.DirInOut, NATokenKind.Aligned, NATokenKind.LBrace,
            NATokenKind.TypeC, NATokenKind.Number,
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.TypeC, NATokenKind.Number,
            NATokenKind.TypeF, NATokenKind.Number,
            NATokenKind.RBrace);
    }

    [Fact]
    public void Pack_Modifier()
    {
        // =@pack(2){C1 I4}
        AssertTokens("=@pack(2){C1 I4}",
            NATokenKind.DirInOut, NATokenKind.Pack, NATokenKind.LParen, NATokenKind.Number, NATokenKind.RParen,
            NATokenKind.LBrace,
            NATokenKind.TypeC, NATokenKind.Number,
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.RBrace);
    }

    [Fact]
    public void Comment_BlockComment()
    {
        // ={/* hwnd */ P /* wFunc */ U4}
        AssertTokens("={/* hwnd */ P /* wFunc */ U4}",
            NATokenKind.DirInOut, NATokenKind.LBrace,
            NATokenKind.TypeP,
            NATokenKind.TypeU, NATokenKind.Number,
            NATokenKind.RBrace);
    }

    [Fact]
    public void Comment_Multiline()
    {
        var input = "={\n  P\n  U4\n}";
        AssertTokens(input,
            NATokenKind.DirInOut, NATokenKind.LBrace,
            NATokenKind.TypeP,
            NATokenKind.TypeU, NATokenKind.Number,
            NATokenKind.RBrace);
    }

    // ── Full SHFILEOPSTRUCT-style example ──

    [Fact]
    public void FullSHFileOp_Example()
    {
        // 'I4 shell32|SHFileOperationW* ={P U4 <00T <00T U2 I4 P <0T}'
        var input = "I4 shell32|SHFileOperationW* ={P U4 <00T <00T U2 I4 P <0T}";
        var tokens = Lex(input);
        var kinds = tokens.Select(t => t.Kind).ToList();

        // Return type
        Assert.Equal(NATokenKind.TypeI, kinds[0]);
        Assert.Equal(NATokenKind.Number, kinds[1]); // 4

        // DLL|Function*
        Assert.Equal(NATokenKind.Identifier, kinds[2]); // shell32
        Assert.Equal(NATokenKind.Pipe, kinds[3]);
        Assert.Equal(NATokenKind.Identifier, kinds[4]); // SHFileOperationW
        Assert.Equal(NATokenKind.Star, kinds[5]); // * (pointer to struct arg)

        // ={...}
        Assert.Equal(NATokenKind.DirInOut, kinds[6]);
        Assert.Equal(NATokenKind.LBrace, kinds[7]);
        Assert.Equal(NATokenKind.TypeP, kinds[8]); // P (hwnd)
        Assert.Equal(NATokenKind.TypeU, kinds[9]); // U4 (wFunc)
        Assert.Equal(NATokenKind.Number, kinds[10]);
        Assert.Equal(NATokenKind.DirIn, kinds[11]); // <00T (pFrom)
        Assert.Equal(NATokenKind.DoubleNull, kinds[12]);
        Assert.Equal(NATokenKind.TypeT, kinds[13]);
        Assert.Equal(NATokenKind.DirIn, kinds[14]); // <00T (pTo)
        Assert.Equal(NATokenKind.DoubleNull, kinds[15]);
        Assert.Equal(NATokenKind.TypeT, kinds[16]);
    }

    [Fact]
    public void Identifier_WithDot()
    {
        // kernel32.dll|Sleep
        var tokens = Lex("kernel32.dll|Sleep U4");
        Assert.Equal("kernel32.dll", tokens[0].Value);
        Assert.Equal(NATokenKind.Identifier, tokens[0].Kind);
    }

    [Fact]
    public void Identifier_WithPath()
    {
        // C:\\libs\\mylib|fn
        var tokens = Lex("C:\\libs\\mylib|fn I4");
        Assert.Equal("C:\\libs\\mylib", tokens[0].Value);
    }

    [Fact]
    public void EmptyInput()
    {
        var tokens = Lex("");
        Assert.Single(tokens);
        Assert.Equal(NATokenKind.End, tokens[0].Kind);
    }

    [Fact]
    public void PointerType_Bare()
    {
        AssertTokens("P", NATokenKind.TypeP);
    }

    [Fact]
    public void NullTermChar_0C()
    {
        AssertTokens("<0C[]",
            NATokenKind.DirIn, NATokenKind.NullTerm, NATokenKind.TypeC,
            NATokenKind.LBracket, NATokenKind.RBracket);
    }

    [Fact]
    public void NestedStruct()
    {
        AssertTokens("<{I2 {I1[6]} F8}[]",
            NATokenKind.DirIn, NATokenKind.LBrace,
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.LBrace,
            NATokenKind.TypeI, NATokenKind.Number,
            NATokenKind.LBracket, NATokenKind.Number, NATokenKind.RBracket,
            NATokenKind.RBrace,
            NATokenKind.TypeF, NATokenKind.Number,
            NATokenKind.RBrace,
            NATokenKind.LBracket, NATokenKind.RBracket);
    }
}
