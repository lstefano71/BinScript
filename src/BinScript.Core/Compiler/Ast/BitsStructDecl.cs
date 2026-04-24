namespace BinScript.Core.Compiler.Ast;

/// <summary>The type of a single field inside a <c>bits</c> struct.</summary>
public abstract record BitFieldType;

/// <summary>A single-bit field: <c>bit</c>.</summary>
public sealed record SingleBit : BitFieldType;

/// <summary>A multi-bit field: <c>bits[N]</c>.</summary>
public sealed record MultiBits(int Count) : BitFieldType;

/// <summary>A field within a <c>bits</c> struct.</summary>
public sealed record BitFieldDecl(string Name, BitFieldType Type, SourceSpan Span) : AstNode(Span);

/// <summary>
/// A <c>bits</c> struct declaration that maps individual bits/bit-ranges of an underlying integral type.
/// </summary>
public sealed record BitsStructDecl(
    string Name,
    TypeReference BaseType,
    IReadOnlyList<BitFieldDecl> Fields,
    SourceSpan Span) : AstNode(Span);
