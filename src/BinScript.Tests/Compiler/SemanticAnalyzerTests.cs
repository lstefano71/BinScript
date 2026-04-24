namespace BinScript.Tests.Compiler;

using BinScript.Core.Api;
using BinScript.Core.Compiler;
using BinScript.Core.Compiler.Ast;
using BinScript.Core.Model;

public class SemanticAnalyzerTests
{
    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private static CompilationResult Compile(string source)
    {
        var compiler = new BinScriptCompiler();
        return compiler.Compile(source, "test.bsx");
    }

    private static CompilationResult CompileWithModules(
        string source, params (string name, string src)[] modules)
    {
        var compiler = new BinScriptCompiler();
        foreach (var (name, src) in modules)
            compiler.AddModule(name, src);
        return compiler.Compile(source, "test.bsx");
    }

    private static List<Diagnostic> Errors(CompilationResult r)
        => r.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    private static List<Diagnostic> Warnings(CompilationResult r)
        => r.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

    // ═══════════════════════════════════════════════════════════════
    //  Type Resolution Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void KnownType_Resolves()
    {
        var result = Compile("""
            struct Header { magic: u32 }
            struct File { hdr: Header }
            """);
        Assert.True(result.Success);
        Assert.Empty(Errors(result));
    }

    [Fact]
    public void UnknownType_ProducesDiagnostic()
    {
        var result = Compile("struct File { hdr: NonExistent }");
        Assert.False(result.Success);
        var error = Assert.Single(Errors(result));
        Assert.Contains("Unknown type", error.Message);
        Assert.Contains("NonExistent", error.Message);
    }

    [Fact]
    public void DuplicateDeclaration_ProducesDiagnostic()
    {
        var result = Compile("""
            struct Foo { x: u8 }
            struct Foo { y: u16 }
            """);
        Assert.False(result.Success);
        var error = Assert.Single(Errors(result));
        Assert.Contains("Duplicate declaration", error.Message);
    }

    [Fact]
    public void StructParamCount_Mismatch()
    {
        var result = Compile("""
            struct OptionalHeader(arch) { x: u32 }
            struct File { opt: OptionalHeader(a, b) }
            """);
        Assert.False(result.Success);
        var error = Assert.Single(Errors(result));
        Assert.Contains("1 parameter(s)", error.Message);
        Assert.Contains("2", error.Message);
    }

    [Fact]
    public void StructParamCount_ZeroParams_NoArgs_Succeeds()
    {
        var result = Compile("""
            struct Header { x: u32 }
            struct File { hdr: Header }
            """);
        Assert.True(result.Success);
    }

    [Fact]
    public void StructParamCount_ZeroParams_WithArgs_Fails()
    {
        var result = Compile("""
            struct Header { x: u32 }
            struct File { hdr: Header(42) }
            """);
        Assert.False(result.Success);
        var error = Assert.Single(Errors(result));
        Assert.Contains("0 parameter(s)", error.Message);
    }

    [Fact]
    public void Import_ResolvesTypes()
    {
        var result = CompileWithModules(
            """
            @import "math"
            struct Main { v: Vec2 }
            """,
            ("math", "struct Vec2 { x: f32, y: f32 }"));

        Assert.True(result.Success);
        Assert.Empty(Errors(result));
    }

    [Fact]
    public void Import_UnknownModule_ProducesDiagnostic()
    {
        var result = Compile("""
            @import "nonexistent"
            struct Main { x: u8 }
            """);
        Assert.False(result.Success);
        var error = Assert.Single(Errors(result));
        Assert.Contains("Unknown module", error.Message);
    }

    [Fact]
    public void Import_NoResolver_ProducesDiagnostic()
    {
        // BinScriptCompiler always provides a resolver, so this tests the
        // case where the resolver has no module registered.
        var result = Compile("""
            @import "missing"
            struct Main { x: u8 }
            """);
        Assert.False(result.Success);
        Assert.Contains(Errors(result), d => d.Message.Contains("missing"));
    }

    [Fact]
    public void PrimitiveTypes_AlwaysResolve()
    {
        var result = Compile("""
            struct AllPrimitives {
                a: u8,
                b: u16,
                c: u32,
                d: u64,
                e: i8,
                f: i16,
                g: i32,
                h: i64,
                i: f32,
                j: f64,
                k: bool,
            }
            """);
        Assert.True(result.Success);
        Assert.Empty(Errors(result));
    }

    [Fact]
    public void EnumAsFieldType_Resolves()
    {
        var result = Compile("""
            enum Machine : u16 { I386 = 0x014C, AMD64 = 0x8664 }
            struct CoffHeader { machine: Machine }
            """);
        Assert.True(result.Success);
        Assert.Empty(Errors(result));
    }

    [Fact]
    public void BitsStructAsFieldType_Resolves()
    {
        var result = Compile("""
            bits struct Characteristics : u16 {
                relocs_stripped: bit,
                executable_image: bit,
            }
            struct CoffHeader { chars: Characteristics }
            """);
        Assert.True(result.Success);
        Assert.Empty(Errors(result));
    }

    [Fact]
    public void ConstantDeclared_NoError()
    {
        var result = Compile("""
            const PE_MAGIC = 0x00004550;
            struct File { sig: u32 = PE_MAGIC }
            """);
        Assert.True(result.Success);
    }

    [Fact]
    public void DuplicateConstAndStruct_ProducesDiagnostic()
    {
        var result = Compile("""
            const Foo = 42;
            struct Foo { x: u8 }
            """);
        Assert.False(result.Success);
        var error = Assert.Single(Errors(result));
        Assert.Contains("Duplicate", error.Message);
    }

    [Fact]
    public void DuplicateEnumAndStruct_ProducesDiagnostic()
    {
        var result = Compile("""
            enum Foo : u8 { A = 1 }
            struct Foo { x: u8 }
            """);
        Assert.False(result.Success);
        Assert.Contains(Errors(result), d => d.Message.Contains("Duplicate"));
    }

    [Fact]
    public void MatchTypeRef_ValidArms_Resolves()
    {
        var result = Compile("""
            struct FmtA { x: u8 }
            struct FmtB { y: u16 }
            struct File {
                tag: u8,
                body: match(tag) {
                    1 => FmtA,
                    2 => FmtB,
                    _ => bytes[4],
                },
            }
            """);
        Assert.True(result.Success);
    }

    [Fact]
    public void MatchTypeRef_UnknownArmType_ProducesDiagnostic()
    {
        var result = Compile("""
            struct File {
                tag: u8,
                body: match(tag) {
                    1 => Missing,
                    _ => u8,
                },
            }
            """);
        Assert.False(result.Success);
        Assert.Contains(Errors(result), d => d.Message.Contains("Unknown type"));
    }

    [Fact]
    public void StructParam_AsTypeRef_Resolves()
    {
        var result = Compile("struct Wrapper(inner_type) { value: inner_type }");
        Assert.True(result.Success);
    }

    [Fact]
    public void ImportedTypes_AvailableInMainFile()
    {
        var result = CompileWithModules(
            """
            @import "common"
            struct PeFile { hdr: DosHeader }
            """,
            ("common", """
                struct DosHeader { e_magic: u16 }
                enum Machine : u16 { I386 = 0x014C }
            """));

        Assert.True(result.Success);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Semantic Analysis Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SingleRoot_IsValid()
    {
        var result = Compile("""
            @root struct File { x: u8 }
            struct Other { y: u16 }
            """);
        Assert.True(result.Success);
    }

    [Fact]
    public void MultipleRoots_ProducesDiagnostic()
    {
        var result = Compile("""
            @root struct A { x: u8 }
            @root struct B { y: u16 }
            """);
        Assert.False(result.Success);
        Assert.Contains(Errors(result), d => d.Code == "BSC300");
    }

    [Fact]
    public void NoRoot_IsValid()
    {
        var result = Compile("""
            struct A { x: u8 }
            struct B { y: u16 }
            """);
        Assert.True(result.Success);
    }

    [Fact]
    public void MatchWithoutDefault_ProducesWarning()
    {
        var result = Compile("""
            struct FmtA { x: u8 }
            struct File {
                tag: u8,
                body: match(tag) {
                    1 => FmtA,
                    2 => u16,
                },
            }
            """);
        Assert.True(result.Success);
        Assert.Contains(Warnings(result), d => d.Code == "BSC301");
    }

    [Fact]
    public void MatchWithDefault_NoWarning()
    {
        var result = Compile("""
            struct FmtA { x: u8 }
            struct File {
                tag: u8,
                body: match(tag) {
                    1 => FmtA,
                    _ => u8,
                },
            }
            """);
        Assert.True(result.Success);
        Assert.DoesNotContain(Warnings(result), d => d.Code == "BSC301");
    }

    [Fact]
    public void DerivedField_ValidBackRef_Succeeds()
    {
        var result = Compile("""
            struct File {
                a: u32,
                b: u32,
                @derived
                total: u32 = a + b,
            }
            """);
        Assert.True(result.Success);
    }

    [Fact]
    public void DerivedField_ForwardRef_ProducesDiagnostic()
    {
        var result = Compile("""
            struct File {
                @derived
                total: u32 = later_field + 1,
                later_field: u32,
            }
            """);
        Assert.False(result.Success);
        Assert.Contains(Errors(result), d =>
            d.Code == "BSC302" && d.Message.Contains("later_field"));
    }

    [Fact]
    public void LetBinding_ValidRef_Succeeds()
    {
        var result = Compile("""
            struct File {
                size: u32,
                @let adjusted = size - 4
                data: bytes[adjusted]
            }
            """);
        Assert.True(result.Success);
    }

    // ═══════════════════════════════════════════════════════════════
    //  End-to-End Compile Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Compile_SimpleStruct_Succeeds()
    {
        var result = Compile("struct Point { x: i32, y: i32 }");
        Assert.True(result.Success);
        Assert.NotNull(result.Ast);
        Assert.NotNull(result.Program); // emitter is now wired in
    }

    [Fact]
    public void Compile_ReadmeExample_Succeeds()
    {
        var result = Compile("""
            @default_endian(little)

            @root
            @coverage(partial)
            struct PeQuickHeader {
                dos: DosHeader,
                @seek(dos.e_lfanew)
                pe_sig: u32 = 0x00004550,
                coff: CoffHeader,
            }

            struct DosHeader {
                e_magic: u16 = 0x5A4D,
                e_cblp: u16,
                e_cp: u16,
                e_crlc: u16,
                e_cparhdr: u16,
                e_minalloc: u16,
                e_maxalloc: u16,
                e_ss: u16,
                e_sp: u16,
                e_csum: u16,
                e_ip: u16,
                e_cs: u16,
                e_lfarlc: u16,
                e_ovno: u16,
                e_res: u16[4],
                e_oemid: u16,
                e_oeminfo: u16,
                e_res2: u16[10],
                e_lfanew: u32,
            }

            struct CoffHeader {
                machine: Machine,
                number_of_sections: u16,
                time_date_stamp: u32,
                pointer_to_symbol_table: u32,
                number_of_symbols: u32,
                size_of_optional_header: u16,
                characteristics: Characteristics,
            }

            enum Machine : u16 {
                I386  = 0x014C,
                AMD64 = 0x8664,
                ARM64 = 0xAA64,
            }

            bits struct Characteristics : u16 {
                relocs_stripped: bit,
                executable_image: bit,
                line_nums_stripped: bit,
                local_syms_stripped: bit,
                aggressive_ws_trim: bit,
                large_address_aware: bit,
                _reserved: bit,
                bytes_reversed_lo: bit,
                _32bit_machine: bit,
                debug_stripped: bit,
                removable_run_from_swap: bit,
                net_run_from_swap: bit,
                system: bit,
                dll: bit,
                up_system_only: bit,
                bytes_reversed_hi: bit,
            }
            """);
        Assert.True(result.Success, string.Join("; ", Errors(result).Select(d => d.Message)));
        Assert.NotNull(result.Ast);
    }

    [Fact]
    public void Compile_InvalidScript_HasErrors()
    {
        var result = Compile("struct File { data: NoSuchType }");
        Assert.False(result.Success);
        Assert.NotEmpty(Errors(result));
    }

    [Fact]
    public void Compile_WithModules_Succeeds()
    {
        var result = CompileWithModules(
            """
            @import "types"
            struct Main { hdr: Header }
            """,
            ("types", "struct Header { magic: u32, size: u16 }"));
        Assert.True(result.Success);
    }

    [Fact]
    public void Compile_EmptySource_Succeeds()
    {
        var result = Compile("");
        Assert.True(result.Success);
        Assert.NotNull(result.Ast);
    }

    [Fact]
    public void Compile_StringAndBytesTypes_Resolve()
    {
        var result = Compile("""
            struct Record {
                name: cstring,
                label: fixed_string[8],
                data: bytes[16],
            }
            """);
        Assert.True(result.Success);
    }

    [Fact]
    public void Compile_EndianQualifiedPrimitives_Resolve()
    {
        var result = Compile("""
            struct Mixed {
                a: u16le,
                b: u32be,
                c: i64le,
                d: f64be,
            }
            """);
        Assert.True(result.Success);
    }

    [Fact]
    public void Compile_MultipleErrors_AllReported()
    {
        var result = Compile("struct A { x: Missing1, y: Missing2 }");
        Assert.False(result.Success);
        Assert.True(Errors(result).Count >= 2);
    }

    [Fact]
    public void Compile_ImportChain_Resolves()
    {
        var compiler = new BinScriptCompiler();
        compiler.AddModule("base", "struct Base { x: u8 }");
        compiler.AddModule("mid", """
            @import "base"
            struct Mid { b: Base }
            """);

        var result = compiler.Compile("""
            @import "mid"
            struct Main { m: Mid }
            """, "test.bsx");

        Assert.True(result.Success, string.Join("; ", Errors(result).Select(d => d.Message)));
    }

    [Fact]
    public void Compile_DiagnosticSeverity_Success()
    {
        // A script with only warnings should still be considered successful.
        var result = Compile("""
            struct FmtA { x: u8 }
            struct File {
                tag: u8,
                body: match(tag) {
                    1 => FmtA,
                },
            }
            """);
        Assert.True(result.Success);
        Assert.NotEmpty(Warnings(result));
    }

    [Fact]
    public void Import_DuplicateType_AcrossModules_ProducesDiagnostic()
    {
        var result = CompileWithModules(
            """
            @import "mod"
            struct Foo { x: u8 }
            """,
            ("mod", "struct Foo { y: u16 }"));
        Assert.False(result.Success);
        Assert.Contains(Errors(result), d => d.Message.Contains("Duplicate"));
    }
}
