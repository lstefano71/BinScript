using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class WavTests
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
    public void Parse_Sine440Hz_Wav()
    {
        var program = CompileScript("wav.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "wav", "sine_440hz.wav"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // RIFF header
        Assert.Equal("RIFF", root.GetProperty("chunk_id").GetString());
        Assert.Equal("WAVE", root.GetProperty("format").GetString());

        var chunks = root.GetProperty("chunks");
        Assert.Equal(2, chunks.GetArrayLength()); // fmt + data

        // fmt chunk
        var fmtChunk = chunks[0];
        Assert.Equal("fmt ", fmtChunk.GetProperty("chunk_id").GetString());
        Assert.Equal(16, fmtChunk.GetProperty("chunk_size").GetInt64());

        var fmt = fmtChunk.GetProperty("body");
        Assert.Equal(1, fmt.GetProperty("audio_format").GetInt64());    // PCM
        Assert.Equal(1, fmt.GetProperty("num_channels").GetInt64());    // mono
        Assert.Equal(8000, fmt.GetProperty("sample_rate").GetInt64());
        Assert.Equal(8000, fmt.GetProperty("byte_rate").GetInt64());    // 8000 * 1 * 1
        Assert.Equal(1, fmt.GetProperty("block_align").GetInt64());     // 1 * 1
        Assert.Equal(8, fmt.GetProperty("bits_per_sample").GetInt64());

        // data chunk
        var dataChunk = chunks[1];
        Assert.Equal("data", dataChunk.GetProperty("chunk_id").GetString());
        Assert.Equal(800, dataChunk.GetProperty("chunk_size").GetInt64());
    }

    [Fact]
    public void Parse_Wav_RiffChunkSize_MatchesFileSize()
    {
        var program = CompileScript("wav.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "wav", "sine_440hz.wav"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        long chunkSize = root.GetProperty("chunk_size").GetInt64();
        // chunk_size = file size - 8 (RIFF header: 4 bytes "RIFF" + 4 bytes size)
        Assert.Equal(data.Length - 8, chunkSize);
    }
}
