using BinScript.Core.Api;
using BinScript.Emitters.Json;
using BinScript.NALight;

namespace BinScript.Tests.NALight;

/// <summary>
/// End-to-end tests: BSX-Light spec → compile → parse binary → verify output.
/// </summary>
public class NALightCompilerTests
{
    [Fact]
    public void Compile_SingleU32_Success()
    {
        var result = NALightCompiler.Compile("lib|fn U4");
        Assert.True(result.Success, DiagString(result));
        Assert.NotNull(result.Inner.Program);
    }

    [Fact]
    public void Compile_MultipleArgs_Success()
    {
        var result = NALightCompiler.Compile("I4 lib|fn I4 U2 P");
        Assert.True(result.Success, DiagString(result));
    }

    [Fact]
    public void Compile_Struct_Success()
    {
        var result = NALightCompiler.Compile("lib|fn {I4 U2 P}");
        Assert.True(result.Success, DiagString(result));
    }

    [Fact]
    public void Compile_NullTermString_Success()
    {
        var result = NALightCompiler.Compile("lib|fn 0T");
        Assert.True(result.Success, DiagString(result));
    }

    [Fact]
    public void Compile_DoubleNullString_Success()
    {
        var result = NALightCompiler.Compile("lib|fn 00T");
        Assert.True(result.Success, DiagString(result));
    }

    [Fact]
    public void Compile_FixedArray_Success()
    {
        var result = NALightCompiler.Compile("lib|fn I4[10]");
        Assert.True(result.Success, DiagString(result));
    }

    [Fact]
    public void Compile_NestedStruct_Success()
    {
        var result = NALightCompiler.Compile("lib|fn {I4 {U2 U2} I4}");
        Assert.True(result.Success, DiagString(result));
    }

    [Fact]
    public void Compile_StructWithDirectionPtr_Success()
    {
        // Direction + type inside struct → ptr<type, u64>
        var result = NALightCompiler.Compile("lib|fn ={P U4 <0T}");
        Assert.True(result.Success, DiagString(result));
    }

    [Fact]
    public void Compile_Complex_J_Success()
    {
        var result = NALightCompiler.Compile("lib|fn J");
        Assert.True(result.Success, DiagString(result));
    }

    [Fact]
    public void Compile_Decimal_D_Success()
    {
        var result = NALightCompiler.Compile("lib|fn D");
        Assert.True(result.Success, DiagString(result));
    }

    // ── Parse round-trip tests ──────────────────────────────────────────────

    [Fact]
    public void ParseBinary_SingleU32()
    {
        var result = NALightCompiler.Compile("lib|fn U4");
        Assert.True(result.Success, DiagString(result));

        // u32le = 0x12345678
        byte[] data = [0x78, 0x56, 0x34, 0x12];
        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("305419896", json); // 0x12345678
    }

    [Fact]
    public void ParseBinary_TwoArgs_I4_U2()
    {
        var result = NALightCompiler.Compile("lib|fn I4 U2");
        Assert.True(result.Success, DiagString(result));

        // i32le = -1 (0xFFFFFFFF), u16le = 42 (0x002A)
        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF, 0x2A, 0x00];
        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("-1", json);
        Assert.Contains("42", json);
    }

    [Fact]
    public void ParseBinary_Struct_I4_U2()
    {
        var result = NALightCompiler.Compile("lib|fn {I4 U2}");
        Assert.True(result.Success, DiagString(result));

        // struct { i32le = 100, u16le = 200 }
        byte[] data = [0x64, 0x00, 0x00, 0x00, 0xC8, 0x00];
        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("100", json);
        Assert.Contains("200", json);
    }

    [Fact]
    public void ParseBinary_CString()
    {
        var result = NALightCompiler.Compile("lib|fn 0T1");
        Assert.True(result.Success, DiagString(result));

        // "Hello\0"
        byte[] data = [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00];
        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("Hello", json);
    }

    [Fact]
    public void ParseBinary_FixedArray_U2()
    {
        var result = NALightCompiler.Compile("lib|fn U2[3]");
        Assert.True(result.Success, DiagString(result));

        // [10, 20, 30]
        byte[] data = [0x0A, 0x00, 0x14, 0x00, 0x1E, 0x00];
        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("10", json);
        Assert.Contains("20", json);
        Assert.Contains("30", json);
    }

    [Fact]
    public void ParseBinary_DoubleNull_CStringArray()
    {
        var result = NALightCompiler.Compile("lib|fn 00T1");
        Assert.True(result.Success, DiagString(result));

        // "abc\0def\0\0" (double-null terminated)
        byte[] data = [0x61, 0x62, 0x63, 0x00, 0x64, 0x65, 0x66, 0x00, 0x00];
        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("abc", json);
        Assert.Contains("def", json);
    }

    [Fact]
    public void ParseBinary_F8_Double()
    {
        var result = NALightCompiler.Compile("lib|fn F8");
        Assert.True(result.Success, DiagString(result));

        // IEEE 754 double for 3.14
        byte[] data = BitConverter.GetBytes(3.14);
        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("3.14", json);
    }

    // ── Plain ⎕NA generation ────────────────────────────────────────────────

    [Fact]
    public void PlainNA_BasicSpec()
    {
        var result = NALightCompiler.Compile("I4 user32|MessageBoxW P <0T <0T U4");
        Assert.Equal("I4 user32|MessageBoxW P <0T2 <0T2 U4", result.PlainNA);
    }

    [Fact]
    public void PlainNA_StructWithDirections_DowngradeToP()
    {
        var result = NALightCompiler.Compile("I4 lib|fn* ={P U4 <0T >0T}");
        // Directions stripped from struct fields, non-P types with direction → P
        Assert.Equal("I4 lib|fn* ={P U4 P P}", result.PlainNA);
    }

    [Fact]
    public void PlainNA_DoubleNull_DowngradeToP()
    {
        var result = NALightCompiler.Compile("lib|fn <00T");
        Assert.Contains("<P", result.PlainNA);
    }

    [Fact]
    public void PlainNA_PassByPointerAndThreaded()
    {
        var result = NALightCompiler.Compile("I4 lib|fn*& I4");
        Assert.Contains("lib|fn*&", result.PlainNA);
    }

    // ── Counted arrays ─────────────────────────────────────────────────────

    [Fact]
    public void Compile_CountedArray_InStruct_Success()
    {
        // {U4 I4[*]} = count field followed by array of that length
        var result = NALightCompiler.Compile("lib|fn {U4 I4[*]}");
        Assert.True(result.Success, DiagString(result));
    }

    [Fact]
    public void ParseBinary_CountedArray_InStruct()
    {
        // struct { count: u32le, items: i32le[count] }
        var result = NALightCompiler.Compile("lib|fn {U4 I4[*]}");
        Assert.True(result.Success, DiagString(result));

        // count=3, items=[10, 20, 30]
        byte[] data = [
            0x03, 0x00, 0x00, 0x00,   // count = 3
            0x0A, 0x00, 0x00, 0x00,   // 10
            0x14, 0x00, 0x00, 0x00,   // 20
            0x1E, 0x00, 0x00, 0x00,   // 30
        ];
        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("10", json);
        Assert.Contains("20", json);
        Assert.Contains("30", json);
    }

    [Fact]
    public void ParseBinary_CountedArray_TopLevel()
    {
        // U4 I4[*] = count then array at top level
        var result = NALightCompiler.Compile("lib|fn U4 I4[*]");
        Assert.True(result.Success, DiagString(result));

        // count=2, items=[100, 200]
        byte[] data = [
            0x02, 0x00, 0x00, 0x00,   // count = 2
            0x64, 0x00, 0x00, 0x00,   // 100
            0xC8, 0x00, 0x00, 0x00,   // 200
        ];
        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("100", json);
        Assert.Contains("200", json);
    }

    // ── Alignment ───────────────────────────────────────────────────────────

    [Fact]
    public void Compile_AlignedStruct_Success()
    {
        var result = NALightCompiler.Compile("lib|fn @aligned{C1 I4 C1 F8}");
        Assert.True(result.Success, DiagString(result));
    }

    [Fact]
    public void ParseBinary_AlignedStruct()
    {
        // @aligned{C1 I4 C1 F8}
        // Natural C alignment on x64:
        //   offset 0: C1 (1 byte)
        //   offset 1: 3 bytes padding (align to 4)
        //   offset 4: I4 (4 bytes)
        //   offset 8: C1 (1 byte)
        //   offset 9: 7 bytes padding (align to 8)
        //   offset 16: F8 (8 bytes)
        //   total: 24 bytes (already aligned to 8)
        var result = NALightCompiler.Compile("lib|fn @aligned{C1 I4 C1 F8}");
        Assert.True(result.Success, DiagString(result));

        byte[] data = new byte[24];
        data[0] = 0x41;                                    // C1 = 'A' (65)
        // 3 bytes padding
        BitConverter.GetBytes(42).CopyTo(data, 4);         // I4 = 42
        data[8] = 0x42;                                    // C1 = 'B' (66)
        // 7 bytes padding
        BitConverter.GetBytes(3.14).CopyTo(data, 16);      // F8 = 3.14

        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("65", json);    // C1 = 0x41
        Assert.Contains("42", json);    // I4
        Assert.Contains("66", json);    // C1 = 0x42
        Assert.Contains("3.14", json);  // F8
    }

    [Fact]
    public void ParseBinary_PackedStruct_NoAlignment()
    {
        // Default packed: {C1 I4 C1 F8} = 14 bytes, no padding
        var result = NALightCompiler.Compile("lib|fn {C1 I4 C1 F8}");
        Assert.True(result.Success, DiagString(result));

        byte[] data = new byte[14];
        data[0] = 0x41;                                   // C1 = 'A'
        BitConverter.GetBytes(42).CopyTo(data, 1);         // I4 at offset 1 (packed!)
        data[5] = 0x42;                                    // C1 = 'B'
        BitConverter.GetBytes(3.14).CopyTo(data, 6);       // F8 at offset 6 (packed!)

        var json = new BinScriptProgram(result.Inner.Program!).ToJson(data);
        Assert.Contains("65", json);
        Assert.Contains("42", json);
        Assert.Contains("66", json);
        Assert.Contains("3.14", json);
    }

    [Fact]
    public void Compile_PackStruct_Success()
    {
        var result = NALightCompiler.Compile("lib|fn @pack(2){C1 I4}");
        Assert.True(result.Success, DiagString(result));
    }

    // ── Struct return transform ─────────────────────────────────────────────

    [Fact]
    public void PlainNA_StructReturn_LargeStruct_HiddenPointer()
    {
        // Struct > 8 bytes → hidden pointer transform
        // User: '{U1 U1 U8 I8} duckdb|get_decimal P'
        // Plain: 'P duckdb|get_decimal >{U1 U1 U8 I8} P'
        var result = NALightCompiler.Compile("{U1 U1 U8 I8} duckdb|get_decimal P");
        Assert.Contains("P duckdb|get_decimal", result.PlainNA);
        Assert.Contains(">{U1 U1 U8 I8}", result.PlainNA);
    }

    [Fact]
    public void PlainNA_StructReturn_SmallStruct_NoTransform()
    {
        // Struct <= 8 bytes → no transform
        var result = NALightCompiler.Compile("{I4 I4} lib|fn P");
        // {I4 I4} = 8 bytes, fits in register
        Assert.StartsWith("{I4 I4}", result.PlainNA);
        Assert.DoesNotContain(">{", result.PlainNA);
    }

    [Fact]
    public void PlainNA_NonStructReturn_NoTransform()
    {
        var result = NALightCompiler.Compile("I4 lib|fn P");
        Assert.StartsWith("I4 lib|fn", result.PlainNA);
    }

    // ── Metadata ────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_Preserved()
    {
        var result = NALightCompiler.Compile("I4 shell32|SHFileOp* ={P U4}");
        Assert.Equal("shell32", result.Spec.LibraryPath);
        Assert.Equal("SHFileOp", result.Spec.FunctionName);
        Assert.True(result.Spec.PassByPointer);
        Assert.NotNull(result.Spec.ReturnType);
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    private static string DiagString(NACompilationResult result) =>
        string.Join("\n", result.Diagnostics.Select(d => $"[{d.Severity}] {d.Code}: {d.Message}"));
}
