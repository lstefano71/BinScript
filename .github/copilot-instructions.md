# BinScript — Copilot Instructions

## Build, Test, Publish

```bash
# Build everything
dotnet build src/BinScript.slnx

# Run all tests
dotnet test src/BinScript.Tests

# Run a single test by name
dotnet test src/BinScript.Tests --filter "FullyQualifiedName~ParseEngineTests.FlatStruct_U8_U16le_U32le"

# Run all tests in a class
dotnet test src/BinScript.Tests --filter "ClassName=BinScript.Tests.Runtime.ParseEngineTests"

# Publish NativeAOT C-ABI DLL
dotnet publish src/BinScript.Interop -c Release
```

Requires **.NET 10 SDK** with C# 14 preview (`LangVersion preview`). All projects enable `Nullable`, `AllowUnsafeBlocks`, and `ImplicitUsings`.

## Architecture

docs/PRD.md is the original product requirements document describing the motivation and, importantly, the goals of the project.

docs/ARCHITECTURE.md describes the overall architecture and design patterns of

docs/IMPLEMENTATION_PLAN.md describes the original implementation plan.

BinScript is a **bidirectional binary structure parser/producer**. Scripts (`.bsx`) are compiled to bytecode, then executed against binary data (parse) or structured data (produce).

### Compilation Pipeline

```
Source (.bsx) → Lexer → Parser → TypeResolver → SemanticAnalyzer → BytecodeEmitter → BytecodeProgram
```

Each stage is a separate class in `BinScript.Core/Compiler/`. Errors accumulate as `List<Diagnostic>` and are returned in `CompilationResult`. The pipeline bails early on errors after parsing and after semantic analysis.

### Runtime — Two Engines

- **`ParseEngine`** — Executes bytecode against `ReadOnlyMemory<byte>`, emits structured output via `IResultEmitter` (SAX-style events). Tier 1 opcodes are a fast path for simple fixed-size reads with no heap allocations.
- **`ProduceEngine`** — Two-pass engine (size calculation + write). Reads structured input via `IDataSource`, writes to `Span<byte>`. Trailing data region handles pointer targets.

### Plugin Interfaces

New output formats only need to implement two interfaces:

- **`IResultEmitter`** — SAX-style event stream for parse output (BeginStruct, EmitInt, etc.)
- **`IDataSource`** — Navigation-based input for produce (EnterField, ReadInt, etc.)
- **`IModuleResolver`** — Resolves `@import` module names to source text

`JsonResultEmitter` / `JsonDataSource` in `BinScript.Emitters.Json` are the only implementations so far.

### C-ABI Interop

Specs in docs/A_ABI.md

`BinScript.Interop` compiles to a NativeAOT DLL with flat C functions (`[UnmanagedCallersOnly]`). Managed objects are wrapped in `GCHandle` → opaque `IntPtr` handles via `HandleTable`. Errors cross the FFI boundary through a `[ThreadStatic]` last-error string (`ErrorState`), not exceptions.

### Bytecode VM

docs/BYTECODE.md defines the bytecode instruction set and VM semantics.

Register-free stack machine using `StackValue` (tagged union: Int/Float/String/Bytes). 80+ opcodes organized in tiers:
- **Tier 1** (0x01–0x18): fast-path fixed-size reads
- **Tier 2**: control flow, seeking, dynamic reads, expressions, builtins, arrays, match

### Standard Library

docs/STDLIB.md defines the standard library conventions and built-in modules.

The `stdlib/` folder contains real-world `.bsx` scripts for formats like PNG, ELF, ZIP. These serve as both documentation and test fixtures. The standard library is not a separate module — these scripts are compiled directly in tests to verify compiler and runtime correctness against known-good binaries.

## Key Conventions

### .bsx Script Language Patterns

Full .bsx syntax at docs/LANGUAGE_SPEC.md

- `@root struct` marks the parse/produce entry point — exactly one per script
- `@default_endian(little|big)` at file top sets the default byte order
- `@coverage(partial)` when not all fields of a format are parsed
- `bits struct` for bitfield flags (each field is a `bit`)
- `enum Foo : u16 { ... }` with explicit backing types
- `match(field) { ... }` for discriminated unions
- `@until(@remaining == 0)` for reading arrays to end of data
- `ptr<T, width>` / `relptr<T, width>` for pointer fields; `?` suffix for nullable

### C# Code Patterns

- `record` types for immutable results: `CompilationResult`, `ParseResult`, `ProduceResult`, `Diagnostic`
- `AggressiveInlining` on hot-path code (e.g., `StackValue` operations)
- `IsTrimmable=true` on library projects for NativeAOT compatibility
- Entry point for compilation: `BinScriptCompiler.Compile(source)` returns `CompilationResult`
- Entry point for execution: `BinScriptProgram.Parse(data)` / `.Produce(data)`
- JSON convenience extensions: `program.ToJson(bytes)` / `program.FromJson(json)`

### Test Patterns

- Tests use xUnit. Test namespaces mirror `src/` structure (e.g., `BinScript.Tests.Runtime`, `BinScript.Tests.Compiler`).
- Helpers: `Compile(string source)` compiles inline BinScript; `ParseToJson(program, data)` runs parse → JSON string.
- Stdlib integration tests compile real `.bsx` files from `stdlib/` against sample binaries in `tests/samples/`.
- Round-trip tests verify `parse(produce(parse(data))) == parse(data)`.
- Tests marked `[Fact(Skip = "Requires compiler support for ...")]` indicate features not yet implemented.

### ADR Decisions

- **ADR-001**: `@map` is syntactic sugar for pure inlined expressions (not real functions). No recursion, no side effects.
- **ADR-002**: Circular structures are **deferred** for v1. Depth limit (256) catches accidental cycles.
- **ADR-003**: Pointers are first-class (`ptr<T>` / `relptr<T>`) with transparent JSON dereferencing by default.

## Instructions
- when a bug is fixed always add non regression tests
- when a new feature is added always add tests covering it
- when big decisions are taken write an ADR and link it in the architecture document
- try and keep a changelog with items including a timestamp
- in case of changes including but not limited to the addition of bytecode instructions, changes to the runtime engines, or modifications to the plugin interfaces, update the relevant documentation files in the `docs/` folder to reflect these changes.
- we don't need to be backward compatible at this stage: there are no existing users to the library, so feel free to make breaking changes without worrying about versioning or deprecation. However, make sure to update the documentation and tests accordingly to reflect any breaking changes.
- make sure the C ABI layer, which is the final output the project is meant to provide, is kept up-to-date, well-documented and tested, as this is the primary interface that users will interact with.
