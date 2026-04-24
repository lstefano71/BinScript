namespace BinScript.Core.Compiler.Ast;

/// <summary>A top-level constant: <c>const PE_MAGIC = 0x5A4D</c>.</summary>
public sealed record ConstDecl(string Name, Expression Value, SourceSpan Span) : AstNode(Span);
