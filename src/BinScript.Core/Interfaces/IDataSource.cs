namespace BinScript.Core.Interfaces;

/// <summary>
/// Provides structured data values during binary production.
/// Implementations read from an input format (JSON, etc.).
/// </summary>
public interface IDataSource
{
    void EnterRoot(string structName);
    void ExitRoot();
    void EnterStruct(string fieldName, string structName);
    void ExitStruct();
    void EnterArray(string fieldName);
    int GetArrayLength();
    void EnterArrayElement(int index);
    void ExitArrayElement();
    void ExitArray();
    long ReadInt(string fieldName);
    ulong ReadUInt(string fieldName);
    double ReadFloat(string fieldName);
    bool ReadBool(string fieldName);
    string ReadString(string fieldName);
    byte[] ReadBytes(string fieldName);
    void EnterBitsStruct(string fieldName);
    bool ReadBit(string fieldName);
    ulong ReadBits(string fieldName, int bitCount);
    void ExitBitsStruct();
    string ReadVariantType(string fieldName);
    void EnterVariant(string fieldName, string variantName);
    void ExitVariant();
    bool HasField(string fieldName);
}
