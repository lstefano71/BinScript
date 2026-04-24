namespace BinScript.Core.Runtime;

using System.IO.Hashing;
using System.Runtime.CompilerServices;

/// <summary>
/// Built-in function implementations for @sizeof, @offset_of, @count, @strlen, @crc32, @adler32.
/// </summary>
public static class BuiltinFunctions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SizeOf(ParseContext ctx, ushort fieldId)
    {
        long size = ctx.CurrentFieldTable.GetSize(fieldId);
        ctx.Push(StackValue.FromInt(size));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void OffsetOf(ParseContext ctx, ushort fieldId)
    {
        long offset = ctx.CurrentFieldTable.GetOffset(fieldId);
        ctx.Push(StackValue.FromInt(offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Count(ParseContext ctx, ushort fieldId)
    {
        long count = ctx.CurrentFieldTable.GetArrayCount(fieldId);
        ctx.Push(StackValue.FromInt(count));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StrLen(ParseContext ctx, ushort fieldId)
    {
        var val = ctx.CurrentFieldTable.GetValue(fieldId);
        long len = val.Kind == StackValueKind.String
            ? System.Text.Encoding.UTF8.GetByteCount(val.StringValue ?? "")
            : val.BytesValue?.Length ?? 0;
        ctx.Push(StackValue.FromInt(len));
    }

    public static void ComputeCrc32(ParseContext ctx, int fieldCount)
    {
        // Pop N values from stack (pushed in order), compute CRC-32 over their byte representations
        var values = new StackValue[fieldCount];
        for (int i = fieldCount - 1; i >= 0; i--)
            values[i] = ctx.Pop();

        uint crc = 0;
        for (int i = 0; i < fieldCount; i++)
        {
            byte[] data = GetValueBytes(values[i]);
            crc = AppendCrc32(crc, data);
        }

        ctx.Push(StackValue.FromInt(crc));
    }

    public static void ComputeAdler32(ParseContext ctx, int fieldCount)
    {
        var values = new StackValue[fieldCount];
        for (int i = fieldCount - 1; i >= 0; i--)
            values[i] = ctx.Pop();

        uint a = 1, b = 0;
        const uint MOD = 65521;

        for (int i = 0; i < fieldCount; i++)
        {
            byte[] data = GetValueBytes(values[i]);
            for (int j = 0; j < data.Length; j++)
            {
                a = (a + data[j]) % MOD;
                b = (b + a) % MOD;
            }
        }

        ctx.Push(StackValue.FromInt((b << 16) | a));
    }

    private static byte[] GetValueBytes(StackValue val)
    {
        return val.Kind switch
        {
            StackValueKind.Bytes => val.BytesValue ?? [],
            StackValueKind.String => System.Text.Encoding.UTF8.GetBytes(val.StringValue ?? ""),
            StackValueKind.Int => BitConverter.GetBytes(val.IntValue),
            StackValueKind.Float => BitConverter.GetBytes(val.FloatValue),
            _ => [],
        };
    }

    private static uint AppendCrc32(uint currentCrc, byte[] data)
    {
        // Use System.IO.Hashing.Crc32
        Span<byte> crcBytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crcBytes, currentCrc);
        var crc32 = new Crc32();
        crc32.Append(crcBytes);
        crc32.Append(data);
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(crc32.GetCurrentHash());
    }
}
