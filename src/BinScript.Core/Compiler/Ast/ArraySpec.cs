namespace BinScript.Core.Compiler.Ast;

/// <summary>Specifies how an array field determines its element count or termination.</summary>
public abstract record ArraySpec(SourceSpan Span) : AstNode(Span);

/// <summary>Fixed or expression-based count: <c>[count]</c>.</summary>
public sealed record CountArraySpec(Expression Count, SourceSpan Span) : ArraySpec(Span);

/// <summary>Read until a boolean condition becomes true: <c>@until(expr)</c>.</summary>
public sealed record UntilArraySpec(Expression Condition, SourceSpan Span) : ArraySpec(Span);

/// <summary>Read until a sentinel predicate matches: <c>@until_sentinel(name, predicate)</c>.</summary>
public sealed record SentinelArraySpec(string ParamName, Expression Predicate, SourceSpan Span) : ArraySpec(Span);

/// <summary>Read greedily until input is exhausted: <c>@greedy</c>.</summary>
public sealed record GreedyArraySpec(SourceSpan Span) : ArraySpec(Span);
