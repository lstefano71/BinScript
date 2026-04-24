namespace BinScript.Core.Bytecode;

using System.Buffers.Binary;
using System.Text;

/// <summary>
/// Serializes a <see cref="BytecodeProgram"/> to the BSC binary format.
/// </summary>
public static class BytecodeSerializer
{
    private static readonly byte[] Magic = "BSC\x01"u8.ToArray();

    public static byte[] Serialize(BytecodeProgram program)
    {
        // Pre-compute section sizes.
        int paramSectionLen = ComputeParamSectionLength(program);
        int stringTableLen = ComputeStringTableLength(program);
        int structTableLen = ComputeStructTableLength(program);
        byte[] sourceBytes = program.Source is not null
            ? Encoding.UTF8.GetBytes(program.Source)
            : Array.Empty<byte>();

        int totalSize = 28 // header
            + paramSectionLen
            + stringTableLen
            + structTableLen
            + program.Bytecode.Length
            + sourceBytes.Length;

        var buffer = new byte[totalSize];
        int pos = 0;

        // ── Header (24 bytes) ────────────────────────────────────
        Magic.CopyTo(buffer.AsSpan(pos)); pos += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(pos), program.Version); pos += 2;
        ushort flags = (ushort)(program.Source is not null ? 1 : 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(pos), flags); pos += 2;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(pos), (uint)program.Bytecode.Length); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(pos), (uint)stringTableLen); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(pos), (uint)structTableLen); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(pos), (uint)sourceBytes.Length); pos += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(pos), (ushort)program.RootStructIndex); pos += 2;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(pos), (ushort)program.Parameters.Count); pos += 2;

        // ── Compile-time parameters ──────────────────────────────
        WriteParameters(buffer.AsSpan(pos), program, out int paramBytesWritten); pos += paramBytesWritten;

        // ── String table ─────────────────────────────────────────
        WriteStringTable(buffer.AsSpan(pos), program, out int strBytesWritten); pos += strBytesWritten;

        // ── Struct metadata table ────────────────────────────────
        WriteStructTable(buffer.AsSpan(pos), program, out int structBytesWritten); pos += structBytesWritten;

        // ── Bytecode section ─────────────────────────────────────
        program.Bytecode.CopyTo(buffer.AsSpan(pos)); pos += program.Bytecode.Length;

        // ── Source section (optional) ────────────────────────────
        if (sourceBytes.Length > 0)
        {
            sourceBytes.CopyTo(buffer.AsSpan(pos));
        }

        return buffer;
    }

    private static int ComputeParamSectionLength(BytecodeProgram program)
    {
        int len = 0;
        foreach (var kv in program.Parameters)
        {
            len += 2 + 1 + 2; // NameIdx + Type + ValueLen
            len += GetParamValueLength(kv.Value);
        }
        return len;
    }

    private static int GetParamValueLength(object value) => value switch
    {
        long => 8,
        int v => 8,
        double => 8,
        string s => Encoding.UTF8.GetByteCount(s),
        bool => 1,
        _ => 8, // default: treat as i64
    };

    private static int ComputeStringTableLength(BytecodeProgram program)
    {
        int len = 4; // Count (u32le)
        for (int i = 0; i < program.StringTable.Length; i++)
        {
            len += 2 + Encoding.UTF8.GetByteCount(program.StringTable[i]); // Length + Data
        }
        return len;
    }

    private static int ComputeStructTableLength(BytecodeProgram program)
    {
        int len = 2; // Count (u16le)
        for (int i = 0; i < program.Structs.Length; i++)
        {
            var s = program.Structs[i];
            len += 2 + 1 + 2 + 4 + 4 + 4 + 2; // NameIdx + ParamCnt + FieldCnt + Offset + Len + StaticSize + Flags
            len += s.Fields.Length * (2 + 1); // per field: NameIdx + Flags
        }
        return len;
    }

    private static void WriteParameters(Span<byte> span, BytecodeProgram program, out int bytesWritten)
    {
        int pos = 0;
        // We need to intern parameter names in the string table.
        // Since we're serializing, the names should already be in the string table.
        // We'll look them up by searching the string table.
        foreach (var kv in program.Parameters)
        {
            ushort nameIdx = FindStringIndex(program.StringTable, kv.Key);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(pos), nameIdx); pos += 2;

            byte typeTag;
            byte[] valueBytes;

            switch (kv.Value)
            {
                case long l:
                    typeTag = 0;
                    valueBytes = new byte[8];
                    BinaryPrimitives.WriteInt64LittleEndian(valueBytes, l);
                    break;
                case int iv:
                    typeTag = 0;
                    valueBytes = new byte[8];
                    BinaryPrimitives.WriteInt64LittleEndian(valueBytes, iv);
                    break;
                case double d:
                    typeTag = 1;
                    valueBytes = new byte[8];
                    BinaryPrimitives.WriteDoubleLittleEndian(valueBytes, d);
                    break;
                case string s:
                    typeTag = 2;
                    valueBytes = Encoding.UTF8.GetBytes(s);
                    break;
                case bool b:
                    typeTag = 3;
                    valueBytes = new byte[] { b ? (byte)1 : (byte)0 };
                    break;
                default:
                    typeTag = 0;
                    valueBytes = new byte[8];
                    break;
            }

            span[pos++] = typeTag;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(pos), (ushort)valueBytes.Length); pos += 2;
            valueBytes.CopyTo(span.Slice(pos)); pos += valueBytes.Length;
        }
        bytesWritten = pos;
    }

    private static void WriteStringTable(Span<byte> span, BytecodeProgram program, out int bytesWritten)
    {
        int pos = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos), (uint)program.StringTable.Length); pos += 4;
        for (int i = 0; i < program.StringTable.Length; i++)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(program.StringTable[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(pos), (ushort)utf8.Length); pos += 2;
            utf8.CopyTo(span.Slice(pos)); pos += utf8.Length;
        }
        bytesWritten = pos;
    }

    private static void WriteStructTable(Span<byte> span, BytecodeProgram program, out int bytesWritten)
    {
        int pos = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(pos), (ushort)program.Structs.Length); pos += 2;
        for (int i = 0; i < program.Structs.Length; i++)
        {
            var s = program.Structs[i];
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(pos), s.NameIndex); pos += 2;
            span[pos++] = s.ParamCount;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(pos), (ushort)s.Fields.Length); pos += 2;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos), (uint)s.BytecodeOffset); pos += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos), (uint)s.BytecodeLength); pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos), s.StaticSize); pos += 4;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(pos), (ushort)s.Flags); pos += 2;

            for (int f = 0; f < s.Fields.Length; f++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(pos), s.Fields[f].NameIndex); pos += 2;
                span[pos++] = (byte)s.Fields[f].Flags;
            }
        }
        bytesWritten = pos;
    }

    private static ushort FindStringIndex(string[] table, string value)
    {
        for (int i = 0; i < table.Length; i++)
        {
            if (table[i] == value)
                return (ushort)i;
        }
        return 0;
    }
}
