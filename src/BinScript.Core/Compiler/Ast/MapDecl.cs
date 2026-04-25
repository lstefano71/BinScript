namespace BinScript.Core.Compiler.Ast;

/// <summary>A typed parameter in a <c>@map</c> declaration.</summary>
public sealed record MapParam(string Name, TypeReference Type, SourceSpan Span) : AstNode(Span);

/// <summary>
/// A <c>@map</c> declaration — a named pure expression inlined at call sites.
/// <code>@map name(param: type, ...): return_type = expression</code>
/// </summary>
public sealed record MapDecl(
    string Name,
    IReadOnlyList<MapParam> Params,
    TypeReference ReturnType,
    Expression Body,
    SourceSpan Span) : AstNode(Span);
