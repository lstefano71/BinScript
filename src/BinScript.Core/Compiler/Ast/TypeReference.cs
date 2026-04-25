namespace BinScript.Core.Compiler.Ast;

/// <summary>Represents a type used in a field declaration.</summary>
public abstract record TypeReference(SourceSpan Span) : AstNode(Span);

/// <summary>A built-in primitive: <c>u8</c>, <c>u32le</c>, <c>bool</c>, etc.</summary>
public sealed record PrimitiveTypeRef(TokenType PrimitiveToken, SourceSpan Span) : TypeReference(Span);

/// <summary>A named struct or enum type, optionally parameterised: <c>CoffHeader</c>, <c>OptionalHeader(arch)</c>.</summary>
public sealed record NamedTypeRef(
    string Name,
    IReadOnlyList<Expression> Arguments,
    SourceSpan Span) : TypeReference(Span);

/// <summary>Null-terminated C string: <c>cstring</c>.</summary>
public sealed record CStringTypeRef(SourceSpan Span) : TypeReference(Span);

/// <summary>Length-prefixed or bounded string: <c>string[expr]</c>.</summary>
public sealed record StringTypeRef(Expression Length, SourceSpan Span) : TypeReference(Span);

/// <summary>Fixed-width string: <c>fixed_string[N]</c>.</summary>
public sealed record FixedStringTypeRef(Expression Length, SourceSpan Span) : TypeReference(Span);

/// <summary>Raw byte span: <c>bytes[expr]</c> or <c>bytes[@remaining]</c>.</summary>
public sealed record BytesTypeRef(Expression Length, SourceSpan Span) : TypeReference(Span);

/// <summary>Single bit inside a <c>bits</c> struct.</summary>
public sealed record BitTypeRef(SourceSpan Span) : TypeReference(Span);

/// <summary>Multi-bit field inside a <c>bits</c> struct: <c>bits[N]</c>.</summary>
public sealed record BitsTypeRef(int Count, SourceSpan Span) : TypeReference(Span);

/// <summary>
/// Type resolved by a match expression — wraps a <see cref="MatchExpr"/>
/// so it can appear in type position.
/// </summary>
public sealed record MatchTypeRef(MatchExpr Match, SourceSpan Span) : TypeReference(Span);

/// <summary>Pointer type: <c>ptr&lt;T, u64&gt;</c> or <c>relptr&lt;T, u32&gt;</c>.</summary>
public sealed record PtrTypeRef(
    TypeReference InnerType,
    PrimitiveTypeRef? Width,
    bool IsRelative,
    FieldModifiers? InnerModifiers,
    SourceSpan Span) : TypeReference(Span);

/// <summary>Nullable wrapper: <c>T?</c>.</summary>
public sealed record NullableTypeRef(TypeReference InnerType, SourceSpan Span) : TypeReference(Span);

/// <summary>Array type in annotation position: <c>SectionHeader[]</c> or <c>u32[count]</c>. Used in <c>@map</c> params and <c>ptr</c> inner types.</summary>
public sealed record ArrayTypeRef(TypeReference ElementType, Expression? Count, SourceSpan Span) : TypeReference(Span);
