using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class ElfTests
{
    private static readonly string StdlibDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "stdlib"));
    private static readonly string SamplesDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests", "samples"));

    private BinScriptProgram CompileScript(string name)
    {
        string source = File.ReadAllText(Path.Combine(StdlibDir, name));
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile(source, name);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        return new BinScriptProgram(result.Program!);
    }

    [Fact]
    public void Parse_HelloX64_Elf()
    {
        var program = CompileScript("elf.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "elf", "hello_x64.elf"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var ident = root.GetProperty("ident");
        Assert.Equal(2, ident.GetProperty("class").GetInt64());      // ELF64
        Assert.Equal(1, ident.GetProperty("data").GetInt64());       // Little-endian
        Assert.Equal(1, ident.GetProperty("version").GetInt64());
        Assert.Equal(0, ident.GetProperty("os_abi").GetInt64());     // SYSV

        var header = root.GetProperty("header");
        Assert.Equal(2, header.GetProperty("type").GetInt64());      // ET_EXEC
        Assert.Equal(0x3E, header.GetProperty("machine").GetInt64()); // EM_X86_64
        Assert.Equal(1, header.GetProperty("version").GetInt64());
        Assert.Equal(0x401000, header.GetProperty("entry").GetInt64());
        Assert.Equal(64, header.GetProperty("phoff").GetInt64());
        Assert.Equal(64, header.GetProperty("ehsize").GetInt64());
        Assert.Equal(56, header.GetProperty("phentsize").GetInt64());
        Assert.Equal(1, header.GetProperty("phnum").GetInt64());
    }
}
