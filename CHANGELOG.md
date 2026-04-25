# Changelog

## 2026-04-25

### Added
- **bsxtool** — Python CLI tool exercising the BinScript C-ABI DLL (`tools/bsxtool/`):
  - `parse` — Compile .bsx and parse binary files to JSON via the C ABI
  - `produce` — Compile .bsx and produce binary from JSON via the C ABI
  - `compile` — Save compiled bytecode to .bsc files
  - `run` — Load pre-compiled .bsc and parse binary files
  - `disasm` — Human-readable bytecode disassembly with resolved names (field IDs, string table, struct names, jump targets)
  - `hexdump` — Annotated hex dump of .bsc binary files with section boundaries
  - `info` — Print DLL version and path
  - Auto-discovers the NativeAOT DLL from publish output; supports `--dll` override
  - Module resolution via `--module name=path` and `--stdlib-dir` for auto-registering .bsx files
  - Pure Python 3.10+ stdlib (no pip dependencies)
  - Full documentation in `tools/bsxtool/README.md` with maintenance checklist

### Implemented
- **C-ABI produce functions** — Implemented the 4 produce native exports that were stubs returning "Not yet implemented":
  - `binscript_from_json_static_size` — Returns compile-time-known output size for a struct
  - `binscript_from_json_calc_size` — Calculates exact output size from JSON input
  - `binscript_from_json_into` — Produces binary into a caller-provided buffer
  - `binscript_from_json` — Produces binary with engine-allocated buffer

### Fixed
- **BytecodeBuilder heap allocations** — Replaced `_bytes.AddRange(buf.ToArray())` with per-byte `_bytes.Add()` calls in `EmitI32`, `EmitU32`, `EmitI64`, and `EmitF64`, eliminating unnecessary heap allocations on compilation hot path
- **relptr bytecode emission bug** — Removed dead `PushConstI64(0)` placeholder in non-nullable `relptr` path that caused a stack leak and incorrect seek target. The nullable variant was already correct.

### Tests
- Added `JsonResultEmitterTests` (26 tests) — Covers flat structs, nested structs, arrays, strings, enums, booleans, floats, bits structs, and direct emitter API
- Added `JsonDataSourceTests` (24 tests) — Covers navigation, value reading, arrays, variants, bits structs, and edge cases
- Added relptr regression tests (4 tests) — Covers non-nullable and nullable `relptr<T, u32>` at various field offsets
- Replaced `ProduceStubs_SetNotImplementedError` stub test with 8 real produce interop tests covering static size queries, round-trip fidelity, nested structs, and error handling
