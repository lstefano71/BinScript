namespace BinScript.Tests.Runtime;

using BinScript.Core.Api;
using BinScript.Core.Bytecode;
using BinScript.Core.Model;
using BinScript.Emitters.Json;

public class RoundTripTests
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

    private static string FormatDiags(IReadOnlyList<Diagnostic> diags) =>
        string.Join("\n", diags.Select(d => $"[{d.Severity}] {d.Code}: {d.Message}"));

    private static byte[] RoundTrip(BinScriptProgram program, byte[] original)
    {
        string json = program.ToJson(original);
        return program.FromJson(json);
    }

    // ─── 1. Flat struct round-trip ───────────────────────────────────

    [Fact]
    public void RoundTrip_FlatStruct()
    {
        var bytecode = Compile("""
            @default_endian(little)
            @root struct Header {
                magic: u16,
                version: u8,
                size: u32,
            }
            """);
        var program = new BinScriptProgram(bytecode);

        byte[] original = [0x4D, 0x5A, 0x01, 0x10, 0x00, 0x00, 0x00];
        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    // ─── 2. Nested struct round-trip ─────────────────────────────────

    [Fact]
    public void RoundTrip_NestedStruct()
    {
        var bytecode = Compile("""
            @default_endian(little)
            struct Point { x: u16, y: u16 }
            @root struct Shape {
                origin: Point,
                color: u8,
            }
            """);
        var program = new BinScriptProgram(bytecode);

        byte[] original = [0x0A, 0x00, 0x14, 0x00, 0xFF];
        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    // ─── 3. Array round-trip ─────────────────────────────────────────

    [Fact]
    public void RoundTrip_Array()
    {
        var bytecode = Compile("""
            @default_endian(little)
            @root struct Data {
                count: u8,
                items: u16[count],
            }
            """);
        var program = new BinScriptProgram(bytecode);

        byte[] original = [0x03, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00];
        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    // ─── 4. String round-trip ────────────────────────────────────────

    [Fact]
    public void RoundTrip_FixedString()
    {
        var bytecode = Compile("""
            @root struct Test {
                name: fixed_string[5],
            }
            """);
        var program = new BinScriptProgram(bytecode);

        byte[] original = [0x48, 0x65, 0x6C, 0x6C, 0x6F]; // "Hello"
        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    [Fact]
    public void RoundTrip_CString()
    {
        var bytecode = Compile("""
            @root struct Test {
                greeting: cstring,
            }
            """);
        var program = new BinScriptProgram(bytecode);

        byte[] original = [0x48, 0x69, 0x00]; // "Hi\0"
        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    // ─── 5. Mixed types (header with magic, length, array) ───────────

    [Fact]
    public void RoundTrip_MixedTypes()
    {
        var bytecode = Compile("""
            @default_endian(little)
            struct Record { id: u8, value: u16 }
            @root struct File {
                magic: u16 = 0xCAFE,
                count: u8,
                records: Record[count],
            }
            """);
        var program = new BinScriptProgram(bytecode);

        byte[] original =
        [
            0xFE, 0xCA,          // magic = 0xCAFE LE
            0x02,                 // count = 2
            0x01, 0x10, 0x00,    // record[0]: id=1, value=16
            0x02, 0x20, 0x00,    // record[1]: id=2, value=32
        ];

        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    // ─── 6. Endianness round-trip ────────────────────────────────────

    [Fact]
    public void RoundTrip_BigEndian()
    {
        var bytecode = Compile("""
            @default_endian(big)
            @root struct Header {
                tag: u32,
                length: u16,
            }
            """);
        var program = new BinScriptProgram(bytecode);

        byte[] original = [0x89, 0x50, 0x4E, 0x47, 0x00, 0x0D];
        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    // ─── 7. Bool round-trip ──────────────────────────────────────────

    [Fact]
    public void RoundTrip_Bool()
    {
        var bytecode = Compile("""
            @root struct Flags {
                active: bool,
                visible: bool,
            }
            """);
        var program = new BinScriptProgram(bytecode);

        byte[] original = [0x01, 0x00];
        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    // ─── 8. Bytes round-trip ─────────────────────────────────────────

    [Fact]
    public void RoundTrip_BytesFixed()
    {
        var bytecode = Compile("""
            @root struct Test {
                payload: bytes[4],
            }
            """);
        var program = new BinScriptProgram(bytecode);

        byte[] original = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    // ─── 9. Skip round-trip ──────────────────────────────────────────

    [Fact]
    public void RoundTrip_Skip()
    {
        var bytecode = Compile("""
            @root struct Test {
                a: u8,
                @skip(3),
                b: u8,
            }
            """);
        var program = new BinScriptProgram(bytecode);

        byte[] original = [0xFF, 0x00, 0x00, 0x00, 0x42];
        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    // ─── 10. Signed integers round-trip ──────────────────────────────

    [Fact]
    public void RoundTrip_SignedIntegers()
    {
        var bytecode = Compile("""
            @default_endian(little)
            @root struct Test {
                a: i8,
                b: i16,
                c: i32,
            }
            """);
        var program = new BinScriptProgram(bytecode);

        // a=-1 (0xFF), b=-256 (0xFF00 LE: 0x00 0xFF), c=-1 (0xFFFFFFFF)
        byte[] original = [0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
        byte[] produced = RoundTrip(program, original);

        Assert.Equal(original, produced);
    }

    // ─── Guard arm round-trip ────────────────────────────────────────

    [Fact]
    public void RoundTrip_MatchGuardArm()
    {
        var bytecode = Compile("""
            @root struct Data {
                version: u8,
                flags: u8,
                body: match(version) {
                    1 when flags > 0 => u32le,
                    1 => u16le,
                    _ => u8,
                }
            }
            """);
        var program = new BinScriptProgram(bytecode);

        // version=1, flags=5 → guarded arm → u32le
        byte[] original = [0x01, 0x05, 0x78, 0x56, 0x34, 0x12];
        byte[] produced = RoundTrip(program, original);
        Assert.Equal(original, produced);
    }

    [Fact]
    public void RoundTrip_MatchGuardArmFallthrough()
    {
        var bytecode = Compile("""
            @root struct Data {
                version: u8,
                flags: u8,
                body: match(version) {
                    1 when flags > 0 => u32le,
                    1 => u16le,
                    _ => u8,
                }
            }
            """);
        var program = new BinScriptProgram(bytecode);

        // version=1, flags=0 → guard fails → plain value match → u16le
        byte[] original = [0x01, 0x00, 0x34, 0x12];
        byte[] produced = RoundTrip(program, original);
        Assert.Equal(original, produced);
    }
}
