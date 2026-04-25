using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using BinScript.Core.Api;
using BinScript.Core.Model;
using BinScript.Emitters.Json;

namespace BinScript.Tests.Stdlib;

public class MemoryTests
{
    private static readonly string StdlibDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "stdlib"));

    private BinScriptProgram CompileScript(string name)
    {
        string source = File.ReadAllText(Path.Combine(StdlibDir, name));
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile(source, name);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        return new BinScriptProgram(result.Program!);
    }

    [Fact]
    public void Parse_CStrings_TwoPointers()
    {
        // Buffer layout: [ptr_s1: 8][ptr_s2: 8]["hello\0"]["world\0"]
        // base_ptr = 0x10000 (arbitrary virtual address)
        // ptr_s1 = base_ptr + 16 (strings start after the two 8-byte pointers)
        // ptr_s2 = base_ptr + 22 (after "hello\0" = 6 bytes)
        const long basePtr = 0x10000;
        var s1Bytes = Encoding.UTF8.GetBytes("hello\0");
        var s2Bytes = Encoding.UTF8.GetBytes("world\0");

        var buffer = new byte[8 + 8 + s1Bytes.Length + s2Bytes.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(0), (ulong)(basePtr + 16));
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8), (ulong)(basePtr + 22));
        s1Bytes.CopyTo(buffer, 16);
        s2Bytes.CopyTo(buffer, 22);

        var program = CompileScript(Path.Combine("memory", "c_strings.bsx"));
        var options = new ParseOptions
        {
            RuntimeParameters = new Dictionary<string, long> { ["base_ptr"] = basePtr }
        };
        string json = program.ToJson(buffer, options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("hello", doc.RootElement.GetProperty("s1").GetString());
        Assert.Equal("world", doc.RootElement.GetProperty("s2").GetString());
    }

    [Fact]
    public void Parse_LinkedList_ThreeNodes()
    {
        // Buffer layout: [head_ptr: 8][node1: 12][node2: 12][node3: 12]
        // node = { value: i32 (4), next: ptr<Node, u64>? (8) }
        // node3.next = 0 (null)
        const long basePtr = 0x20000;
        const int nodeSize = 12; // 4 (i32) + 8 (u64 ptr)
        int node1Offset = 8;   // after head pointer
        int node2Offset = 8 + nodeSize;
        int node3Offset = 8 + 2 * nodeSize;

        var buffer = new byte[8 + 3 * nodeSize];

        // head → node1
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(0), (ulong)(basePtr + node1Offset));

        // node1: value=42, next→node2
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(node1Offset), 42);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(node1Offset + 4), (ulong)(basePtr + node2Offset));

        // node2: value=17, next→node3
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(node2Offset), 17);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(node2Offset + 4), (ulong)(basePtr + node3Offset));

        // node3: value=99, next=null (0)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(node3Offset), 99);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(node3Offset + 4), 0);

        var program = CompileScript(Path.Combine("memory", "linked_list.bsx"));
        var options = new ParseOptions
        {
            RuntimeParameters = new Dictionary<string, long> { ["base_ptr"] = basePtr }
        };
        string json = program.ToJson(buffer, options);
        using var doc = JsonDocument.Parse(json);

        var head = doc.RootElement.GetProperty("head");
        Assert.Equal(42, head.GetProperty("value").GetInt32());

        var next1 = head.GetProperty("next");
        Assert.Equal(17, next1.GetProperty("value").GetInt32());

        var next2 = next1.GetProperty("next");
        Assert.Equal(99, next2.GetProperty("value").GetInt32());

        Assert.Equal(JsonValueKind.Null, next2.GetProperty("next").ValueKind);
    }

    [Fact]
    public void Parse_Win32Startup_DesktopString()
    {
        // Verifies: ptr<cstring @encoding(utf16le), u64>? with bits struct flags.
        // Build a minimal STARTUPINFOW buffer with only the desktop pointer set.
        const long basePtr = 0x30000;

        // STARTUPINFOW layout (64-bit):
        // cb: u32 (4)
        // _reserved1: ptr? (8) — null
        // desktop: ptr? (8) — points to UTF-16LE string
        // title: ptr? (8) — null
        // dw_x: u32 (4)
        // dw_y: u32 (4)
        // dw_x_size: u32 (4)
        // dw_y_size: u32 (4)
        // dw_x_count_chars: u32 (4)
        // dw_y_count_chars: u32 (4)
        // dw_fill_attribute: u32 (4)
        // dw_flags: StartupFlags (u32 = 4)
        // w_show_window: u16 (2)
        // cb_reserved2: u16 (2) — 0
        // _reserved2: ptr? (8) — null
        // h_std_input: u64 (8)
        // h_std_output: u64 (8)
        // h_std_error: u64 (8)
        // Total fixed: 4+8+8+8+4+4+4+4+4+4+4+4+2+2+8+8+8+8 = 96 bytes
        // Then: UTF-16LE "WinSta0\\Default\0" at offset 96

        var desktopStr = Encoding.Unicode.GetBytes("WinSta0\\Default");
        var desktopWithNull = new byte[desktopStr.Length + 2]; // +2 for UTF-16 null
        desktopStr.CopyTo(desktopWithNull, 0);

        int structSize = 96;
        var buffer = new byte[structSize + desktopWithNull.Length];

        int pos = 0;
        // cb
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(pos), (uint)structSize);
        pos += 4;
        // _reserved1: null
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(pos), 0);
        pos += 8;
        // desktop: points to string data
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(pos), (ulong)(basePtr + structSize));
        pos += 8;
        // title: null
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(pos), 0);
        pos += 8;
        // dw_x through dw_fill_attribute: all zeros (7 × u32 = 28 bytes)
        pos += 28;
        // dw_flags: StartupFlags = 0 (no flags set)
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(pos), 0);
        pos += 4;
        // w_show_window
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(pos), 0);
        pos += 2;
        // cb_reserved2
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(pos), 0);
        pos += 2;
        // _reserved2: null
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(pos), 0);
        pos += 8;
        // h_std_input, h_std_output, h_std_error
        pos += 24;
        Assert.Equal(structSize, pos);

        // Desktop string data (UTF-16LE)
        desktopWithNull.CopyTo(buffer, structSize);

        var program = CompileScript(Path.Combine("memory", "win32_startup.bsx"));
        var options = new ParseOptions
        {
            RuntimeParameters = new Dictionary<string, long> { ["base_ptr"] = basePtr }
        };
        string json = program.ToJson(buffer, options);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(structSize, doc.RootElement.GetProperty("cb").GetInt32());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("_reserved1").ValueKind);
        Assert.Equal("WinSta0\\Default", doc.RootElement.GetProperty("desktop").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("title").ValueKind);

        // Verify bits struct flags (all zero)
        var flags = doc.RootElement.GetProperty("dw_flags");
        Assert.False(flags.GetProperty("use_show_window").GetBoolean());
    }
}
