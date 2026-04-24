namespace BinScript.Core.Bytecode;

using System.Buffers.Binary;
using System.Text;

/// <summary>
/// Deserializes a BSC binary format back to a <see cref="BytecodeProgram"/>.
/// </summary>
public static class BytecodeDeserializer
{
    private static readonly byte[] ExpectedMagic = "BSC\x01"u8.ToArray();

    public static BytecodeProgram Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 28)
            throw new InvalidOperationException("BSC data too short: missing header.");

        int pos = 0;

        // ── Header (24 bytes) ────────────────────────────────────
        if (!data.Slice(0, 4).SequenceEqual(ExpectedMagic))
            throw new InvalidOperationException("Invalid BSC magic bytes.");
        pos += 4;

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;
        bool hasSource = (flags & 1) != 0;

        uint bytecodeLen = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos)); pos += 4;
        uint stringTableLen = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos)); pos += 4;
        uint structTableLen = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos)); pos += 4;
        uint sourceLen = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos)); pos += 4;
        ushort rootStructIdx = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;
        int rootIndex = rootStructIdx == 0xFFFF ? -1 : rootStructIdx;
        ushort paramCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;

        // ── Compile-time parameters ──────────────────────────────
        // We need the string table to resolve parameter names, but params come first in the format.
        // We'll read the raw bytes and defer name resolution until after reading the string table.
        int paramStart = pos;
        var rawParams = new List<(ushort nameIdx, byte typeTag, byte[] value)>();
        for (int i = 0; i < paramCount; i++)
        {
            ushort nameIdx = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;
            byte typeTag = data[pos++];
            ushort valueLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;
            byte[] value = data.Slice(pos, valueLen).ToArray(); pos += valueLen;
            rawParams.Add((nameIdx, typeTag, value));
        }

        // ── String table ─────────────────────────────────────────
        uint strCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos)); pos += 4;
        var stringTable = new string[strCount];
        for (int i = 0; i < (int)strCount; i++)
        {
            ushort sLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;
            stringTable[i] = Encoding.UTF8.GetString(data.Slice(pos, sLen)); pos += sLen;
        }

        // Now resolve parameters.
        var parameters = new Dictionary<string, object>();
        for (int i = 0; i < rawParams.Count; i++)
        {
            var (nameIdx, typeTag, value) = rawParams[i];
            string name = nameIdx < stringTable.Length ? stringTable[nameIdx] : $"param_{nameIdx}";
            object paramValue = typeTag switch
            {
                0 => BinaryPrimitives.ReadInt64LittleEndian(value),
                1 => BinaryPrimitives.ReadDoubleLittleEndian(value),
                2 => Encoding.UTF8.GetString(value),
                3 => value[0] != 0,
                _ => BinaryPrimitives.ReadInt64LittleEndian(value),
            };
            parameters[name] = paramValue;
        }

        // ── Struct metadata table ────────────────────────────────
        ushort structCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;
        var structs = new StructMeta[structCount];
        for (int i = 0; i < structCount; i++)
        {
            ushort nameIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;
            byte paramCnt = data[pos++];
            ushort fieldCnt = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;
            uint bcOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos)); pos += 4;
            uint bcLen = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos)); pos += 4;
            int staticSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos)); pos += 4;
            ushort sFlags = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;

            var fields = new FieldMeta[fieldCnt];
            for (int f = 0; f < fieldCnt; f++)
            {
                ushort fNameIdx = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos)); pos += 2;
                byte fFlags = data[pos++];
                fields[f] = new FieldMeta { NameIndex = fNameIdx, Flags = (FieldFlags)fFlags };
            }

            structs[i] = new StructMeta
            {
                NameIndex = nameIndex,
                ParamCount = paramCnt,
                Fields = fields,
                BytecodeOffset = (int)bcOffset,
                BytecodeLength = (int)bcLen,
                StaticSize = staticSize,
                Flags = (StructFlags)sFlags,
            };
        }

        // ── Bytecode section ─────────────────────────────────────
        byte[] bytecode = data.Slice(pos, (int)bytecodeLen).ToArray(); pos += (int)bytecodeLen;

        // ── Source section (optional) ────────────────────────────
        string? source = null;
        if (hasSource && sourceLen > 0)
        {
            source = Encoding.UTF8.GetString(data.Slice(pos, (int)sourceLen));
        }

        return new BytecodeProgram
        {
            Bytecode = bytecode,
            StringTable = stringTable,
            Structs = structs,
            RootStructIndex = rootIndex,
            Parameters = parameters,
            Source = source,
            Version = version,
        };
    }
}
