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

        // EOCD
        var eocd = root.GetProperty("eocd");
        Assert.Equal(0x06054B50, eocd.GetProperty("signature").GetInt64());
        Assert.Equal(1, eocd.GetProperty("cd_entries_total").GetInt64());
        Assert.Equal(54, eocd.GetProperty("cd_offset").GetInt64());

        // Central directory
        var cd = root.GetProperty("central_dir");
        Assert.Equal(1, cd.GetArrayLength());
        Assert.Equal(0x02014B50, cd[0].GetProperty("signature").GetInt64());
        Assert.Equal("single.txt", cd[0].GetProperty("file_name").GetString());

        // Local files
        var lf = root.GetProperty("local_files");
        Assert.Equal(1, lf.GetArrayLength());
        Assert.Equal(0x04034B50, lf[0].GetProperty("signature").GetInt64());
        Assert.Equal("single.txt", lf[0].GetProperty("file_name").GetString());
        Assert.Equal(14, lf[0].GetProperty("compressed_size").GetInt64());
        Assert.Equal(14, lf[0].GetProperty("uncompressed_size").GetInt64());
    }

    [Fact]
    public void Parse_Hello_Zip()
    {
        var program = CompileScript("zip.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "zip", "hello.zip"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // EOCD
        var eocd = root.GetProperty("eocd");
        Assert.Equal(0x06054B50, eocd.GetProperty("signature").GetInt64());
        Assert.Equal(2, eocd.GetProperty("cd_entries_total").GetInt64());

        // Central directory
        var cd = root.GetProperty("central_dir");
        Assert.Equal(2, cd.GetArrayLength());
        Assert.Equal("hello.txt", cd[0].GetProperty("file_name").GetString());
        Assert.Equal("data.txt", cd[1].GetProperty("file_name").GetString());

        // Local files
        var lf = root.GetProperty("local_files");
        Assert.Equal(2, lf.GetArrayLength());
        Assert.Equal(0x04034B50, lf[0].GetProperty("signature").GetInt64());
        Assert.Equal(0, lf[0].GetProperty("compression").GetInt64());
        Assert.Equal(13, lf[0].GetProperty("compressed_size").GetInt64());
        Assert.Equal(13, lf[0].GetProperty("uncompressed_size").GetInt64());
        Assert.Equal(9, lf[0].GetProperty("name_length").GetInt64());
        Assert.Equal("hello.txt", lf[0].GetProperty("file_name").GetString());

        Assert.Equal("data.txt", lf[1].GetProperty("file_name").GetString());
        Assert.Equal(29, lf[1].GetProperty("compressed_size").GetInt64());
    }
}
