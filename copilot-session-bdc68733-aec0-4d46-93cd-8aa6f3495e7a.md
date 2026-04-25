# 🤖 Copilot CLI Session

> [!NOTE]
> - **Session ID:** `bdc68733-aec0-4d46-93cd-8aa6f3495e7a`  
> - **Started:** 25/04/2026, 11:37:09  
> - **Duration:** 13m 4s  
> - **Exported:** 25/04/2026, 11:50:14  

---

<sub>⏱️ 0s</sub>

### ℹ️ Info

💡 No copilot instructions found. Run /init to generate a copilot-instructions.md file for this project.

---

<sub>⏱️ 3s</sub>

### 👤 User

/init

---

<sub>⏱️ 2s</sub>

### ℹ️ Info

Environment loaded: 13 skills, 3 MCP servers, 2 plugins

---

<sub>⏱️ 7s</sub>

### 💭 Reasoning

*Let me start by exploring the repository structure to understand the codebase before creating the copilot-instructions.md file.*

---

<sub>⏱️ 11s</sub>

### ✅ `view`

**view the file at C:\Users\stf\Source\Repos\Binary.**

C:\Users\stf\Source\Repos\Binary

<details>
<summary>8 lines</summary>

```
.git
.gitignore
LICENSE
README.md
docs
src
stdlib
tests
```

</details>


---

<sub>⏱️ 11s</sub>

### ✅ `view`

**view the file at C:\Users\stf\Source\Repos\Binary\README.md.**

C:\Users\stf\Source\Repos\Binary\README.md

<details>
<summary>108 lines</summary>

````
1. # BinScript
2. 
3. **A high-performance binary structure parser and producer with a human-readable description language.**
4. 
5. BinScript lets you describe binary formats — PE executables, ZIP archives, PNG images, ELF binaries, Parquet files, and more — using a concise, declarative script language. It compiles scripts into a fast bytecode that can parse binary data into structured output (JSON initially) and produce binary data from structured input.
6. 
7. ## Key Features
8. 
9. - **Declarative script language** (`.bsx`) with embedded expressions for complex formats
10. - **Two-phase architecture**: compile once, parse many times (like a prepared statement)
11. - **Bidirectional**: parse binary → JSON, produce JSON → binary with the same script
12. - **High performance**: tight bytecode, caller-owns-buffer, persistable compiled scripts
13. - **C-ABI DLL** (NativeAOT): usable from any language with an FFI
14. - **Extensible emitters**: JSON first, pluggable interface for future formats
15. - **Standard library**: ships with `.bsx` definitions for PE, ZIP, PNG, ELF, BMP, GIF
16. 
17. ## Architecture
18. 
19. ```
20. C# Core Library (.NET 10, C# 14, NativeAOT)
21. ├── BinScript.Core         — compiler, bytecode VM, IR
22. ├── BinScript.Emitters.Json — IResultEmitter / IDataSource for JSON
23. ├── BinScript.Interop       — C-ABI shim (NativeAOT exported functions)
24. └── BinScript.Tests         — unit + integration tests with real binary samples
25. ```
26. 
27. ## Quick Example
28. 
29. ```
30. // pe_quick.bsx — parse just the DOS + COFF headers
31. @default_endian(little)
32. @coverage(partial)
33. 
34. @root struct PeQuickHeader {
35.     dos: DosHeader,
36.     @seek(dos.e_lfanew)
37.     pe_sig: u32 = 0x00004550,
38.     coff: CoffHeader,
39. }
40. 
41. struct DosHeader {
42.     e_magic: u16 = 0x5A4D,
43.     e_cblp: u16,
44.     e_cp: u16,
45.     // ... more fields ...
46.     e_lfanew: u32,
47. }
48. 
49. struct CoffHeader {
50.     machine: Machine,
51.     number_of_sections: u16,
52.     time_date_stamp: u32,
53.     pointer_to_symbol_table: u32,
54.     number_of_symbols: u32,
55.     size_of_optional_header: u16,
56.     characteristics: Characteristics,
57. }
58. 
59. enum Machine : u16 {
60.     I386  = 0x014C,
61.     AMD64 = 0x8664,
62.     ARM64 = 0xAA64,
63. }
64. 
65. bits struct Characteristics : u16 {
66.     relocs_stripped: bit,
67.     executable_image: bit,
68.     line_nums_stripped: bit,
69.     local_syms_stripped: bit,
70.     aggressive_ws_trim: bit,
71.     large_address_aware: bit,
72.     _reserved: bit,
73.     bytes_reversed_lo: bit,
74.     _32bit_machine: bit,
75.     debug_stripped: bit,
76.     removable_run_from_swap: bit,
77.     net_run_from_swap: bit,
78.     system: bit,
79.     dll: bit,
80.     up_system_only: bit,
81.     bytes_reversed_hi: bit,
82. }
83. ```
84. 
85. ## Documentation
86. 
87. - [Product Requirements Document](docs/PRD.md)
88. - [Language Specification](docs/LANGUAGE_SPEC.md)
89. - [Architecture & Design](docs/ARCHITECTURE.md)
90. - [C-ABI Reference](docs/C_ABI.md)
91. - [Bytecode Format](docs/BYTECODE.md)
92. - [Standard Library](docs/STDLIB.md)
93. - [Implementation Plan](docs/IMPLEMENTATION_PLAN.md)
94. 
95. ## Building
96. 
97. Requires .NET 10 SDK with C# 14 support.
98. 
99. ```bash
100. dotnet build src/BinScript.sln
101. dotnet test src/BinScript.Tests
102. dotnet publish src/BinScript.Interop -c Release  # produces NativeAOT DLL
103. ```
104. 
105. ## License
106. 
107. TBD
108.
````

</details>


---

<sub>⏱️ 11s</sub>

### ✅ `glob`

**.github/**/***

".github/**/*"

```
No files matched the pattern.
```


---

<sub>⏱️ 11s</sub>

### ✅ `glob`

**{CLAUDE.md,AGENTS.md,CONTRIBUTING.md,.cursorrules,.windsurfrules,CONVENTIONS.md,AIDER_CONVENTIONS.md,.clinerules,.cline_rules,.cursor/rules/*}**

"{CLAUDE.md,AGENTS.md,CONTRIBUTING.md,.cursorrules,.windsurfrules,CONVENTIONS.md,AIDER_CONVENTIONS.md,.clinerules,.cline_rules,.cursor/rules/*}"

```
No files matched the pattern.
```


---

<sub>⏱️ 15s</sub>

### ✅ `view`

**view the file at C:\Users\stf\Source\Repos\Binary\src.**

C:\Users\stf\Source\Repos\Binary\src

```
BinScript.Core
BinScript.Emitters.Json
BinScript.Interop
BinScript.Tests
BinScript.slnx
```


---

<sub>⏱️ 15s</sub>

### ✅ `view`

**view the file at C:\Users\stf\Source\Repos\Binary\docs.**

C:\Users\stf\Source\Repos\Binary\docs

<details>
<summary>8 lines</summary>

```
ARCHITECTURE.md
BYTECODE.md
C_ABI.md
IMPLEMENTATION_PLAN.md
LANGUAGE_SPEC.md
PRD.md
STDLIB.md
adr
```

</details>


---

<sub>⏱️ 15s</sub>

### ✅ `view`

**view the file at C:\Users\stf\Source\Repos\Binary\stdlib.**

C:\Users\stf\Source\Repos\Binary\stdlib

<details>
<summary>9 lines</summary>

```
.gitkeep
bmp.bsx
elf.bsx
gif.bsx
jpg.bsx
memory
pe.bsx
png.bsx
zip.bsx
```

</details>


---

<sub>⏱️ 15s</sub>

### ✅ `view`

**view the file at C:\Users\stf\Source\Repos\Binary\tests.**

C:\Users\stf\Source\Repos\Binary\tests

```
expected
samples
```


---

<sub>⏱️ 26s</sub>

### ✅ `task`

**Deep codebase exploration**

<details>
<summary>Arguments</summary>

```json
{
  "name": "explore-codebase",
  "prompt": "Explore this BinScript codebase thoroughly. I need to understand:\n\n1. **Project structure**: What's in each src/ project directory? List key files and their purposes.\n2. **Build system**: Look at BinScript.slnx and any .csproj files. What target framework, language version, nullable settings, etc.?\n3. **Test structure**: What's in BinScript.Tests? What test framework is used? How are tests organized? How to run a single test?\n4. **Architecture patterns**: Look at key source files in BinScript.Core, BinScript.Emitters.Json, BinScript.Interop. What are the main interfaces, base classes, patterns?\n5. **Docs**: Read docs/ARCHITECTURE.md and docs/LANGUAGE_SPEC.md for key conventions.\n6. **ADRs**: What's in docs/adr/?\n7. **stdlib**: Look at the .bsx files for patterns/conventions.\n8. **Tests**: Look at tests/expected/ and tests/samples/ directories.\n\nWorking directory: C:\\Users\\stf\\Source\\Repos\\Binary\n\nProvide a comprehensive summary of everything you find, organized by category.",
  "agent_type": "explore",
  "description": "Deep codebase exploration",
  "mode": "background"
}
```

</details>

```
Agent started in background with agent_id: explore-codebase. You'll be notified when it completes. Tell the user you're waiting and end your response, or continue unrelated work until notified. The agent supports multi-turn conversations — use write_agent to send follow-up messages.
```


---

<sub>⏱️ 34s</sub>

### 💬 Copilot

Exploring the codebase in the background — I'll have a comprehensive analysis shortly. Waiting for the exploration to complete before drafting the instructions file.

---

<sub>⏱️ 2m 36s</sub>

### ✅ `read_agent`

**Explore agent (Deep codebase exploration)**

<details>
<summary>Arguments</summary>

```json
{
  "agent_id": "explore-codebase",
  "wait": true,
  "timeout": 120
}
```

</details>

```
Agent is still running after waiting 120s. agent_id: explore-codebase, agent_type: explore, status: running, description: Deep codebase exploration, elapsed: 129s, total_turns: 0, tool_calls_completed: 75, (timed out waiting for completion) Consider telling the user you're waiting, then end your response with no further tool calls. A completion notification will arrive as a new turn; no need to poll or redo its work.
```


---

<sub>⏱️ 2m 38s</sub>

### 💬 Copilot

Still running — the codebase has a lot to cover. I'll wait for completion.

---

<sub>⏱️ 3m 4s</sub>

### ✅ `read_agent`

**Explore agent (Deep codebase exploration)**

<details>
<summary>Arguments</summary>

```json
{
  "agent_id": "explore-codebase",
  "timeout": 180,
  "wait": true
}
```

</details>

<details>
<summary>181 lines</summary>

````
Agent is idle (waiting for messages). agent_id: explore-codebase, agent_type: explore, status: idle, description: Deep codebase exploration, elapsed: 157s, total_turns: 1

[Turn 0]
Now I have a comprehensive picture. Here's the full summary:

---

## 1. Project Structure

**Solution**: `src/BinScript.slnx` — 4 projects:

### `BinScript.Core/` — The main library
| Directory/File | Purpose |
|---|---|
| **`Api/`** | Public API surface |
| `BinScriptCompiler.cs` | Entry point: compiles `.bsx` source → `CompilationResult` (pipeline: Lex → Parse → TypeResolve → SemanticAnalyze → BytecodeEmit) |
| `BinScriptProgram.cs` | Wraps `BytecodeProgram`; provides `Parse()` and `Produce()`/`ProduceAlloc()` methods |
| `DictionaryModuleResolver.cs` | Simple `Dictionary<string,string>`-backed `IModuleResolver` |
| **`Compiler/`** | Compilation pipeline |
| `Lexer.cs` | Chars → tokens |
| `Token.cs` | Token types/definitions |
| `Parser.cs` | Recursive-descent LL(1) parser → AST |
| `Ast/` (13 files) | AST node types: `ScriptFile`, `StructDecl`, `FieldDecl`/`MemberDecl`, `EnumDecl`, `BitsStructDecl`, `ConstDecl`, `ImportDecl`, `ParamDecl`, `Expression`, `MatchExpr`, `ArraySpec`, `TypeReference`, `AstNode` |
| `SemanticAnalyzer.cs` | Type checking, name resolution, cycle detection (SCC), `@root`/`@derived`/`@map`/pointer validation |
| `TypeResolver.cs` | Resolves type references, handles `@import` |
| `BytecodeEmitter.cs` | Typed AST → `BytecodeProgram` |
| `CompilationResult.cs` | Record: `Program?`, `Ast?`, `Diagnostics`, `Success` property |
| **`Bytecode/`** | VM instruction format |
| `Opcode.cs` | 80+ opcodes: Tier 1 (fast-path reads), Tier 2 (control flow, seeking, emitter events, dynamic reads, expression VM, arithmetic, builtins, string methods, arrays, match, alignment) |
| `BytecodeProgram.cs` | Program container: `StructMeta[]`, `FieldMeta[]`, string table, `StructFlags`, `FieldFlags`, `StringEncoding`, `RuntimeVar` enums |
| `BytecodeBuilder.cs` | Builder API for constructing programs |
| `BytecodeSerializer.cs` / `BytecodeDeserializer.cs` | Binary save/load of compiled programs |
| **`Runtime/`** | Execution engines |
| `ParseEngine.cs` | Bytecode VM executing against `ReadOnlyMemory<byte>`, calling `IResultEmitter` |
| `ProduceEngine.cs` | Two-pass (size + write) engine: reads `IDataSource` → writes `Span<byte>` |
| `ParseContext.cs` | Stack of struct contexts for nested parsing |
| `ExpressionEvaluator.cs` | Evaluates expression opcodes on the stack |
| `BuiltinFunctions.cs` | `@sizeof`, `@offset_of`, `@count`, `@strlen`, `@crc32`, `@adler32` |
| `FieldValueTable.cs` | Stores field values during struct execution |
| `StackValue.cs` | Tagged union (`Int`/`Float`/`String`/`Bytes`) with aggressive inlining |
| **`Interfaces/`** | Core abstractions |
| `IResultEmitter.cs` | SAX-style event interface for parse output (Begin/EndRoot, Begin/EndStruct, EmitInt/UInt/Float/Bool/String/Bytes, bits, variants, arrays) |
| `IDataSource.cs` | Inverse of emitter — provides values during produce (Enter/Exit navigation, Read* methods) |
| `IModuleResolver.cs` | Resolves `@import` module names → source text |
| **`Model/`** | Data transfer objects |
| `ParseResult.cs` | Record: `Success`, `Output`, `Diagnostics`, `BytesConsumed`, `InputSize` |
| `ProduceResult.cs` | Record: `Success`, `BytesWritten`, `Diagnostics`, `OutputBytes?` |
| `Diagnostic.cs` | Record: `Severity` (Error/Warning/Info), `Code`, `Message`, `Span` |
| `ParseOptions.cs` | `MaxDepth` (default 256), `MaxArrayElements` (default 10M) |

### `BinScript.Emitters.Json/` — JSON emitter plugin
| File | Purpose |
|---|---|
| `JsonResultEmitter.cs` | `IResultEmitter` impl using `Utf8JsonWriter` (zero-allocation JSON) |
| `JsonDataSource.cs` | `IDataSource` impl navigating a `JsonDocument` via stack |
| `BinScriptProgramJsonExtensions.cs` | Extension methods: `program.ToJson(bytes)`, `program.FromJson(json)` |

### `BinScript.Interop/` — C-ABI native exports
| File | Purpose |
|---|---|
| `NativeExports.cs` | `[UnmanagedCallersOnly]` methods: `binscript_compiler_new`, `_add_module`, `_compile`, `_parse`, `_produce`, `_mem_free`, `_version`, `_last_error` etc. |
| `HandleTable.cs` | `GCHandle`-based opaque handle management (`Alloc`/`Get<T>`/`Free`) using `ConcurrentDictionary` |
| `ErrorState.cs` | `[ThreadStatic]` last-error string for C-ABI error reporting |

### `BinScript.Tests/` — Test project (see §3)

---

## 2. Build System

All 4 `.csproj` files share:
- **Target**: `net10.0` (.NET 10)
- **Language**: `LangVersion preview` (cutting-edge C#)
- **Nullable**: `enable`
- **Unsafe**: `AllowUnsafeBlocks true`
- **ImplicitUsings**: `enable`

Project-specific settings:
| Project | Extra Settings | Dependencies |
|---|---|---|
| **Core** | `IsTrimmable=true` | `System.IO.Hashing` (floating `*` version) |
| **Emitters.Json** | `IsTrimmable=true` | → Core |
| **Interop** | `PublishAot=true`, `IsTrimmable=true`, `InvariantGlobalization=true` | → Core, → Emitters.Json |
| **Tests** | `IsPackable=false` | xUnit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4; → Core, → Emitters.Json, → Interop |

Global `<Using Include="Xunit"/>` in the test project.

---

## 3. Test Structure

**Framework**: **xUnit** (v2.9.3)

**Organization** — mirrors `src/BinScript.Core` + other projects:

| Test Directory | Files | What it tests |
|---|---|---|
| `Compiler/` | `LexerTests.cs`, `ParserTests.cs`, `SemanticAnalyzerTests.cs`, `BytecodeEmitterTests.cs`, `SmokeTests.cs`, `InfrastructureTests.cs` | Each compilation pipeline stage |
| `Runtime/` | `ParseEngineTests.cs`, `ProduceEngineTests.cs`, `ExpressionEvaluatorTests.cs`, `RoundTripTests.cs` | VM execution, round-trip fidelity |
| `Bytecode/` | `SerializerTests.cs` | Bytecode save/load |
| `Emitters/` | (empty) | Placeholder for JSON emitter tests |
| `Interop/` | `NativeExportTests.cs` | C-ABI surface |
| `Stdlib/` | `PeTests.cs`, `ZipTests.cs`, `PngTests.cs`, `ElfTests.cs`, `BmpTests.cs`, `GifTests.cs`, `JpgTests.cs`, `MemoryTests.cs` | Integration tests compiling real `.bsx` stdlib scripts against sample binaries |

**Test patterns**:
- Helper `Compile(string source)` method compiles inline BinScript → `BytecodeProgram`
- Helper `ParseToJson(program, data)` runs parse → JSON string
- `RoundTripTests` verify `parse(produce(parse(data))) == parse(data)` via `ToJson`/`FromJson` extensions
- Stdlib tests use relative paths (`../../../../stdlib/`) to find `.bsx` files and `tests/samples/` for real binaries
- Many stdlib tests are `[Fact(Skip = "Requires compiler support for ...")]` — marking features not yet implemented

**Run a single test**:
```bash
cd src
dotnet test BinScript.Tests --filter "FullyQualifiedName~ParseEngineTests.FlatStruct_U8_U16le_U32le"
# or by class:
dotnet test BinScript.Tests --filter "ClassName=BinScript.Tests.Runtime.ParseEngineTests"
```

---

## 4. Architecture Patterns

### Pipeline Pattern
`Source → Lexer → Parser → TypeResolver → SemanticAnalyzer → BytecodeEmitter → BytecodeProgram`

Each stage is a separate class. Errors accumulate as `List<Diagnostic>` and are returned in `CompilationResult`. Pipeline bails early on errors after parsing and after semantic analysis.

### Strategy/Plugin Pattern
- **`IResultEmitter`** — SAX-style event-driven output (parse direction). `JsonResultEmitter` is the only impl.
- **`IDataSource`** — Navigation-based input (produce direction). `JsonDataSource` is the only impl.
- **`IModuleResolver`** — Module resolution. `DictionaryModuleResolver` is the only impl.

New output formats (MessagePack, CBOR, etc.) just implement `IResultEmitter`/`IDataSource`.

### Bytecode VM
- Register-free **stack machine** with a `StackValue` tagged union (Int/Float/String/Bytes)
- Two engines: `ParseEngine` (binary→structured) and `ProduceEngine` (structured→binary, two-pass: size then write)
- Opcodes organized in tiers: Tier 1 = fast-path fixed reads (0x01–0x18), Tier 2 = complex (control flow, seeking, dynamic reads, expressions)

### Interop / Handle Table Pattern
- NativeAOT-compiled DLL exposes flat C functions via `[UnmanagedCallersOnly]`
- Managed objects (compiler, program) wrapped in `GCHandle` → opaque `IntPtr` handles
- Thread-local `ErrorState` for last-error reporting (no exceptions across FFI boundary)

### Key Data Types
- `CompilationResult` — record with `Program?`, `Ast?`, `Diagnostics`
- `ParseResult` / `ProduceResult` — records with `Success`, diagnostics, byte counts
- `ParseOptions` — `MaxDepth=256`, `MaxArrayElements=10M`
- `StackValue` — performance-tuned tagged union with `AggressiveInlining`

---

## 5. Docs Summary

### `ARCHITECTURE.md`
- Full system diagram: Host App → C-ABI (`BinScript.Interop`) → `BinScript.Core` + `BinScript.Emitters.Json`
- Detailed compilation pipeline (4.1–4.4): Lexer, Parser (LL(1) recursive descent), SemanticAnalyzer (type checking, SCC cycle detection, `@map`/pointer validation), BytecodeEmitter
- Runtime: ParseEngine (position stack, no heap allocs on Tier 1 fast path), ProduceEngine (two-pass: size + write, trailing data region for pointers)
- Flat/Tier 1 structs get optimized compact bytecode

### `LANGUAGE_SPEC.md` (v0.2 Draft)
- **Declarative binary structure description language** (`.bsx`)
- **Bidirectional** — same script drives parse (binary→data) and produce (data→binary)
- **Lexical**: line/block comments, hex/binary/octal literals, string escapes, ~20 keywords
- **Directives**: `@root`, `@default_endian`, `@param`, `@import`, `@coverage`, `@let`, `@seek`, `@at`, `@align`, `@skip`, `@hidden`, `@derived`, `@assert`, `@encoding`, `@until`, `@until_sentinel`, `@greedy`, `@map`, `@max_depth`, `@inline`, `@show_ptr`, etc.
- **Types**: primitives (`u8`–`u64`, `i8`–`i64`, `f32`/`f64`, `bool`), endianness suffixes (`le`/`be`), strings (`cstring`, `string[n]`, `fixed_string[n]`), `bytes[n]`, pointers (`ptr<T, width>`, `relptr<T, width>`, nullable `?`)
- **Compound types**: `struct`, `bits struct`, `enum`, `const`, `match` expressions with arms and guards

---

## 6. ADRs

| ADR | Title | Status | Summary |
|---|---|---|---|
| **ADR-001** | `@map` — Pure Expression Functions | Accepted | Compile-time inlined pure expressions with named params. No recursion, no side effects, expression-only bodies. Used for RVA-to-offset etc. Not real functions — syntactic sugar with type checking. |
| **ADR-002** | Circular Structures — Deferred | Accepted (deferred) | v1 does NOT support circular references. Depth limit (256) catches cycles with clear error. JSON can't represent cycles; real binary formats are acyclic. Future options: `$ref`, visited-set, `@max_visits`, graph-mode. |
| **ADR-003** | Pointer Extension Design | Accepted | First-class `ptr<T, width>` (absolute, needs `@param base_ptr`) and `relptr<T, width>` (relative to field position). Nullable `?` for recursion termination. Transparent JSON deref by default; `@show_ptr` exposes address. Trailing data region layout for produce. New opcodes: `READ_PTR`, `DEREF_BEGIN/END`, `WRITE_PTR`, `NULL_CHECK`. |

---

## 7. Stdlib (`.bsx` files)

**Top-level** (`stdlib/`):
| File | Format | Key Patterns |
|---|---|---|
| `pe.bsx` | PE/COFF | `@default_endian(little)`, `@coverage(partial)`, extensive enums (`Machine`, `Subsystem`, `DebugType`...), `@root struct PeFile`, `match` on `coff_header.machine`, `@at`/`@seek` for data directories, `@map rva_to_offset` |
| `elf.bsx` | ELF | `@coverage(partial)`, `match` on `ei_class` (ELF32 vs ELF64), enums for OS ABI, type, machine |
| `png.bsx` | PNG | `@default_endian(big)`, `@root struct PngFile`, `chunks: PngChunk[] @until(@remaining == 0)`, `match(chunk_type)` dispatching IHDR/PLTE/IDAT/IEND/tEXt/etc. |
| `zip.bsx` | ZIP | `@default_endian(little)`, `bits struct GeneralPurposeFlags`, compression method enum, EOCD-based parsing |
| `bmp.bsx` | BMP | Bitmap format |
| `gif.bsx` | GIF | GIF87a/89a |
| `jpg.bsx` | JPEG | JPEG/JFIF |

**`stdlib/memory/`** — In-memory C structure descriptions:
| File | Description |
|---|---|
| `linked_list.bsx` | Singly-linked list with `ptr<Node, u64>?` nullable pointers, `@param base_ptr: u64` |
| `c_strings.bsx` | C string pointer patterns |
| `win32_startup.bsx` | Windows STARTUPINFO-like struct |

**Common conventions**:
- `@default_endian(...)` at top
- `@coverage(partial)` when not all fields are parsed
- Enums with explicit backing type (`: u16`, `: u32`)
- `@root struct` marks the entry point
- `match(field)` for discriminated unions
- `@until(@remaining == 0)` for reading until end of data
- `bits struct` for bitfield flags

---

## 8. Test Fixtures

### `tests/samples/` — Real binary files (small, valid)
| Format | Files |
|---|---|
| `pe/` | `tiny_x86.exe`, `tiny_x64.exe`, `tiny_dll.dll` |
| `elf/` | `hello_x64.elf` |
| `png/` | `rgb_8x8.png`, `indexed_4x4.png` |
| `bmp/` | `4x4_24bit.bmp`, `4x4_8bit.bmp` |
| `gif/` | `single.gif`, `2frame.gif` |
| `zip/` | `single.zip`, `hello.zip` |

### `tests/expected/` — Expected JSON output
Directories exist for `bmp/`, `elf/`, `gif/`, `pe/`, `png/`, `zip/` but are currently **empty** (no expected output files yet — matching the `Skip` annotations on stdlib tests).
````

</details>


---

<sub>⏱️ 3m 8s</sub>

### 💭 Reasoning

*Excellent, I now have a comprehensive understanding of the codebase. Let me now create the `.github/copilot-instructions.md` file. Let me first check if the `.github` directory exists.*

---

<sub>⏱️ 3m 8s</sub>

### 💬 Copilot

Now I have a thorough understanding. Let me draft the instructions file.

---

<sub>⏱️ 3m 18s</sub>

### ✅ `powershell`

**Create .github directory**

<details>
<summary>Arguments</summary>

```json
{
  "command": "if (-not (Test-Path \"C:\\Users\\stf\\Source\\Repos\\Binary\\.github\")) { New-Item -ItemType Directory -Path \"C:\\Users\\stf\\Source\\Repos\\Binary\\.github\" }",
  "description": "Create .github directory"
}
```

</details>

<details>
<summary>5 lines</summary>

```
Directory: C:\Users\stf\Source\Repos\Binary       

Mode                 LastWriteTime         Length Name
----                 -------------         ------ ----
d----          2026-04-25    11:40.github
<exited with exit code 0>
```

</details>


---

<sub>⏱️ 3m 22s</sub>

### ℹ️ Info

All permissions are now enabled. Tool, path, and URL requests will be automatically approved.

---

<sub>⏱️ 3m 46s</sub>

### ✅ `create`

**create a new file at C:\Users\stf\Source\Repos\Binary\.github\copilot-instructions.md.**

C:\Users\stf\Source\Repos\Binary\.github\copilot-instructions.md

```
Created file C:\Users\stf\Source\Repos\Binary\.github\copilot-instructions.md with 4788 characters
```


---

<sub>⏱️ 3m 50s</sub>

### 💬 Copilot

Created `.github/copilot-instructions.md` covering:

- **Build/test/publish commands** including single-test filtering
- **Architecture**: compilation pipeline, two runtime engines, plugin interfaces, C-ABI interop, bytecode VM tiers
- **Conventions**: `.bsx` language patterns, C# code patterns, test organization, and ADR decisions

No MCP servers seem particularly relevant for this binary-format tooling project.

Would you like to adjust anything or add coverage for areas I may have missed?

---

<sub>Generated by [GitHub Copilot CLI](https://github.com/features/copilot/cli)</sub>
