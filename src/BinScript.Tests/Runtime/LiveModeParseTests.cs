namespace BinScript.Tests.Runtime;

using System.Runtime.InteropServices;
using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Core.Bytecode;
using BinScript.Core.Model;
using BinScript.Core.Runtime;
using BinScript.Emitters.Json;

/// <summary>
/// Tests for dual-mode ParseContext: verifying that live-mode parsing
/// (reading from pinned memory via raw addresses) produces identical
/// results to buffer-mode parsing.
/// </summary>
public class LiveModeParseTests
{
    // ─── Helpers ──────────────────────────────────────────────────────

    private static BytecodeProgram Compile(string source)
    {
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile(source, "test.bsx");
        Assert.True(result.Success, FormatDiags(result.Diagnostics));
        Assert.NotNull(result.Program);
        return result.Program!;
    }

    private static string ParseToJson(BytecodeProgram program, byte[] data)
    {
        using var emitter = new JsonResultEmitter();
        var engine = new ParseEngine();
        var result = engine.Parse(program, data, emitter);
        Assert.True(result.Success, FormatDiags(result.Diagnostics));
        return emitter.GetJson();
    }

    private static string ParseLiveToJson(BytecodeProgram program, byte[] data, ParseOptions? options = null)
    {
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            nint address = pin.AddrOfPinnedObject();
            using var emitter = new JsonResultEmitter();
            var engine = new ParseEngine();
            var result = engine.ParseLive(program, address, data.Length, emitter, options);
            Assert.True(result.Success, FormatDiags(result.Diagnostics));
            return emitter.GetJson();
        }
        finally
        {
            pin.Free();
        }
    }

    private static string FormatDiags(IReadOnlyList<Diagnostic> diags) =>
        string.Join("\n", diags.Select(d => $"[{d.Severity}] {d.Code}: {d.Message}"));

    // ─── Basic: flat struct identical in both modes ───────────────────

    [Fact]
    public void LiveMode_FlatStruct_MatchesBufferMode()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Header {
                a: u8,
                b: u16,
                c: u32,
            }
            """);

        byte[] data = [0x42, 0x34, 0x12, 0x78, 0x56, 0x34, 0x12];

        string bufferJson = ParseToJson(program, data);
        string liveJson = ParseLiveToJson(program, data);

        Assert.Equal(bufferJson, liveJson);

        // Verify actual values
        using var doc = JsonDocument.Parse(liveJson);
        Assert.Equal(0x42, doc.RootElement.GetProperty("a").GetInt32());
        Assert.Equal(0x1234, doc.RootElement.GetProperty("b").GetInt32());
        Assert.Equal(0x12345678u, doc.RootElement.GetProperty("c").GetUInt32());
    }

    [Fact]
    public void LiveMode_BigEndian_MatchesBufferMode()
    {
        var program = Compile("""
            @default_endian(big)
            @root struct Header {
                x: u16,
                y: u32,
            }
            """);

        byte[] data = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];

        string bufferJson = ParseToJson(program, data);
        string liveJson = ParseLiveToJson(program, data);
        Assert.Equal(bufferJson, liveJson);
    }

    // ─── Strings ─────────────────────────────────────────────────────

    [Fact]
    public void LiveMode_CString_MatchesBufferMode()
    {
        var program = Compile("""
            @root struct Msg {
                name: cstring,
            }
            """);

        byte[] data = [.. "Hello\0"u8];

        string bufferJson = ParseToJson(program, data);
        string liveJson = ParseLiveToJson(program, data);
        Assert.Equal(bufferJson, liveJson);

        using var doc = JsonDocument.Parse(liveJson);
        Assert.Equal("Hello", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void LiveMode_FixedString_MatchesBufferMode()
    {
        var program = Compile("""
            @root struct Msg {
                tag: fixed_string[4],
            }
            """);

        byte[] data = [.. "ABCD"u8];

        string bufferJson = ParseToJson(program, data);
        string liveJson = ParseLiveToJson(program, data);
        Assert.Equal(bufferJson, liveJson);
    }

    // ─── Nested structs ──────────────────────────────────────────────

    [Fact]
    public void LiveMode_NestedStruct_MatchesBufferMode()
    {
        var program = Compile("""
            @default_endian(little)
            struct Inner { x: u8, y: u8 }
            @root struct Outer { a: u16, inner: Inner }
            """);

        byte[] data = [0x01, 0x00, 0xAA, 0xBB];

        string bufferJson = ParseToJson(program, data);
        string liveJson = ParseLiveToJson(program, data);
        Assert.Equal(bufferJson, liveJson);
    }

    // ─── Arrays ──────────────────────────────────────────────────────

    [Fact]
    public void LiveMode_FixedArray_MatchesBufferMode()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Data { items: u16[3] }
            """);

        byte[] data = [0x01, 0x00, 0x02, 0x00, 0x03, 0x00];

        string bufferJson = ParseToJson(program, data);
        string liveJson = ParseLiveToJson(program, data);
        Assert.Equal(bufferJson, liveJson);
    }

    [Fact]
    public void LiveMode_UntilRemainingArray_MatchesBufferMode()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Data {
                items: u8[] @until(@remaining == 0)
            }
            """);

        byte[] data = [1, 2, 3, 4, 5];

        string bufferJson = ParseToJson(program, data);
        string liveJson = ParseLiveToJson(program, data);
        Assert.Equal(bufferJson, liveJson);
    }

    // ─── Runtime variables ───────────────────────────────────────────

    [Fact]
    public void LiveMode_RuntimeVars_OffsetAndRemaining()
    {
        // In live mode with a size hint, @offset and @remaining should work as expected
        var program = Compile("""
            @default_endian(little)
            @root struct Data {
                a: u8,
                @derived after_a_offset: u64 = @offset,
                @derived after_a_remaining: u64 = @remaining,
            }
            """);

        byte[] data = [0x42, 0xFF, 0xFF]; // 3 bytes

        string liveJson = ParseLiveToJson(program, data);
        using var doc = JsonDocument.Parse(liveJson);
        Assert.Equal(0x42, doc.RootElement.GetProperty("a").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("after_a_offset").GetInt64());
        Assert.Equal(2, doc.RootElement.GetProperty("after_a_remaining").GetInt64());
    }

    // ─── ParseContext properties ─────────────────────────────────────

    [Fact]
    public void ParseContext_BufferMode_Properties()
    {
        byte[] data = [1, 2, 3, 4, 5];
        var ctx = new ParseContext(data);

        Assert.False(ctx.IsLiveMode);
        Assert.Equal(0, (long)ctx.BaseAddress);
        Assert.Equal(5, ctx.InputSize);
        Assert.Equal(0, ctx.Position);
        Assert.Equal(5, ctx.Remaining);

        ctx.Position = 2;
        Assert.Equal(2, ctx.Offset);
        Assert.Equal(3, ctx.Remaining);
    }

    [Fact]
    public void ParseContext_LiveMode_Properties()
    {
        nint addr = 0x10000;
        var ctx = new ParseContext(addr, sizeHint: 100);

        Assert.True(ctx.IsLiveMode);
        Assert.Equal(addr, ctx.BaseAddress);
        Assert.Equal(100, ctx.InputSize);
        Assert.Equal(0, ctx.Position);
        Assert.Equal(100, ctx.Remaining);

        ctx.Position = 30;
        Assert.Equal(30, ctx.Offset);
        Assert.Equal(70, ctx.Remaining);
    }

    [Fact]
    public void ParseContext_LiveMode_NoSizeHint_ReturnsMaxValue()
    {
        nint addr = 0x10000;
        var ctx = new ParseContext(addr, sizeHint: 0);

        Assert.True(ctx.IsLiveMode);
        Assert.Equal(long.MaxValue, ctx.InputSize);
        Assert.Equal(long.MaxValue, ctx.Remaining);
    }

    // ─── Pointer chasing in live mode ────────────────────────────────

    [Fact]
    public unsafe void LiveMode_PtrField_ChasesAbsolutePointer()
    {
        // Allocate unmanaged memory for a struct + pointed-to data
        // Layout:
        //   offset 0: u32 value = 0x42
        //   offset 4: u64 pointer → points to offset 12 (absolute address)
        //   offset 12: u16 target = 0xBEEF
        int totalSize = 14;
        byte* mem = (byte*)NativeMemory.Alloc((nuint)totalSize);
        try
        {
            // Zero out
            new Span<byte>(mem, totalSize).Clear();

            // Write u32 at offset 0
            *(uint*)mem = 0x42;

            // The pointer at offset 4 should point to offset 12 in absolute terms
            nint baseAddr = (nint)mem;
            *(ulong*)(mem + 4) = (ulong)(baseAddr + 12);

            // Write u16 at offset 12
            *(ushort*)(mem + 12) = 0xBEEF;

            var program = Compile("""
                @default_endian(little)
                @param base_ptr: u64
                struct Target { value: u16 }
                @root struct Main {
                    a: u32,
                    target: ptr<Target, u64>,
                }
                """);

            var options = new ParseOptions
            {
                RuntimeParameters = new Dictionary<string, long>
                {
                    ["base_ptr"] = baseAddr,
                }
            };

            using var emitter = new JsonResultEmitter();
            var engine = new ParseEngine();
            var result = engine.ParseLive(program, baseAddr, totalSize, emitter, options);
            Assert.True(result.Success, FormatDiags(result.Diagnostics));

            string json = emitter.GetJson();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(0x42u, doc.RootElement.GetProperty("a").GetUInt32());
            Assert.Equal(0xBEEF, doc.RootElement.GetProperty("target").GetProperty("value").GetInt32());
        }
        finally
        {
            NativeMemory.Free(mem);
        }
    }

    // ─── BinScriptProgram API ────────────────────────────────────────

    [Fact]
    public void BinScriptProgram_ParseLive_Works()
    {
        var compiler = new BinScriptCompiler();
        var compiled = compiler.Compile("""
            @default_endian(little)
            @root struct Simple { x: u32 }
            """, "test.bsx");
        Assert.True(compiled.Success);
        var prog = new BinScriptProgram(compiled.Program!);

        byte[] data = [0x78, 0x56, 0x34, 0x12];
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            using var emitter = new JsonResultEmitter();
            var result = prog.ParseLive(pin.AddrOfPinnedObject(), data.Length, emitter);
            Assert.True(result.Success);
            using var doc = JsonDocument.Parse(emitter.GetJson());
            Assert.Equal(0x12345678u, doc.RootElement.GetProperty("x").GetUInt32());
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void BinScriptProgram_ParseLive_NamedEntry_InvalidName_Fails()
    {
        var compiler = new BinScriptCompiler();
        var compiled = compiler.Compile("""
            @default_endian(little)
            @root struct Simple { x: u32 }
            """, "test.bsx");
        var prog = new BinScriptProgram(compiled.Program!);

        using var emitter = new JsonResultEmitter();
        var result = prog.ParseLive(nint.Zero, 0, "NonExistent", emitter);
        Assert.False(result.Success);
    }

    // ─── JSON extension methods ──────────────────────────────────────

    [Fact]
    public void ToJsonLive_Extension_Works()
    {
        var compiler = new BinScriptCompiler();
        var compiled = compiler.Compile("""
            @default_endian(little)
            @root struct Simple { x: u32 }
            """, "test.bsx");
        var prog = new BinScriptProgram(compiled.Program!);

        byte[] data = [0x78, 0x56, 0x34, 0x12];
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            string json = prog.ToJsonLive(pin.AddrOfPinnedObject(), data.Length);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(0x12345678u, doc.RootElement.GetProperty("x").GetUInt32());
        }
        finally
        {
            pin.Free();
        }
    }
}
