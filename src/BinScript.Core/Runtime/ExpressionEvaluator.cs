namespace BinScript.Core.Runtime;

using System.Runtime.CompilerServices;
using BinScript.Core.Bytecode;

/// <summary>
/// Stack-based expression evaluator for bytecode expressions.
/// All operations work on the ParseContext's evaluation stack.
/// </summary>
public static class ExpressionEvaluator
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Add(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.Float || b.Kind == StackValueKind.Float)
            ctx.Push(StackValue.FromFloat(a.AsDouble() + b.AsDouble()));
        else
            ctx.Push(StackValue.FromInt(a.IntValue + b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sub(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.Float || b.Kind == StackValueKind.Float)
            ctx.Push(StackValue.FromFloat(a.AsDouble() - b.AsDouble()));
        else
            ctx.Push(StackValue.FromInt(a.IntValue - b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Mul(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.Float || b.Kind == StackValueKind.Float)
            ctx.Push(StackValue.FromFloat(a.AsDouble() * b.AsDouble()));
        else
            ctx.Push(StackValue.FromInt(a.IntValue * b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Div(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.Float || b.Kind == StackValueKind.Float)
        {
            double denom = b.AsDouble();
            ctx.Push(StackValue.FromFloat(denom == 0 ? 0 : a.AsDouble() / denom));
        }
        else
        {
            long denom = b.IntValue;
            ctx.Push(StackValue.FromInt(denom == 0 ? 0 : a.IntValue / denom));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Mod(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        long denom = b.AsLong();
        ctx.Push(StackValue.FromInt(denom == 0 ? 0 : a.AsLong() % denom));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void And(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        ctx.Push(StackValue.FromInt(a.IntValue & b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Or(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        ctx.Push(StackValue.FromInt(a.IntValue | b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Xor(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        ctx.Push(StackValue.FromInt(a.IntValue ^ b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Not(ParseContext ctx)
    {
        var a = ctx.Pop();
        ctx.Push(StackValue.FromInt(~a.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Shl(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        ctx.Push(StackValue.FromInt(a.IntValue << (int)b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Shr(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        ctx.Push(StackValue.FromInt(a.IntValue >> (int)b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Eq(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.String && b.Kind == StackValueKind.String)
            ctx.Push(StackValue.FromBool(a.StringValue == b.StringValue));
        else if (a.Kind == StackValueKind.Float || b.Kind == StackValueKind.Float)
            ctx.Push(StackValue.FromBool(a.AsDouble() == b.AsDouble()));
        else
            ctx.Push(StackValue.FromBool(a.IntValue == b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Ne(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.String && b.Kind == StackValueKind.String)
            ctx.Push(StackValue.FromBool(a.StringValue != b.StringValue));
        else if (a.Kind == StackValueKind.Float || b.Kind == StackValueKind.Float)
            ctx.Push(StackValue.FromBool(a.AsDouble() != b.AsDouble()));
        else
            ctx.Push(StackValue.FromBool(a.IntValue != b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Lt(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.Float || b.Kind == StackValueKind.Float)
            ctx.Push(StackValue.FromBool(a.AsDouble() < b.AsDouble()));
        else
            ctx.Push(StackValue.FromBool(a.IntValue < b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Gt(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.Float || b.Kind == StackValueKind.Float)
            ctx.Push(StackValue.FromBool(a.AsDouble() > b.AsDouble()));
        else
            ctx.Push(StackValue.FromBool(a.IntValue > b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Le(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.Float || b.Kind == StackValueKind.Float)
            ctx.Push(StackValue.FromBool(a.AsDouble() <= b.AsDouble()));
        else
            ctx.Push(StackValue.FromBool(a.IntValue <= b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Ge(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.Float || b.Kind == StackValueKind.Float)
            ctx.Push(StackValue.FromBool(a.AsDouble() >= b.AsDouble()));
        else
            ctx.Push(StackValue.FromBool(a.IntValue >= b.IntValue));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogicalAnd(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        ctx.Push(StackValue.FromBool(a.AsBool() && b.AsBool()));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogicalOr(ParseContext ctx)
    {
        var b = ctx.Pop();
        var a = ctx.Pop();
        ctx.Push(StackValue.FromBool(a.AsBool() || b.AsBool()));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogicalNot(ParseContext ctx)
    {
        var a = ctx.Pop();
        ctx.Push(StackValue.FromBool(!a.AsBool()));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Negate(ParseContext ctx)
    {
        var a = ctx.Pop();
        if (a.Kind == StackValueKind.Float)
            ctx.Push(StackValue.FromFloat(-a.FloatValue));
        else
            ctx.Push(StackValue.FromInt(-a.IntValue));
    }

    // String methods
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StartsWith(ParseContext ctx)
    {
        var pattern = ctx.Pop();
        var str = ctx.Pop();
        ctx.Push(StackValue.FromBool(
            (str.StringValue ?? "").StartsWith(pattern.StringValue ?? "", StringComparison.Ordinal)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EndsWith(ParseContext ctx)
    {
        var pattern = ctx.Pop();
        var str = ctx.Pop();
        ctx.Push(StackValue.FromBool(
            (str.StringValue ?? "").EndsWith(pattern.StringValue ?? "", StringComparison.Ordinal)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Contains(ParseContext ctx)
    {
        var pattern = ctx.Pop();
        var str = ctx.Pop();
        ctx.Push(StackValue.FromBool(
            (str.StringValue ?? "").Contains(pattern.StringValue ?? "", StringComparison.Ordinal)));
    }
}
