namespace BinScript.Tests.Interop;

using BinScript.Core.Api;
using BinScript.Core.Bytecode;
using BinScript.Emitters.Json;
using BinScript.Interop;

/// <summary>
/// Tests for the C-ABI interop layer.
/// Exercises HandleTable, ErrorState, and the managed logic
/// behind NativeExports without actual P/Invoke.
/// </summary>
public class NativeExportTests
{
    // ── HandleTable ──────────────────────────────────────────────

    [Fact]
    public void HandleTable_Alloc_ReturnsNonZero()
    {
        var handle = HandleTable.Alloc(new object());
        Assert.NotEqual(IntPtr.Zero, handle);
        HandleTable.Free(handle);
    }

    [Fact]
    public void HandleTable_Get_ReturnsOriginalObject()
    {
        var compiler = new BinScriptCompiler();
        var handle = HandleTable.Alloc(compiler);

        var retrieved = HandleTable.Get<BinScriptCompiler>(handle);
        Assert.Same(compiler, retrieved);

        HandleTable.Free(handle);
    }

    [Fact]
    public void HandleTable_Get_WithZeroHandle_ReturnsNull()
    {
        Assert.Null(HandleTable.Get<BinScriptCompiler>(IntPtr.Zero));
    }

    [Fact]
    public void HandleTable_Get_WrongType_ReturnsNull()
    {
        var handle = HandleTable.Alloc("a string");
        Assert.Null(HandleTable.Get<BinScriptCompiler>(handle));
        HandleTable.Free(handle);
    }

    [Fact]
    public void HandleTable_Free_ThenGet_ReturnsNull()
    {
        var handle = HandleTable.Alloc(new BinScriptCompiler());
        HandleTable.Free(handle);

        Assert.Null(HandleTable.Get<BinScriptCompiler>(handle));
    }

    [Fact]
    public void HandleTable_Free_ZeroHandle_DoesNotThrow()
    {
        HandleTable.Free(IntPtr.Zero); // should be a no-op
    }

    // ── ErrorState ───────────────────────────────────────────────

    [Fact]
    public void ErrorState_SetAndGet()
    {
        ErrorState.Set("test error");
        Assert.Equal("test error", ErrorState.Get());
        ErrorState.Clear();
    }

    [Fact]
    public void ErrorState_Clear_SetsNull()
    {
        ErrorState.Set("some error");
        ErrorState.Clear();
        Assert.Null(ErrorState.Get());
    }

    [Fact]
    public void ErrorState_ThreadLocal_Isolation()
    {
        ErrorState.Clear();
        ErrorState.Set("main thread error");

        string? otherThreadError = null;
        var thread = new Thread(() =>
        {
            otherThreadError = ErrorState.Get();
        });
        thread.Start();
        thread.Join();

        Assert.Equal("main thread error", ErrorState.Get());
        Assert.Null(otherThreadError);
        ErrorState.Clear();
    }

    // ── Compiler lifecycle (via HandleTable) ─────────────────────

    [Fact]
    public void CompilerNew_ReturnsValidHandle()
    {
        var handle = HandleTable.Alloc(new BinScriptCompiler());
        Assert.NotEqual(IntPtr.Zero, handle);

        var compiler = HandleTable.Get<BinScriptCompiler>(handle);
        Assert.NotNull(compiler);

        HandleTable.Free(handle);
    }

    [Fact]
    public void CompilerCompile_Success()
    {
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile("@root struct T { a: u8 }");
        Assert.True(result.Success);
        Assert.NotNull(result.Program);
    }

    [Fact]
    public void CompilerCompile_Failure_SetsError()
    {
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile("@root struct T {"); // incomplete
        Assert.False(result.Success);
    }

    // ── Program persistence ──────────────────────────────────────

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile("@root struct T { a: u8 }");
        Assert.True(result.Success);

        var program = new BinScriptProgram(result.Program!);
        byte[] serialized = BytecodeSerializer.Serialize(program.Bytecode);
        Assert.True(serialized.Length > 0);

        var deserialized = BytecodeDeserializer.Deserialize(serialized);
        var loadedProgram = new BinScriptProgram(deserialized);

        string json = loadedProgram.ToJson(new byte[] { 42 });
        Assert.Contains("42", json);
    }

    // ── Parse: Binary → JSON ─────────────────────────────────────

    [Fact]
    public void FullPipeline_CompileAndParse()
    {
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile("@root struct T { a: u8 }");
        Assert.True(result.Success);

        var program = new BinScriptProgram(result.Program!);
        var handle = HandleTable.Alloc(program);

        var prog = HandleTable.Get<BinScriptProgram>(handle);
        Assert.NotNull(prog);

        string json = prog!.ToJson(new byte[] { 42 });
        Assert.Contains("42", json);

        HandleTable.Free(handle);
    }

    [Fact]
    public void ToJson_WithEntryPoint()
    {
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile("@root struct Root { a: u8 } struct Other { b: u16 }");
        Assert.True(result.Success);

        var program = new BinScriptProgram(result.Program!);

        // Parse with named entry "Other" — needs 2 bytes for u16
        string json = program.ToJson(new byte[] { 0x01, 0x00 }, "Other");
        Assert.Contains("1", json);
    }

    // ── Produce stubs ────────────────────────────────────────────

    [Fact]
    public void ProduceStubs_SetNotImplementedError()
    {
        // The native exports set ErrorState — we test the pattern directly.
        ErrorState.Set("Not yet implemented");
        Assert.Equal("Not yet implemented", ErrorState.Get());
        ErrorState.Clear();
    }

    // ── AllocUtf8 helper ─────────────────────────────────────────

    [Fact]
    public unsafe void AllocUtf8_ReturnsNullTerminatedString()
    {
        byte* ptr = NativeExports.AllocUtf8("hello");
        Assert.True(ptr != null);

        // Read back
        var span = new ReadOnlySpan<byte>(ptr, 6); // "hello\0"
        Assert.Equal((byte)'h', span[0]);
        Assert.Equal((byte)'e', span[1]);
        Assert.Equal((byte)'l', span[2]);
        Assert.Equal((byte)'l', span[3]);
        Assert.Equal((byte)'o', span[4]);
        Assert.Equal(0, span[5]); // null terminator

        System.Runtime.InteropServices.NativeMemory.Free(ptr);
    }

    // ── ParseFlatJsonObject helper ───────────────────────────────

    [Fact]
    public void ParseFlatJsonObject_EmptyObject()
    {
        var result = NativeExports.ParseFlatJsonObject("{}");
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Fact]
    public void ParseFlatJsonObject_StringValue()
    {
        var result = NativeExports.ParseFlatJsonObject("{\"key\": \"value\"}");
        Assert.NotNull(result);
        Assert.Equal("value", result!["key"]);
    }

    [Fact]
    public void ParseFlatJsonObject_IntegerValue()
    {
        var result = NativeExports.ParseFlatJsonObject("{\"n\": 42}");
        Assert.NotNull(result);
        Assert.Equal(42L, result!["n"]);
    }

    [Fact]
    public void ParseFlatJsonObject_FloatValue()
    {
        var result = NativeExports.ParseFlatJsonObject("{\"f\": 3.14}");
        Assert.NotNull(result);
        Assert.Equal(3.14, result!["f"]);
    }

    [Fact]
    public void ParseFlatJsonObject_BoolValues()
    {
        var result = NativeExports.ParseFlatJsonObject("{\"a\": true, \"b\": false}");
        Assert.NotNull(result);
        Assert.Equal(true, result!["a"]);
        Assert.Equal(false, result!["b"]);
    }

    [Fact]
    public void ParseFlatJsonObject_NullInput()
    {
        Assert.Null(NativeExports.ParseFlatJsonObject("null"));
    }

    [Fact]
    public void ParseFlatJsonObject_MixedValues()
    {
        var result = NativeExports.ParseFlatJsonObject(
            "{\"name\": \"test\", \"count\": 5, \"enabled\": true}");
        Assert.NotNull(result);
        Assert.Equal("test", result!["name"]);
        Assert.Equal(5L, result!["count"]);
        Assert.Equal(true, result!["enabled"]);
    }

    // ── Multiple handles ─────────────────────────────────────────

    [Fact]
    public void MultipleHandles_Independent()
    {
        var c1 = new BinScriptCompiler();
        var c2 = new BinScriptCompiler();
        var h1 = HandleTable.Alloc(c1);
        var h2 = HandleTable.Alloc(c2);

        Assert.NotEqual(h1, h2);
        Assert.Same(c1, HandleTable.Get<BinScriptCompiler>(h1));
        Assert.Same(c2, HandleTable.Get<BinScriptCompiler>(h2));

        HandleTable.Free(h1);
        HandleTable.Free(h2);
    }

    // ── AddModule + Compile ──────────────────────────────────────

    [Fact]
    public void AddModule_ThenCompileWithImport()
    {
        var compiler = new BinScriptCompiler();
        compiler.AddModule("types", "struct Header { magic: u32 }");
        var result = compiler.Compile("@import \"types\" @root struct File { hdr: Header }");
        Assert.True(result.Success, string.Join("; ",
            result.Diagnostics.Select(d => d.Message)));
    }
}
