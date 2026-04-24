namespace BinScript.Core.Runtime;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Tagged union for values on the evaluation stack and in field value tables.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct StackValue
{
    public readonly long IntValue;
    public readonly double FloatValue;
    public readonly string? StringValue;
    public readonly byte[]? BytesValue;
    public readonly StackValueKind Kind;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private StackValue(long intValue, double floatValue, string? stringValue, byte[]? bytesValue, StackValueKind kind)
    {
        IntValue = intValue;
        FloatValue = floatValue;
        StringValue = stringValue;
        BytesValue = bytesValue;
        Kind = kind;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StackValue FromInt(long value) => new(value, 0, null, null, StackValueKind.Int);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StackValue FromFloat(double value) => new(0, value, null, null, StackValueKind.Float);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StackValue FromBool(bool value) => new(value ? 1 : 0, 0, null, null, StackValueKind.Int);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StackValue FromString(string value) => new(0, 0, value, null, StackValueKind.String);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StackValue FromBytes(byte[] value) => new(0, 0, null, value, StackValueKind.Bytes);

    public static readonly StackValue Zero = FromInt(0);

    /// <summary>Coerce to long for integer operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long AsLong() => Kind == StackValueKind.Float ? (long)FloatValue : IntValue;

    /// <summary>Coerce to double for float operations.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double AsDouble() => Kind == StackValueKind.Float ? FloatValue : IntValue;

    /// <summary>Coerce to bool.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AsBool() => IntValue != 0;

    public override string ToString() => Kind switch
    {
        StackValueKind.Int => IntValue.ToString(),
        StackValueKind.Float => FloatValue.ToString(),
        StackValueKind.String => StringValue ?? "",
        StackValueKind.Bytes => $"byte[{BytesValue?.Length ?? 0}]",
        _ => "?",
    };
}

public enum StackValueKind : byte
{
    Int = 0,
    Float = 1,
    String = 2,
    Bytes = 3,
}
