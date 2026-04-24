namespace BinScript.Tests.Compiler;

using BinScript.Core.Api;
using BinScript.Core.Bytecode;
using BinScript.Core.Compiler;
using BinScript.Core.Compiler.Ast;
using BinScript.Core.Model;

public class BytecodeEmitterTests
{
    // ─── Helpers ──────────────────────────────────────────────────────

    private static BytecodeProgram Compile(string source)
    {
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile(source, "test.bsx");
        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.NotNull(result.Program);
        return result.Program!;
    }

    private static string FormatDiagnostics(IReadOnlyList<Diagnostic> diags) =>
        string.Join("\n", diags.Select(d => $"[{d.Severity}] {d.Code}: {d.Message}"));

    private static bool ContainsOpcode(byte[] bytecode, Opcode op)
    {
        byte b = (byte)op;
        for (int i = 0; i < bytecode.Length; i++)
            if (bytecode[i] == b) return true;
        return false;
    }

    // ─── 1. Simple flat struct ────────────────────────────────────────

    [Fact]
    public void SimpleFlatStruct_EmitsTier1Opcodes()
    {
        var program = Compile("""
            @root
            struct Header {
                magic: u8,
                version: u16le,
                size: u32le,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU8));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU16Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU32Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.Return));
    }

    // ─── 2. Endianness resolution ─────────────────────────────────────

    [Fact]
    public void DefaultEndianBig_ResolvesCorrectOpcodes()
    {
        var program = Compile("""
            @default_endian(big)
            @root
            struct Header {
                a: u16,
                b: u32,
                c: u16le,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU16Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU32Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU16Le));
    }

    [Fact]
    public void DefaultEndianLittle_IsDefault()
    {
        var program = Compile("""
            @root
            struct Header {
                a: u16,
                b: u32,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU16Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU32Le));
    }

    [Fact]
    public void ExplicitEndianOverridesDefault()
    {
        var program = Compile("""
            @default_endian(little)
            @root
            struct Header {
                a: u16be,
                b: i32be,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU16Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadI32Be));
    }

    // ─── 3. String types ──────────────────────────────────────────────

    [Fact]
    public void CString_EmitsReadCString()
    {
        var program = Compile("""
            @root
            struct Data { name: cstring }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadCString));
    }

    [Fact]
    public void FixedString_EmitsReadFixedStr()
    {
        var program = Compile("""
            @root
            struct Data { tag: fixed_string[4] }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadFixedStr));
    }

    [Fact]
    public void DynamicString_EmitsReadStringDyn()
    {
        var program = Compile("""
            @root
            struct Data {
                len: u8,
                name: string[len],
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU8));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadStringDyn));
    }

    // ─── 4. Bytes fixed ───────────────────────────────────────────────

    [Fact]
    public void BytesFixed_EmitsReadBytesFixed()
    {
        var program = Compile("""
            @root
            struct Data { blob: bytes[16] }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadBytesFixed));
    }

    // ─── 5. Bytes dynamic ─────────────────────────────────────────────

    [Fact]
    public void BytesDynamic_EmitsReadBytesDyn()
    {
        var program = Compile("""
            @root
            struct Data {
                size: u32le,
                blob: bytes[size],
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadBytesDyn));
    }

    // ─── 6. Nested struct ─────────────────────────────────────────────

    [Fact]
    public void NestedStruct_EmitsCallStruct()
    {
        var program = Compile("""
            struct Inner { x: u8 }
            @root
            struct Outer { inner: Inner }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.CallStruct));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.EmitStructBegin));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.EmitStructEnd));
    }

    // ─── 7. Enum field ────────────────────────────────────────────────

    [Fact]
    public void EnumField_ReadsUnderlyingType()
    {
        var program = Compile("""
            enum Color : u8 {
                Red = 0,
                Green = 1,
                Blue = 2,
            }
            @root
            struct Pixel { color: Color }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU8));
    }

    // ─── 8. Bits struct ───────────────────────────────────────────────

    [Fact]
    public void BitsStruct_EmitsReadBitAndReadBits()
    {
        var program = Compile("""
            bits struct Flags : u8 {
                a: bit,
                b: bits[3],
                c: bits[4],
            }
            @root
            struct Data { flags: Flags }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.EmitBitsBegin));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadBit));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadBits));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.EmitBitsEnd));
    }

    // ─── 9. Match expression ──────────────────────────────────────────

    [Fact]
    public void MatchType_EmitsMatchOpcodes()
    {
        var program = Compile("""
            struct TypeA { x: u8 }
            struct TypeB { y: u16le }
            @root
            struct Container {
                tag: u8,
                data: match(tag) {
                    1 => TypeA,
                    2 => TypeB,
                    _ => TypeA,
                }
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.MatchBegin));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.MatchArmEq));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.MatchDefault));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.MatchEnd));
    }

    // ─── 10. Array (count-based) ──────────────────────────────────────

    [Fact]
    public void CountArray_EmitsArrayOpcodes()
    {
        var program = Compile("""
            @root
            struct Data {
                count: u8,
                items: u16le[count],
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ArrayBeginCount));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ArrayNext));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ArrayEnd));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.EmitArrayBegin));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.EmitArrayEnd));
    }

    // ─── 11. Array (until) ────────────────────────────────────────────

    [Fact]
    public void UntilArray_EmitsArrayBeginUntil()
    {
        var program = Compile("""
            @root
            struct Data {
                items: u8[] @until(@remaining == 0),
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ArrayBeginUntil));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ArrayEnd));
    }

    // ─── 12. @seek ────────────────────────────────────────────────────

    [Fact]
    public void SeekDirective_EmitsSeekAbs()
    {
        var program = Compile("""
            @root
            struct Data {
                @seek(0x100)
                value: u32le,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.PushConstI64));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.SeekAbs));
    }

    // ─── 13. @at block ────────────────────────────────────────────────

    [Fact]
    public void AtBlock_EmitsSeekPushPop()
    {
        var program = Compile("""
            @root
            struct Data {
                offset: u32le,
                @at(offset) {
                    value: u8,
                }
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.SeekPush));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.SeekAbs));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.SeekPop));
    }

    // ─── 14. @let binding ─────────────────────────────────────────────

    [Fact]
    public void LetBinding_StoresFieldValue()
    {
        var program = Compile("""
            @root
            struct Data {
                a: u8,
                @let x = a + 1,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.PushFieldVal));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.OpAdd));
    }

    // ─── 15. @align ───────────────────────────────────────────────────

    [Fact]
    public void AlignFixed_EmitsAlignFixed()
    {
        var program = Compile("""
            @root
            struct Data {
                a: u8,
                @align(4)
                b: u32le,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.AlignFixed));
    }

    // ─── 16. @skip ────────────────────────────────────────────────────

    [Fact]
    public void SkipDirective_EmitsSkipFixed()
    {
        var program = Compile("""
            @root
            struct Data {
                @skip(8)
                value: u32le,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.SkipFixed));
    }

    // ─── 17. @hidden field ────────────────────────────────────────────

    [Fact]
    public void HiddenField_FlagSetInMetadata()
    {
        var program = Compile("""
            @root
            struct Data {
                @hidden reserved: u32le,
                value: u8,
            }
            """);

        var structMeta = program.Structs[program.RootStructIndex];
        var reservedField = structMeta.Fields[0];
        Assert.True((reservedField.Flags & FieldFlags.Hidden) != 0);
    }

    // ─── 18. @derived field ───────────────────────────────────────────

    [Fact]
    public void DerivedField_FlagSetInMetadata()
    {
        var program = Compile("""
            @root
            struct Data {
                raw: u8,
                @derived doubled: u8 = raw * 2,
            }
            """);

        var structMeta = program.Structs[program.RootStructIndex];
        var derivedField = structMeta.Fields[1];
        Assert.True((derivedField.Flags & FieldFlags.Derived) != 0);
    }

    // ─── 19. Magic value ──────────────────────────────────────────────

    [Fact]
    public void MagicValue_EmitsAssertValue()
    {
        var program = Compile("""
            @root
            struct Data {
                magic: u16le = 0x5A4D,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.AssertValue));
    }

    // ─── 20. Expression codegen ───────────────────────────────────────

    [Fact]
    public void ArithmeticExpression_EmitsCorrectOps()
    {
        var program = Compile("""
            @root
            struct Data {
                a: u8,
                b: u8,
                @let sum = a + b,
                @let diff = a - b,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.OpAdd));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.OpSub));
    }

    [Fact]
    public void ComparisonExpression_EmitsCorrectOps()
    {
        var program = Compile("""
            @root
            struct Data {
                a: u8,
                @let check = a == 42,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.OpEq));
    }

    // ─── 21. Static size calculation ──────────────────────────────────

    [Fact]
    public void FlatStruct_HasKnownStaticSize()
    {
        var program = Compile("""
            @root
            struct Header {
                a: u8,
                b: u16le,
                c: u32le,
            }
            """);

        var meta = program.Structs[program.RootStructIndex];
        Assert.Equal(7, meta.StaticSize);
    }

    [Fact]
    public void DynamicStruct_HasNegativeStaticSize()
    {
        var program = Compile("""
            @root
            struct Data {
                len: u8,
                name: string[len],
            }
            """);

        var meta = program.Structs[program.RootStructIndex];
        Assert.Equal(-1, meta.StaticSize);
    }

    // ─── 22. RootStructIndex ──────────────────────────────────────────

    [Fact]
    public void RootStructIndex_CorrectlySet()
    {
        var program = Compile("""
            struct Inner { x: u8 }
            @root
            struct Outer { y: u16le }
            """);

        Assert.True(program.RootStructIndex >= 0);
        var rootMeta = program.Structs[program.RootStructIndex];
        string rootName = program.StringTable[rootMeta.NameIndex];
        Assert.Equal("Outer", rootName);
    }

    // ─── 23. String table ─────────────────────────────────────────────

    [Fact]
    public void StringTable_ContainsInternedStrings()
    {
        var program = Compile("""
            @root
            struct Header { name: cstring }
            """);

        Assert.Contains("Header", program.StringTable);
        Assert.Contains("name", program.StringTable);
    }

    // ─── Additional coverage tests ────────────────────────────────────

    [Fact]
    public void AllPrimitiveTypes_Emit()
    {
        var program = Compile("""
            @root
            struct AllTypes {
                a: u8, b: i8,
                c: u16le, d: u16be,
                e: u32le, f: u32be,
                g: u64le, h: u64be,
                i: i16le, j: i16be,
                k: i32le, l: i32be,
                m: i64le, n: i64be,
                o: f32le, p: f32be,
                q: f64le, r: f64be,
                s: bool,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU8));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadI8));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU16Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU16Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU32Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU32Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU64Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadU64Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadI16Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadI16Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadI32Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadI32Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadI64Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadI64Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadF32Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadF32Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadF64Le));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadF64Be));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ReadBool));
    }

    [Fact]
    public void BitsStructAsField_HasIsBitsFlag()
    {
        var program = Compile("""
            bits struct Flags : u8 {
                a: bit,
                b: bits[7],
            }
            @root
            struct Data { flags: Flags }
            """);

        bool found = false;
        for (int i = 0; i < program.Structs.Length; i++)
        {
            if (program.StringTable[program.Structs[i].NameIndex] == "Flags")
            {
                Assert.True((program.Structs[i].Flags & StructFlags.IsBits) != 0);
                found = true;
            }
        }
        Assert.True(found, "Flags bits-struct not found in struct table");
    }

    [Fact]
    public void GreedyArray_EmitsArrayBeginGreedy()
    {
        var program = Compile("""
            @root
            struct Data {
                items: u8[] @greedy,
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.ArrayBeginGreedy));
    }

    [Fact]
    public void StructWithParameters_EmitsCallStructWithParams()
    {
        var program = Compile("""
            struct Inner(size) { data: bytes[size] }
            @root
            struct Outer {
                len: u32le,
                body: Inner(len),
            }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.CallStruct));
        Assert.True(ContainsOpcode(program.Bytecode, Opcode.PushFieldVal));
    }

    [Fact]
    public void Constant_ResolvedInExpression()
    {
        var program = Compile("""
            const MAGIC = 0x5A4D;
            @root
            struct Data { magic: u16le = MAGIC }
            """);

        Assert.True(ContainsOpcode(program.Bytecode, Opcode.AssertValue));
    }

    [Fact]
    public void MultipleStructs_AllEmitted()
    {
        var program = Compile("""
            struct A { x: u8 }
            struct B { y: u16le }
            @root
            struct C { z: u32le }
            """);

        Assert.Equal(3, program.Structs.Length);
    }

    [Fact]
    public void NoRoot_NegativeRootIndex()
    {
        var compiler = new BinScriptCompiler();
        var result = compiler.Compile("struct Data { x: u8 }", "test.bsx");
        Assert.True(result.Success);
        Assert.NotNull(result.Program);
        Assert.Equal(-1, result.Program!.RootStructIndex);
    }
}
