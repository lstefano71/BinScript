namespace BinScript.Core.Runtime;

/// <summary>
/// Stores parsed field values, offsets, sizes, and array counts for a struct scope.
/// Indexed by field_id (u16).
/// </summary>
public sealed class FieldValueTable
{
    private readonly StackValue[] _values;
    private readonly long[] _offsets;
    private readonly long[] _sizes;
    private readonly long[] _arrayCounts;

    public FieldValueTable(int fieldCount)
    {
        int capacity = Math.Max(fieldCount, 16);
        _values = new StackValue[capacity];
        _offsets = new long[capacity];
        _sizes = new long[capacity];
        _arrayCounts = new long[capacity];
    }

    public StackValue GetValue(ushort fieldId) =>
        fieldId < _values.Length ? _values[fieldId] : StackValue.Zero;

    public void SetValue(ushort fieldId, StackValue value)
    {
        EnsureCapacity(fieldId);
        _values[fieldId] = value;
    }

    public long GetOffset(ushort fieldId) =>
        fieldId < _offsets.Length ? _offsets[fieldId] : 0;

    public void SetOffset(ushort fieldId, long offset)
    {
        EnsureCapacity(fieldId);
        _offsets[fieldId] = offset;
    }

    public long GetSize(ushort fieldId) =>
        fieldId < _sizes.Length ? _sizes[fieldId] : 0;

    public void SetSize(ushort fieldId, long size)
    {
        EnsureCapacity(fieldId);
        _sizes[fieldId] = size;
    }

    public long GetArrayCount(ushort fieldId) =>
        fieldId < _arrayCounts.Length ? _arrayCounts[fieldId] : 0;

    public void SetArrayCount(ushort fieldId, long count)
    {
        EnsureCapacity(fieldId);
        _arrayCounts[fieldId] = count;
    }

    private void EnsureCapacity(int fieldId)
    {
        // Tables are pre-allocated to field count, so this should rarely trigger.
        // In case of dynamic field allocation, we do nothing if within bounds.
        if (fieldId >= _values.Length)
        {
            throw new InvalidOperationException(
                $"Field ID {fieldId} exceeds field table capacity {_values.Length}.");
        }
    }

    public FieldValueTable Clone()
    {
        var clone = new FieldValueTable(_values.Length);
        Array.Copy(_values, clone._values, _values.Length);
        Array.Copy(_offsets, clone._offsets, _offsets.Length);
        Array.Copy(_sizes, clone._sizes, _sizes.Length);
        Array.Copy(_arrayCounts, clone._arrayCounts, _arrayCounts.Length);
        return clone;
    }
}
