# Changelog

## 2025-07-23

### Added
- **Match guard arms** — Full implementation of `IdentifierPattern` with guard expressions in match blocks (`s when s.starts_with("AB") => Type`). Guard expressions are emitted as inline bytecode followed by `JUMP_IF_FALSE`, enabling conditional type dispatch based on string methods (`.starts_with()`, `.ends_with()`, `.contains()`), comparisons, and arbitrary expressions. Previously, guard arms compiled but were silently treated as unconditional defaults at runtime.
- **ValuePattern + guard** — Match arms like `1 when flags > 0 => Type` now correctly evaluate both the value check and the guard condition, falling through to subsequent arms when the guard fails.
- Added 10 new tests: 8 parse engine tests covering guard arms (string methods, equality, multiple guards, fallthrough, value+guard) and 2 round-trip tests.

### Changed
- **`MATCH_ARM_GUARD` opcode** — Changed from `jump:i32` (4 bytes) to `field_id:u16` (2 bytes). The opcode now peeks the discriminant from the eval stack and stores it to a hidden field so the guard expression can reference the discriminant by name. Conditional skip logic is handled by inline `JUMP_IF_FALSE`.
- Updated BYTECODE.md with `MATCH_ARM_GUARD` new operand format and `MATCH_BEGIN` design rationale.
- Updated `disasm.py` opcode decoder for new operand format.

## 2025-04-25

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

### Fixed
- **@skip overflow** — `@skip(N)` where N > 65535 now raises a compile-time error instead of silently truncating to `N & 0xFFFF`

### Documentation
- **BYTECODE.md** — Comprehensive update to match actual implementation:
  - Added §3.0 Tagged Inline Literals section documenting the type_tag encoding used by `ASSERT_VALUE`, `MATCH_ARM_EQ`, `MATCH_ARM_RANGE`
  - Fixed operand formats: `SKIP_FIXED` (u16 not u32), `ASSERT_VALUE` (tagged literal not fixed u64), `EMIT_*_BEGIN` (name_id, field_id order), `PUSH_PARAM` (u16 not u8), `MATCH_BEGIN` (no operands), `MATCH_ARM_*` (tagged literals), `ARRAY_BEGIN_UNTIL`/`SENTINEL` (no operands)
  - Replaced obsolete opcodes: `READ_PTR`/`DEREF_BEGIN`/`DEREF_END`/`WRITE_PTR`/`NULL_CHECK`/`DEPTH_CHECK`/`DEPTH_POP` → `READ_PTR_U32`/`READ_PTR_U64`/`EMIT_NULL`/`COPY_CHILD_FIELD`/`FORWARD_ARRAY_STORE`/`FORWARD_PARAM_ARRAY_STORE`/`ARRAY_SEARCH_BEGIN_PARAM`
  - Replaced obsolete `ARRAY_FIND`/`ARRAY_FIND_OR`/`ARRAY_ANY`/`ARRAY_ALL` → new search system: `ARRAY_STORE_ELEM`/`ARRAY_SEARCH_BEGIN`/`PUSH_ELEM_FIELD`/`ARRAY_SEARCH_CHECK`/`ARRAY_SEARCH_COPY`/`ARRAY_SEARCH_END`
  - Added sentinel opcodes: `SENTINEL_SAVE`/`SENTINEL_CHECK`
  - Added new expression VM opcodes: `STORE_FIELD_VAL`/`PUSH_FILE_PARAM`
  - Added §3.3 Parse-Only vs Full-VM Support noting which opcodes lack produce engine support
  - Updated §4.1 Execution Model state to include child field tables, array element stores, search state, param forwarding, and file-level params
  - Added versioning note about opcode reassignment during pre-1.0 development

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
