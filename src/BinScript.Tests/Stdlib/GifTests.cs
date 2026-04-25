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

        // Header
        var header = root.GetProperty("header");
        Assert.Equal("GIF", header.GetProperty("signature").GetString());
        Assert.Equal("87a", header.GetProperty("version").GetString());

        // Logical Screen Descriptor
        var screen = root.GetProperty("screen");
        Assert.Equal(4, screen.GetProperty("width").GetInt64());
        Assert.Equal(4, screen.GetProperty("height").GetInt64());
        Assert.Equal(0x81, screen.GetProperty("packed").GetInt64());
        Assert.Equal(0, screen.GetProperty("bg_color_index").GetInt64());
        Assert.Equal(0, screen.GetProperty("pixel_aspect_ratio").GetInt64());

        // Global Color Table (4 colors: red, green, blue, yellow)
        var gct = root.GetProperty("global_color_table");
        Assert.Equal(4, gct.GetArrayLength());
        Assert.Equal(255, gct[0].GetProperty("red").GetInt64());
        Assert.Equal(0, gct[0].GetProperty("green").GetInt64());
        Assert.Equal(0, gct[0].GetProperty("blue").GetInt64());
        Assert.Equal(0, gct[1].GetProperty("red").GetInt64());
        Assert.Equal(255, gct[1].GetProperty("green").GetInt64());
        Assert.Equal(0, gct[2].GetProperty("red").GetInt64());
        Assert.Equal(0, gct[2].GetProperty("green").GetInt64());
        Assert.Equal(255, gct[2].GetProperty("blue").GetInt64());
        Assert.Equal(255, gct[3].GetProperty("red").GetInt64());
        Assert.Equal(255, gct[3].GetProperty("green").GetInt64());
        Assert.Equal(0, gct[3].GetProperty("blue").GetInt64());

        // Blocks: single image
        var blocks = root.GetProperty("blocks");
        Assert.Equal(1, blocks.GetArrayLength());

        var block0 = blocks[0];
        Assert.Equal(0x2C, block0.GetProperty("introducer").GetInt64());
        var img = block0.GetProperty("body");
        Assert.Equal(0, img.GetProperty("left").GetInt64());
        Assert.Equal(0, img.GetProperty("top").GetInt64());
        Assert.Equal(4, img.GetProperty("width").GetInt64());
        Assert.Equal(4, img.GetProperty("height").GetInt64());
        Assert.Equal(0, img.GetProperty("packed").GetInt64());
        Assert.Equal(8, img.GetProperty("lzw_min_code_size").GetInt64());

        // No local color table (packed bit 7 = 0)
        var lct = img.GetProperty("local_color_table");
        Assert.Equal(JsonValueKind.String, lct.ValueKind); // bytes[0] → empty base64

        // Sub-blocks: 1 data block
        var subBlocks = img.GetProperty("sub_blocks");
        Assert.Equal(1, subBlocks.GetArrayLength());
        Assert.Equal(15, subBlocks[0].GetProperty("size").GetInt64());
    }

    [Fact]
    public void Parse_TwoFrame_Gif89a()
    {
        var program = CompileScript("gif.bsx");
        byte[] data = File.ReadAllBytes(Path.Combine(SamplesDir, "gif", "2frame.gif"));
        string json = program.ToJson(data);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Header
        var header = root.GetProperty("header");
        Assert.Equal("GIF", header.GetProperty("signature").GetString());
        Assert.Equal("89a", header.GetProperty("version").GetString());

        // Logical Screen Descriptor
        var screen = root.GetProperty("screen");
        Assert.Equal(4, screen.GetProperty("width").GetInt64());
        Assert.Equal(4, screen.GetProperty("height").GetInt64());
        Assert.Equal(0x81, screen.GetProperty("packed").GetInt64());

        // Global Color Table (4 colors)
        var gct = root.GetProperty("global_color_table");
        Assert.Equal(4, gct.GetArrayLength());

        // Blocks: app ext + gce + image + gce + image = 5 blocks
        var blocks = root.GetProperty("blocks");
        Assert.Equal(5, blocks.GetArrayLength());

        // Block 0: Application Extension (NETSCAPE2.0)
        var appBlock = blocks[0];
        Assert.Equal(0x21, appBlock.GetProperty("introducer").GetInt64());
        var ext0 = appBlock.GetProperty("body");
        Assert.Equal(0xFF, ext0.GetProperty("label").GetInt64());
        var appBody = ext0.GetProperty("body");
        Assert.Equal(11, appBody.GetProperty("block_size").GetInt64());
        Assert.Equal("NETSCAPE", appBody.GetProperty("identifier").GetString());
        Assert.Equal("2.0", appBody.GetProperty("auth_code").GetString());
        Assert.Equal(1, appBody.GetProperty("sub_blocks").GetArrayLength());

        // Block 1: Graphics Control Extension (frame 1)
        var gceBlock1 = blocks[1];
        Assert.Equal(0x21, gceBlock1.GetProperty("introducer").GetInt64());
        var gce1 = gceBlock1.GetProperty("body").GetProperty("body");
        Assert.Equal(4, gce1.GetProperty("block_size").GetInt64());
        Assert.Equal(0, gce1.GetProperty("packed").GetInt64());
        Assert.Equal(10, gce1.GetProperty("delay_time").GetInt64());
        Assert.Equal(0, gce1.GetProperty("transparent_color_index").GetInt64());

        // Block 2: Image (frame 1, no LCT)
        var img1 = blocks[2].GetProperty("body");
        Assert.Equal(0, img1.GetProperty("left").GetInt64());
        Assert.Equal(0, img1.GetProperty("top").GetInt64());
        Assert.Equal(4, img1.GetProperty("width").GetInt64());
        Assert.Equal(4, img1.GetProperty("height").GetInt64());
        Assert.Equal(0, img1.GetProperty("packed").GetInt64());
        Assert.Equal(8, img1.GetProperty("lzw_min_code_size").GetInt64());

        // Block 3: Graphics Control Extension (frame 2, transparency)
        var gce2 = blocks[3].GetProperty("body").GetProperty("body");
        Assert.Equal(1, gce2.GetProperty("packed").GetInt64());
        Assert.Equal(10, gce2.GetProperty("delay_time").GetInt64());
        Assert.Equal(4, gce2.GetProperty("transparent_color_index").GetInt64());

        // Block 4: Image (frame 2, with LCT)
        var img2 = blocks[4].GetProperty("body");
        Assert.Equal(4, img2.GetProperty("width").GetInt64());
        Assert.Equal(4, img2.GetProperty("height").GetInt64());
        Assert.Equal(0x81, img2.GetProperty("packed").GetInt64());
        Assert.Equal(8, img2.GetProperty("lzw_min_code_size").GetInt64());

        // Local color table present (packed bit 7 = 1, lct_size = 1 → 4 colors)
        var lct = img2.GetProperty("local_color_table");
        Assert.Equal(4, lct.GetArrayLength());
        Assert.Equal(255, lct[0].GetProperty("red").GetInt64());
        Assert.Equal(0, lct[0].GetProperty("green").GetInt64());
        Assert.Equal(0, lct[0].GetProperty("blue").GetInt64());
    }
}
