namespace BinScript.Core.Bytecode;

using System.Buffers.Binary;

/// <summary>
/// Utility for building bytecode instruction streams.
/// Uses a growable byte list with efficient binary writes.
/// </summary>
public sealed class BytecodeBuilder
{
    private readonly List<byte> _bytes = new(256);
    private readonly Dictionary<string, ushort> _stringPool = new();
    private readonly List<string> _strings = new();

    public int Position => _bytes.Count;

    public void Emit(Opcode op) => _bytes.Add((byte)op);

    public void EmitU8(byte value) => _bytes.Add(value);

    public void EmitU16(ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        _bytes.Add(buf[0]);
        _bytes.Add(buf[1]);
    }

    public void EmitI32(int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        _bytes.Add(buf[0]);
        _bytes.Add(buf[1]);
        _bytes.Add(buf[2]);
        _bytes.Add(buf[3]);
    }

    public void EmitU32(uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        _bytes.Add(buf[0]);
        _bytes.Add(buf[1]);
        _bytes.Add(buf[2]);
        _bytes.Add(buf[3]);
    }

    public void EmitI64(long value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf, value);
        _bytes.Add(buf[0]);
        _bytes.Add(buf[1]);
        _bytes.Add(buf[2]);
        _bytes.Add(buf[3]);
        _bytes.Add(buf[4]);
        _bytes.Add(buf[5]);
        _bytes.Add(buf[6]);
        _bytes.Add(buf[7]);
    }

    public void EmitF64(double value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buf, value);
        _bytes.Add(buf[0]);
        _bytes.Add(buf[1]);
        _bytes.Add(buf[2]);
        _bytes.Add(buf[3]);
        _bytes.Add(buf[4]);
        _bytes.Add(buf[5]);
        _bytes.Add(buf[6]);
        _bytes.Add(buf[7]);
    }

    /// <summary>Intern a string and return its index in the string table.</summary>
    public ushort InternString(string s)
    {
        if (_stringPool.TryGetValue(s, out var index))
            return index;
        index = (ushort)_strings.Count;
        _strings.Add(s);
        _stringPool[s] = index;
        return index;
    }

    /// <summary>Patch a previously emitted I32 at the given offset.</summary>
    public void PatchI32(int offset, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        _bytes[offset] = buf[0];
        _bytes[offset + 1] = buf[1];
        _bytes[offset + 2] = buf[2];
        _bytes[offset + 3] = buf[3];
    }

    /// <summary>Reserve space for an I32 value and return the offset for later patching.</summary>
    public int ReserveI32()
    {
        int offset = _bytes.Count;
        EmitI32(0);
        return offset;
    }

    public byte[] ToArray() => _bytes.ToArray();

    public string[] GetStringTable() => _strings.ToArray();
}
