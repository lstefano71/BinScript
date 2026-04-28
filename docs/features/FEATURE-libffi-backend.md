# Feature: libffi Backend — Bypass ⎕NA Entirely

## Status

Proposed (future development)

## Summary

Replace Dyalog APL's `⎕NA` as the foreign-call mechanism with a bridge DLL that combines BinScript's existing produce/parse engines, BSX-Light's type language, and [libffi](https://github.com/libffi/libffi) for platform-native calling-convention dispatch. The result is a single native DLL (`bsx_ffi`) that APL talks to via a handful of trivial `⎕NA` bindings (scalars and strings only). All complex marshalling, ABI handling, and pointer chasing happens inside the bridge — invisible to APL.

## Motivation

### Limitations of ⎕NA that this feature eliminates entirely

| Limitation | Impact |
|---|---|
| No direction markers inside `{}` | Every struct with pointer-to-string fields requires manual `MEMCPY` / `HeapAlloc` / `HeapFree` boilerplate |
| No struct return by value | Functions returning composite types are unsupported; hidden-pointer ABI transform must be done manually |
| No small-struct-in-registers (System V / ARM64) | Two-register struct returns (INTEGER+SSE) and HFA/HVA returns are impossible via `⎕NA` |
| No auto-alignment | Struct padding must be inserted manually as phantom `{I1[N]}` fields |
| No double-null-terminated strings | `PCZZSTR` / `REG_MULTI_SZ` patterns require byte-level manual work |
| No counted arrays | `{count, items[count]}` patterns require manual bookkeeping |
| No variadic function support | `printf`-family and similar APIs cannot be called correctly |

### Why libffi instead of extending ⎕NA

`⎕NA` is a closed system — Dyalog controls its implementation and calling-convention handling. libffi is a mature, well-tested, open-source library that already implements correct ABI dispatch for every target platform. Rather than reimplementing ABI knowledge, we delegate it to the library purpose-built for this job.

### Why BinScript/BSX-Light for marshalling

BinScript already solves the data-layout problem for binary formats. BSX-Light extends this to C struct layouts with pointer chasing, alignment, counted arrays, and sentinel-terminated strings. The produce engine serializes structured data to C-compatible memory layouts; the parse engine (in live mode) reads them back. This is exactly what FFI marshalling needs.

## Target Platforms

| Platform | libffi support | Calling convention | Notes |
|---|---|---|---|
| Windows x64 | ✅ libffi 3.5.x | Microsoft x64 | Fully supported |
| Linux x64 | ✅ libffi 3.5.x | System V AMD64 | Fully supported |
| Linux ARM64 | ✅ libffi 3.5.x | AAPCS64 | BTI-aware since 3.5.x |
| macOS ARM64 | ✅ libffi 3.5.x | Apple ARM64 | `ffi_call` needs no special entitlements; `ffi_closure` needs `MAP_JIT` |

These are exactly the platforms supported by Dyalog APL (2026).

## Architecture

```
┌─ Dyalog APL ───────────────────────────────────────────────────┐
│                                                                 │
│  handle ← bsx_ffi_compile '={P U4 <00T <00T U2 I4 P <0T}'    │
│  lib    ← bsx_ffi_load_lib 'shell32'                           │
│  result ← bsx_ffi_call handle lib 'SHFileOperationW' (⎕JSON ns)│
│                                                                 │
│  ⎕NA declarations are ONLY to bsx_ffi — trivial scalars        │
│  and strings. No complex struct marshalling whatsoever.          │
└──────────────────────────┬──────────────────────────────────────┘
                           │ ⎕NA (P, <0T, >0T — dead simple)
                           ▼
┌─ bsx_ffi.dll/.so/.dylib (compiled ahead of time) ──────────────┐
│                                                                  │
│  ┌──────────────────┐  ┌───────────┐  ┌──────────────────────┐ │
│  │ BinScript engine  │  │ libffi    │  │ Platform DLL loader  │ │
│  │ (NativeAOT)       │  │ (static)  │  │ LoadLibrary / dlopen │ │
│  │                    │  │           │  │ GetProcAddress /     │ │
│  │ • produce: JSON    │  │ ffi_call  │  │ dlsym                │ │
│  │   → C struct       │  │ ffi_cif   │  │                      │ │
│  │ • parse: live      │  │ ffi_type  │  │                      │ │
│  │   memory → JSON    │  │           │  │                      │ │
│  └──────────────────┘  └───────────┘  └──────────────────────┘ │
│                                                                  │
│  C-ABI exports (6 functions):                                   │
│    bsx_ffi_compile(spec)            → handle                    │
│    bsx_ffi_load_lib(path)           → lib_handle                │
│    bsx_ffi_call(h, lib, fn, json)   → result_json               │
│    bsx_ffi_free(handle)                                         │
│    bsx_ffi_free_lib(lib_handle)                                 │
│    bsx_ffi_last_error()             → error string              │
└──────────────────────────────────────────────────────────────────┘
                           │
                           │ libffi ffi_call() — data-driven,
                           │ NO runtime code generation
                           ▼
                    ┌──────────────┐
                    │ Target DLL   │
                    │ (user32,     │
                    │  shell32,    │
                    │  custom...)  │
                    └──────────────┘
```

### Call Flow

**Definition time** (once per spec):
1. APL passes BSX-Light spec string to `bsx_ffi_compile` via ⎕NA.
2. Bridge compiles BSX-Light into **two products from one spec**:
   - **Marshal plan**: BinScript produce/parse bytecode (how data is laid out in memory).
   - **ABI plan**: `ffi_cif` + `ffi_type[]` descriptors (how the calling convention passes and returns values).
3. Both are cached in the returned opaque handle. Zero cost on subsequent calls.

**Call time** (each invocation):
1. APL serializes arguments: `⎕JSON namespace` → UTF-8 string.
2. APL calls `bsx_ffi_call` via ⎕NA, passing handle + lib + function name + JSON string.
3. Bridge: `JsonDataSource` → `ProduceEngine` → contiguous C buffer with trailing pointer-target region.
4. Bridge: `ffi_call(cif, func_ptr, &rvalue, arg_pointers)` — the actual foreign call.
5. Bridge: `ParseEngine` (live mode, `base_ptr = 0`) on result memory → `JsonResultEmitter` → JSON string.
6. Bridge returns JSON string to APL.
7. APL deserializes: `⎕JSON⍣¯1` → namespace.

### The Two-Product Compilation (Critical Design Point)

From one BSX-Light spec, the compiler **must** produce two separate artifacts:

1. **Marshal plan** — BinScript bytecode for produce/parse. This describes how bytes are laid out in memory: field offsets, padding, pointer targets in trailing data, string encodings, sentinel termination.

2. **ABI plan** — `ffi_cif` + recursive `ffi_type[]` tree. This describes how the calling convention passes and returns values: which arguments go in which registers, whether a struct return uses a hidden pointer or register pairs, variadic argument promotion rules.

These are related but **not the same problem**. Example: a `{I4 F8}` struct returned by value on System V AMD64 — the marshal plan says "4 bytes int + 4 bytes padding + 8 bytes double = 16 bytes." The ABI plan says "classify as INTEGER+SSE → return in `rax` + `xmm0`." libffi handles the ABI plan given correct `ffi_type` structs, but those structs must be built correctly from the BSX-Light AST.

### The JSON Bridge (APL ↔ Bridge Data Transfer)

The hardest design problem in replacing ⎕NA is not libffi — it's getting APL data into the bridge DLL without depending on Dyalog-private internal representations.

**Solution**: JSON as the universal wire format.

- **APL → bridge**: `⎕JSON` (built into Dyalog, fast) serializes APL namespaces/arrays to a UTF-8 string. Passed as `<0T` via a trivial ⎕NA binding.
- **Bridge → APL**: JSON result string returned as `>0T` (or allocated and freed via `binscript_mem_free`). APL deserializes with `⎕JSON⍣¯1`.

The ⎕NA declarations to the bridge are **dead simple** — just `P` handles and `<0T` / `>0T` strings. All complex marshalling happens inside the bridge.

**Performance**: JSON serialization of typical API arguments (< 1KB) takes sub-microsecond. The actual DLL call (kernel transitions, I/O, computation) dominates. For a potential future "fast path," a binary builder API could bypass JSON — but JSON-first is the right 80/20.

### Build Strategy

Single DLL via NativeAOT + static libffi:

```
BinScript C# code  ──→  NativeAOT compiler  ──→  bsx_ffi.dll/.so/.dylib
libffi.a (static)  ──→  (linked in)          ──╯
bsx_ffi_exports.c  ──→  (linked in)          ──╯
```

.NET NativeAOT supports `<NativeLibrary Include="libffi.a" />` for static native linking. The result is a single DLL per platform — no external dependencies besides the OS.

## "No Runtime Compilation" Constraint

The constraint is that at runtime, everything must be achieved from pre-compiled DLLs — no code generation on the fly.

**`ffi_call` satisfies this completely.** It uses pre-compiled assembly routines (shipped inside libffi as `.S` files compiled at library build time) that interpret `ffi_type` descriptors at runtime to place arguments into registers and stack per ABI. This is pure data-driven dispatch — no JIT, no writable+executable memory, no code generation.

**The one exception**: `ffi_closure` (for callbacks) instantiates small trampoline stubs at runtime. This isn't general code compilation — it's filling in a fixed template with patched addresses — but it does require W^X memory. On macOS ARM64, this needs `MAP_JIT` + `com.apple.security.cs.allow-jit` entitlement. This affects only **Phase 2** (callbacks).

## Phased Delivery

### Phase 1: Foreign Calls (no callbacks)

Everything needed for the vast majority of ⎕NA use cases:

- Bridge DLL with the 6-function C-ABI surface
- BSX-Light compilation → marshal plan + ABI plan
- `ffi_call` for all supported platforms
- JSON argument/result transfer
- Library loading (`LoadLibrary`/`dlopen`) and symbol resolution (`GetProcAddress`/`dlsym`)
- `errno` / `GetLastError` capture (snapshotted immediately after `ffi_call`)
- ANSI/Unicode `*` suffix resolution (append `A` or `W` based on platform)
- Call by ordinal (Windows only, via `GetProcAddress` with `MAKEINTRESOURCE`)
- Threaded calls (`&` modifier) — bridge manages OS thread pool

### Phase 2: Callbacks

Requires `ffi_closure` — writable trampoline memory:

- Define APL callback functions that can be passed as C function pointers
- macOS ARM64: requires `MAP_JIT` entitlement in the bridge DLL's codesign
- Callback must safely re-enter the APL runtime — depends on Dyalog providing a supported re-entry mechanism
- This is the most platform-sensitive part; treat as a separate deliverable

### Phase 3: Performance Fast Path (optional)

- Binary builder API (`bsx_ffi_arg_int`, `bsx_ffi_arg_string`, `bsx_ffi_arg_begin_struct`, etc.) to bypass JSON serialization for performance-critical inner loops
- Pre-resolved symbol caching (skip `dlsym` on repeated calls — likely already optimized by OS, but explicit caching avoids any lookup overhead)
- Reusable unmanaged buffer arenas to minimize allocation per call

## Capability Matrix

| Feature | ⎕NA | libffi + BSX-Light | Notes |
|---|---|---|---|
| Scalar args (int, float, ptr) | ✅ | ✅ | |
| Pointer args (in/out/inout) | ✅ | ✅ | |
| Struct by pointer | ✅ | ✅ | |
| Null-terminated strings | ✅ | ✅ | |
| Direction markers inside structs | ❌ | ✅ | BSX-Light pointer chasing |
| Struct return by value (small) | ❌ | ✅ | System V 2-register, ARM64 HFA/HVA |
| Auto-alignment | ❌ | ✅ | BSX-Light `@aligned`, `@pack(N)` |
| Double-null strings (PCZZSTR) | ❌ | ✅ | BSX-Light `00T` |
| Counted arrays | ❌ | ✅ | BSX-Light `*` back-reference |
| Variadic functions | ❌ (implicit) | ✅ | Needs variadic boundary marker in spec |
| Struct return (hidden pointer) | ❌ | ✅ | libffi handles sret automatically |
| Threaded calls `&` | ✅ | ✅ Phase 1 | Bridge manages threads |
| Callbacks `∇` | ✅ (limited) | ⚠️ Phase 2 | Requires `ffi_closure` + APL re-entry API |
| `A` / `Z` APL-internal types | ✅ | ❌ Excluded | Dyalog-private, rarely used |
| `errno` / `GetLastError` | Implicit | ✅ | Bridge snapshots immediately after call |
| ANSI/Unicode `*` suffix | ✅ | ✅ | Bridge resolves `FuncA`/`FuncW` |
| Call by ordinal (Windows) | ✅ | ✅ | `GetProcAddress` supports ordinals |

## APL Integration

### ⎕NA Declarations (the only ⎕NA calls needed)

```apl
⍝ All declarations target bsx_ffi.dll — nothing else
⎕NA 'P   bsx_ffi|bsx_ffi_compile    <0T'            ⍝ spec → handle
⎕NA 'P   bsx_ffi|bsx_ffi_load_lib   <0T'            ⍝ path → lib handle
⎕NA '    bsx_ffi|bsx_ffi_free       P'               ⍝ free spec handle
⎕NA '    bsx_ffi|bsx_ffi_free_lib   P'               ⍝ free lib handle
⎕NA 'P   bsx_ffi|bsx_ffi_call       P P <0T <0T'    ⍝ handle lib func json → result_ptr
⎕NA '=0T bsx_ffi|bsx_ffi_last_error'                 ⍝ → error string
⎕NA '    bsx_ffi|bsx_ffi_mem_free   P'               ⍝ free result string
```

### User-Facing Wrapper (`bsx∆Call`)

```apl
∇ result ← spec bsx∆Call args;handle;lib;fn;json;rptr;rjson
⍝ High-level wrapper: compile + call + parse in one step
⍝ spec: 'I4 user32|MessageBoxW P <0T <0T U4'
⍝ args: APL data matching the spec's argument types
⍝
⍝ In practice, definition and invocation would be separated
⍝ (compile once, call many times) via a composed function
⍝ similar to the existing bsx∆NA design.
∇
```

### Backward Compatibility

BSX-Light is a strict superset of ⎕NA syntax. Any valid ⎕NA right-argument string is valid BSX-Light input. Users can paste existing ⎕NA definitions unchanged and get identical behavior — plus the new capabilities when they use BSX-Light extensions.

## Risks and Mitigations

### Risk: ABI classification edge cases

**Description**: Small struct returns on System V (INTEGER/SSE classification) and ARM64 (HFA/HVA rules) have complex, platform-specific rules. Incorrect `ffi_type` construction would cause silent data corruption.

**Mitigation**: Build a **conformance test DLL** compiled with each platform's native C compiler, exporting functions that exercise every edge case: small struct returns, HFA/HVA, sret, variadics with default promotions, mixed int/float struct members. Run the test suite on all 4 platforms in CI.

### Risk: macOS codesigning for callbacks

**Description**: `ffi_closure` requires `MAP_JIT` on Apple Silicon, which needs the `com.apple.security.cs.allow-jit` entitlement. The bridge DLL must be codesigned with this entitlement.

**Mitigation**: Phase 2 delivery. Investigate whether Dyalog APL's process already holds this entitlement (it likely does for its own `∇` callback mechanism). If so, `ffi_closure` inherits it automatically.

### Risk: NativeAOT + static libffi build complexity

**Description**: Linking a static C library into a NativeAOT DLL requires per-platform build configuration (linker flags, PIC settings, export visibility).

**Mitigation**: Prototype with a minimal single-export DLL on each platform first. Validate the build pipeline before adding BinScript + libffi complexity.

### Risk: Performance for high-frequency calls

**Description**: JSON serialization + bridge indirection adds overhead vs. direct ⎕NA for trivial calls (e.g., `strlen`).

**Mitigation**: For typical API calls, overhead is negligible (< 1μs JSON vs. millisecond+ kernel calls). Phase 3 adds a binary fast path for the rare case where it matters. Benchmark early with representative workloads.

## Explicit Non-Support

The following ⎕NA features are **not** planned for this backend:

- **`A` type** (APL Auxiliary Processor array format) — Dyalog-private internal representation
- **`Z` type** (APL TCP/IP socket array format) — Dyalog-private internal representation

These are rarely used in practice and require knowledge of Dyalog's internal array layout, which is not publicly documented or stable.

## Dependencies

- **BSX-Light compiler** (FEATURE-bsx-light-compiler.md) — the type language and produce/parse bytecode generation
- **Dual-mode ParseContext** (FEATURE-dual-mode-parse-context.md) — live memory access for reading call results
- **`@last_size` sentinel** (FEATURE-last-size-sentinel.md) — required for `00T` / PCZZSTR support
- **libffi 3.5.x** — external dependency, statically linked

## Related

- [FEATURE-bsx-light-compiler.md](FEATURE-bsx-light-compiler.md) — BSX-Light language and compiler
- [FEATURE-dual-mode-parse-context.md](FEATURE-dual-mode-parse-context.md) — Live memory parsing
- [FEATURE-last-size-sentinel.md](FEATURE-last-size-sentinel.md) — `@last_size` for sentinel arrays
- [ADR-006: BSX-Light — A ⎕NA Superset Type Language](../adr/ADR-006-bsx-light-na-superset.md)
- [docs/dyalog/NA_REFERENCE.md](../dyalog/NA_REFERENCE.md) — ⎕NA type system reference
- [docs/dyalog/NA_PRACTICAL_FINDINGS.md](../dyalog/NA_PRACTICAL_FINDINGS.md) — Empirical ⎕NA limitations
