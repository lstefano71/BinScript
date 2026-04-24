namespace BinScript.Tests.Compiler;

using System.Buffers.Binary;
using BinScript.Core.Api;
using BinScript.Core.Bytecode;

public class InfrastructureTests
{
    // ── DictionaryModuleResolver ──────────────────────────────────────

    [Fact]
    public void ModuleResolver_AddAndResolve()
    {
        var resolver = new DictionaryModuleResolver();
        resolver.AddModule("math", "struct Vec2 { x: f32; y: f32; }");

        Assert.Equal("struct Vec2 { x: f32; y: f32; }", resolver.ResolveModule("math"));
    }

    [Fact]
    public void ModuleResolver_ReturnsNullForUnknown()
    {
        var resolver = new DictionaryModuleResolver();
        Assert.Null(resolver.ResolveModule("nonexistent"));
    }

    [Fact]
    public void ModuleResolver_OverwritesExistingModule()
    {
        var resolver = new DictionaryModuleResolver();
        resolver.AddModule("fmt", "v1");
        resolver.AddModule("fmt", "v2");

        Assert.Equal("v2", resolver.ResolveModule("fmt"));
    }

    [Fact]
    public void ModuleResolver_MultipleModules()
    {
        var resolver = new DictionaryModuleResolver();
        resolver.AddModule("a", "sourceA");
        resolver.AddModule("b", "sourceB");

        Assert.Equal("sourceA", resolver.ResolveModule("a"));
        Assert.Equal("sourceB", resolver.ResolveModule("b"));
        Assert.Null(resolver.ResolveModule("c"));
    }

    // ── BytecodeBuilder — Emit ────────────────────────────────────────

    [Fact]
    public void Builder_EmitOpcode()
    {
        var b = new BytecodeBuilder();
        b.Emit(Opcode.ReadU8);
        b.Emit(Opcode.Return);

        var bytes = b.ToArray();
        Assert.Equal(2, bytes.Length);
        Assert.Equal((byte)Opcode.ReadU8, bytes[0]);
        Assert.Equal((byte)Opcode.Return, bytes[1]);
    }

    [Fact]
    public void Builder_EmitU8()
    {
        var b = new BytecodeBuilder();
        b.EmitU8(0xFF);

        Assert.Equal([0xFF], b.ToArray());
    }

    [Fact]
    public void Builder_EmitU16_LittleEndian()
    {
        var b = new BytecodeBuilder();
        b.EmitU16(0x1234);

        var bytes = b.ToArray();
        Assert.Equal(2, bytes.Length);
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
    }

    [Fact]
    public void Builder_EmitI32_LittleEndian()
    {
        var b = new BytecodeBuilder();
        b.EmitI32(-42);

        var bytes = b.ToArray();
        Assert.Equal(4, bytes.Length);
        Assert.Equal(-42, BinaryPrimitives.ReadInt32LittleEndian(bytes));
    }

    [Fact]
    public void Builder_EmitU32_LittleEndian()
    {
        var b = new BytecodeBuilder();
        b.EmitU32(0xDEADBEEF);

        var bytes = b.ToArray();
        Assert.Equal(4, bytes.Length);
        Assert.Equal(0xDEADBEEF, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
    }

    [Fact]
    public void Builder_EmitI64_LittleEndian()
    {
        var b = new BytecodeBuilder();
        b.EmitI64(long.MinValue);

        var bytes = b.ToArray();
        Assert.Equal(8, bytes.Length);
        Assert.Equal(long.MinValue, BinaryPrimitives.ReadInt64LittleEndian(bytes));
    }

    [Fact]
    public void Builder_EmitF64_LittleEndian()
    {
        var b = new BytecodeBuilder();
        b.EmitF64(3.14);

        var bytes = b.ToArray();
        Assert.Equal(8, bytes.Length);
        Assert.Equal(3.14, BinaryPrimitives.ReadDoubleLittleEndian(bytes));
    }

    // ── BytecodeBuilder — String Interning ────────────────────────────

    [Fact]
    public void Builder_InternString_ReturnsSameIndex()
    {
        var b = new BytecodeBuilder();
        var idx1 = b.InternString("hello");
        var idx2 = b.InternString("hello");

        Assert.Equal(idx1, idx2);
    }

    [Fact]
    public void Builder_InternString_DifferentStrings()
    {
        var b = new BytecodeBuilder();
        var i0 = b.InternString("alpha");
        var i1 = b.InternString("beta");
        var i2 = b.InternString("gamma");

        Assert.Equal((ushort)0, i0);
        Assert.Equal((ushort)1, i1);
        Assert.Equal((ushort)2, i2);

        var table = b.GetStringTable();
        Assert.Equal(["alpha", "beta", "gamma"], table);
    }

    // ── BytecodeBuilder — Patch / Reserve ─────────────────────────────

    [Fact]
    public void Builder_ReserveAndPatchI32()
    {
        var b = new BytecodeBuilder();
        b.Emit(Opcode.Jump);
        int hole = b.ReserveI32();

        // emit some filler
        b.Emit(Opcode.ReadU8);
        b.Emit(Opcode.ReadU8);

        // patch jump target
        b.PatchI32(hole, b.Position);

        var bytes = b.ToArray();
        int patched = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(hole));
        Assert.Equal(7, patched); // 1 (Jump) + 4 (reserved) + 2 (ReadU8 x2)
    }

    [Fact]
    public void Builder_Position_TracksEmittedBytes()
    {
        var b = new BytecodeBuilder();
        Assert.Equal(0, b.Position);

        b.Emit(Opcode.Return);
        Assert.Equal(1, b.Position);

        b.EmitI32(100);
        Assert.Equal(5, b.Position);

        b.EmitI64(200);
        Assert.Equal(13, b.Position);
    }

    // ── Opcode enum hex values ────────────────────────────────────────

    [Theory]
    [InlineData(Opcode.ReadU8, 0x01)]
    [InlineData(Opcode.ReadBool, 0x13)]
    [InlineData(Opcode.AssertValue, 0x18)]
    [InlineData(Opcode.CallStruct, 0x40)]
    [InlineData(Opcode.Return, 0x41)]
    [InlineData(Opcode.EmitStructBegin, 0x60)]
    [InlineData(Opcode.PushConstI64, 0x80)]
    [InlineData(Opcode.OpAdd, 0x90)]
    [InlineData(Opcode.OpNeg, 0xA4)]
    [InlineData(Opcode.FnSizeOf, 0xB0)]
    [InlineData(Opcode.ArrayBeginCount, 0xD0)]
    [InlineData(Opcode.MatchBegin, 0xE0)]
    [InlineData(Opcode.Align, 0xF0)]
    [InlineData(Opcode.AlignFixed, 0xF1)]
    public void Opcode_HexValues(Opcode op, byte expected)
    {
        Assert.Equal(expected, (byte)op);
    }

    // ── StringEncoding / RuntimeVar ───────────────────────────────────

    [Theory]
    [InlineData(StringEncoding.Utf8, 0)]
    [InlineData(StringEncoding.Ascii, 1)]
    [InlineData(StringEncoding.Utf16Le, 2)]
    [InlineData(StringEncoding.Utf16Be, 3)]
    [InlineData(StringEncoding.Latin1, 4)]
    public void StringEncoding_Values(StringEncoding enc, byte expected)
    {
        Assert.Equal(expected, (byte)enc);
    }

    [Theory]
    [InlineData(RuntimeVar.InputSize, 0)]
    [InlineData(RuntimeVar.Offset, 1)]
    [InlineData(RuntimeVar.Remaining, 2)]
    public void RuntimeVar_Values(RuntimeVar rv, byte expected)
    {
        Assert.Equal(expected, (byte)rv);
    }

    // ── BytecodeProgram ───────────────────────────────────────────────

    [Fact]
    public void Program_FindStruct_ByName()
    {
        var program = new BytecodeProgram
        {
            Bytecode = [],
            StringTable = ["Header", "Body"],
            Structs =
            [
                new StructMeta
                {
                    NameIndex = 0, ParamCount = 0, Fields = [],
                    BytecodeOffset = 0, BytecodeLength = 10,
                    StaticSize = 8, Flags = StructFlags.IsRoot,
                },
                new StructMeta
                {
                    NameIndex = 1, ParamCount = 0, Fields = [],
                    BytecodeOffset = 10, BytecodeLength = 20,
                    StaticSize = -1, Flags = StructFlags.None,
                },
            ],
            RootStructIndex = 0,
            Parameters = new(),
        };

        Assert.NotNull(program.FindStruct("Header"));
        Assert.Equal(0, program.FindStructIndex("Header"));
        Assert.Equal(1, program.FindStructIndex("Body"));
        Assert.Null(program.FindStruct("Missing"));
        Assert.Equal(-1, program.FindStructIndex("Missing"));
    }

    [Fact]
    public void Program_GetString()
    {
        var program = new BytecodeProgram
        {
            Bytecode = [],
            StringTable = ["foo", "bar"],
            Structs = [],
            RootStructIndex = -1,
            Parameters = new(),
        };

        Assert.Equal("foo", program.GetString(0));
        Assert.Equal("bar", program.GetString(1));
    }

    // ── StructFlags / FieldFlags ──────────────────────────────────────

    [Fact]
    public void StructFlags_Combinable()
    {
        var flags = StructFlags.IsRoot | StructFlags.IsBits;
        Assert.True(flags.HasFlag(StructFlags.IsRoot));
        Assert.True(flags.HasFlag(StructFlags.IsBits));
        Assert.False(flags.HasFlag(StructFlags.IsPartialCoverage));
    }

    [Fact]
    public void FieldFlags_Values()
    {
        Assert.Equal(0, (byte)FieldFlags.None);
        Assert.Equal(1, (byte)FieldFlags.Derived);
        Assert.Equal(2, (byte)FieldFlags.Hidden);
        Assert.Equal(3, (byte)(FieldFlags.Derived | FieldFlags.Hidden));
    }
}
