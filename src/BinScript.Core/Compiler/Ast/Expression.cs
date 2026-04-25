namespace BinScript.Core.Compiler.Ast;

// ── Operator enums ───────────────────────────────────────────────────────────

public enum BinaryOp
{
    Add, Sub, Mul, Div, Mod,
    BitAnd, BitOr, BitXor, Shl, Shr,
    Eq, Ne, Lt, Gt, Le, Ge,
    LogicAnd, LogicOr,
}

public enum UnaryOp
{
    Negate,   // -
    BitNot,   // ~
    LogicNot, // !
}

// ── Expression hierarchy ─────────────────────────────────────────────────────

/// <summary>Base class for all expression AST nodes.</summary>
public abstract record Expression(SourceSpan Span) : AstNode(Span);

public sealed record IntLiteralExpr(long Value, SourceSpan Span) : Expression(Span);

public sealed record StringLiteralExpr(string Value, SourceSpan Span) : Expression(Span);

public sealed record BoolLiteralExpr(bool Value, SourceSpan Span) : Expression(Span);

/// <summary>References a field, constant, or parameter by name.</summary>
public sealed record IdentifierExpr(string Name, SourceSpan Span) : Expression(Span);

/// <summary>Dot access: <c>dos.e_lfanew</c>.</summary>
public sealed record FieldAccessExpr(Expression Object, string FieldName, SourceSpan Span) : Expression(Span);

/// <summary>Bracket access: <c>array[0]</c>.</summary>
public sealed record IndexAccessExpr(Expression Object, Expression Index, SourceSpan Span) : Expression(Span);

public sealed record BinaryExpr(Expression Left, BinaryOp Op, Expression Right, SourceSpan Span) : Expression(Span);

public sealed record UnaryExpr(UnaryOp Op, Expression Operand, SourceSpan Span) : Expression(Span);

/// <summary>Built-in function call: <c>@sizeof(field)</c>, <c>@crc32(data)</c>, etc.</summary>
public sealed record FunctionCallExpr(
    string FunctionName,
    IReadOnlyList<Expression> Args,
    SourceSpan Span) : Expression(Span);

/// <summary>Method call on an expression: <c>s.starts_with("PK")</c>.</summary>
public sealed record MethodCallExpr(
    Expression Object,
    string MethodName,
    IReadOnlyList<Expression> Args,
    SourceSpan Span) : Expression(Span);

/// <summary>Lambda expression: <c>s =&gt; predicate</c>.</summary>
public sealed record LambdaExpr(string Parameter, Expression Body, SourceSpan Span) : Expression(Span);

/// <summary>Null literal: <c>null</c>.</summary>
public sealed record NullLiteralExpr(SourceSpan Span) : Expression(Span);

/// <summary>A let-binding inside a block expression.</summary>
public sealed record MapLetBinding(string Name, Expression Value, SourceSpan Span) : AstNode(Span);

/// <summary>Block expression: <c>{ @let x = expr, ..., result_expr }</c>.</summary>
public sealed record BlockExpr(
    IReadOnlyList<MapLetBinding> Bindings,
    Expression Result,
    SourceSpan Span) : Expression(Span);
