# BinScript — Product Requirements Document

## 1. Problem Statement

Binary file formats are everywhere — executables (PE, ELF), image formats (PNG, BMP, GIF), archives (ZIP), columnar data (Parquet, Arrow/Feather), network protocols, and countless proprietary formats. Developers who need to parse or produce these formats face a choice between:

1. **Hand-written parsers** — fast but tedious, error-prone, format-specific, and not reusable.
2. **Existing tools** (Kaitai Struct, 010 Editor, etc.) — useful but often tied to specific runtimes, lack bidirectionality, don't offer a C-ABI for FFI embedding, or have script languages that become unreadable for complex formats.

BinScript fills the gap: a **high-performance, embeddable binary structure parser and producer** with a human-readable description language, compiled to fast bytecode, exposed via a C-ABI DLL for use from any language with an FFI.

## 2. Target Users

- **Systems programmers** embedding binary parsing into tools written in C, C++, Rust, Go, Zig, Python (ctypes/cffi), or any FFI-capable language.
- **Security researchers** analyzing PE/ELF binaries, file format forensics.
- **Data engineers** working with binary columnar formats (Parquet, Arrow).
- **Protocol developers** parsing/producing binary network messages at high throughput.

## 3. Core Requirements

### 3.1 Script Language (`.bsx`)

The script language describes binary structures declaratively with embedded expressions for dynamic aspects.

**Must support:**
- Fixed-size primitives with explicit endianness (`u32le`, `u32be`) and a per-file/struct default
- Three string types: nul-terminated (`cstring`), length-prefixed (`string[expr]`), fixed-width (`fixed_string[N]`)
- Byte blobs: `bytes[expr]`, `bytes[@remaining]`
- Bit-level fields: `bits struct` with individual `bit` and `bits[N]` fields
- Enums mapping numeric/string values to named constants
- Named constants: `const`
- Nested structures with parameters: `struct Section(arch, alignment) { ... }`
- Tagged unions via inline `match` with full boolean expression guards
- Cross-struct references: auto-scoped parent/sibling fields, explicit `@let` for cross-branch
- Non-linear parsing: `@at(expr) { ... }` (jump + return) and `@seek(expr)` (jump + stay)
- Four array termination strategies: count, condition (`@until`), sentinel (`@until_sentinel`), greedy (`@greedy`)
- Alignment: `@align(expr)`, `@skip(N)`, `@hidden`
- Derived fields: `@derived` for fields recomputed in produce mode
- Assertions: `@assert(expr, "message")` and implicit assertion via `field: type = value`
- Coverage annotation: `@coverage(partial)` to signal intentional partial parse
- String encoding: `@encoding(...)`, `@default_encoding(...)`
- Compile-time parameters: `@param name: type`
- Script modularity: `@import "module_name"` resolved via host-provided registry
- Entry point: `@root struct Name { ... }` with caller override
- Comments: `//` line, `/* */` block
- Numeric literals: decimal, `0x` hex, `0b` binary, `0o` octal

**Built-in pseudo-variables:** `@input_size`, `@offset`, `@remaining`

**Built-in functions:** `@sizeof`, `@offset_of`, `@count`, `@strlen`, `@crc32`, `@adler32`

**Operators:** Arithmetic (`+ - * / %`), bitwise (`& | ^ ~ << >>`), comparison (`== != < > <= >=`), logical (`&& || !`), string methods (`.starts_with()`, `.ends_with()`, `.contains()`)

### 3.2 Two-Phase Architecture

1. **Compile**: Script text → validated, optimized bytecode. Compile-time parameters are baked in. All imports are resolved and bundled. The compiled form is an opaque handle.
2. **Parse/Produce**: The compiled handle is applied to binary data (parse) or structured data (produce) repeatedly with minimal overhead.

The compiled bytecode must be **persistable** — save to disk, reload in a new process without recompiling. The persisted format includes source by default (strippable for production).

### 3.3 Performance

- **Bytecode design**: Two tiers. Tier 1 (flat structs) should be close to a C struct cast in speed — a tight loop over (read-type, offset, field-id) tuples. Tier 2 (complex structs with conditionals, seeks) uses richer instructions.
- **No engine-allocated output buffers** in the produce path. Caller queries size (compile-time static or runtime-calculated), allocates, and calls produce-into.
- **Compiled handle is immutable and thread-safe** — safe to share across threads for concurrent parsing.

### 3.4 C-ABI (NativeAOT DLL)

A thin C-ABI shim over the C# core, usable from any FFI-capable language.

**Functions:**
- Compiler lifecycle: `binscript_compiler_new`, `binscript_compiler_add_module`, `binscript_compiler_compile`, `binscript_compiler_free`
- Script persistence: `binscript_save`, `binscript_load`, `binscript_free`
- Parse → JSON: `binscript_to_json`, `binscript_to_json_entry`
- Produce ← JSON: `binscript_from_json_static_size`, `binscript_from_json_calc_size`, `binscript_from_json_into`, `binscript_from_json` (convenience)
- Memory & errors: `binscript_mem_free`, `binscript_last_error`

See [C-ABI Reference](C_ABI.md) for full signatures.

### 3.5 Bidirectionality

The same `.bsx` script describes both parse (binary → structured) and produce (structured → binary) directions.

- `@derived` fields are recomputed in produce mode, not taken from input data.
- `match`/variant fields require a `_variant` discriminator in the input data.
- Magic constants are written as-is in produce mode.
- Produce uses two-pass layout: size calculation, then write. Host provides the buffer.

### 3.6 Extensible Emitters

JSON is the first implementation. The architecture supports pluggable emitters/data-sources via C# interfaces:

- **`IResultEmitter`** — parse direction: engine calls emitter methods as it walks the structure.
- **`IDataSource`** — produce direction: engine reads field values from the data source.

Future emitters (MessagePack, direct struct builders, callback-based visitors) plug in without changing the engine.

### 3.7 Error Handling

**Compilation errors**: Rich diagnostics with line, column, span, human-readable message.

**Parse-time errors**: Three strictness levels:
- `Lenient` (default): parse what the script describes, ignore the rest.
- `WarnUnconsumed`: emit warning if bytes remain and script doesn't declare `@coverage(partial)`.
- `StrictFull`: error if any input bytes are unaccounted for.

**Coverage reporting**: `ParseResult` includes `BytesConsumed`, `InputSize`, `Coverage.Percentage`, `ReadRanges`, `Gaps`.

**Script-level assertions**: `@assert(expr, "msg")` and implicit magic-value assertions.

### 3.8 Standard Library

Ships at launch with `.bsx` definitions for real-world formats:

| Format | Exercises |
|--------|-----------|
| PE/COFF | Nested structs, `@seek`, `@let`, enums, match, `@align`, bit flags |
| ZIP | `@at` from end, `@input_size`, count arrays, `@derived` CRC |
| PNG | `@until(@remaining==0)`, sentinel IEND, `@crc32`, chunk-type match |
| ELF | 32/64-bit via `@param`, endian switching, section/segment tables |
| BMP | Flat headers (Tier 1 fast-path), variable pixel data |
| GIF | Sub-blocks with sentinel, extension blocks via match |

Each format includes real binary sample files and expected JSON output for testing.

## 4. Non-Requirements (Explicitly Out of Scope for v1)

- JIT compilation (bytecode → IL → native) — future optimization
- Streaming parse/produce on `Stream` / file descriptors — future
- Non-JSON emitters at the C-ABI level — future (internal interface is ready)
- GUI or interactive explorer — not planned
- Full format coverage of every binary format — the stdlib covers 6 representative formats

## 5. Success Criteria

1. A developer can write a `.bsx` script for a new binary format and parse real files without touching C#.
2. Parsing 10,000 small structures (PE DOS+COFF headers from pre-loaded buffers) completes within 2x the time of equivalent hand-written C# code.
3. Round-trip fidelity: `parse(produce(parse(data))) == parse(data)` for all stdlib formats.
4. The C-ABI DLL is loadable and functional from at least C and Python (ctypes).
5. All stdlib `.bsx` scripts parse their real-world sample files correctly.
