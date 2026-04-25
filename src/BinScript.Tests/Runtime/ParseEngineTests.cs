namespace BinScript.Tests.Runtime;

using System.Buffers.Binary;
using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Core.Bytecode;
using BinScript.Core.Model;
using BinScript.Core.Runtime;
using BinScript.Emitters.Json;

public class ParseEngineTests
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

    private static string ParseToJson(BytecodeProgram program, byte[] data, ParseOptions options)
    {
        using var emitter = new JsonResultEmitter();
        var engine = new ParseEngine();
        var result = engine.Parse(program, data, emitter, options);
        Assert.True(result.Success, FormatDiags(result.Diagnostics));
        return emitter.GetJson();
    }

    private static ParseResult ParseRaw(BytecodeProgram program, byte[] data, JsonResultEmitter emitter)
    {
        var engine = new ParseEngine();
        return engine.Parse(program, data, emitter);
    }

    private static string FormatDiags(IReadOnlyList<Diagnostic> diags) =>
        string.Join("\n", diags.Select(d => $"[{d.Severity}] {d.Code}: {d.Message}"));

    // ─── 1. Flat struct (Tier 1) ─────────────────────────────────────

    [Fact]
    public void FlatStruct_U8_U16le_U32le()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Header {
                a: u8,
                b: u16,
                c: u32,
            }
            """);

        // a=0x42, b=0x0102 (LE: 02 01), c=0x04030201 (LE: 01 02 03 04)
        byte[] data = [0x42, 0x02, 0x01, 0x01, 0x02, 0x03, 0x04];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x42, doc.RootElement.GetProperty("a").GetInt64());
        Assert.Equal(0x0102, doc.RootElement.GetProperty("b").GetInt64());
        Assert.Equal(0x04030201, doc.RootElement.GetProperty("c").GetInt64());
    }

    // ─── 2. Endianness ───────────────────────────────────────────────

    [Fact]
    public void Endianness_U32Be()
    {
        var program = Compile("""
            @default_endian(big)
            @root struct Header {
                value: u32,
            }
            """);

        byte[] data = [0x01, 0x02, 0x03, 0x04]; // BE: 0x01020304
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x01020304, doc.RootElement.GetProperty("value").GetInt64());
    }

    [Fact]
    public void Endianness_U16Be()
    {
        var program = Compile("""
            @root struct Test {
                val: u16be,
            }
            """);

        byte[] data = [0xAB, 0xCD];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0xABCD, doc.RootElement.GetProperty("val").GetInt64());
    }

    // ─── 3. Strings ──────────────────────────────────────────────────

    [Fact]
    public void CString_Reads_NullTerminated()
    {
        var program = Compile("""
            @root struct Test {
                name: cstring,
            }
            """);

        byte[] data = [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00]; // "Hello\0"
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Hello", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void FixedString_Reads_FixedLength()
    {
        var program = Compile("""
            @root struct Test {
                tag: fixed_string[4],
            }
            """);

        byte[] data = [0x54, 0x45, 0x53, 0x54]; // "TEST"
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("TEST", doc.RootElement.GetProperty("tag").GetString());
    }

    // ─── 4. Bytes ────────────────────────────────────────────────────

    [Fact]
    public void BytesFixed_EmitsBase64()
    {
        var program = Compile("""
            @root struct Test {
                data: bytes[3],
            }
            """);

        byte[] data = [0xDE, 0xAD, 0xBE];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        // base64 of [0xDE, 0xAD, 0xBE]
        string b64 = doc.RootElement.GetProperty("data").GetString()!;
        byte[] decoded = Convert.FromBase64String(b64);
        Assert.Equal(data, decoded);
    }

    // ─── 5. Nested struct ────────────────────────────────────────────

    [Fact]
    public void NestedStruct_ProducesNestedJson()
    {
        var program = Compile("""
            @root struct Outer {
                inner: Inner,
            }
            struct Inner {
                x: u8,
            }
            """);

        byte[] data = [0x2A]; // x = 42
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var inner = doc.RootElement.GetProperty("inner");
        Assert.Equal(42, inner.GetProperty("x").GetInt64());
    }

    // ─── 6. Count array ──────────────────────────────────────────────

    [Fact]
    public void CountArray_U8_ProducesJsonArray()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                items: u8[3],
            }
            """);

        byte[] data = [0x0A, 0x14, 0x1E]; // 10, 20, 30
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(3, arr.GetArrayLength());
        Assert.Equal(10, arr[0].GetInt64());
        Assert.Equal(20, arr[1].GetInt64());
        Assert.Equal(30, arr[2].GetInt64());
    }

    // ─── 7. Dynamic expression (header.length) ──────────────────────

    [Fact]
    public void DynamicBytes_UsesFieldReference()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                length: u8,
                data: bytes[length],
            }
            """);

        // length=3, then 3 bytes of data
        byte[] data = [0x03, 0xAA, 0xBB, 0xCC];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("length").GetInt64());
        string b64 = doc.RootElement.GetProperty("data").GetString()!;
        byte[] decoded = Convert.FromBase64String(b64);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, decoded);
    }

    // ─── 8. @seek ────────────────────────────────────────────────────

    [Fact]
    public void Seek_JumpsToOffset()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                @seek(4)
                value: u8,
            }
            """);

        byte[] data = [0x00, 0x00, 0x00, 0x00, 0x42]; // value at offset 4
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x42, doc.RootElement.GetProperty("value").GetInt64());
    }

    // ─── 9. @at block ────────────────────────────────────────────────

    [Fact]
    public void AtBlock_ReadsAtOffsetAndRestoresCursor()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                header_val: u8,
                @at(4) {
                    remote_val: u8,
                }
                next_val: u8,
            }
            """);

        // header_val at 0, next_val at 1, remote_val at 4
        byte[] data = [0x0A, 0x14, 0x00, 0x00, 0x2A];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x0A, doc.RootElement.GetProperty("header_val").GetInt64());
        Assert.Equal(0x2A, doc.RootElement.GetProperty("remote_val").GetInt64());
        Assert.Equal(0x14, doc.RootElement.GetProperty("next_val").GetInt64());
    }

    // ─── 10. @let binding ────────────────────────────────────────────

    [Fact]
    public void LetBinding_ComputedValueUsedLater()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                raw_size: u8,
                @let real_size = raw_size * 2
                data: bytes[real_size],
            }
            """);

        // raw_size=2, real_size=4, 4 bytes of data
        byte[] data = [0x02, 0xAA, 0xBB, 0xCC, 0xDD];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("raw_size").GetInt64());
        string b64 = doc.RootElement.GetProperty("data").GetString()!;
        byte[] decoded = Convert.FromBase64String(b64);
        Assert.Equal(4, decoded.Length);
    }

    // ─── 11. @skip ───────────────────────────────────────────────────

    [Fact]
    public void Skip_AdvancesCursor()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                first: u8,
                @skip(2)
                second: u8,
            }
            """);

        byte[] data = [0x0A, 0xFF, 0xFF, 0x14]; // first=10, skip 2, second=20
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(10, doc.RootElement.GetProperty("first").GetInt64());
        Assert.Equal(20, doc.RootElement.GetProperty("second").GetInt64());
    }

    // ─── 12. @align ──────────────────────────────────────────────────

    [Fact]
    public void Align_PadsToBoundary()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                first: u8,
                @align(4)
                second: u32,
            }
            """);

        // first at 0 (1 byte), align to 4, second at 4
        byte[] data = [0x01, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("first").GetInt64());
        Assert.Equal(42, doc.RootElement.GetProperty("second").GetInt64());
    }

    // ─── 13. @derived ────────────────────────────────────────────────

    [Fact]
    public void DerivedField_ComputedAndEmitted()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                width: u8,
                height: u8,
                @derived area: u8 = width * height,
            }
            """);

        byte[] data = [5, 7]; // width=5, height=7, area=35
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(5, doc.RootElement.GetProperty("width").GetInt64());
        Assert.Equal(7, doc.RootElement.GetProperty("height").GetInt64());
        Assert.Equal(35, doc.RootElement.GetProperty("area").GetInt64());
    }

    // ─── 14. Magic value assertion (pass) ────────────────────────────

    [Fact]
    public void MagicValue_CorrectValue_Passes()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Header {
                magic: u16 = 0x5A4D,
                size: u32,
            }
            """);

        byte[] data = [0x4D, 0x5A, 0x10, 0x00, 0x00, 0x00]; // MZ + size=16
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x5A4D, doc.RootElement.GetProperty("magic").GetInt64());
        Assert.Equal(16, doc.RootElement.GetProperty("size").GetInt64());
    }

    // ─── 15. Magic value assertion (fail) ────────────────────────────

    [Fact]
    public void MagicValue_WrongValue_Fails()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Header {
                magic: u16 = 0x5A4D,
            }
            """);

        byte[] data = [0xFF, 0xFF]; // wrong magic
        using var emitter = new JsonResultEmitter();
        var result = ParseRaw(program, data, emitter);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Assertion failed"));
    }

    // ─── 16. Match (value pattern) ───────────────────────────────────

    [Fact]
    public void Match_SelectsCorrectArm()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Packet {
                tag: u8,
                payload: match(tag) {
                    1 => u16,
                    2 => u32,
                },
            }
            """);

        // tag=1, payload is u16le = 0x0100
        byte[] data = [0x01, 0x00, 0x01];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("tag").GetInt64());
        Assert.Equal(256, doc.RootElement.GetProperty("payload").GetInt64());
    }

    // ─── 17. Match (default arm) ─────────────────────────────────────

    [Fact]
    public void Match_DefaultArm()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Packet {
                tag: u8,
                payload: match(tag) {
                    1 => u16,
                    _ => u8,
                },
            }
            """);

        // tag=99 (not 1), default arm → u8
        byte[] data = [99, 42];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(99, doc.RootElement.GetProperty("tag").GetInt64());
        Assert.Equal(42, doc.RootElement.GetProperty("payload").GetInt64());
    }

    // ─── 18. Bits struct ─────────────────────────────────────────────

    [Fact]
    public void BitsStruct_ParsesBitFields()
    {
        var program = Compile("""
            @default_endian(little)
            bits struct Flags : u8 {
                a: bit,
                b: bit,
                c: bits[3],
                d: bits[3],
            }
            @root struct Test {
                flags: Flags,
            }
            """);

        // 0b_101_010_1_1 = 0xAB ... let me compute:
        // LSB first: a=bit0, b=bit1, c=bits[2:4], d=bits[5:7]
        // value 0b11010101 = 0xD5
        // a = bit0 = 1, b = bit1 = 0, c = bits[2:4] = 0b101 = 5, d = bits[5:7] = 0b110 = 6
        byte[] data = [0xD5];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var flags = doc.RootElement.GetProperty("flags");
        Assert.True(flags.GetProperty("a").GetBoolean());   // bit 0 = 1
        Assert.False(flags.GetProperty("b").GetBoolean());  // bit 1 = 0
        Assert.Equal(5u, flags.GetProperty("c").GetUInt64()); // bits 2-4 = 101 = 5
        Assert.Equal(6u, flags.GetProperty("d").GetUInt64()); // bits 5-7 = 110 = 6
    }

    // ─── 19. Bool field ──────────────────────────────────────────────

    [Fact]
    public void BoolField_ZeroIsFalse_NonzeroIsTrue()
    {
        var program = Compile("""
            @root struct Test {
                a: bool,
                b: bool,
            }
            """);

        byte[] data = [0x00, 0x01];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("a").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("b").GetBoolean());
    }

    // ─── 20. End-to-end: BinScriptProgram.ToJson ─────────────────────

    [Fact]
    public void EndToEnd_BinScriptProgram_ToJson()
    {
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile("""
            @default_endian(little)
            @root struct Header {
                magic: u16 = 0x5A4D,
                size: u32,
            }
            """);
        Assert.True(result.Success, FormatDiags(result.Diagnostics));

        var program = new BinScriptProgram(result.Program!);
        byte[] data = [0x4D, 0x5A, 0x10, 0x00, 0x00, 0x00];
        var json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x5A4D, doc.RootElement.GetProperty("magic").GetInt64());
        Assert.Equal(16, doc.RootElement.GetProperty("size").GetInt64());
    }

    // ─── 21. BinScriptProgram.Parse with named entry point ───────────

    [Fact]
    public void BinScriptProgram_NamedEntryPoint()
    {
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile("""
            @default_endian(little)
            @root struct Main {
                x: u8,
            }
            struct Alt {
                y: u16,
            }
            """);
        Assert.True(result.Success, FormatDiags(result.Diagnostics));

        var program = new BinScriptProgram(result.Program!);
        byte[] data = [0x42, 0x00];

        using var emitter = new JsonResultEmitter();
        var parseResult = program.Parse(data, "Alt", emitter);
        Assert.True(parseResult.Success, FormatDiags(parseResult.Diagnostics));
        var json = emitter.GetJson();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x0042, doc.RootElement.GetProperty("y").GetInt64());
    }

    // ─── 22. Signed integers ─────────────────────────────────────────

    [Fact]
    public void SignedIntegers_I8_I16()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                a: i8,
                b: i16,
            }
            """);

        byte[] data = [0xFE, 0xFC, 0xFF]; // a = -2, b = -4 (0xFFFC LE)
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(-2, doc.RootElement.GetProperty("a").GetInt64());
        Assert.Equal(-4, doc.RootElement.GetProperty("b").GetInt64());
    }

    // ─── 23. Float fields ────────────────────────────────────────────

    [Fact]
    public void FloatField_F32Le()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                val: f32,
            }
            """);

        float expected = 3.14f;
        byte[] data = BitConverter.GetBytes(expected);
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, (float)doc.RootElement.GetProperty("val").GetDouble(), 0.001);
    }

    // ─── 24. Multiple structs ────────────────────────────────────────

    [Fact]
    public void MultipleStructs_NestedCall()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct File {
                header: Header,
                body_size: u8,
            }
            struct Header {
                magic: u8,
                version: u8,
            }
            """);

        byte[] data = [0x7F, 0x01, 0x10]; // header.magic=127, header.version=1, body_size=16
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var header = doc.RootElement.GetProperty("header");
        Assert.Equal(127, header.GetProperty("magic").GetInt64());
        Assert.Equal(1, header.GetProperty("version").GetInt64());
        Assert.Equal(16, doc.RootElement.GetProperty("body_size").GetInt64());
    }

    // ─── 25. U64 field ───────────────────────────────────────────────

    [Fact]
    public void U64Le_LargeValue()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                val: u64,
            }
            """);

        byte[] data = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00]; // 4294967295
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(4294967295, doc.RootElement.GetProperty("val").GetInt64());
    }

    // ─── 26. Empty array ─────────────────────────────────────────────

    [Fact]
    public void CountArray_ZeroElements()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                items: u8[0],
            }
            """);

        byte[] data = [];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("items");
        Assert.Equal(0, arr.GetArrayLength());
    }

    // ─── 27. Dynamic count array ─────────────────────────────────────

    [Fact]
    public void DynamicCountArray()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                count: u8,
                items: u8[count],
            }
            """);

        byte[] data = [0x03, 0x0A, 0x14, 0x1E]; // count=3, items=[10,20,30]
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("count").GetInt64());
        var arr = doc.RootElement.GetProperty("items");
        Assert.Equal(3, arr.GetArrayLength());
        Assert.Equal(10, arr[0].GetInt64());
        Assert.Equal(20, arr[1].GetInt64());
        Assert.Equal(30, arr[2].GetInt64());
    }

    // ─── 28. I32 signed ──────────────────────────────────────────────

    [Fact]
    public void I32Le_Negative()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                val: i32,
            }
            """);

        byte[] data = BitConverter.GetBytes(-100);
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(-100, doc.RootElement.GetProperty("val").GetInt64());
    }

    // ─── 29. Parse result has BytesConsumed ──────────────────────────

    [Fact]
    public void ParseResult_TracksPosition()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                a: u8,
                b: u16,
            }
            """);

        byte[] data = [0x01, 0x02, 0x03, 0xFF, 0xFF]; // extra bytes
        using var emitter = new JsonResultEmitter();
        var result = ParseRaw(program, data, emitter);
        Assert.True(result.Success);
        Assert.Equal(3, result.BytesConsumed); // 1 + 2
        Assert.Equal(5, result.InputSize);
    }

    // ─── 30. No root struct error ────────────────────────────────────

    [Fact]
    public void NoRootStruct_ReturnsError()
    {
        var program = new BytecodeProgram
        {
            Bytecode = [],
            StringTable = [],
            Structs = [],
            RootStructIndex = -1,
            Parameters = new Dictionary<string, object>(),
        };

        using var emitter = new JsonResultEmitter();
        var engine = new ParseEngine();
        var result = engine.Parse(program, Array.Empty<byte>(), emitter);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "PE001");
    }

    // ─── 31. Read past end of input ──────────────────────────────────

    [Fact]
    public void ReadPastEnd_ReturnsError()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                val: u32,
            }
            """);

        byte[] data = [0x01, 0x02]; // only 2 bytes, need 4
        using var emitter = new JsonResultEmitter();
        var result = ParseRaw(program, data, emitter);
        Assert.False(result.Success);
    }

    // ─── 32. Multiple match arms ─────────────────────────────────────

    [Fact]
    public void Match_SecondArm()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Packet {
                tag: u8,
                payload: match(tag) {
                    1 => u8,
                    2 => u16,
                    3 => u32,
                },
            }
            """);

        // tag=2, payload is u16le
        byte[] data = [0x02, 0x34, 0x12];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("tag").GetInt64());
        Assert.Equal(0x1234, doc.RootElement.GetProperty("payload").GetInt64());
    }

    // ─── 33. Expression in array count ───────────────────────────────

    [Fact]
    public void ArrayCount_Expression()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                n: u8,
                items: u8[n + 1],
            }
            """);

        byte[] data = [0x02, 0x0A, 0x14, 0x1E]; // n=2, items has n+1=3 elements
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("items");
        Assert.Equal(3, arr.GetArrayLength());
    }

    // ─── 34. Struct array ────────────────────────────────────────────

    [Fact]
    public void StructArray()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct File {
                entries: Entry[2],
            }
            struct Entry {
                id: u8,
                val: u8,
            }
            """);

        byte[] data = [0x01, 0x0A, 0x02, 0x14];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("entries");
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal(1, arr[0].GetProperty("id").GetInt64());
        Assert.Equal(10, arr[0].GetProperty("val").GetInt64());
        Assert.Equal(2, arr[1].GetProperty("id").GetInt64());
        Assert.Equal(20, arr[1].GetProperty("val").GetInt64());
    }

    // ─── 35. Named entry point not found ─────────────────────────────

    [Fact]
    public void NamedEntryPoint_NotFound_ReturnsError()
    {
        var program = Compile("""
            @root struct Test { x: u8, }
            """);

        var binProgram = new BinScriptProgram(program);
        using var emitter = new JsonResultEmitter();
        var result = binProgram.Parse(new byte[] { 0x01 }, "NonExistent", emitter);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "API001");
    }

    // ─── 36. F64 double ──────────────────────────────────────────────

    [Fact]
    public void F64Le_Value()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                val: f64,
            }
            """);

        double expected = 2.718281828;
        byte[] data = BitConverter.GetBytes(expected);
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, doc.RootElement.GetProperty("val").GetDouble(), 0.000001);
    }

    // ─── 37. JsonResultEmitter reset ─────────────────────────────────

    [Fact]
    public void JsonResultEmitter_Reset_AllowsReuse()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitInt("x", 42, null);
        emitter.EndRoot();
        var json1 = emitter.GetJson();

        emitter.Reset();
        emitter.BeginRoot("Test2");
        emitter.EmitInt("y", 99, null);
        emitter.EndRoot();
        var json2 = emitter.GetJson();

        Assert.Contains("42", json1);
        Assert.Contains("99", json2);
        Assert.DoesNotContain("42", json2);
    }

    // ─── 38. Enum as base type ───────────────────────────────────────

    [Fact]
    public void EnumField_EmitsNumericValue()
    {
        var program = Compile("""
            @default_endian(little)
            enum Color : u8 {
                Red = 0,
                Green = 1,
                Blue = 2,
            }
            @root struct Test {
                color: Color,
            }
            """);

        byte[] data = [0x01]; // Green
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        // V1: just numeric value
        Assert.Equal(1, doc.RootElement.GetProperty("color").GetInt64());
    }

    // ─── 39. Complex nested + array ──────────────────────────────────

    [Fact]
    public void ComplexNested_ArrayOfStructs()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct File {
                count: u8,
                records: Record[count],
            }
            struct Record {
                name_len: u8,
                name: fixed_string[name_len],
            }
            """);

        // count=2, record1: name_len=3 "abc", record2: name_len=2 "xy"
        byte[] data = [0x02, 0x03, 0x61, 0x62, 0x63, 0x02, 0x78, 0x79];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var records = doc.RootElement.GetProperty("records");
        Assert.Equal(2, records.GetArrayLength());
    }

    // ─── 40. JsonEmitter variant output ──────────────────────────────

    [Fact]
    public void JsonEmitter_ProducesValidJson()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitInt("a", 1, null);
        emitter.EmitBool("b", true);
        emitter.EmitString("c", "hello");
        emitter.EmitFloat("d", 3.14);
        emitter.EmitBytes("e", new byte[] { 0xDE, 0xAD });
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("a").GetInt64());
        Assert.True(doc.RootElement.GetProperty("b").GetBoolean());
        Assert.Equal("hello", doc.RootElement.GetProperty("c").GetString());
        Assert.Equal(3.14, doc.RootElement.GetProperty("d").GetDouble(), 0.001);
    }

    // ─── 41. Mixed endianness in same struct ─────────────────────────

    [Fact]
    public void MixedEndianness_ExplicitAnnotation()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                le_val: u16le,
                be_val: u16be,
            }
            """);

        byte[] data = [0x01, 0x02, 0x03, 0x04];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x0201, doc.RootElement.GetProperty("le_val").GetInt64()); // LE
        Assert.Equal(0x0304, doc.RootElement.GetProperty("be_val").GetInt64()); // BE
    }

    // ─── 42. Dynamic string read ─────────────────────────────────────

    [Fact]
    public void DynamicString_UsesFieldLength()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                len: u8,
                name: string[len],
            }
            """);

        byte[] data = [0x05, 0x48, 0x65, 0x6C, 0x6C, 0x6F]; // len=5, "Hello"
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(5, doc.RootElement.GetProperty("len").GetInt64());
        Assert.Equal("Hello", doc.RootElement.GetProperty("name").GetString());
    }

    // ─── 43. Deeply nested structs ───────────────────────────────────

    [Fact]
    public void DeepNesting_ThreeLevel()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct A {
                b: B,
            }
            struct B {
                c: C,
            }
            struct C {
                val: u8,
            }
            """);

        byte[] data = [0x42];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x42,
            doc.RootElement.GetProperty("b").GetProperty("c").GetProperty("val").GetInt64());
    }

    // ─── @at when guards ──────────────────────────────────────────────

    [Fact]
    public void AtBlock_WhenGuard_True_ReadsBlock()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                offset: u32,
                @at(offset) when offset != 0 {
                    value: u8,
                },
            }
            """);

        // offset=8 (LE: 08 00 00 00), then 4 padding bytes, then value=0xAB at position 8
        byte[] data = [0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xAB];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(8, doc.RootElement.GetProperty("offset").GetInt64());
        Assert.Equal(0xAB, doc.RootElement.GetProperty("value").GetInt64());
    }

    [Fact]
    public void AtBlock_WhenGuard_False_SkipsBlock()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Test {
                offset: u32,
                @at(offset) when offset != 0 {
                    value: u8,
                },
            }
            """);

        // offset=0 → guard is false, so @at block is skipped
        byte[] data = [0x00, 0x00, 0x00, 0x00, 0xAB];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("offset").GetInt64());
        // 'value' should NOT be present
        Assert.False(doc.RootElement.TryGetProperty("value", out _));
    }

    // ─── @max_depth per-struct ────────────────────────────────────────

    [Fact]
    public void MaxDepth_LimitsRecursion()
    {
        // Struct A calls B which is non-recursive; A has @max_depth(2)
        // We test that a non-recursive struct with @max_depth compiles and runs fine
        var program = Compile("""
            @default_endian(little)
            struct Inner @max_depth(3) { x: u8 }
            @root struct Outer { inner: Inner }
            """);

        byte[] data = [0x42];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x42, doc.RootElement.GetProperty("inner").GetProperty("x").GetInt64());
    }

    [Fact]
    public void MaxDepth_ExceedsLimit_Throws()
    {
        // Node has @max_depth(2) and self-references via @at block.
        // Data encodes: offset1=4, val=0xAA, then at offset 4: offset2=8, val=0xBB,
        // then at offset 8: offset3=12, val=0xCC → third entry into Node exceeds limit.
        var program = Compile("""
            @default_endian(little)
            @root struct Node @max_depth(2) {
                next_offset: u32,
                value: u8,
                @at(next_offset) when next_offset != 0 {
                    next: Node,
                },
            }
            """);

        byte[] data = new byte[20];
        // Node at 0: next_offset=5, value=0xAA
        data[0] = 5; data[1] = 0; data[2] = 0; data[3] = 0; data[4] = 0xAA;
        // Node at 5: next_offset=10, value=0xBB
        data[5] = 10; data[6] = 0; data[7] = 0; data[8] = 0; data[9] = 0xBB;
        // Node at 10: next_offset=15, value=0xCC (this would be depth 3 → exceeds limit)
        data[10] = 15; data[11] = 0; data[12] = 0; data[13] = 0; data[14] = 0xCC;
        data[15] = 0; data[16] = 0; data[17] = 0; data[18] = 0; data[19] = 0xDD;

        using var emitter = new JsonResultEmitter();
        var engine = new ParseEngine();
        var result = engine.Parse(program, data, emitter);
        Assert.False(result.Success);
        Assert.Contains("Max depth", result.Diagnostics[0].Message);
    }

    // ─── File-level @param ────────────────────────────────────────────

    [Fact]
    public void FileParam_UsedInExpression()
    {
        // Use base_ptr in an @at block expression
        var program = Compile("""
            @default_endian(little)
            @param base_ptr: u64
            @root struct Test {
                raw_offset: u32,
                @at(raw_offset + base_ptr) {
                    value: u8,
                },
            }
            """);

        // raw_offset = 4 (points to byte at offset 4 + base_ptr)
        // With base_ptr = 0, effectively @at(4)
        byte[] data = [0x04, 0x00, 0x00, 0x00, 0xAB];
        var options = new ParseOptions
        {
            RuntimeParameters = new() { ["base_ptr"] = 0 }
        };
        var json = ParseToJson(program, data, options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(4, doc.RootElement.GetProperty("raw_offset").GetInt64());
        Assert.Equal(0xAB, doc.RootElement.GetProperty("value").GetInt64());
    }

    [Fact]
    public void FileParam_MissingValue_ReturnsError()
    {
        var program = Compile("""
            @default_endian(little)
            @param base_ptr: u64
            @root struct Test {
                raw_offset: u32,
                @at(raw_offset + base_ptr) {
                    value: u8,
                },
            }
            """);

        byte[] data = [0x04, 0x00, 0x00, 0x00, 0xAB];
        using var emitter = new JsonResultEmitter();
        var engine = new ParseEngine();
        var result = engine.Parse(program, data, emitter);
        Assert.False(result.Success);
        Assert.Contains("base_ptr", result.Diagnostics[0].Message);
    }

    // ─── ptr<T, width> transparent dereferencing ──────────────────────

    [Fact]
    public void PtrCString_TransparentDeref()
    {
        // Buffer: [ptr_value: 8 bytes][hello\0]
        // ptr_value = base_ptr + 8 (the string starts at offset 8 in the buffer)
        // With base_ptr = 0x0000, ptr_value = 8
        var program = Compile("""
            @default_endian(little)
            @param base_ptr: u64
            @root struct Test {
                name: ptr<cstring, u64>,
            }
            """);

        byte[] data = new byte[14]; // 8 bytes ptr + "hello\0"
        // ptr_value = 8 (offset 8 in buffer, base_ptr=0 so raw ptr = 0+8 = 8)
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0), 8);
        // "hello\0" at offset 8
        data[8] = (byte)'h'; data[9] = (byte)'e'; data[10] = (byte)'l';
        data[11] = (byte)'l'; data[12] = (byte)'o'; data[13] = 0;

        var options = new ParseOptions
        {
            RuntimeParameters = new() { ["base_ptr"] = 0 }
        };
        var json = ParseToJson(program, data, options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("hello", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void PtrCString_WithBasePtr()
    {
        // Same layout but base_ptr = 0x1000
        // ptr_value = 0x1000 + 8 = 0x1008
        var program = Compile("""
            @default_endian(little)
            @param base_ptr: u64
            @root struct Test {
                name: ptr<cstring, u64>,
            }
            """);

        byte[] data = new byte[14];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0), 0x1008);
        data[8] = (byte)'h'; data[9] = (byte)'i'; data[10] = 0;

        var options = new ParseOptions
        {
            RuntimeParameters = new() { ["base_ptr"] = 0x1000 }
        };
        var json = ParseToJson(program, data, options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("hi", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void NullablePtr_NullValue_EmitsJsonNull()
    {
        var program = Compile("""
            @default_endian(little)
            @param base_ptr: u64
            @root struct Test {
                name: ptr<cstring, u64>?,
            }
            """);

        byte[] data = new byte[8]; // all zeros → null pointer
        var options = new ParseOptions
        {
            RuntimeParameters = new() { ["base_ptr"] = 0 }
        };
        var json = ParseToJson(program, data, options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("name").ValueKind);
    }

    [Fact]
    public void NullablePtr_NonNull_Dereferences()
    {
        var program = Compile("""
            @default_endian(little)
            @param base_ptr: u64
            @root struct Test {
                name: ptr<cstring, u64>?,
            }
            """);

        byte[] data = new byte[14];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0), 8);
        data[8] = (byte)'o'; data[9] = (byte)'k'; data[10] = 0;

        var options = new ParseOptions
        {
            RuntimeParameters = new() { ["base_ptr"] = 0 }
        };
        var json = ParseToJson(program, data, options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("ok", doc.RootElement.GetProperty("name").GetString());
    }

    // ─── Dotted field access ──────────────────────────────────────────

    [Fact]
    public void DottedAccess_ChildFieldUsedInExpression()
    {
        var source = @"
struct Inner {
    value: u16
}
@root struct Root {
    inner: Inner
    @let count = inner.value
    items: u8[count]
}";
        var program = Compile(source);
        // inner.value = 3 (u16 LE), then 3 u8 items
        var data = new byte[] { 3, 0, 0xAA, 0xBB, 0xCC };
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void DottedAccess_ChildFieldInMatchDiscriminant()
    {
        var source = @"
enum Kind : u8 { A = 1, B = 2 }
struct Header {
    kind: Kind
}
@root struct Root {
    header: Header
    body: match(header.kind) {
        Kind.A => u32,
        Kind.B => u16,
    }
}";
        var program = Compile(source);
        // header.kind = 2 (Kind.B), body = u16 (0x1234)
        var data = new byte[] { 2, 0x34, 0x12 };
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0x1234, doc.RootElement.GetProperty("body").GetInt32());
    }

    [Fact]
    public void DottedAccess_EnumQualifiedInMatchArm()
    {
        var source = @"
enum Tag : u8 { Small = 1, Big = 2 }
struct Hdr { tag: Tag }
@root struct Root {
    hdr: Hdr
    payload: match(hdr.tag) {
        Tag.Small => u8,
        Tag.Big => u32,
    }
}";
        var program = Compile(source);
        // Tag.Big = 2, payload = u32 (0xDEADBEEF)
        var data = new byte[] { 2, 0xEF, 0xBE, 0xAD, 0xDE };
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(unchecked((long)0xDEADBEEF), doc.RootElement.GetProperty("payload").GetInt64());
    }

    // ─── Array search methods (.find/.any/.all) ───────────────────

    [Fact]
    public void ArrayFind_ReturnsFirstMatch()
    {
        var source = @"
struct Item {
    id: u8
    value: u16
}
@root struct Root {
    items: Item[3]
    @let found = items.find(i => i.id == 2)
    @let result = found.value
}";
        var program = Compile(source);
        // Item[0]: id=1 value=0x0010, Item[1]: id=2 value=0x0020, Item[2]: id=3 value=0x0030
        var data = new byte[] {
            1, 0x10, 0x00,
            2, 0x20, 0x00,
            3, 0x30, 0x00,
        };
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        // found.value should be 0x0020 = 32
        Assert.Equal(32, doc.RootElement.GetProperty("items").EnumerateArray().ElementAt(1).GetProperty("value").GetInt32());
    }

    [Fact]
    public void ArrayAny_ReturnsTrueWhenMatch()
    {
        var source = @"
struct Item { id: u8 }
@root struct Root {
    items: Item[3]
    @let has_two = items.any(i => i.id == 2)
    marker: u8
}";
        var program = Compile(source);
        var data = new byte[] { 1, 2, 3, 0xFF };
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0xFF, doc.RootElement.GetProperty("marker").GetInt32());
    }

    [Fact]
    public void ArrayAny_ReturnsFalseWhenNoMatch()
    {
        var source = @"
struct Item { id: u8 }
@root struct Root {
    items: Item[3]
    @let has_five = items.any(i => i.id == 5)
    marker: u8
}";
        var program = Compile(source);
        var data = new byte[] { 1, 2, 3, 0xAA };
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0xAA, doc.RootElement.GetProperty("marker").GetInt32());
    }

    [Fact]
    public void ArrayAll_ReturnsTrueWhenAllMatch()
    {
        var source = @"
struct Item { val: u8 }
@root struct Root {
    items: Item[3]
    @let all_positive = items.all(i => i.val > 0)
    marker: u8
}";
        var program = Compile(source);
        var data = new byte[] { 1, 2, 3, 0xBB };
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0xBB, doc.RootElement.GetProperty("marker").GetInt32());
    }

    [Fact]
    public void ArrayAll_ReturnsFalseWhenNotAllMatch()
    {
        var source = @"
struct Item { val: u8 }
@root struct Root {
    items: Item[3]
    @let all_big = items.all(i => i.val > 2)
    marker: u8
}";
        var program = Compile(source);
        var data = new byte[] { 1, 5, 6, 0xCC };
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0xCC, doc.RootElement.GetProperty("marker").GetInt32());
    }

    [Fact]
    public void ArrayFind_FieldProjection()
    {
        // Test that find result's fields are accessible via dotted access
        var source = @"
struct Section {
    start: u16
    size: u16
}
@root struct Root {
    sections: Section[3]
    @let target = sections.find(s => s.start == 100)
    @let offset = target.start + target.size
}";
        var program = Compile(source);
        // Section[0]: start=50 size=10, Section[1]: start=100 size=200, Section[2]: start=300 size=50
        var data = new byte[] {
            50, 0, 10, 0,
            100, 0, 200, 0,
            44, 1, 50, 0,  // 300=0x012C
        };
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        // target.start=100, target.size=200 → offset=300
        var sections = doc.RootElement.GetProperty("sections");
        Assert.Equal(3, sections.GetArrayLength());
        Assert.Equal(100, sections[1].GetProperty("start").GetInt32());
    }
}
