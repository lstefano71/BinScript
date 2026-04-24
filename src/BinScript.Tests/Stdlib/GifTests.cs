using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class GifTests
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
    public void Parse_SingleFrame_Gif87a()
    {
        var program = CompileScript("gif.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "gif", "single.gif"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var header = root.GetProperty("header");
        Assert.Equal("GIF", header.GetProperty("signature").GetString());
        Assert.Equal("87a", header.GetProperty("version").GetString());

        var screen = root.GetProperty("screen");
        Assert.Equal(4, screen.GetProperty("width").GetInt64());
        Assert.Equal(4, screen.GetProperty("height").GetInt64());
        Assert.Equal(0x81, screen.GetProperty("packed").GetInt64());
        Assert.Equal(0, screen.GetProperty("bg_color_index").GetInt64());
    }

    [Fact]
    public void Parse_TwoFrame_Gif89a()
    {
        var program = CompileScript("gif.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "gif", "2frame.gif"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var header = root.GetProperty("header");
        Assert.Equal("GIF", header.GetProperty("signature").GetString());
        Assert.Equal("89a", header.GetProperty("version").GetString());

        var screen = root.GetProperty("screen");
        Assert.Equal(4, screen.GetProperty("width").GetInt64());
        Assert.Equal(4, screen.GetProperty("height").GetInt64());
    }
}
