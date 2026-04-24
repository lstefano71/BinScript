namespace BinScript.Core.Bytecode;

/// <summary>String encoding identifiers used in bytecode operands.</summary>
public enum StringEncoding : byte
{
    Utf8 = 0,
    Ascii = 1,
    Utf16Le = 2,
    Utf16Be = 3,
    Latin1 = 4,
}

/// <summary>Runtime variable identifiers for PushRuntimeVar.</summary>
public enum RuntimeVar : byte
{
    InputSize = 0,
    Offset = 1,
    Remaining = 2,
}

/// <summary>Metadata for a struct definition in the bytecode.</summary>
public sealed class StructMeta
{
    public required ushort NameIndex { get; init; }
    public required byte ParamCount { get; init; }
    public required FieldMeta[] Fields { get; init; }
    public required int BytecodeOffset { get; init; }
    public required int BytecodeLength { get; init; }
    public required int StaticSize { get; init; } // -1 if dynamic
    public required StructFlags Flags { get; init; }
}

[Flags]
public enum StructFlags : ushort
{
    None = 0,
    IsRoot = 1 << 0,
    IsBits = 1 << 1,
    IsPartialCoverage = 1 << 2,
}

/// <summary>Metadata for a single field within a struct.</summary>
public sealed class FieldMeta
{
    public required ushort NameIndex { get; init; }
    public required FieldFlags Flags { get; init; }
}

[Flags]
public enum FieldFlags : byte
{
    None = 0,
    Derived = 1 << 0,
    Hidden = 1 << 1,
}

/// <summary>
/// A compiled BinScript program ready for execution.
/// Immutable after construction — safe to share across threads.
/// </summary>
public sealed class BytecodeProgram
{
    /// <summary>The raw bytecode instructions.</summary>
    public required byte[] Bytecode { get; init; }

    /// <summary>Interned strings table (field names, struct names, string literals).</summary>
    public required string[] StringTable { get; init; }

    /// <summary>Struct definitions with their metadata.</summary>
    public required StructMeta[] Structs { get; init; }

    /// <summary>Index of the @root struct in the Structs array. -1 if none.</summary>
    public required int RootStructIndex { get; init; }

    /// <summary>Baked compile-time parameters.</summary>
    public required Dictionary<string, object> Parameters { get; init; }

    /// <summary>Original source text (optional, can be stripped).</summary>
    public string? Source { get; init; }

    /// <summary>Format version.</summary>
    public ushort Version { get; init; } = 1;

    /// <summary>Look up a string by its index.</summary>
    public string GetString(ushort index) => StringTable[index];

    /// <summary>Find the struct metadata by name index.</summary>
    public StructMeta? FindStruct(string name)
    {
        for (int i = 0; i < Structs.Length; i++)
        {
            if (StringTable[Structs[i].NameIndex] == name)
                return Structs[i];
        }
        return null;
    }

    /// <summary>Find a struct's index by name.</summary>
    public int FindStructIndex(string name)
    {
        for (int i = 0; i < Structs.Length; i++)
        {
            if (StringTable[Structs[i].NameIndex] == name)
                return i;
        }
        return -1;
    }
}
