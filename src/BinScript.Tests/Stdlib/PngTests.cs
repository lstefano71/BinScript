using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class PngTests
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
    public void Parse_Rgb_8x8_Png()
    {
        var program = CompileScript("png.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "png", "rgb_8x8.png"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(13, root.GetProperty("ihdr_length").GetInt64());
        Assert.Equal("IHDR", root.GetProperty("ihdr_type").GetString());

        var ihdr = root.GetProperty("ihdr");
        Assert.Equal(8, ihdr.GetProperty("width").GetInt64());
        Assert.Equal(8, ihdr.GetProperty("height").GetInt64());
        Assert.Equal(8, ihdr.GetProperty("bit_depth").GetInt64());
        Assert.Equal(2, ihdr.GetProperty("color_type").GetInt64());
        Assert.Equal(0, ihdr.GetProperty("interlace").GetInt64());
    }

    [Fact]
    public void Parse_Indexed_4x4_Png()
    {
        var program = CompileScript("png.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "png", "indexed_4x4.png"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(13, root.GetProperty("ihdr_length").GetInt64());
        Assert.Equal("IHDR", root.GetProperty("ihdr_type").GetString());

        var ihdr = root.GetProperty("ihdr");
        Assert.Equal(4, ihdr.GetProperty("width").GetInt64());
        Assert.Equal(4, ihdr.GetProperty("height").GetInt64());
        Assert.Equal(8, ihdr.GetProperty("bit_depth").GetInt64());
        Assert.Equal(3, ihdr.GetProperty("color_type").GetInt64());
    }
}
