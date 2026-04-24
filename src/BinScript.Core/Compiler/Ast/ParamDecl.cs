namespace BinScript.Core.Compiler.Ast;

/// <summary><c>@param name: type</c> — a file-level parameter supplied at parse time.</summary>
public sealed record ParamDecl(string Name, TypeReference Type, SourceSpan Span) : AstNode(Span);
