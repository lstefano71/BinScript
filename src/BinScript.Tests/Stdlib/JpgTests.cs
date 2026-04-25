using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class JpgTests
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

    [Fact(Skip = "Requires JPEG test samples and compiler support for enum, @until")]
    public void Parse_Minimal_Jpg()
    {
        var program = CompileScript("jpg.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "jpg", "minimal.jpg"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(0xFFD8, root.GetProperty("soi").GetInt64());

        var segments = root.GetProperty("segments");
        Assert.True(segments.GetArrayLength() >= 1);
    }
}
