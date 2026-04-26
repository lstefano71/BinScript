# BinScript Architecture & Design

## 1. System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Host Application                             │
│  (C, C++, Rust, Python, Go, C#, ... any FFI-capable language)       │
└────────────┬─────────────────────────────────────────┬──────────────┘
             │ C-ABI (NativeAOT DLL)                   │
┌────────────▼─────────────────────────────────────────▼──────────────┐
│                    BinScript.Interop                                  │
│            Thin C-ABI shim, marshalling layer                        │
└────────────┬─────────────────────────────────────────┬──────────────┘
             │                                         │
┌────────────▼──────────────┐        ┌─────────────────▼──────────────┐
│   BinScript.Core          │        │   BinScript.Emitters.Json      │
│                           │        │                                 │
│  ┌─────────────────────┐  │        │  JsonResultEmitter             │
│  │ Script Compiler     │  │        │    implements IResultEmitter    │
│  │  Lexer → Parser →   │  │        │                                 │
│  │  Semantic Analysis →│  │        │  JsonDataSource                │
│  │  Bytecode Emitter   │  │        │    implements IDataSource       │
│  └─────────────────────┘  │        └─────────────────────────────────┘
│                           │
│  ┌─────────────────────┐  │
│  │ Bytecode VM         │  │
│  │  Parse Engine       │  │
│  │  Produce Engine     │  │
│  └─────────────────────┘  │
│                           │
│  ┌─────────────────────┐  │
│  │ Bytecode Serializer │  │
│  │  Save / Load        │  │
│  └─────────────────────┘  │
│                           │
│  ┌─────────────────────┐  │
│  │ Core Interfaces     │  │
│  │  IResultEmitter     │  │
│  │  IDataSource        │  │
│  └─────────────────────┘  │
└───────────────────────────┘
```

## 2. Project Structure

```
BinScript/
├── README.md
├── docs/
│   ├── PRD.md
│   ├── LANGUAGE_SPEC.md
│   ├── GRAMMAR.md                ← formal EBNF grammar
│   ├── ARCHITECTURE.md          ← this file
│   ├── C_ABI.md
│   ├── BYTECODE.md
│   ├── STDLIB.md
│   ├── FUTURE_EXTENSIONS.md       ← planned language extensions
│   ├── tutorial/                  ← progressive tutorial (17 chapters)
│   └── IMPLEMENTATION_PLAN.md
├── stdlib/
│   ├── pe.bsx
│   ├── zip.bsx
│   ├── png.bsx
│   ├── elf.bsx
│   ├── bmp.bsx
│   ├── gif.bsx
│   └── wav.bsx
├── tests/
│   ├── samples/
│   │   ├── pe/       ← real binary files (tiny, valid)
│   │   ├── zip/
│   │   ├── png/
│   │   ├── elf/
│   │   ├── bmp/
│   │   ├── gif/
│   │   └── wav/
│   └── expected/
│       ├── pe/       ← expected JSON output
│       ├── zip/
│       ├── png/
│       ├── elf/
│       ├── bmp/
│       └── gif/
├── tools/
│   ├── bsxtool/                    ← Python CLI for compile/parse/produce/disasm
│   └── examples/                   ← Integration examples (C, Python, Go)
│       ├── c/parse_bmp.c           ← C: BMP parse + round-trip produce
│       ├── python/parse_gif.py     ← Python: GIF parse via ctypes
│       └── go/produce_wav.go       ← Go: WAV produce (Twinkle Twinkle)
└── src/
    ├── BinScript.sln
    ├── BinScript.Core/
    │   ├── BinScript.Core.csproj
    │   ├── Compiler/
    │   │   ├── Lexer.cs
    │   │   ├── Token.cs
    │   │   ├── Parser.cs
    │   │   ├── Ast/
    │   │   │   ├── AstNode.cs
    │   │   │   ├── StructDecl.cs
    │   │   │   ├── FieldDecl.cs
    │   │   │   ├── EnumDecl.cs
    │   │   │   ├── BitsStructDecl.cs
    │   │   │   ├── ConstDecl.cs
    │   │   │   ├── ImportDecl.cs
    │   │   │   ├── ParamDecl.cs
    │   │   │   ├── Expression.cs
    │   │   │   └── MatchExpr.cs
    │   │   ├── SemanticAnalyzer.cs
    │   │   ├── TypeResolver.cs
    │   │   ├── BytecodeEmitter.cs
    │   │   └── CompilationResult.cs
    │   ├── Bytecode/
    │   │   ├── Opcode.cs
    │   │   ├── Instruction.cs
    │   │   ├── BytecodeProgram.cs
    │   │   ├── BytecodeSerializer.cs
    │   │   └── BytecodeDeserializer.cs
    │   ├── Runtime/
    │   │   ├── ParseEngine.cs
    │   │   ├── ProduceEngine.cs
    │   │   ├── ParseContext.cs
    │   │   ├── ProduceContext.cs
    │   │   ├── ExpressionEvaluator.cs
    │   │   ├── BuiltinFunctions.cs
    │   │   └── Coverage.cs
    │   ├── Interfaces/
    │   │   ├── IResultEmitter.cs
    │   │   ├── IDataSource.cs
    │   │   └── IModuleResolver.cs
    │   ├── Model/
    │   │   ├── ParseResult.cs
    │   │   ├── ProduceResult.cs
    │   │   ├── Diagnostic.cs
    │   │   ├── ParseOptions.cs
    │   │   └── CoverageReport.cs
    │   └── Api/
    │       ├── BinScriptCompiler.cs    ← public API: compile scripts
    │       └── BinScriptProgram.cs     ← public API: parse/produce
    ├── BinScript.Emitters.Json/
    │   ├── BinScript.Emitters.Json.csproj
    │   ├── JsonResultEmitter.cs
    │   └── JsonDataSource.cs
    ├── BinScript.Interop/
    │   ├── BinScript.Interop.csproj    ← NativeAOT publish target
    │   ├── binscript.h                  ← canonical C header (single source of truth)
    │   ├── NativeExports.cs            ← [UnmanagedCallersOnly] methods
    │   ├── HandleTable.cs              ← GCHandle-based opaque handle management
    │   └── ErrorState.cs               ← thread-local last error
    └── BinScript.Tests/
        ├── BinScript.Tests.csproj
        ├── Compiler/
        │   ├── LexerTests.cs
        │   ├── ParserTests.cs
        │   ├── SemanticAnalyzerTests.cs
        │   └── BytecodeEmitterTests.cs
        ├── Runtime/
        │   ├── ParseEngineTests.cs
        │   ├── ProduceEngineTests.cs
        │   ├── ExpressionEvaluatorTests.cs
        │   └── RoundTripTests.cs
        ├── Interop/
        │   └── NativeExportTests.cs
        └── Stdlib/
            ├── PeTests.cs
            ├── ZipTests.cs
            ├── PngTests.cs
            ├── ElfTests.cs
            ├── BmpTests.cs
            ├── GifTests.cs
            └── WavTests.cs
```

## 3. Core Interfaces

### 3.1 IResultEmitter

The parse engine calls this interface as it walks the binary data. Implementations decide how to represent the structured output.

```csharp
/// <summary>
/// Receives structured data events during binary parsing.
/// Implementations transform these events into an output format (JSON, etc.).
/// </summary>
public interface IResultEmitter
{
    void BeginRoot(string structName);
    void EndRoot();

    void BeginStruct(string fieldName, string structName);
    void EndStruct();

    void BeginArray(string fieldName, int count);
    void EndArray();

    void EmitInt(string fieldName, long value, string? enumName);
    void EmitUInt(string fieldName, ulong value, string? enumName);
    void EmitFloat(string fieldName, double value);
    void EmitBool(string fieldName, bool value);
    void EmitString(string fieldName, string value);
    void EmitBytes(string fieldName, ReadOnlySpan<byte> value);

    void BeginBitsStruct(string fieldName, string structName);
    void EndBitsStruct();
    void EmitBit(string fieldName, bool value);
    void EmitBits(string fieldName, ulong value, int bitCount);

    void BeginVariant(string fieldName, string variantName);
    void EndVariant();
}
```

### 3.2 IDataSource

The produce engine reads from this interface to get field values for binary production.

```csharp
/// <summary>
/// Provides structured data values during binary production.
/// Implementations read from an input format (JSON, etc.).
/// </summary>
public interface IDataSource
{
    void EnterRoot(string structName);
    void ExitRoot();

    void EnterStruct(string fieldName, string structName);
    void ExitStruct();

    void EnterArray(string fieldName);
    int GetArrayLength();
    void EnterArrayElement(int index);
    void ExitArrayElement();
    void ExitArray();

    long ReadInt(string fieldName);
    ulong ReadUInt(string fieldName);
    double ReadFloat(string fieldName);
    bool ReadBool(string fieldName);
    string ReadString(string fieldName);
    byte[] ReadBytes(string fieldName);

    void EnterBitsStruct(string fieldName);
    bool ReadBit(string fieldName);
    ulong ReadBits(string fieldName, int bitCount);
    void ExitBitsStruct();

    string ReadVariantType(string fieldName);
    void EnterVariant(string fieldName, string variantName);
    void ExitVariant();

    bool HasField(string fieldName);
}
```

### 3.3 IModuleResolver

Used during compilation to resolve `@import` references.

```csharp
/// <summary>
/// Resolves module names to script source text during compilation.
/// </summary>
public interface IModuleResolver
{
    /// <summary>
    /// Returns the script source for the given module name, or null if not found.
    /// </summary>
    string? ResolveModule(string moduleName);
}
```

The default implementation is `DictionaryModuleResolver` backed by a `Dictionary<string, string>`, populated via the C-ABI's `binscript_compiler_add_module`.

## 4. Compilation Pipeline

```
Source (.bsx)
    │
    ▼
┌─────────┐     ┌─────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Lexer   │ ──▶ │ Parser  │ ──▶ │ Semantic        │ ──▶ │ Bytecode        │
│          │     │         │     │ Analyzer        │     │ Emitter         │
│ chars →  │     │ tokens →│     │ AST →           │     │ typed AST →     │
│ tokens   │     │ AST     │     │ typed AST       │     │ BytecodeProgram │
└─────────┘     └─────────┘     └─────────────────┘     └─────────────────┘
```

### 4.1 Lexer

Converts source text into a stream of tokens. Handles comments, string literals (with escapes), numeric literals (decimal, hex, binary, octal), identifiers, keywords, operators, and directives.

### 4.2 Parser

Recursive descent parser producing an AST. The formal grammar is defined in [GRAMMAR.md](GRAMMAR.md) (EBNF notation). The grammar is LL(1)-friendly with minimal lookahead. Key AST nodes:

- `ScriptFile` → list of top-level declarations
- `StructDecl` → name, parameters, fields, directives
- `BitsStructDecl` → name, base type, bit fields
- `EnumDecl` → name, base type, variants
- `ConstDecl` → name, value expression
- `FieldDecl` → name, type, modifiers (derived, hidden, assert, etc.)
- `MatchExpr` → discriminant expression, arms (pattern + optional guard + type)
- `Expression` → binary ops, unary ops, field access, function calls, literals

### 4.3 Semantic Analyzer

Validates the AST:
- Type checking: field types exist, expression types are compatible
- Name resolution: all referenced fields, structs, enums, constants are defined
- Import resolution: all `@import` modules are resolved via `IModuleResolver`
- Parameter validation: struct parameters match call sites
- Recursion analysis: builds the struct reference graph, detects cycles via SCC (strongly connected components). Cycles are allowed only if every edge in the cycle passes through a termination guard (`when`, non-default `match` arm, or nullable `?`). Unguarded cycles produce error E030.
- `@root` validation: exactly one root struct (or zero, if caller always specifies entry point)
- `@derived` validation: expression references only fields that precede this field in the struct
- `@map` validation: bodies are pure expressions (no side effects, no self-references, no cycles between maps)
- Pointer validation: `ptr<T>` fields require `@param base_ptr` in scope; inner type `T` must be a valid field type

### 4.4 Bytecode Emitter

Translates the typed AST into a flat bytecode program. See [Bytecode Format](BYTECODE.md) for the instruction set.

Key optimization: for "flat" structs (no conditionals, no seeks, no variable-length fields), the emitter produces a compact Tier 1 instruction sequence that the runtime can execute as a simple loop.

## 5. Runtime

### 5.1 Parse Engine

Executes bytecode against a `ReadOnlySpan<byte>` input buffer, calling `IResultEmitter` methods as it processes each field.

**State:**
- `ReadOnlySpan<byte> Input` — the input buffer (never copied)
- `long Position` — current read cursor
- `ParseContext` — stack of struct contexts (for nested structs, `@let` bindings, parameters)
- `IResultEmitter Emitter` — output sink

**Key invariants:**
- The input buffer is never modified.
- No heap allocations in the Tier 1 fast path (flat structs with primitives only).
- `@at` blocks push/pop the position stack.
- Array parsing uses a tight loop with the termination condition evaluated after each element.

### 5.2 Produce Engine

Reads from `IDataSource` and writes into a `Span<byte>` output buffer.

**Two-pass architecture:**
1. **Size pass**: Walk the data source and bytecode to calculate the total output size and the offset of every field. This pass doesn't write any bytes. For `ptr<T>` fields, the size pass also computes the data region layout — where each pointed-to value will be placed.
2. **Write pass**: Walk again, writing bytes at the calculated offsets. `@derived` fields are computed during this pass. Pointer values are written as `base_ptr + data_offset` (absolute) or `target_offset - field_offset` (relative).

For Tier 1 (fixed-size) structs, the compiler statically knows the size, so the size pass is skipped.

#### Pointer Data Region Layout

When a struct contains `ptr<T>` fields, the produce engine manages a **trailing data region** after the struct's fixed-size fields. During the size pass:

1. Compute the fixed-size portion of the struct (all non-`@inline` pointer widths + other fields).
2. For each `ptr<T>` field (in declaration order), compute the size of the pointed-to data and append it to the data region.
3. Total struct size = fixed portion + data region size.

During the write pass:
- Pointer field slots receive computed pointer values (`base_ptr + data_region_start + field_data_offset`).
- The data region is filled with the serialized pointed-to values.

For `@inline` pointers, the pointed-to data is placed immediately after the pointer field instead of in the trailing region. This makes the struct variable-sized and may affect layout of subsequent fields.

#### Recursive Produce

For recursive structs (e.g., linked lists, IFD chains), the produce engine recurses into the data source tree. The `@max_depth` limit applies as a safety net. Recursion terminates when:
- A nullable field is null in the data source
- The data source has no value for the recursive field
- The depth limit is reached

### 5.3 Expression Evaluator

A stack-based evaluator for bytecode expressions. Supports:
- Integer and float arithmetic
- Bitwise operations
- Comparisons
- Logical operations
- Field value lookup (from the parse context)
- Built-in function calls (`@sizeof`, `@crc32`, etc.)
- String method calls (`.starts_with()`, etc.)
- Array search methods (`.find()`, `.any()`, `.all()`) via linear scan with predicate evaluation

### 5.4 Recursion Depth Tracking

> **Note:** The original design used dedicated `DEPTH_CHECK` / `DEPTH_POP` opcodes for per-struct depth tracking. These were removed during implementation — the current approach relies on the call stack depth limit inherent in the struct call mechanism. Circular structures are deferred for v1 (see ADR-002), so explicit depth tracking is not currently needed.

## 6. Thread Safety Model

- `BinScriptProgram` (compiled bytecode) is **immutable** after compilation. It can be shared across threads without synchronization.
- `ParseEngine` and `ProduceEngine` are **not thread-safe** — each parse/produce call creates its own context (stack-allocated where possible).
- `BinScriptCompiler` is **not thread-safe** — compile from a single thread, then share the result.
- The C-ABI `BinScript*` handle wraps a `BinScriptProgram` pinned via `GCHandle`. The handle itself is a pointer, safe to pass across threads.

## 7. Error Model

### 7.1 Compilation Errors

```csharp
public class Diagnostic
{
    public DiagnosticSeverity Severity { get; }  // Error, Warning, Info
    public string Code { get; }                   // e.g., "E012"
    public string Message { get; }                // human-readable
    public SourceSpan Span { get; }               // file, line, column, length
}

public class CompilationResult
{
    public BinScriptProgram? Program { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool Success => Program is not null;
}
```

### 7.2 Parse Errors

```csharp
public class ParseResult
{
    public bool Success { get; }
    public string? Output { get; }                // emitter output (e.g., JSON string)
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public long BytesConsumed { get; }
    public long InputSize { get; }
    public CoverageReport Coverage { get; }
}

public class CoverageReport
{
    public double Percentage { get; }
    public IReadOnlyList<(long Offset, long Length)> ReadRanges { get; }
    public IReadOnlyList<(long Offset, long Length)> Gaps { get; }
}

public enum ParseStrictness
{
    Lenient,           // parse what you can, no warnings for unconsumed
    WarnUnconsumed,    // warn if bytes remain and script isn't @coverage(partial)
    StrictFull,        // error if any bytes unaccounted for
}
```

## 8. NativeAOT / C-ABI Layer

`BinScript.Interop` is a NativeAOT library that:

1. Exposes `[UnmanagedCallersOnly]` static methods matching the C-ABI surface.
2. Uses `GCHandle.Alloc` to pin managed objects behind opaque `IntPtr` handles.
3. Marshals strings as UTF-8 `byte*` (null-terminated).
4. Uses thread-local storage for `binscript_last_error()`.
5. Never throws exceptions across the native boundary — all errors are captured and surfaced via the error API.

The canonical C header is at `src/BinScript.Interop/binscript.h` — this is the single source of truth for all C-ABI consumers. Working integration examples in C, Python, and Go live in `tools/examples/`.

See [C-ABI Reference](C_ABI.md) for the complete function list and the maintenance checklist for keeping all consumers in sync when the API surface changes.

## 9. Design Decisions & Rationale

| Decision | Rationale |
|----------|-----------|
| Custom bytecode, not expression trees | Persistable, fast to interpret, cache-friendly |
| Two-tier bytecode | Tier 1 is near-zero-overhead for flat structs; Tier 2 handles complex control flow |
| Caller-owns-buffer for produce | Zero-copy, no engine allocation overhead, host controls memory |
| `IResultEmitter` / `IDataSource` interfaces | Decouples the engine from any specific serialization format |
| `@let` for cross-branch refs | Makes data flow explicit; avoids implicit global scope |
| `@at` block scoping | Makes seek-and-return explicit; prevents cursor state confusion |
| `@map` instead of UDFs | Compile-time inlined pure expressions — reusable without Turing-completeness. See [ADR-001](adr/ADR-001-map-pure-expressions.md) |
| `ptr<T>` transparent deref | Hides pointer mechanics from JSON output; pointer values are computed, not user-provided |
| Trailing data region for produce | Natural C-like layout; `@inline` opt-in for cache-friendly alternatives. See [ADR-003](adr/ADR-003-pointer-extension.md) |
| Guarded recursion, not unlimited | Compiler verifies termination guards; runtime depth limit as safety net |
| Circular structures deferred | JSON can't represent cycles; real binary formats are acyclic. See [ADR-002](adr/ADR-002-circular-structures-deferred.md) |
| Reserved keyword escaping deferred | Keyword collisions are rare; backtick escaping planned for future. See [ADR-004](adr/ADR-004-keyword-escaping.md) |
| Dual-mode ParseContext | Live-mode pointer chasing for FFI structs; buffer mode unchanged. See [ADR-005](adr/ADR-005-dual-mode-parse-context.md) |
| BSX-Light ⎕NA superset | Compact ⎕NA-compatible front-end compiling to BinScript bytecode. See [ADR-006](adr/ADR-006-bsx-light-na-superset.md) |
| Enums in JSON as names | Readable output; both name and numeric accepted on input |
| `_variant` tag for match | Explicit discrimination avoids fragile inference in produce mode |
