namespace BinScript.Core.Compiler.Ast;

/// <summary>Coverage mode for a struct declaration.</summary>
public enum CoverageMode
{
    /// <summary>The struct does not need to consume every byte of its input.</summary>
    Partial,
}

/// <summary>A struct declaration that describes a binary layout.</summary>
public sealed record StructDecl(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<MemberDecl> Members,
    bool IsRoot,
    CoverageMode? Coverage,
    int? MaxDepth,
    SourceSpan Span) : AstNode(Span);
