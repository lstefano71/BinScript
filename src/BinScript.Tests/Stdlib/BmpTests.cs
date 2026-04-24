using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class BmpTests
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
    public void Parse_24bit_Bmp()
    {
        var program = CompileScript("bmp.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "bmp", "4x4_24bit.bmp"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var header = root.GetProperty("header");
        Assert.Equal(0x4D42, header.GetProperty("signature").GetInt64());
        Assert.Equal(102, header.GetProperty("file_size").GetInt64());
        Assert.Equal(54, header.GetProperty("data_offset").GetInt64());

        var dib = root.GetProperty("dib_header");
        Assert.Equal(40, dib.GetProperty("size").GetInt64());
        Assert.Equal(4, dib.GetProperty("width").GetInt64());
        Assert.Equal(4, dib.GetProperty("height").GetInt64());
        Assert.Equal(24, dib.GetProperty("bits_per_pixel").GetInt64());
    }

    [Fact]
    public void Parse_8bit_Bmp()
    {
        var program = CompileScript("bmp.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "bmp", "4x4_8bit.bmp"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var header = root.GetProperty("header");
        Assert.Equal(0x4D42, header.GetProperty("signature").GetInt64());
        Assert.Equal(1094, header.GetProperty("file_size").GetInt64());
        Assert.Equal(1078, header.GetProperty("data_offset").GetInt64());

        var dib = root.GetProperty("dib_header");
        Assert.Equal(40, dib.GetProperty("size").GetInt64());
        Assert.Equal(4, dib.GetProperty("width").GetInt64());
        Assert.Equal(4, dib.GetProperty("height").GetInt64());
        Assert.Equal(8, dib.GetProperty("bits_per_pixel").GetInt64());
        Assert.Equal(256, dib.GetProperty("colors_used").GetInt64());
    }
}
