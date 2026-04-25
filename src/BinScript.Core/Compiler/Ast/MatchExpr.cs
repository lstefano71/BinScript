namespace BinScript.Core.Compiler.Ast;

/// <summary>
/// A match expression that maps a discriminant value to a <see cref="TypeReference"/>
/// (used for type-level dispatch in field declarations).
/// </summary>
public sealed record MatchExpr(
    Expression Discriminant,
    IReadOnlyList<MatchArm> Arms,
    SourceSpan Span) : Expression(Span);

public sealed record MatchArm(MatchPattern Pattern, Expression? Guard, TypeReference Type, ArraySpec? Array, SourceSpan Span) : AstNode(Span);

// ── Match patterns ───────────────────────────────────────────────────────────

public abstract record MatchPattern(SourceSpan Span) : AstNode(Span);

/// <summary>Matches a single value: <c>1</c>, <c>0x5A4D</c>, <c>"PE"</c>.</summary>
public sealed record ValuePattern(Expression Value, SourceSpan Span) : MatchPattern(Span);

/// <summary>Matches a range: <c>1..10</c>.</summary>
public sealed record RangePattern(Expression Low, Expression High, SourceSpan Span) : MatchPattern(Span);

/// <summary>Wildcard pattern: <c>_</c>.</summary>
public sealed record WildcardPattern(SourceSpan Span) : MatchPattern(Span);

/// <summary>Identifier pattern used with a guard: <c>s when s.starts_with(...)</c>.</summary>
public sealed record IdentifierPattern(string Name, SourceSpan Span) : MatchPattern(Span);
