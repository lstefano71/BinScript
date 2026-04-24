namespace BinScript.Emitters.Json;

using System.Text.Json;
using BinScript.Core.Interfaces;

/// <summary>
/// Implements <see cref="IResultEmitter"/> using <see cref="Utf8JsonWriter"/> for zero-allocation JSON output.
/// </summary>
public sealed class JsonResultEmitter : IResultEmitter, IDisposable
{
    private readonly MemoryStream _stream;
    private readonly Utf8JsonWriter _writer;
    private readonly Stack<ContainerKind> _containerStack = new();

    private enum ContainerKind { Object, Array }

    public JsonResultEmitter()
    {
        _stream = new MemoryStream();
        _writer = new Utf8JsonWriter(_stream, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = true,
        });
    }

    public JsonResultEmitter(JsonWriterOptions options)
    {
        _stream = new MemoryStream();
        _writer = new Utf8JsonWriter(_stream, options);
    }

    private bool InArray => _containerStack.Count > 0 && _containerStack.Peek() == ContainerKind.Array;

    private void WritePropertyNameIfNeeded(string fieldName)
    {
        if (!InArray && !string.IsNullOrEmpty(fieldName))
            _writer.WritePropertyName(fieldName);
    }

    public void BeginRoot(string structName)
    {
        _writer.WriteStartObject();
        _containerStack.Push(ContainerKind.Object);
    }

    public void EndRoot()
    {
        _writer.WriteEndObject();
        _containerStack.Pop();
        _writer.Flush();
    }

    public void BeginStruct(string fieldName, string structName)
    {
        WritePropertyNameIfNeeded(fieldName);
        _writer.WriteStartObject();
        _containerStack.Push(ContainerKind.Object);
    }

    public void EndStruct()
    {
        _writer.WriteEndObject();
        _containerStack.Pop();
    }

    public void BeginArray(string fieldName, int count)
    {
        WritePropertyNameIfNeeded(fieldName);
        _writer.WriteStartArray();
        _containerStack.Push(ContainerKind.Array);
    }

    public void EndArray()
    {
        _writer.WriteEndArray();
        _containerStack.Pop();
    }

    public void EmitInt(string fieldName, long value, string? enumName)
    {
        WritePropertyNameIfNeeded(fieldName);
        if (enumName is not null)
            _writer.WriteStringValue(enumName);
        else
            _writer.WriteNumberValue(value);
    }

    public void EmitUInt(string fieldName, ulong value, string? enumName)
    {
        WritePropertyNameIfNeeded(fieldName);
        if (enumName is not null)
            _writer.WriteStringValue(enumName);
        else
            _writer.WriteNumberValue(value);
    }

    public void EmitFloat(string fieldName, double value)
    {
        WritePropertyNameIfNeeded(fieldName);
        _writer.WriteNumberValue(value);
    }

    public void EmitBool(string fieldName, bool value)
    {
        WritePropertyNameIfNeeded(fieldName);
        _writer.WriteBooleanValue(value);
    }

    public void EmitString(string fieldName, string value)
    {
        WritePropertyNameIfNeeded(fieldName);
        _writer.WriteStringValue(value);
    }

    public void EmitBytes(string fieldName, ReadOnlySpan<byte> value)
    {
        WritePropertyNameIfNeeded(fieldName);
        _writer.WriteBase64StringValue(value);
    }

    public void BeginBitsStruct(string fieldName, string structName)
    {
        WritePropertyNameIfNeeded(fieldName);
        _writer.WriteStartObject();
        _containerStack.Push(ContainerKind.Object);
    }

    public void EndBitsStruct()
    {
        _writer.WriteEndObject();
        _containerStack.Pop();
    }

    public void EmitBit(string fieldName, bool value)
    {
        WritePropertyNameIfNeeded(fieldName);
        _writer.WriteBooleanValue(value);
    }

    public void EmitBits(string fieldName, ulong value, int bitCount)
    {
        WritePropertyNameIfNeeded(fieldName);
        _writer.WriteNumberValue(value);
    }

    public void BeginVariant(string fieldName, string variantName)
    {
        WritePropertyNameIfNeeded(fieldName);
        _writer.WriteStartObject();
        _containerStack.Push(ContainerKind.Object);
        _writer.WriteString("_variant", variantName);
    }

    public void EndVariant()
    {
        _writer.WriteEndObject();
        _containerStack.Pop();
    }

    /// <summary>Get the JSON output as a UTF-8 string.</summary>
    public string GetJson()
    {
        _writer.Flush();
        return System.Text.Encoding.UTF8.GetString(_stream.ToArray());
    }

    /// <summary>Get the raw UTF-8 JSON bytes.</summary>
    public ReadOnlyMemory<byte> GetUtf8Json()
    {
        _writer.Flush();
        return _stream.ToArray();
    }

    /// <summary>Reset the writer for reuse.</summary>
    public void Reset()
    {
        _writer.Reset();
        _stream.SetLength(0);
        _containerStack.Clear();
    }

    public void Dispose()
    {
        _writer.Dispose();
        _stream.Dispose();
    }
}
