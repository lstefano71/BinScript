using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class PeTests
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
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => $"[{d.Severity}] {d.Code} L{d.Span.Line}:{d.Span.Column}: {d.Message}")));
        return new BinScriptProgram(result.Program!);
    }

    [Fact]
    public void Parse_TinyX64_Exe()
    {
        var program = CompileScript("pe.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "pe", "tiny_x64.exe"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var dos = root.GetProperty("dos_header");
        Assert.Equal(0x5A4D, dos.GetProperty("e_magic").GetInt64());
        Assert.Equal(0x00004550, root.GetProperty("pe_signature").GetInt64());

        var coff = root.GetProperty("coff_header");
        Assert.Equal(0x8664, coff.GetProperty("machine").GetInt64());       // AMD64
        Assert.Equal(1, coff.GetProperty("number_of_sections").GetInt64());

        var sections = root.GetProperty("sections");
        Assert.True(sections.GetArrayLength() >= 1);
    }

    [Fact]
    public void Parse_TinyX86_Exe()
    {
        var program = CompileScript("pe.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "pe", "tiny_x86.exe"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var dos = root.GetProperty("dos_header");
        Assert.Equal(0x5A4D, dos.GetProperty("e_magic").GetInt64());
        Assert.Equal(0x00004550, root.GetProperty("pe_signature").GetInt64());

        var coff = root.GetProperty("coff_header");
        Assert.Equal(0x014C, coff.GetProperty("machine").GetInt64());       // I386
        Assert.Equal(1, coff.GetProperty("number_of_sections").GetInt64());
    }

    [Fact]
    public void Parse_TinyDll()
    {
        var program = CompileScript("pe.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "pe", "tiny_dll.dll"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var dos = root.GetProperty("dos_header");
        Assert.Equal(0x5A4D, dos.GetProperty("e_magic").GetInt64());
        Assert.Equal(0x00004550, root.GetProperty("pe_signature").GetInt64());

        var coff = root.GetProperty("coff_header");
        Assert.Equal(0x8664, coff.GetProperty("machine").GetInt64());       // AMD64
        Assert.Equal(1, coff.GetProperty("number_of_sections").GetInt64());
    }
}
