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
        Assert.Equal(2, ident.GetProperty("ei_class").GetInt64());    // ELFCLASS64
        Assert.Equal(1, ident.GetProperty("ei_data").GetInt64());     // ELFDATA2LSB
        Assert.Equal(1, ident.GetProperty("ei_version").GetInt64());
        Assert.Equal(0, ident.GetProperty("ei_osabi").GetInt64());    // ELFOSABI_NONE

        var header = root.GetProperty("header");
        Assert.Equal(2, header.GetProperty("e_type").GetInt64());     // ET_EXEC
        Assert.Equal(0x3E, header.GetProperty("e_machine").GetInt64()); // EM_X86_64
        Assert.Equal(1, header.GetProperty("e_version").GetInt64());
        Assert.Equal(0x401000, header.GetProperty("e_entry").GetInt64());
        Assert.Equal(64, header.GetProperty("e_phoff").GetInt64());
        Assert.Equal(64, header.GetProperty("e_ehsize").GetInt64());
        Assert.Equal(56, header.GetProperty("e_phentsize").GetInt64());
        Assert.Equal(1, header.GetProperty("e_phnum").GetInt64());
    }
}
