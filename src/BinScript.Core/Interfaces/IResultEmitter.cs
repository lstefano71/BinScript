namespace BinScript.Core.Interfaces;

/// <summary>
/// Receives structured data events during binary parsing.
/// Implementations transform these events into an output format (JSON, etc.).
/// </summary>
public interface IResultEmitter
{
    void BeginRoot(string structName);
    void EndRoot();
    void BeginStruct(string fieldName, string structName);
    void EndStruct();
    void BeginArray(string fieldName, int count);
    void EndArray();
    void EmitInt(string fieldName, long value, string? enumName);
    void EmitUInt(string fieldName, ulong value, string? enumName);
    void EmitFloat(string fieldName, double value);
    void EmitBool(string fieldName, bool value);
    void EmitString(string fieldName, string value);
    void EmitBytes(string fieldName, ReadOnlySpan<byte> value);
    void BeginBitsStruct(string fieldName, string structName);
    void EndBitsStruct();
    void EmitBit(string fieldName, bool value);
    void EmitBits(string fieldName, ulong value, int bitCount);
    void BeginVariant(string fieldName, string variantName);
    void EndVariant();
    void EmitNull(string fieldName);
}
