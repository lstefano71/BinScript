namespace BinScript.Tests.Bytecode;

using BinScript.Core.Bytecode;

public class SerializerTests
{
    // ─── Helpers ──────────────────────────────────────────────────────

    private static BytecodeProgram CreateSampleProgram(
        string? source = null,
        Dictionary<string, object>? parameters = null)
    {
        var builder = new BytecodeBuilder();
        var nameIdx = builder.InternString("TestStruct");
        var fieldIdx = builder.InternString("field_a");

        builder.Emit(Opcode.ReadU8);
        builder.EmitU16(0);
        builder.Emit(Opcode.ReadU16Le);
        builder.EmitU16(1);
        builder.Emit(Opcode.Return);

        return new BytecodeProgram
        {
            Bytecode = builder.ToArray(),
            StringTable = builder.GetStringTable(),
            Structs = new[]
            {
                new StructMeta
                {
                    NameIndex = nameIdx,
                    ParamCount = 0,
                    Fields = new[]
                    {
                        new FieldMeta { NameIndex = fieldIdx, Flags = FieldFlags.None },
                    },
                    BytecodeOffset = 0,
                    BytecodeLength = builder.ToArray().Length,
                    StaticSize = 3,
                    Flags = StructFlags.IsRoot,
                },
            },
            RootStructIndex = 0,
            Parameters = parameters ?? new Dictionary<string, object>(),
            Source = source,
            Version = 1,
        };
    }

    // ─── 1. Round-trip ────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_AllFieldsMatch()
    {
        var original = CreateSampleProgram();
        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.RootStructIndex, restored.RootStructIndex);
        Assert.Equal(original.Bytecode, restored.Bytecode);
        Assert.Equal(original.StringTable, restored.StringTable);
        Assert.Equal(original.Structs.Length, restored.Structs.Length);
        Assert.Null(restored.Source);
    }

    // ─── 2. Magic bytes ──────────────────────────────────────────────

    [Fact]
    public void SerializedOutput_StartsWithMagic()
    {
        var program = CreateSampleProgram();
        byte[] serialized = BytecodeSerializer.Serialize(program);

        Assert.True(serialized.Length >= 4);
        Assert.Equal((byte)'B', serialized[0]);
        Assert.Equal((byte)'S', serialized[1]);
        Assert.Equal((byte)'C', serialized[2]);
        Assert.Equal((byte)0x01, serialized[3]);
    }

    // ─── 3. String table round-trip ──────────────────────────────────

    [Fact]
    public void StringTable_RoundTripsCorrectly()
    {
        var original = CreateSampleProgram();
        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Equal(original.StringTable.Length, restored.StringTable.Length);
        for (int i = 0; i < original.StringTable.Length; i++)
            Assert.Equal(original.StringTable[i], restored.StringTable[i]);
    }

    // ─── 4. Struct metadata round-trip ───────────────────────────────

    [Fact]
    public void StructMetadata_RoundTripsCorrectly()
    {
        var original = CreateSampleProgram();
        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Equal(original.Structs.Length, restored.Structs.Length);
        for (int i = 0; i < original.Structs.Length; i++)
        {
            var orig = original.Structs[i];
            var rest = restored.Structs[i];
            Assert.Equal(orig.NameIndex, rest.NameIndex);
            Assert.Equal(orig.ParamCount, rest.ParamCount);
            Assert.Equal(orig.BytecodeOffset, rest.BytecodeOffset);
            Assert.Equal(orig.BytecodeLength, rest.BytecodeLength);
            Assert.Equal(orig.StaticSize, rest.StaticSize);
            Assert.Equal(orig.Flags, rest.Flags);
            Assert.Equal(orig.Fields.Length, rest.Fields.Length);
            for (int f = 0; f < orig.Fields.Length; f++)
            {
                Assert.Equal(orig.Fields[f].NameIndex, rest.Fields[f].NameIndex);
                Assert.Equal(orig.Fields[f].Flags, rest.Fields[f].Flags);
            }
        }
    }

    // ─── 5. Source section ───────────────────────────────────────────

    [Fact]
    public void WithSource_RoundTripsCorrectly()
    {
        var original = CreateSampleProgram(source: "struct Foo { x: u8 }");
        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Equal("struct Foo { x: u8 }", restored.Source);
    }

    [Fact]
    public void WithoutSource_SourceIsNull()
    {
        var original = CreateSampleProgram(source: null);
        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Null(restored.Source);
    }

    // ─── 6. Parameters round-trip ────────────────────────────────────

    [Fact]
    public void Parameters_RoundTripCorrectly()
    {
        var builder = new BytecodeBuilder();
        builder.InternString("TestStruct");
        builder.InternString("arch");

        // Intern the parameter name so the serializer can find it.
        builder.Emit(Opcode.Return);

        var parameters = new Dictionary<string, object>
        {
            ["arch"] = 64L,
        };

        var original = new BytecodeProgram
        {
            Bytecode = builder.ToArray(),
            StringTable = builder.GetStringTable(),
            Structs = Array.Empty<StructMeta>(),
            RootStructIndex = 0,
            Parameters = parameters,
            Version = 1,
        };

        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Single(restored.Parameters);
        Assert.True(restored.Parameters.ContainsKey("arch"));
        Assert.Equal(64L, restored.Parameters["arch"]);
    }

    [Fact]
    public void StringParameter_RoundTrips()
    {
        var builder = new BytecodeBuilder();
        builder.InternString("mode");
        builder.Emit(Opcode.Return);

        var parameters = new Dictionary<string, object> { ["mode"] = "debug" };
        var original = new BytecodeProgram
        {
            Bytecode = builder.ToArray(),
            StringTable = builder.GetStringTable(),
            Structs = Array.Empty<StructMeta>(),
            RootStructIndex = 0,
            Parameters = parameters,
            Version = 1,
        };

        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Equal("debug", restored.Parameters["mode"]);
    }

    [Fact]
    public void BoolParameter_RoundTrips()
    {
        var builder = new BytecodeBuilder();
        builder.InternString("verbose");
        builder.Emit(Opcode.Return);

        var parameters = new Dictionary<string, object> { ["verbose"] = true };
        var original = new BytecodeProgram
        {
            Bytecode = builder.ToArray(),
            StringTable = builder.GetStringTable(),
            Structs = Array.Empty<StructMeta>(),
            RootStructIndex = 0,
            Parameters = parameters,
            Version = 1,
        };

        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Equal(true, restored.Parameters["verbose"]);
    }

    [Fact]
    public void DoubleParameter_RoundTrips()
    {
        var builder = new BytecodeBuilder();
        builder.InternString("scale");
        builder.Emit(Opcode.Return);

        var parameters = new Dictionary<string, object> { ["scale"] = 3.14 };
        var original = new BytecodeProgram
        {
            Bytecode = builder.ToArray(),
            StringTable = builder.GetStringTable(),
            Structs = Array.Empty<StructMeta>(),
            RootStructIndex = 0,
            Parameters = parameters,
            Version = 1,
        };

        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Equal(3.14, (double)restored.Parameters["scale"], 0.0001);
    }

    // ─── 7. Invalid data ─────────────────────────────────────────────

    [Fact]
    public void InvalidMagic_Throws()
    {
        var data = new byte[28];
        data[0] = (byte)'X';

        Assert.Throws<InvalidOperationException>(() =>
            BytecodeDeserializer.Deserialize(data));
    }

    [Fact]
    public void TooShortData_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BytecodeDeserializer.Deserialize(new byte[20]));
    }

    // ─── 8. Multiple structs ─────────────────────────────────────────

    [Fact]
    public void MultipleStructs_RoundTrip()
    {
        var builder = new BytecodeBuilder();
        var name1 = builder.InternString("StructA");
        var name2 = builder.InternString("StructB");
        var f1 = builder.InternString("x");
        var f2 = builder.InternString("y");

        builder.Emit(Opcode.ReadU8);
        builder.EmitU16(0);
        builder.Emit(Opcode.Return);
        builder.Emit(Opcode.ReadU16Le);
        builder.EmitU16(0);
        builder.Emit(Opcode.Return);

        var bytecode = builder.ToArray();

        var original = new BytecodeProgram
        {
            Bytecode = bytecode,
            StringTable = builder.GetStringTable(),
            Structs = new[]
            {
                new StructMeta
                {
                    NameIndex = name1, ParamCount = 0,
                    Fields = new[] { new FieldMeta { NameIndex = f1, Flags = FieldFlags.None } },
                    BytecodeOffset = 0, BytecodeLength = 4, StaticSize = 1,
                    Flags = StructFlags.None,
                },
                new StructMeta
                {
                    NameIndex = name2, ParamCount = 0,
                    Fields = new[] { new FieldMeta { NameIndex = f2, Flags = FieldFlags.Hidden } },
                    BytecodeOffset = 4, BytecodeLength = 4, StaticSize = 2,
                    Flags = StructFlags.IsRoot,
                },
            },
            RootStructIndex = 1,
            Parameters = new Dictionary<string, object>(),
            Version = 1,
        };

        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Equal(2, restored.Structs.Length);
        Assert.Equal(StructFlags.IsRoot, restored.Structs[1].Flags);
        Assert.Equal(FieldFlags.Hidden, restored.Structs[1].Fields[0].Flags);
    }

    // ─── 9. Field flags round-trip ───────────────────────────────────

    [Fact]
    public void FieldFlags_RoundTrip()
    {
        var builder = new BytecodeBuilder();
        var name = builder.InternString("S");
        var f1 = builder.InternString("a");
        var f2 = builder.InternString("b");
        var f3 = builder.InternString("c");
        builder.Emit(Opcode.Return);

        var original = new BytecodeProgram
        {
            Bytecode = builder.ToArray(),
            StringTable = builder.GetStringTable(),
            Structs = new[]
            {
                new StructMeta
                {
                    NameIndex = name, ParamCount = 0,
                    Fields = new[]
                    {
                        new FieldMeta { NameIndex = f1, Flags = FieldFlags.None },
                        new FieldMeta { NameIndex = f2, Flags = FieldFlags.Derived },
                        new FieldMeta { NameIndex = f3, Flags = FieldFlags.Hidden | FieldFlags.Derived },
                    },
                    BytecodeOffset = 0, BytecodeLength = 1, StaticSize = -1,
                    Flags = StructFlags.None,
                },
            },
            RootStructIndex = 0,
            Parameters = new Dictionary<string, object>(),
            Version = 1,
        };

        byte[] serialized = BytecodeSerializer.Serialize(original);
        var restored = BytecodeDeserializer.Deserialize(serialized);

        Assert.Equal(FieldFlags.None, restored.Structs[0].Fields[0].Flags);
        Assert.Equal(FieldFlags.Derived, restored.Structs[0].Fields[1].Flags);
        Assert.Equal(FieldFlags.Hidden | FieldFlags.Derived, restored.Structs[0].Fields[2].Flags);
    }
}
