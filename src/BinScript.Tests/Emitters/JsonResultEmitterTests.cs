namespace BinScript.Tests.Emitters;

using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Core.Bytecode;
using BinScript.Core.Model;
using BinScript.Core.Runtime;
using BinScript.Emitters.Json;

public class JsonResultEmitterTests
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

    private static string FormatDiags(IReadOnlyList<Diagnostic> diags) =>
        string.Join("\n", diags.Select(d => $"[{d.Severity}] {d.Code}: {d.Message}"));

    // ─── 1. Flat struct with integer fields ──────────────────────────

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
        var root = doc.RootElement;

        Assert.Equal(0x42, root.GetProperty("a").GetInt64());
        Assert.Equal(0x0102, root.GetProperty("b").GetInt64());
        Assert.Equal(0x04030201, root.GetProperty("c").GetInt64());
    }

    [Fact]
    public void FlatStruct_SignedIntegers()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Signed {
                a: i8,
                b: i16,
                c: i32,
            }
            """);

        // a=-1 (0xFF), b=-2 (LE: FE FF), c=-3 (LE: FD FF FF FF)
        byte[] data = [0xFF, 0xFE, 0xFF, 0xFD, 0xFF, 0xFF, 0xFF];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(-1, root.GetProperty("a").GetInt64());
        Assert.Equal(-2, root.GetProperty("b").GetInt64());
        Assert.Equal(-3, root.GetProperty("c").GetInt64());
    }

    // ─── 2. Nested struct ────────────────────────────────────────────

    [Fact]
    public void NestedStruct_EmitsNestedJson()
    {
        var program = Compile("""
            @default_endian(little)

            struct Inner {
                x: u8,
                y: u8,
            }

            @root struct Outer {
                tag: u8,
                inner: Inner,
            }
            """);

        byte[] data = [0x01, 0x0A, 0x0B];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(1, root.GetProperty("tag").GetInt64());
        var inner = root.GetProperty("inner");
        Assert.Equal(JsonValueKind.Object, inner.ValueKind);
        Assert.Equal(0x0A, inner.GetProperty("x").GetInt64());
        Assert.Equal(0x0B, inner.GetProperty("y").GetInt64());
    }

    // ─── 3. Array field ──────────────────────────────────────────────

    [Fact]
    public void ArrayField_EmitsJsonArray()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Data {
                count: u8,
                items: u16[count],
            }
            """);

        // count=3, items=[0x0001, 0x0002, 0x0003] in LE
        byte[] data = [0x03, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(3, root.GetProperty("count").GetInt64());
        var items = root.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal(1, items[0].GetInt64());
        Assert.Equal(2, items[1].GetInt64());
        Assert.Equal(3, items[2].GetInt64());
    }

    [Fact]
    public void ArrayOfStructs_EmitsObjectArray()
    {
        var program = Compile("""
            @default_endian(little)

            struct Point {
                x: u8,
                y: u8,
            }

            @root struct Data {
                count: u8,
                points: Point[count],
            }
            """);

        // count=2, points=[{x=1,y=2},{x=3,y=4}]
        byte[] data = [0x02, 0x01, 0x02, 0x03, 0x04];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var points = doc.RootElement.GetProperty("points");

        Assert.Equal(2, points.GetArrayLength());
        Assert.Equal(1, points[0].GetProperty("x").GetInt64());
        Assert.Equal(2, points[0].GetProperty("y").GetInt64());
        Assert.Equal(3, points[1].GetProperty("x").GetInt64());
        Assert.Equal(4, points[1].GetProperty("y").GetInt64());
    }

    // ─── 4. String fields ────────────────────────────────────────────

    [Fact]
    public void CString_EmitsJsonString()
    {
        var program = Compile("""
            @root struct Data {
                name: cstring,
            }
            """);

        byte[] data = [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00]; // "Hello\0"
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("Hello", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void FixedString_EmitsJsonString()
    {
        var program = Compile("""
            @root struct Data {
                tag: fixed_string[4],
            }
            """);

        byte[] data = [0x54, 0x45, 0x53, 0x54]; // "TEST"
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("TEST", doc.RootElement.GetProperty("tag").GetString());
    }

    // ─── 5. Enum field ───────────────────────────────────────────────

    [Fact]
    public void Enum_EmitsNumericValue()
    {
        var program = Compile("""
            @default_endian(little)

            enum Color : u8 {
                Red = 0,
                Green = 1,
                Blue = 2,
            }

            @root struct Data {
                color: Color,
            }
            """);

        byte[] data = [0x01]; // Green
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);

        // V1: enum fields emit numeric values
        Assert.Equal(1, doc.RootElement.GetProperty("color").GetInt64());
    }

    [Fact]
    public void Enum_UnknownValue_EmitsNumber()
    {
        var program = Compile("""
            @default_endian(little)

            enum Color : u8 {
                Red = 1,
                Green = 2,
            }

            @root struct Data {
                color: Color,
            }
            """);

        byte[] data = [0xFF]; // unknown
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("color").ValueKind);
        Assert.Equal(255, doc.RootElement.GetProperty("color").GetInt64());
    }

    // ─── 6. Boolean field ────────────────────────────────────────────

    [Fact]
    public void Bool_EmitsTrueAndFalse()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Data {
                flag_on: bool,
                flag_off: bool,
            }
            """);

        byte[] data = [0x01, 0x00]; // true, false
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("flag_on").GetBoolean());
        Assert.False(root.GetProperty("flag_off").GetBoolean());
    }

    // ─── 7. Float fields ─────────────────────────────────────────────

    [Fact]
    public void Float32_EmitsJsonNumber()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Data {
                value: f32,
            }
            """);

        // float 3.14 in LE
        var buf = new byte[4];
        BitConverter.TryWriteBytes(buf, 3.14f);
        var json = ParseToJson(program, buf);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(3.14f, (float)doc.RootElement.GetProperty("value").GetDouble(), 0.001f);
    }

    [Fact]
    public void Float64_EmitsJsonNumber()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Data {
                value: f64,
            }
            """);

        var buf = new byte[8];
        BitConverter.TryWriteBytes(buf, 2.71828);
        var json = ParseToJson(program, buf);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(2.71828, doc.RootElement.GetProperty("value").GetDouble(), 5);
    }

    // ─── 8. Bits struct ──────────────────────────────────────────────

    [Fact]
    public void BitsStruct_EmitsBooleans()
    {
        var program = Compile("""
            @default_endian(little)

            bits struct Flags : u8 {
                read: bit,
                write: bit,
                execute: bit,
            }

            @root struct Data {
                flags: Flags,
            }
            """);

        // 0b00000101 = bit0=1 (read), bit1=0 (write), bit2=1 (execute)
        byte[] data = [0x05];
        var json = ParseToJson(program, data);
        using var doc = JsonDocument.Parse(json);
        var flags = doc.RootElement.GetProperty("flags");

        Assert.Equal(JsonValueKind.Object, flags.ValueKind);
        Assert.True(flags.GetProperty("read").GetBoolean());
        Assert.False(flags.GetProperty("write").GetBoolean());
        Assert.True(flags.GetProperty("execute").GetBoolean());
    }

    // ─── 9. Direct emitter API tests ─────────────────────────────────

    [Fact]
    public void EmitInt_WithEnumName_WritesString()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitInt("color", 2, "Green");
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Green", doc.RootElement.GetProperty("color").GetString());
    }

    [Fact]
    public void EmitInt_WithoutEnumName_WritesNumber()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitInt("value", 42, null);
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(42, doc.RootElement.GetProperty("value").GetInt64());
    }

    [Fact]
    public void EmitUInt_WithEnumName_WritesString()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitUInt("kind", 1, "FileType");
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("FileType", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void EmitUInt_WithoutEnumName_WritesNumber()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitUInt("size", 1234567890UL, null);
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1234567890UL, doc.RootElement.GetProperty("size").GetUInt64());
    }

    [Fact]
    public void EmitBytes_WritesBase64()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitBytes("data", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        var bytes = doc.RootElement.GetProperty("data").GetBytesFromBase64();
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, bytes);
    }

    [Fact]
    public void EmitNull_WritesJsonNull()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitNull("missing");
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("missing").ValueKind);
    }

    [Fact]
    public void ArrayOfIntegers_InArray_NoPropertyNames()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.BeginArray("values", 3);
        emitter.EmitInt("", 10, null);
        emitter.EmitInt("", 20, null);
        emitter.EmitInt("", 30, null);
        emitter.EndArray();
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement.GetProperty("values");
        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(3, arr.GetArrayLength());
        Assert.Equal(10, arr[0].GetInt64());
        Assert.Equal(20, arr[1].GetInt64());
        Assert.Equal(30, arr[2].GetInt64());
    }

    [Fact]
    public void BeginVariant_WritesVariantTag()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.BeginVariant("payload", "TypeA");
        emitter.EmitInt("x", 1, null);
        emitter.EndVariant();
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        var payload = doc.RootElement.GetProperty("payload");
        Assert.Equal("TypeA", payload.GetProperty("_variant").GetString());
        Assert.Equal(1, payload.GetProperty("x").GetInt64());
    }

    [Fact]
    public void EmitBits_WritesNumber()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.BeginBitsStruct("flags", "Flags");
        emitter.EmitBits("nibble", 0x0F, 4);
        emitter.EndBitsStruct();
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(15, doc.RootElement.GetProperty("flags").GetProperty("nibble").GetInt64());
    }

    // ─── 10. Checkpoint / Rollback ───────────────────────────────────

    [Fact]
    public void SaveCheckpoint_And_Rollback_TruncatesOutput()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitInt("keep", 1, null);
        var cp = emitter.SaveCheckpoint();
        emitter.EmitInt("discard", 2, null);
        emitter.RollbackToCheckpoint(cp);
        emitter.EndRoot();

        var json = emitter.GetJson();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("keep").GetInt64());
        Assert.False(doc.RootElement.TryGetProperty("discard", out _));
    }

    // ─── 11. Reset ───────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsOutputForReuse()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitInt("x", 1, null);
        emitter.EndRoot();

        var first = emitter.GetJson();
        Assert.Contains("\"x\"", first);

        emitter.Reset();
        emitter.BeginRoot("Test");
        emitter.EmitInt("y", 2, null);
        emitter.EndRoot();

        var second = emitter.GetJson();
        Assert.DoesNotContain("\"x\"", second);
        Assert.Contains("\"y\"", second);
    }

    // ─── 12. GetUtf8Json ─────────────────────────────────────────────

    [Fact]
    public void GetUtf8Json_ReturnsSameContentAsGetJson()
    {
        using var emitter = new JsonResultEmitter();
        emitter.BeginRoot("Test");
        emitter.EmitString("msg", "hello");
        emitter.EndRoot();

        var jsonStr = emitter.GetJson();
        var utf8Bytes = emitter.GetUtf8Json();
        var fromUtf8 = System.Text.Encoding.UTF8.GetString(utf8Bytes.Span);

        Assert.Equal(jsonStr, fromUtf8);
    }
}
