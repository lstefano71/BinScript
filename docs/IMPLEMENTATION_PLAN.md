# BinScript Implementation Plan

## Phase Overview

The implementation is organized into 6 phases, each producing a testable, working increment.

```
Phase 1: Compiler Frontend (Lexer, Parser, AST)
    ↓
Phase 2: Semantic Analysis & Bytecode Emission
    ↓
Phase 3: Parse Engine (Tier 1 + Tier 2) + JSON Emitter
    ↓
Phase 4: C-ABI Interop Layer (NativeAOT)
    ↓
Phase 5: Produce Engine (JSON → Binary) + Bidirectionality
    ↓
Phase 6: Standard Library (.bsx definitions + test samples)
```

---

## Phase 1: Compiler Frontend

**Goal:** Parse `.bsx` source text into a validated AST.

### Tasks

1. **Project scaffolding**
   - Create `BinScript.sln` with projects: `BinScript.Core`, `BinScript.Emitters.Json`, `BinScript.Interop`, `BinScript.Tests`
   - Target `.NET 10`, `C# 14`, enable `<PublishAot>true</PublishAot>` on Interop project
   - Add test framework (xUnit)

2. **Token types & Lexer** (`Compiler/Token.cs`, `Compiler/Lexer.cs`)
   - Tokenize all keywords, directives, operators, literals, identifiers
   - Handle comments (`//`, `/* */`)
   - Handle numeric literals (decimal, `0x`, `0b`, `0o`)
   - Handle string literals with escape sequences
   - Track source positions (line, column) for error reporting
   - **Tests:** Lex every token type, edge cases (nested comments, escape sequences, unterminated strings)

3. **AST node types** (`Compiler/Ast/*.cs`)
   - `ScriptFile`, `StructDecl`, `BitsStructDecl`, `EnumDecl`, `ConstDecl`
   - `FieldDecl` with modifiers (derived, hidden, assert, encoding, etc.)
   - `ImportDecl`, `ParamDecl`
   - `Expression` hierarchy: `BinaryExpr`, `UnaryExpr`, `LiteralExpr`, `FieldAccessExpr`, `FunctionCallExpr`, `MethodCallExpr`, `MatchExpr`
   - `MatchArm` with pattern (value, range, wildcard) + optional guard expression
   - `ArraySpec` (count, until, sentinel, greedy)
   - `SeekDirective`, `AtBlock`, `AlignDirective`, `SkipDirective`

4. **Parser** (`Compiler/Parser.cs`)
   - Recursive descent, LL(1)
   - Parse all declarations: struct, bits struct, enum, const, import, param
   - Parse field declarations with all modifiers
   - Parse expressions with precedence climbing
   - Parse match expressions with guards and range patterns
   - Parse array specifications
   - Parse `@at` blocks, `@seek`, `@align`, `@skip`
   - Error recovery: skip to next declaration on error, collect diagnostics
   - **Tests:** Parse valid scripts, parse error cases, verify AST structure

### Deliverable
A `Lexer` + `Parser` that converts `.bsx` source → AST with full error reporting. No semantic validation yet.

---

## Phase 2: Semantic Analysis & Bytecode Emission

**Goal:** Validate the AST and compile it to bytecode.

### Tasks

1. **Module resolution** (`Core/Interfaces/IModuleResolver.cs`, `Core/Api/DictionaryModuleResolver.cs`)
   - `IModuleResolver` interface
   - `DictionaryModuleResolver` backed by `Dictionary<string, string>`
   - Resolve `@import` declarations: parse imported scripts, merge ASTs

2. **Type resolver** (`Compiler/TypeResolver.cs`)
   - Build a type table: all structs, bits structs, enums, constants
   - Resolve field types to their declarations
   - Validate struct parameter counts match call sites
   - Detect unknown types, duplicate declarations, circular references

3. **Semantic analyzer** (`Compiler/SemanticAnalyzer.cs`)
   - Validate `@root` annotation (at most one)
   - Validate `@derived` expressions reference only preceding fields
   - Validate `@assert` expressions
   - Validate match exhaustiveness (warn if no default arm)
   - Validate `@let` bindings reference valid fields
   - Validate array count expressions
   - Type-check expressions (numeric vs string vs bool)
   - Produce typed AST with resolved references
   - **Tests:** Valid scripts pass, invalid scripts produce specific diagnostics

4. **Bytecode definition** (`Bytecode/Opcode.cs`, `Bytecode/Instruction.cs`, `Bytecode/BytecodeProgram.cs`)
   - Define all opcodes as an enum
   - `BytecodeProgram`: bytecode array + string table + struct metadata table

5. **Bytecode emitter** (`Compiler/BytecodeEmitter.cs`)
   - Walk the typed AST, emit bytecode instructions
   - Emit Tier 1 instructions for flat structs (detect eligibility)
   - Emit Tier 2 instructions for complex structs
   - Emit expression bytecode (precedence-climbing → stack operations)
   - Build string table (intern all field names, struct names, string literals)
   - Calculate static sizes where possible
   - **Tests:** Compile known scripts, verify bytecode sequences, verify string table

6. **Bytecode serialization** (`Bytecode/BytecodeSerializer.cs`, `Bytecode/BytecodeDeserializer.cs`)
   - Serialize `BytecodeProgram` to `byte[]` (format per BYTECODE.md)
   - Deserialize `byte[]` → `BytecodeProgram` with version validation
   - Option to include/strip source
   - **Tests:** Round-trip serialize/deserialize, validate format, reject bad versions

7. **Public compile API** (`Api/BinScriptCompiler.cs`)
   - `BinScriptCompiler` class with `AddModule`, `Compile` methods
   - Returns `CompilationResult` (program + diagnostics)
   - **Tests:** End-to-end compile from script text to program

### Deliverable
Complete compilation pipeline: `.bsx` source → validated AST → bytecode. Bytecode save/load.

---

## Phase 3: Parse Engine + JSON Emitter

**Goal:** Execute bytecode against binary data and produce JSON output.

### Tasks

1. **Parse context** (`Runtime/ParseContext.cs`)
   - Field value table (indexed by field_id)
   - Parameter stack for nested struct calls
   - Position stack for `@at` blocks
   - Array index stack
   - Bit accumulator for bit-level reads
   - `@let` binding storage

2. **Expression evaluator** (`Runtime/ExpressionEvaluator.cs`)
   - Stack-based evaluation of expression bytecodes
   - Support all operators, field access, built-in functions
   - String method evaluation (`.starts_with()`, etc.)
   - **Tests:** Evaluate expressions in isolation

3. **Built-in functions** (`Runtime/BuiltinFunctions.cs`)
   - `@sizeof`, `@offset_of`, `@count`, `@strlen`
   - `@crc32`, `@adler32` (use System.IO.Hashing)
   - **Tests:** Verify each function against known values

4. **Parse engine** (`Runtime/ParseEngine.cs`)
   - Main interpretation loop
   - Tier 1 fast path: tight loop for flat struct instructions
   - Tier 2: full instruction dispatch with stack, seeking, control flow
   - Array handling: count, until, sentinel, greedy
   - Match handling: evaluate arms, dispatch to selected struct
   - Coverage tracking: record all read ranges
   - Error handling: bounds checks, assertion failures, strictness modes
   - **Tests:** Parse hand-crafted binary buffers with known scripts

5. **IResultEmitter interface** (`Interfaces/IResultEmitter.cs`)
   - Define the interface (as per ARCHITECTURE.md)

6. **JSON emitter** (`Emitters.Json/JsonResultEmitter.cs`)
   - Implement `IResultEmitter` using `System.Text.Json.Utf8JsonWriter`
   - Emit bytes as hex strings
   - Emit enums as names (or numeric if unknown)
   - Emit bit structs as objects with named fields
   - Emit match variants with `_variant` tag
   - **Tests:** Verify JSON shape for all field types

7. **ParseResult & CoverageReport** (`Model/ParseResult.cs`, `Model/Coverage.cs`)
   - Aggregate results: success, output, diagnostics, coverage

8. **Public parse API** (`Api/BinScriptProgram.cs`)
   - `BinScriptProgram.Parse(ReadOnlySpan<byte>, ParseOptions)` → `ParseResult`
   - `BinScriptProgram.ParseJson(ReadOnlySpan<byte>, ParseOptions)` → convenience
   - **Tests:** End-to-end: compile + parse + verify JSON

### Deliverable
Working end-to-end pipeline: `.bsx` → compile → parse binary buffer → JSON output.

---

## Phase 4: C-ABI Interop Layer

**Goal:** Expose the C# API as a NativeAOT DLL with C-compatible functions.

### Tasks

1. **Handle management** (`Interop/HandleTable.cs`)
   - `GCHandle`-based table for `BinCompiler*` and `BinScript*` handles
   - Thread-safe handle allocation and retrieval
   - Handle validation (detect use-after-free)

2. **Error state** (`Interop/ErrorState.cs`)
   - Thread-local last error string
   - Helper to set error from exceptions

3. **Native exports** (`Interop/NativeExports.cs`)
   - `[UnmanagedCallersOnly(EntryPoint = "binscript_compiler_new")]` etc.
   - All functions from C_ABI.md
   - UTF-8 string marshalling (manual, since NativeAOT)
   - Exception → error state conversion (no exceptions cross native boundary)
   - **Tests:** Integration tests that compile, parse, and verify via the C# wrapper (simulating native calls)

4. **NativeAOT build configuration** (`Interop/BinScript.Interop.csproj`)
   - `<PublishAot>true</PublishAot>`
   - `<InvariantGlobalization>true</InvariantGlobalization>` (if applicable)
   - Trim settings
   - Generate `binscript.h` header file alongside the DLL

5. **Build & smoke test**
   - `dotnet publish -c Release` → verify DLL exports
   - Load DLL from a simple C or Python test

### Deliverable
NativeAOT DLL (`binscript.dll` / `libbinscript.so`) with all C-ABI functions working.

---

## Phase 5: Produce Engine (Bidirectionality)

**Goal:** Produce binary data from JSON using the same compiled script.

### Tasks

1. **IDataSource interface** (`Interfaces/IDataSource.cs`)
   - Define the interface (as per ARCHITECTURE.md)

2. **JSON data source** (`Emitters.Json/JsonDataSource.cs`)
   - Implement `IDataSource` using `System.Text.Json.JsonDocument`
   - Navigate JSON object structure matching struct fields
   - Read `_variant` for match arms
   - Accept enum names or numeric values
   - Decode hex strings back to bytes
   - **Tests:** Read all field types from JSON

3. **Produce context** (`Runtime/ProduceContext.cs`)
   - Output buffer (`Span<byte>`)
   - Write cursor position
   - Position stack for `@at` blocks
   - Field offset table (for derived field calculation)

4. **Size calculation pass** (`Runtime/ProduceEngine.cs` — CalcSize)
   - Walk bytecode + data source to compute total output size
   - Record offsets of every field
   - Handle static-size optimization (skip this pass when compiler reports static size)
   - **Tests:** Verify sizes for fixed and variable structs

5. **Write pass** (`Runtime/ProduceEngine.cs` — ProduceInto)
   - Walk bytecode + data source, write bytes at calculated offsets
   - Compute `@derived` fields (CRC, sizeof, etc.)
   - Write magic constants directly
   - Handle `@seek`/`@at` positioning
   - Handle alignment padding
   - Buffer overflow detection
   - **Tests:** Produce known binary structures from JSON

6. **Static size query** (`Api/BinScriptProgram.cs`)
   - `GetStaticSize(string? entryPoint)` → `long?`
   - Returns the compiler-calculated size, or null if dynamic

7. **C-ABI produce functions** (`Interop/NativeExports.cs`)
   - `binscript_from_json_static_size`
   - `binscript_from_json_calc_size`
   - `binscript_from_json_into`
   - `binscript_from_json` (convenience)

8. **Round-trip tests**
   - For each test case: parse → JSON → produce → parse again → compare JSON
   - **Tests:** Round-trip fidelity for all supported types

### Deliverable
Full bidirectional pipeline: binary ↔ JSON via the same script.

---

## Phase 6: Standard Library & Integration Tests

**Goal:** Write `.bsx` definitions for 6 real formats, with real binary samples and comprehensive tests.

### Tasks

1. **Acquire / create binary samples**
   - PE: create minimal valid PEs using a linker or by hand-crafting
   - ZIP: create with standard zip tools
   - PNG: create tiny PNGs with image libraries
   - ELF: cross-compile minimal programs (or hand-craft)
   - BMP: create tiny BMPs with image libraries
   - GIF: create with image libraries
   - Keep files small (under 10KB each where possible)

2. **Write `.bsx` scripts** (one per format)
   - `stdlib/pe.bsx` — full PE/COFF including optional header, sections
   - `stdlib/zip.bsx` — EOCD, central directory, local file headers
   - `stdlib/png.bsx` — signature, chunks, IHDR/PLTE/IDAT/IEND
   - `stdlib/elf.bsx` — ELF header, program headers, section headers
   - `stdlib/bmp.bsx` — file header, DIB header, palette, pixel data
   - `stdlib/gif.bsx` — header, screen descriptor, image blocks, extensions
   - Each script should be well-commented and serve as a language tutorial

3. **Generate expected JSON**
   - Parse each sample with its script
   - Hand-verify the JSON output against known-good format documentation
   - Save as `tests/expected/{format}/{sample}.json`

4. **Write test classes** (`Tests/Stdlib/*.cs`)
   - `PeTests.cs`, `ZipTests.cs`, `PngTests.cs`, `ElfTests.cs`, `BmpTests.cs`, `GifTests.cs`
   - Parse test: compare against expected JSON
   - Round-trip test: parse → produce → parse → compare
   - Error test: truncated files, wrong magic bytes
   - Coverage test: verify coverage percentages

5. **Performance benchmark**
   - Parse 10,000 PE DOS+COFF headers from pre-loaded buffers
   - Measure against a hand-written C# parser
   - Target: within 2x of hand-written

6. **Documentation updates**
   - Update README with final API examples
   - Update STDLIB.md with actual struct lists
   - Review and finalize all docs

### Deliverable
Complete, tested library with 6 format definitions, ready for real-world use.

---

## Dependency Graph

```
Phase 1 ──→ Phase 2 ──→ Phase 3 ──→ Phase 4
                              │
                              └──→ Phase 5 ──→ Phase 6
```

- Phases 4 and 5 can be worked in parallel after Phase 3.
- Phase 6 depends on both Phase 3 (parse) and Phase 5 (produce) for round-trip tests, but parse-only tests can begin after Phase 3.

## Testing Strategy

| Level | What | When |
|-------|------|------|
| Unit tests | Individual components (lexer, parser, evaluator, etc.) | Each phase |
| Integration tests | End-to-end compile + parse/produce | Phases 3, 5 |
| Format tests | Real binary files + `.bsx` scripts | Phase 6 |
| C-ABI tests | Native interop round-trips | Phase 4 |
| Performance tests | Benchmark against hand-written parsers | Phase 6 |
| Round-trip tests | Parse ↔ produce fidelity | Phase 5, 6 |

## Risk Areas

| Risk | Mitigation |
|------|------------|
| Expression evaluator performance | Keep Tier 1 fast path; expressions only in Tier 2 |
| Bidirectional complexity | Start with simple structs; add match/seek produce incrementally |
| NativeAOT trimming breaks reflection | Avoid reflection; use source generators if needed |
| Complex formats reveal language gaps | Start stdlib early (Phase 6 task 2 can begin during Phase 3) |
| Bytecode format churn | Version the format; accept that v1 may change |
