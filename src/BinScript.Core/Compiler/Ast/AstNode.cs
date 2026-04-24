namespace BinScript.Core.Compiler.Ast;

/// <summary>Abstract base for every node in the BinScript AST.</summary>
public abstract record AstNode(SourceSpan Span);
