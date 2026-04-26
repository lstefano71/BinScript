namespace BinScript.NALight;

/// <summary>Direction markers for arguments and struct fields.</summary>
public enum NADirection
{
    /// <summary>No direction specified (bare type).</summary>
    None,
    /// <summary>Input to DLL (<c>&lt;</c>).</summary>
    In,
    /// <summary>Output from DLL (<c>&gt;</c>).</summary>
    Out,
    /// <summary>In/out (<c>=</c>).</summary>
    InOut,
}

/// <summary>Alignment mode for structs.</summary>
public enum NAAlignMode
{
    /// <summary>No padding (default, ⎕NA compatible).</summary>
    Packed,
    /// <summary>Natural C alignment (x64 rules).</summary>
    Natural,
}

/// <summary>
/// Top-level parsed representation of a BSX-Light / ⎕NA spec string.
/// </summary>
public sealed record NASpec(
    NATypeDesc? ReturnType,
    string? LibraryPath,
    string? FunctionName,
    bool PassByPointer,
    bool ThreadSafe,
    IReadOnlyList<NAArgDesc> Arguments);

/// <summary>A single argument in the spec (direction + type).</summary>
public sealed record NAArgDesc(NADirection Direction, NATypeDesc Type);

// ── Type description hierarchy ──────────────────────────────────────────────

/// <summary>Base type for all NA type descriptions.</summary>
public abstract record NATypeDesc;

/// <summary>Primitive integer, char, text, or float: <c>I4</c>, <c>U2</c>, <c>C1</c>, <c>T2</c>, <c>F8</c>.</summary>
public sealed record NAPrimitiveDesc(char TypeLetter, int Width) : NATypeDesc;

/// <summary>Dyalog decimal (<c>D</c>): 64-bit float.</summary>
public sealed record NADecimalDesc() : NATypeDesc;

/// <summary>Complex number (<c>J</c>): pair of 64-bit floats.</summary>
public sealed record NAComplexDesc() : NATypeDesc;

/// <summary>Raw pointer (<c>P</c>).</summary>
public sealed record NAPointerDesc() : NATypeDesc;

/// <summary>Null-terminated string: <c>0T</c>, <c>0C</c>, <c>0T2</c>, etc.</summary>
public sealed record NANullTermDesc(char TypeLetter, int Width) : NATypeDesc;

/// <summary>Double-null-terminated string list: <c>00T</c>, <c>00C</c>.</summary>
public sealed record NADoubleNullDesc(char TypeLetter, int Width) : NATypeDesc;

/// <summary>Struct type: <c>{I4 U2 P}</c>.</summary>
public sealed record NAStructDesc(
    IReadOnlyList<NAStructField> Fields,
    NAAlignMode AlignMode,
    int? PackSize) : NATypeDesc;

/// <summary>A single field inside a struct.</summary>
public sealed record NAStructField(NADirection Direction, NATypeDesc Type);

/// <summary>Wraps a type with an array specification.</summary>
public sealed record NAArrayedDesc(NATypeDesc Element, NAArrayKind ArrayKind) : NATypeDesc;

// ── Array kinds ─────────────────────────────────────────────────────────────

/// <summary>Base type for array length specifications.</summary>
public abstract record NAArrayKind;

/// <summary>Fixed-length array: <c>[10]</c>.</summary>
public sealed record FixedArrayKind(int Count) : NAArrayKind;

/// <summary>Variable-length array: <c>[]</c>.</summary>
public sealed record VariableArrayKind() : NAArrayKind;

/// <summary>Counted array: <c>[*]</c> or <c>[*:256]</c>.</summary>
public sealed record CountedArrayKind(int? MaxCount) : NAArrayKind;
