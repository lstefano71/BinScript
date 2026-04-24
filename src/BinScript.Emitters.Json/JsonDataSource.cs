namespace BinScript.Emitters.Json;

using System.Text.Json;
using BinScript.Core.Interfaces;

/// <summary>
/// Implements <see cref="IDataSource"/> by navigating a <see cref="JsonDocument"/> with a stack.
/// </summary>
public sealed class JsonDataSource : IDataSource, IDisposable
{
    private readonly JsonDocument _doc;
    private readonly bool _ownsDoc;
    private readonly Stack<JsonElement> _elementStack = new();

    public JsonDataSource(string json)
    {
        _doc = JsonDocument.Parse(json);
        _ownsDoc = true;
    }

    public JsonDataSource(JsonDocument doc)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _ownsDoc = false;
    }

    private JsonElement Current => _elementStack.Peek();

    public void EnterRoot(string structName) => _elementStack.Push(_doc.RootElement);
    public void ExitRoot() => _elementStack.Pop();

    public void EnterStruct(string fieldName, string structName)
    {
        var current = Current;
        // If current is already an object and the named property doesn't exist,
        // we're likely inside an array element that IS the struct — no navigation needed.
        if (current.ValueKind == JsonValueKind.Object && !string.IsNullOrEmpty(fieldName)
            && current.TryGetProperty(fieldName, out var prop))
        {
            _elementStack.Push(prop);
        }
        else
        {
            _elementStack.Push(current); // already positioned on the struct
        }
    }

    public void ExitStruct() => _elementStack.Pop();

    public void EnterArray(string fieldName)
    {
        var current = Current;
        _elementStack.Push(current.GetProperty(fieldName));
    }

    public int GetArrayLength() => Current.GetArrayLength();

    public void EnterArrayElement(int index)
    {
        var arr = Current;
        _elementStack.Push(arr[index]);
    }

    public void ExitArrayElement() => _elementStack.Pop();
    public void ExitArray() => _elementStack.Pop();

    public long ReadInt(string fieldName)
    {
        var current = Current;
        if (current.ValueKind == JsonValueKind.Number)
            return current.GetInt64();
        return current.GetProperty(fieldName).GetInt64();
    }

    public ulong ReadUInt(string fieldName)
    {
        var current = Current;
        if (current.ValueKind == JsonValueKind.Number)
        {
            if (current.TryGetUInt64(out ulong uval)) return uval;
            return (ulong)current.GetInt64();
        }
        var prop = current.GetProperty(fieldName);
        // JSON numbers may be stored as signed; handle both
        if (prop.TryGetUInt64(out ulong uval2))
            return uval2;
        return (ulong)prop.GetInt64();
    }

    public double ReadFloat(string fieldName)
    {
        var current = Current;
        if (current.ValueKind == JsonValueKind.Number)
            return current.GetDouble();
        return current.GetProperty(fieldName).GetDouble();
    }

    public bool ReadBool(string fieldName)
    {
        var current = Current;
        if (current.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return current.GetBoolean();
        return current.GetProperty(fieldName).GetBoolean();
    }

    public string ReadString(string fieldName)
    {
        var current = Current;
        if (current.ValueKind == JsonValueKind.String)
            return current.GetString() ?? "";
        return current.GetProperty(fieldName).GetString() ?? "";
    }

    public byte[] ReadBytes(string fieldName)
    {
        var current = Current;
        if (current.ValueKind == JsonValueKind.String)
            return current.GetBytesFromBase64();
        return current.GetProperty(fieldName).GetBytesFromBase64();
    }

    public void EnterBitsStruct(string fieldName)
    {
        var current = Current;
        _elementStack.Push(current.GetProperty(fieldName));
    }

    public bool ReadBit(string fieldName)
    {
        var current = Current;
        return current.GetProperty(fieldName).GetBoolean();
    }

    public ulong ReadBits(string fieldName, int bitCount)
    {
        var current = Current;
        return current.GetProperty(fieldName).GetUInt64();
    }

    public void ExitBitsStruct() => _elementStack.Pop();

    public string ReadVariantType(string fieldName)
    {
        var current = Current;
        var obj = current.GetProperty(fieldName);
        return obj.GetProperty("_variant").GetString()!;
    }

    public void EnterVariant(string fieldName, string variantName)
    {
        var current = Current;
        _elementStack.Push(current.GetProperty(fieldName));
    }

    public void ExitVariant() => _elementStack.Pop();

    public bool HasField(string fieldName)
    {
        return Current.TryGetProperty(fieldName, out _);
    }

    public void Dispose()
    {
        if (_ownsDoc)
            _doc.Dispose();
    }
}
