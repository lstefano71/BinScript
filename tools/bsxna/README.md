# BSX-Light APL Integration

APL integration for BSX-Light (⎕NA superset) — call C functions with rich struct handling from Dyalog APL.

## Overview

`bsxna` provides a namespace with functions that:
1. **Compile** a BSX-Light spec (⎕NA superset) to BinScript bytecode via the C-ABI
2. **Create** a thin `⎕NA` binding for the actual DLL call (plain ⎕NA, extensions stripped)
3. **Parse** returned struct memory into APL nested arrays (via `⎕JSON`)

## Prerequisites

Build the BinScript NativeAOT DLL:

```bash
dotnet publish src/BinScript.Interop -c Release -r win-x64
```

Set `BINSCRIPT_DLL` to the path of the published DLL, or ensure it's on the system PATH.

## Quick Start

```apl
⍝ Load the integration
)COPY tools/bsxna/bsxna

⍝ Initialize (finds the DLL)
bsxna.Init

⍝ Compile a BSX-Light spec
(naH progH plain) ← bsxna.Compile 'I4 user32|MessageBoxW P <0T <0T U4'

⍝ plain is the thin ⎕NA string — use it directly
⎕NA plain
MessageBoxW 0 (⊂'Hello') (⊂'World') 0

⍝ For struct returns — parse with BinScript
(naH progH plain) ← bsxna.Compile '{U1 U1 U8 I8} duckdb|get_decimal P'
⎕NA plain   ⍝ thin binding (struct return → hidden pointer transform)
ptr ← get_decimal arg
result ← bsxna.Parse progH ptr 0   ⍝ parse struct at ptr

⍝ Clean up
bsxna.Free naH
```

## API Reference

### `bsxna.Init`
Initialize the integration. Called automatically on first use of `Compile`.

### `bsxna.Compile spec`
Compile a BSX-Light spec string. Returns `(naHandle progHandle plainNA)`:
- `naHandle` — opaque handle (free with `bsxna.Free`)
- `progHandle` — BinScript program handle (for `bsxna.Parse`)
- `plainNA` — plain ⎕NA string with extensions stripped

### `bsxna.Parse (progHandle baseAddr sizeHint)`
Parse memory at `baseAddr` using the compiled program. Returns an APL nested array.
- `sizeHint` — advisory size in bytes (0 = unknown)

### `bsxna.Free naHandle`
Free a compiled NA handle and its associated resources.

## Known Limitations (v1)

- **JSON serialization overhead**: Data exchange between BinScript and APL goes through `⎕JSON`. A future DWA-based implementation will directly create APL nested arrays.
- **No produce path yet**: Only parsing (C → APL) is implemented. Producing (APL → C) requires the reverse `binscript_from_json` path.
- **Memory management**: Callers must free handles explicitly. Future versions may use reference counting.

## BSX-Light Extensions

The BSX-Light spec supports these extensions beyond standard `⎕NA`:

| Extension | Syntax | Description |
|-----------|--------|-------------|
| Direction in structs | `={<P >U4}` | Pointer semantics inside structs |
| Counted arrays | `U4 P[*]` | Previous field is array count |
| Double-null strings | `<00T` | Multi-string (null-separated, double-null terminated) |
| Alignment | `@aligned{...}` | C-style natural alignment with padding |
| Pack control | `@pack(1){...}` | Explicit packing |
| Struct return | `{U8 U8} lib\|fn` | Auto hidden-pointer transform for large structs |
| Comments | `/* hwnd */ P` | Inline documentation |
| Line feeds | Multi-line specs | Whitespace-flexible formatting |

See [ADR-006](../../docs/adr/ADR-006-bsx-light-na-superset.md) for design details.
