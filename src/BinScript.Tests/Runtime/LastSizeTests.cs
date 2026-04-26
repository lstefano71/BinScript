namespace BinScript.Tests.Runtime;

using System.Text;
using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Core.Bytecode;
using BinScript.Core.Runtime;
using BinScript.Emitters.Json;

public class LastSizeTests
{
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

    private static JsonElement ParseToElement(BytecodeProgram program, byte[] data)
    {
        var json = ParseToJson(program, data);
        return JsonDocument.Parse(json).RootElement;
    }

    private static string FormatDiags(IReadOnlyList<BinScript.Core.Model.Diagnostic> diags)
        => string.Join("\n", diags.Select(d => $"[{d.Severity}] {d.Message} at {d.Span}"));

    // @last_size measures bytes consumed (position delta) per array element.
    // For cstring, an empty string still consumes 1 byte (the null terminator),
    // so the PCZZSTR (00T) termination condition is @last_size == 1, not 0.

    [Fact]
    public void CStringArray_UntilLastSizeOne_TerminatesOnEmptyString()
    {
        var source = """
            @root struct Strings {
                items: cstring[] @until(@last_size == 1)
            }
            """;
        var program = Compile(source);

        // "hello\0world\0\0" — two strings plus the empty terminator
        var data = Encoding.UTF8.GetBytes("hello\0world\0\0");
        var root = ParseToElement(program, data);
        var items = root.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        // "hello" (6 bytes), "world" (6 bytes), "" (1 byte → terminates)
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("hello", items[0].GetString());
        Assert.Equal("world", items[1].GetString());
        Assert.Equal("", items[2].GetString());
    }

    [Fact]
    public void CStringArray_UntilLastSizeOne_SingleEmptyString()
    {
        var source = """
            @root struct Strings {
                items: cstring[] @until(@last_size == 1)
            }
            """;
        var program = Compile(source);

        // Just a single \0 — empty cstring immediately
        var data = new byte[] { 0 };
        var root = ParseToElement(program, data);
        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("", items[0].GetString());
    }

    [Fact]
    public void LastSize_FixedU8Elements_ReportsOne()
    {
        // u8 elements → each consumes 1 byte
        var source = """
            @root struct Data {
                items: u8[3]
            }
            """;
        var program = Compile(source);

        var data = new byte[] { 10, 20, 30 };
        var root = ParseToElement(program, data);
        Assert.Equal(3, root.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void LastSize_FixedU32Elements_ReportsFour()
    {
        // u32le elements → each consumes 4 bytes
        var source = """
            @root struct Data {
                items: u32le[] @until(@remaining == 0)
            }
            """;
        var program = Compile(source);

        var data = new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0 };
        var root = ParseToElement(program, data);
        Assert.Equal(3, root.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void LastSize_OutsideArray_ReturnsZero()
    {
        var source = """
            @root struct Data {
                @derived ls: u64 = @last_size,
                value: u8
            }
            """;
        var program = Compile(source);

        var data = new byte[] { 42 };
        var root = ParseToElement(program, data);
        // No active array → @last_size should be 0
        Assert.Equal(0, root.GetProperty("ls").GetInt64());
    }

    [Fact]
    public void LastSize_VariableSizeStructElements_TracksCorrectly()
    {
        // Struct with a length-prefixed payload — different sizes per element
        var source = """
            struct Record {
                len: u8,
                payload: u8[len]
            }
            @root struct Data {
                records: Record[] @until(@remaining == 0)
            }
            """;
        var program = Compile(source);

        // Record 1: len=2, payload=[1,2] → 3 bytes consumed
        // Record 2: len=1, payload=[3]   → 2 bytes consumed
        var data = new byte[] { 2, 1, 2, 1, 3 };
        var root = ParseToElement(program, data);
        Assert.Equal(2, root.GetProperty("records").GetArrayLength());
    }

    [Fact]
    public void LastSize_UsedInUntilCondition_TerminatesOnSmallElement()
    {
        // Array of variable-length records, terminate when element is just 1 byte
        // (a Record with len=0 consumes exactly 1 byte — just the len field)
        var source = """
            struct Entry {
                len: u8,
                data: u8[len]
            }
            @root struct Data {
                entries: Entry[] @until(@last_size == 1)
            }
            """;
        var program = Compile(source);

        // Entry 1: len=2, data=[0xAA, 0xBB] → 3 bytes consumed
        // Entry 2: len=1, data=[0xCC]        → 2 bytes consumed
        // Entry 3: len=0                     → 1 byte consumed (terminates)
        var data = new byte[] { 2, 0xAA, 0xBB, 1, 0xCC, 0 };
        var root = ParseToElement(program, data);
        var entries = root.GetProperty("entries");
        Assert.Equal(3, entries.GetArrayLength());
    }

    [Fact]
    public void LastSize_CString_MeasuresBytesConsumedIncludingNull()
    {
        // "hi\0" → cstring consumes 3 bytes; "x\0" → 2 bytes; "\0" → 1 byte
        var source = """
            @root struct Data {
                items: cstring[] @until(@last_size == 1)
            }
            """;
        var program = Compile(source);

        var data = Encoding.UTF8.GetBytes("hi\0x\0\0");
        var root = ParseToElement(program, data);
        var items = root.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("hi", items[0].GetString());
        Assert.Equal("x", items[1].GetString());
        Assert.Equal("", items[2].GetString());
    }
}
