using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class MemoryTests
{
    private static readonly string StdlibDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "stdlib"));

    private BinScriptProgram CompileScript(string name)
    {
        string source = File.ReadAllText(Path.Combine(StdlibDir, name));
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile(source, name);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        return new BinScriptProgram(result.Program!);
    }

    [Fact(Skip = "Requires compiler support for ptr<T> type and @param")]
    public void Parse_CStrings_TwoPointers()
    {
        // Verifies: ptr<cstring, u64> transparent dereferencing with @param base_ptr.
        //
        // Buffer layout: [ptr_s1: 8][ptr_s2: 8]["hello\0"]["world\0"]
        // ptr values = base_ptr + offset within buffer.
        //
        // Expected JSON: { "s1": "hello", "s2": "world" }
        //
        // NOTE: ToJson API does not yet accept runtime @param values.
        // When ptr<T> and @param are implemented, this test should pass
        // using a new overload like: program.ToJson(buffer, params: ...)
        var program = CompileScript(Path.Combine("memory", "c_strings.bsx"));
        Assert.NotNull(program);
    }

    [Fact(Skip = "Requires compiler support for ptr<T> type, nullable, and @param")]
    public void Parse_LinkedList_ThreeNodes()
    {
        // Verifies: recursive ptr<Node, u64>? with guarded null termination.
        //
        // Layout: [head_ptr: 8][node1: 12][node2: 12][node3: 12]
        // node = { value: i32 (4), next: ptr<Node, u64>? (8) }
        //
        // Expected JSON:
        // { "head": { "value": 42, "next": { "value": 17,
        //   "next": { "value": 99, "next": null } } } }
        var program = CompileScript(Path.Combine("memory", "linked_list.bsx"));
        Assert.NotNull(program);
    }

    [Fact(Skip = "Requires compiler support for ptr<T> type, @encoding, bits struct, and @param")]
    public void Parse_Win32Startup_DesktopString()
    {
        // Verifies: ptr<cstring @encoding(utf16le), u64>? with bits struct flags.
        var program = CompileScript(Path.Combine("memory", "win32_startup.bsx"));
        Assert.NotNull(program);
    }
}
