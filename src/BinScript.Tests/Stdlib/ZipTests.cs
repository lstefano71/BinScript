using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class ZipTests
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
    public void Parse_Single_Zip()
    {
        var program = CompileScript("zip.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "zip", "single.zip"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var lh = root.GetProperty("local_header");
        Assert.Equal(0x04034B50, lh.GetProperty("signature").GetInt64());
        Assert.Equal(20, lh.GetProperty("version_needed").GetInt64());
        Assert.Equal(0, lh.GetProperty("compression").GetInt64());
        Assert.Equal(14, lh.GetProperty("compressed_size").GetInt64());
        Assert.Equal(14, lh.GetProperty("uncompressed_size").GetInt64());
        Assert.Equal(10, lh.GetProperty("name_length").GetInt64());
        Assert.Equal(0, lh.GetProperty("extra_length").GetInt64());
        Assert.Equal("single.txt", lh.GetProperty("file_name").GetString());
    }

    [Fact]
    public void Parse_Hello_Zip()
    {
        var program = CompileScript("zip.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "zip", "hello.zip"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var lh = root.GetProperty("local_header");
        Assert.Equal(0x04034B50, lh.GetProperty("signature").GetInt64());
        Assert.Equal(0, lh.GetProperty("compression").GetInt64());
        Assert.Equal(13, lh.GetProperty("compressed_size").GetInt64());
        Assert.Equal(13, lh.GetProperty("uncompressed_size").GetInt64());
        Assert.Equal(9, lh.GetProperty("name_length").GetInt64());
        Assert.Equal("hello.txt", lh.GetProperty("file_name").GetString());
    }
}
