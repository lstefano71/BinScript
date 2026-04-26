# ADR-006: BSX-Light — A ⎕NA Superset Type Language

## Status

Proposed

## Context

Dyalog APL's `⎕NA` system function provides FFI access to C libraries via a compact type declaration language. While practical, `⎕NA` has significant limitations:

1. **No pointer chasing inside structs** — direction markers (`<`, `>`, `=`), null-terminated strings (`0T`), byte-counted strings (`#T`), variable-length arrays (`[]`), UTF types, and APL array types (`A`, `Z`) are all forbidden inside `{}`
2. **No auto-alignment** — struct padding must be inserted manually as phantom fields
3. **No struct return values** — functions returning composite types are unsupported
4. **No double-null-terminated strings** (`PCZZSTR`) — common in Win32 shell APIs
5. **No counted arrays** — the pattern "length field followed by array of that length" requires manual bookkeeping

BinScript already solves most of these problems for file-format parsing. The gap is a compact front-end that accepts `⎕NA`-compatible syntax extended with these capabilities, compiling to BinScript bytecode for execution.

## Decision

### BSX-Light is a strict superset of ⎕NA's type language

Any valid `⎕NA` right-argument string is also valid BSX-Light input. BSX-Light adds:

### Extension 1: Direction markers inside structs

`⎕NA` forbids `<`, `>`, `=` inside `{}`. BSX-Light allows them, enabling pointer chasing:

```
={P U4 <0T <0T U2 I4 P <0T}
```

| Prefix | Produce phase | Parse phase |
|--------|--------------|-------------|
| `<` | Serialize data to trailing region, write pointer | Skip (input-only) |
| `>` | Allocate space, write pointer | Chase pointer, read result |
| `=` | Serialize data, write pointer | Chase pointer, read modified result |

Bare `P` (no direction marker) remains "raw opaque pointer value, no chasing."

### Extension 2: Double-null-terminated string lists (`00T`)

New type specifier for `PCZZSTR`/`REG_MULTI_SZ` patterns:

```
<00T    ⍝ pointer to double-null-terminated wide string list
```

- **Produce**: takes a nested vector of strings, concatenates with null separators, appends final null
- **Parse**: scans for double-null sentinel, splits on single nulls, returns array of strings

### Extension 3: Counted arrays (`*` back-reference)

```
U4 <I4[*]           ⍝ top-level: count then pointer to array
{U4 I4[*]}          ⍝ in struct: count then inline array (flexible array member)
{U4 I4[*:256]}      ⍝ max 256 elements, actual count from previous field
```

`*` means "length determined by the immediately preceding integer field."

### Extension 4: Alignment control

Default is **packed** (⎕NA-compatible). Opt-in to C natural alignment:

```
={C1 I4 C1 F8}              ⍝ packed (14 bytes) — ⎕NA compatible
=@aligned{C1 I4 C1 F8}      ⍝ natural C alignment (24 bytes)
=@pack(2){C1 I4 C1 F8}      ⍝ 2-byte alignment boundary
```

Packed-by-default preserves backward compatibility: existing `⎕NA` definitions with manual padding fields paste in and work unchanged.

### Extension 5: Comments and whitespace

- `/* ... */` comments allowed anywhere whitespace is
- Linefeeds are insignificant whitespace (enables multi-line definitions)

### Extension 6: Struct return values (hidden-pointer transform)

For functions returning structs > 8 bytes (Windows x64) or > 16 bytes (System V), BSX-Light automatically transforms the call:

```
⍝ User writes:
'GetDec' bsx∆NA '{U1 U1 U8 I8} duckdb|get_decimal ...'

⍝ BSX-Light generates thin ⎕NA with hidden first output pointer:
⎕NA 'P duckdb|get_decimal >{U1 U1 U8 I8} ...'
```

The transformation is automatic based on struct size and platform ABI.

### Reserved for future: Field names

Field names are deferred for v1. When added, they will likely use a semicolon-separated syntax that doesn't clash with existing `⎕NA` tokens:

```
={P hwnd; U4 wFunc; <00T pFrom; <00T pTo}
```

### Architecture

BSX-Light compiles to BinScript bytecode — it is a front-end to `BytecodeEmitter`, not a separate runtime. The full execution reuses `ParseEngine` and `ProduceEngine` unchanged.

### APL Integration

`bsx∆NA` is a tradfn that:
1. Compiles the BSX-Light spec to a `BytecodeProgram` via the C-ABI
2. Stores the opaque script handle (pinned via `HandleTable`)
3. Creates a thin `⎕NA` binding for the actual DLL call (all pointers as `P`)
4. Returns a monadic function via `handle ∘ worker` (tradfn returning derived function)

The returned function follows `⎕NA` argument conventions exactly (`⊂` for single-arg, etc.).

### Memory Model

- **Produce**: single contiguous buffer with trailing data region for pointer targets. `P` values are patched to `base_address + offset`. One allocation, one free.
- **Parse**: uses dual-mode `ParseContext` (ADR-005) in live mode (`base_ptr = 0`) to chase pointers directly in process memory without copying.

## Consequences

- New project/namespace: `BinScript.NALight` containing the BSX-Light lexer, parser, and code generator
- `BinScript.Core` gains dual-mode `ParseContext` (ADR-005) — cherry-pickable independently
- `BinScript.Core` gains `@last_size` sentinel primitive — cherry-pickable independently
- `BinScript.Interop` gains C-ABI functions for BSX-Light compile/execute
- APL integration layer (`tools/bsxtool/` or new `tools/bsxna/`) provides `bsx∆NA`
- Future: bypass `⎕NA` entirely via `libffi` for full ABI coverage including two-register struct returns

### Known Limitation: JSON-only data exchange (v1)

The current APL integration uses `⎕JSON` to serialize/deserialize structured data between APL and BinScript. This works correctly but incurs serialization overhead on every call. A future DWA-based `IResultEmitter`/`IDataSource` implementation will directly create/read APL nested arrays in the workspace, eliminating the JSON round-trip.

## Related

- ADR-003: Pointer Extension Design
- ADR-005: Dual-Mode ParseContext
- `docs/dyalog/NA_REFERENCE.md`: ⎕NA type system reference
- `docs/dyalog/NA_PRACTICAL_FINDINGS.md`: Empirical ⎕NA limitation testing
