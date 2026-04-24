namespace BinScript.Core.Compiler.Ast;

/// <summary>A single variant in an <c>enum</c> declaration.</summary>
public sealed record EnumVariant(string Name, Expression Value, SourceSpan Span) : AstNode(Span);

/// <summary>An <c>enum</c> declaration mapping names to integral values.</summary>
public sealed record EnumDecl(
    string Name,
    TypeReference BaseType,
    IReadOnlyList<EnumVariant> Variants,
    SourceSpan Span) : AstNode(Span);
