namespace BinScript.Core.Compiler.Ast;

/// <summary>
/// Base for anything that can appear inside a struct body:
/// field declarations, positional directives, let-bindings, etc.
/// </summary>
public abstract record MemberDecl(SourceSpan Span) : AstNode(Span);

/// <summary>
/// Field modifiers that can be attached to a <see cref="FieldDecl"/> via directives.
/// </summary>
public sealed record FieldModifiers
{
    public bool IsHidden { get; init; }
    public bool IsDerived { get; init; }
    public string? Encoding { get; init; }
    public Expression? AssertExpression { get; init; }
    public string? AssertMessage { get; init; }
    public Expression? DerivedExpression { get; init; }
}

/// <summary>A data field read from (or computed over) the binary stream.</summary>
public sealed record FieldDecl(
    string Name,
    TypeReference Type,
    Expression? MagicValue,
    ArraySpec? Array,
    FieldModifiers Modifiers,
    SourceSpan Span) : MemberDecl(Span);

/// <summary><c>@seek(expr)</c> — repositions the stream before the next field.</summary>
public sealed record SeekDirective(Expression Offset, SourceSpan Span) : MemberDecl(Span);

/// <summary><c>@at(expr) { ... }</c> — reads members at an absolute offset, then restores position.</summary>
public sealed record AtBlock(Expression Offset, IReadOnlyList<MemberDecl> Members, SourceSpan Span) : MemberDecl(Span);

/// <summary><c>@let name = expr</c> — introduces a computed binding visible to later members.</summary>
public sealed record LetBinding(string Name, Expression Value, SourceSpan Span) : MemberDecl(Span);

/// <summary><c>@align(expr)</c> — aligns the stream to the given boundary.</summary>
public sealed record AlignDirective(Expression Alignment, SourceSpan Span) : MemberDecl(Span);

/// <summary><c>@skip(N)</c> — advances the stream by a fixed number of bytes.</summary>
public sealed record SkipDirective(int ByteCount, SourceSpan Span) : MemberDecl(Span);

/// <summary><c>@assert(expr, "msg")</c> — standalone assertion that must hold at this point.</summary>
public sealed record AssertDirective(Expression Condition, string Message, SourceSpan Span) : MemberDecl(Span);
