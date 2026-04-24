namespace BinScript.Tests.Runtime;

using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Core.Bytecode;
using BinScript.Core.Model;
using BinScript.Core.Runtime;
using BinScript.Emitters.Json;

public class ProduceEngineTests
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

    private static byte[] ProduceFromJson(BytecodeProgram program, string json)
    {
        var p = new BinScriptProgram(program);
        return p.FromJson(json);
    }

    private static string FormatDiags(IReadOnlyList<Diagnostic> diags) =>
        string.Join("\n", diags.Select(d => $"[{d.Severity}] {d.Code}: {d.Message}"));

    // ─── 1. Flat struct produce ──────────────────────────────────────

    [Fact]
    public void FlatStruct_U8_U16le()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Header {
                a: u8,
                b: u16,
            }
            """);

        string json = """{"a":66,"b":258}""";
        byte[] produced = ProduceFromJson(program, json);

        Assert.Equal(3, produced.Length);
        Assert.Equal(66, produced[0]);     // a = 0x42
        Assert.Equal(0x02, produced[1]);   // b = 258 = 0x0102 LE
        Assert.Equal(0x01, produced[2]);
    }

    // ─── 2. Endianness ──────────────────────────────────────────────

    [Fact]
    public void Endianness_U32Be()
    {
        var program = Compile("""
            @default_endian(big)
            @root struct Header {
                value: u32,
            }
            """);

        string json = """{"value":16909060}"""; // 0x01020304
        byte[] produced = ProduceFromJson(program, json);

        Assert.Equal(4, produced.Length);
        Assert.Equal([0x01, 0x02, 0x03, 0x04], produced);
    }

    // ─── 3. CString produce ─────────────────────────────────────────

    [Fact]
    public void CString_WriteWithNullTerminator()
    {
        var program = Compile("""
            @root struct Test {
                name: cstring,
            }
            """);

        string json = """{"name":"Hi"}""";
        byte[] produced = ProduceFromJson(program, json);

        Assert.Equal(3, produced.Length);
        Assert.Equal((byte)'H', produced[0]);
        Assert.Equal((byte)'i', produced[1]);
        Assert.Equal(0, produced[2]); // null terminator
    }

    // ─── 4. Bytes produce ───────────────────────────────────────────

    [Fact]
    public void BytesFixed_WritesRawBytes()
    {
        var program = Compile("""
            @root struct Test {
                data: bytes[4],
            }
            """);

        // base64 of [0xDE, 0xAD, 0xBE, 0xEF] = "3q2+7w=="
        string json = """{"data":"3q2+7w=="}""";
        byte[] produced = ProduceFromJson(program, json);

        Assert.Equal(4, produced.Length);
        Assert.Equal([0xDE, 0xAD, 0xBE, 0xEF], produced);
    }

    // ─── 5. Magic values ────────────────────────────────────────────

    [Fact]
    public void MagicValue_WrittenAutomatically()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Header {
                magic: u16 = 0x4D5A,
                version: u8,
            }
            """);

        // magic has a constant assertion — the data source provides the value;
        // AssertValue simply validates. So the JSON must contain the magic value.
        string json = """{"magic":19802,"version":1}"""; // 0x4D5A = 19802
        byte[] produced = ProduceFromJson(program, json);

        Assert.Equal(3, produced.Length);
        Assert.Equal(0x5A, produced[0]); // LE
        Assert.Equal(0x4D, produced[1]);
        Assert.Equal(0x01, produced[2]);
    }

    // ─── 6. Nested struct ───────────────────────────────────────────

    [Fact]
    public void NestedStruct_WritesCorrectly()
    {
        var program = Compile("""
            @default_endian(little)
            struct Inner { x: u8, y: u8 }
            @root struct Outer {
                header: Inner,
                value: u16,
            }
            """);

        string json = """{"header":{"x":10,"y":20},"value":300}""";
        byte[] produced = ProduceFromJson(program, json);

        Assert.Equal(4, produced.Length);
        Assert.Equal(10, produced[0]);
        Assert.Equal(20, produced[1]);
        Assert.Equal(0x2C, produced[2]); // 300 = 0x012C LE
        Assert.Equal(0x01, produced[3]);
    }

    // ─── 7. Count array ─────────────────────────────────────────────

    [Fact]
    public void CountArray_WritesElements()
    {
        var program = Compile("""
            @default_endian(little)
            @root struct Data {
                count: u8,
                items: u16[count],
            }
            """);

        // count=2, items=[0x0100, 0x0200]
        string json = """{"count":2,"items":[256,512]}""";
        byte[] produced = ProduceFromJson(program, json);

        Assert.Equal(5, produced.Length);
        Assert.Equal(2, produced[0]);
        Assert.Equal(0x00, produced[1]); // 256 LE
        Assert.Equal(0x01, produced[2]);
        Assert.Equal(0x00, produced[3]); // 512 LE
        Assert.Equal(0x02, produced[4]);
    }

    // ─── 8. @skip padding ───────────────────────────────────────────

    [Fact]
    public void Skip_WritesZeroPadding()
    {
        var program = Compile("""
            @root struct Test {
                a: u8,
                @skip(2),
                b: u8,
            }
            """);

        string json = """{"a":255,"b":128}""";
        byte[] produced = ProduceFromJson(program, json);

        Assert.Equal(4, produced.Length);
        Assert.Equal(255, produced[0]);
        Assert.Equal(0, produced[1]); // padding
        Assert.Equal(0, produced[2]); // padding
        Assert.Equal(128, produced[3]);
    }

    // ─── 9. @align padding ──────────────────────────────────────────

    [Fact]
    public void AlignFixed_WritesZeroPadding()
    {
        var program = Compile("""
            @root struct Test {
                a: u8,
                @align(4),
                b: u8,
            }
            """);

        string json = """{"a":1,"b":2}""";
        byte[] produced = ProduceFromJson(program, json);

        Assert.Equal(5, produced.Length);
        Assert.Equal(1, produced[0]);
        Assert.Equal(0, produced[1]); // padding
        Assert.Equal(0, produced[2]); // padding
        Assert.Equal(0, produced[3]); // padding
        Assert.Equal(2, produced[4]);
    }
}
