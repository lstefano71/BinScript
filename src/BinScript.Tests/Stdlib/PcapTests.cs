using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class PcapTests
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
    public void Parse_TNS_Oracle1_Header()
    {
        var program = CompileScript("pcap.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "pcap", "TNS_Oracle1.pcap"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var header = root.GetProperty("header");
        Assert.Equal(0xA1B2C3D4, header.GetProperty("magic_number").GetUInt64());
        Assert.Equal(2, header.GetProperty("version_major").GetInt64());
        Assert.Equal(4, header.GetProperty("version_minor").GetInt64());
        Assert.Equal(0, header.GetProperty("thiszone").GetInt64());
        Assert.Equal(0, header.GetProperty("sigfigs").GetInt64());
        Assert.Equal(0xFFFF, header.GetProperty("snaplen").GetInt64());
        Assert.Equal(1, header.GetProperty("network").GetInt64()); // Ethernet
    }

    [Fact]
    public void Parse_TNS_Oracle1_PacketCount()
    {
        var program = CompileScript("pcap.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "pcap", "TNS_Oracle1.pcap"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var packets = doc.RootElement.GetProperty("packets");
        Assert.Equal(78, packets.GetArrayLength());
    }

    [Fact]
    public void Parse_TNS_Oracle1_FirstPacket()
    {
        var program = CompileScript("pcap.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "pcap", "TNS_Oracle1.pcap"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("packets")[0];
        Assert.Equal(66, first.GetProperty("incl_len").GetInt64());
        Assert.Equal(66, first.GetProperty("orig_len").GetInt64());
    }

    [Fact]
    public void Parse_TruncatedPcap_GreedyRecoversPartialPackets()
    {
        var program = CompileScript("pcap.bsx");
        byte[] fullData = File.ReadAllBytes(Path.Combine(SamplesDir, "pcap", "TNS_Oracle1.pcap"));

        // Truncate mid-way through the last packet (remove last 100 bytes)
        byte[] truncated = fullData[..^100];
        string json = program.ToJson(truncated);

        using var doc = JsonDocument.Parse(json);
        var packets = doc.RootElement.GetProperty("packets");

        // The last packet (78th) is truncated and should be discarded by @greedy.
        // All 77 prior packets should parse successfully.
        Assert.Equal(77, packets.GetArrayLength());
    }
}
