namespace BinScript.Tests.Runtime;

using BinScript.Core.Runtime;

public class ExpressionEvaluatorTests
{
    private static ParseContext MakeContext() => new(ReadOnlyMemory<byte>.Empty);

    // ─── Arithmetic ──────────────────────────────────────────────────

    [Fact]
    public void Add_Integers()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(10));
        ctx.Push(StackValue.FromInt(3));
        ExpressionEvaluator.Add(ctx);
        Assert.Equal(13, ctx.Pop().IntValue);
    }

    [Fact]
    public void Add_Floats()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromFloat(1.5));
        ctx.Push(StackValue.FromFloat(2.5));
        ExpressionEvaluator.Add(ctx);
        Assert.Equal(4.0, ctx.Pop().FloatValue);
    }

    [Fact]
    public void Sub_Integers()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(10));
        ctx.Push(StackValue.FromInt(3));
        ExpressionEvaluator.Sub(ctx);
        Assert.Equal(7, ctx.Pop().IntValue);
    }

    [Fact]
    public void Mul_Integers()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(6));
        ctx.Push(StackValue.FromInt(7));
        ExpressionEvaluator.Mul(ctx);
        Assert.Equal(42, ctx.Pop().IntValue);
    }

    [Fact]
    public void Div_Integers()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(20));
        ctx.Push(StackValue.FromInt(4));
        ExpressionEvaluator.Div(ctx);
        Assert.Equal(5, ctx.Pop().IntValue);
    }

    [Fact]
    public void Div_ByZero_ReturnsZero()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(10));
        ctx.Push(StackValue.FromInt(0));
        ExpressionEvaluator.Div(ctx);
        Assert.Equal(0, ctx.Pop().IntValue);
    }

    [Fact]
    public void Mod_Integers()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(17));
        ctx.Push(StackValue.FromInt(5));
        ExpressionEvaluator.Mod(ctx);
        Assert.Equal(2, ctx.Pop().IntValue);
    }

    // ─── Comparisons ─────────────────────────────────────────────────

    [Fact]
    public void Eq_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(42));
        ctx.Push(StackValue.FromInt(42));
        ExpressionEvaluator.Eq(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void Eq_False()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(42));
        ctx.Push(StackValue.FromInt(43));
        ExpressionEvaluator.Eq(ctx);
        Assert.False(ctx.Pop().AsBool());
    }

    [Fact]
    public void Ne_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(1));
        ctx.Push(StackValue.FromInt(2));
        ExpressionEvaluator.Ne(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void Lt_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(1));
        ctx.Push(StackValue.FromInt(2));
        ExpressionEvaluator.Lt(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void Gt_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(5));
        ctx.Push(StackValue.FromInt(3));
        ExpressionEvaluator.Gt(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void Le_EqualValues()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(5));
        ctx.Push(StackValue.FromInt(5));
        ExpressionEvaluator.Le(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void Ge_EqualValues()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(5));
        ctx.Push(StackValue.FromInt(5));
        ExpressionEvaluator.Ge(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    // ─── Bitwise ─────────────────────────────────────────────────────

    [Fact]
    public void And_Integers()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(0xFF));
        ctx.Push(StackValue.FromInt(0x0F));
        ExpressionEvaluator.And(ctx);
        Assert.Equal(0x0F, ctx.Pop().IntValue);
    }

    [Fact]
    public void Or_Integers()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(0xF0));
        ctx.Push(StackValue.FromInt(0x0F));
        ExpressionEvaluator.Or(ctx);
        Assert.Equal(0xFF, ctx.Pop().IntValue);
    }

    [Fact]
    public void Xor_Integers()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(0xFF));
        ctx.Push(StackValue.FromInt(0x0F));
        ExpressionEvaluator.Xor(ctx);
        Assert.Equal(0xF0, ctx.Pop().IntValue);
    }

    [Fact]
    public void Not_Integer()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(0));
        ExpressionEvaluator.Not(ctx);
        Assert.Equal(~0L, ctx.Pop().IntValue);
    }

    [Fact]
    public void Shl_Integers()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(1));
        ctx.Push(StackValue.FromInt(4));
        ExpressionEvaluator.Shl(ctx);
        Assert.Equal(16, ctx.Pop().IntValue);
    }

    [Fact]
    public void Shr_Integers()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(16));
        ctx.Push(StackValue.FromInt(2));
        ExpressionEvaluator.Shr(ctx);
        Assert.Equal(4, ctx.Pop().IntValue);
    }

    // ─── Logical ─────────────────────────────────────────────────────

    [Fact]
    public void LogicalAnd_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromBool(true));
        ctx.Push(StackValue.FromBool(true));
        ExpressionEvaluator.LogicalAnd(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void LogicalAnd_False()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromBool(true));
        ctx.Push(StackValue.FromBool(false));
        ExpressionEvaluator.LogicalAnd(ctx);
        Assert.False(ctx.Pop().AsBool());
    }

    [Fact]
    public void LogicalOr_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromBool(false));
        ctx.Push(StackValue.FromBool(true));
        ExpressionEvaluator.LogicalOr(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void LogicalNot_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromBool(true));
        ExpressionEvaluator.LogicalNot(ctx);
        Assert.False(ctx.Pop().AsBool());
    }

    // ─── Unary ───────────────────────────────────────────────────────

    [Fact]
    public void Negate_Integer()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(42));
        ExpressionEvaluator.Negate(ctx);
        Assert.Equal(-42, ctx.Pop().IntValue);
    }

    [Fact]
    public void Negate_Float()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromFloat(3.14));
        ExpressionEvaluator.Negate(ctx);
        Assert.Equal(-3.14, ctx.Pop().FloatValue);
    }

    // ─── String methods ──────────────────────────────────────────────

    [Fact]
    public void StartsWith_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromString("hello world"));
        ctx.Push(StackValue.FromString("hello"));
        ExpressionEvaluator.StartsWith(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void StartsWith_False()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromString("hello world"));
        ctx.Push(StackValue.FromString("world"));
        ExpressionEvaluator.StartsWith(ctx);
        Assert.False(ctx.Pop().AsBool());
    }

    [Fact]
    public void EndsWith_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromString("hello world"));
        ctx.Push(StackValue.FromString("world"));
        ExpressionEvaluator.EndsWith(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void Contains_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromString("hello world"));
        ctx.Push(StackValue.FromString("lo wo"));
        ExpressionEvaluator.Contains(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void Contains_False()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromString("hello"));
        ctx.Push(StackValue.FromString("xyz"));
        ExpressionEvaluator.Contains(ctx);
        Assert.False(ctx.Pop().AsBool());
    }

    // ─── Runtime vars ────────────────────────────────────────────────

    [Fact]
    public void RuntimeVar_InputSize()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var ctx = new ParseContext(data);
        Assert.Equal(5, ctx.InputSize);
    }

    [Fact]
    public void RuntimeVar_Offset()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var ctx = new ParseContext(data);
        ctx.Position = 3;
        Assert.Equal(3, ctx.Offset);
    }

    [Fact]
    public void RuntimeVar_Remaining()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var ctx = new ParseContext(data);
        ctx.Position = 2;
        Assert.Equal(3, ctx.Remaining);
    }

    // ─── Mixed types ─────────────────────────────────────────────────

    [Fact]
    public void Add_IntAndFloat_ProducesFloat()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromInt(10));
        ctx.Push(StackValue.FromFloat(0.5));
        ExpressionEvaluator.Add(ctx);
        var result = ctx.Pop();
        Assert.Equal(StackValueKind.Float, result.Kind);
        Assert.Equal(10.5, result.FloatValue);
    }

    [Fact]
    public void Eq_Strings_True()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromString("abc"));
        ctx.Push(StackValue.FromString("abc"));
        ExpressionEvaluator.Eq(ctx);
        Assert.True(ctx.Pop().AsBool());
    }

    [Fact]
    public void Eq_Strings_False()
    {
        var ctx = MakeContext();
        ctx.Push(StackValue.FromString("abc"));
        ctx.Push(StackValue.FromString("def"));
        ExpressionEvaluator.Eq(ctx);
        Assert.False(ctx.Pop().AsBool());
    }
}
