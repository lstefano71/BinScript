namespace BinScript.Core.Compiler.Ast;

/// <summary><c>@import "filename"</c> — imports declarations from another <c>.bsx</c> file.</summary>
public sealed record ImportDecl(string Path, SourceSpan Span) : AstNode(Span);
