# Feature: BSX-Light — ⎕NA Superset Compiler

## Summary

A compact type-description language that is a strict superset of Dyalog APL's `⎕NA` syntax, adding pointer chasing inside structs, double-null-terminated strings, counted arrays, alignment control, struct return values, and comments. BSX-Light compiles to BinScript bytecode and reuses the existing `ParseEngine`/`ProduceEngine` for execution.

## Motivation

Dyalog APL's `⎕NA` provides FFI access to C libraries via a type declaration mini-language. While practical for simple calls, it has fundamental limitations (empirically verified — see `docs/dyalog/NA_PRACTICAL_FINDINGS.md`):

**Inside `⎕NA` structs (`{}`), the following are forbidden (DOMAIN ERROR):**
- Direction markers: `<`, `>`, `=`
- Null-terminated strings: `0T`, `0C`
- Byte-counted strings: `#T`, `#C`
- Variable-length arrays: `[]`
- UTF types
- APL array types: `A`, `Z`

This means any struct containing pointer-to-string fields (e.g., `SHFILEOPSTRUCT`, `STARTUPINFO`, `IP_ADAPTER_ADDRESSES`) requires the APL programmer to:
1. Declare all pointer fields as raw `P`
2. Manually allocate memory for each pointer target
3. Use `MEMCPY`/`STRNCPY` to copy data in and out
4. Manually free each allocation

BSX-Light eliminates this boilerplate while maintaining full backward compatibility with existing `⎕NA` definitions.

## Design

See ADR-006 for the full architectural decision.

### Syntax Extensions Over ⎕NA

#### 1. Direction markers inside structs

```
={P U4 <0T <0T U2 I4 P <0T}
```

- `<` inside struct: pointer to input data (serialize to trailing region during produce; skip during parse)
- `>` inside struct: pointer to output data (allocate during produce; chase and read during parse)
- `=` inside struct: pointer to in/out data (both directions)
- Bare `P`: raw opaque pointer, no chasing (backward compatible)

#### 2. Double-null-terminated string lists (`00T`)

```
<00T    ⍝ pointer to PCZZSTR (double-null-terminated wide string list)
```

- Produce: takes nested vector of strings → concatenates with null separators → appends final null
- Parse: scans for double-null → splits on single nulls → returns array of strings
- Compiles to: `cstring[] @until(@last_size == 0)` bytecode

#### 3. Counted arrays (`*` back-reference)

```
U4 <I4[*]           ⍝ count then pointer to array of that length
{U4 I4[*]}          ⍝ count then inline flexible array member
{U4 I4[*:256]}      ⍝ max 256, actual count from previous field
```

`*` means "length from the immediately preceding integer field."

#### 4. Alignment control

```
={C1 I4 C1 F8}              ⍝ packed (default, ⎕NA compatible)
=@aligned{C1 I4 C1 F8}      ⍝ natural C alignment
=@pack(2){C1 I4 C1 F8}      ⍝ 2-byte alignment boundary
```

Default is **packed** to preserve backward compatibility with existing `⎕NA` definitions that include manual padding fields.

Alignment rules (for `@aligned`, matching x64 C compilers):
| Type size | Aligned to |
|-----------|-----------|
| 1 byte | 1 |
| 2 bytes | 2 |
| 4 bytes | 4 |
| 8 bytes | 8 |
| struct | alignment of largest member |
| total struct size | padded to multiple of struct alignment |

#### 5. Comments and whitespace

```
={  /* hwnd */ P  /* wFunc */ U4  /* pFrom */ <00T  }
```

- `/* ... */` comments allowed anywhere whitespace is
- Linefeeds are insignificant whitespace (enables multi-line definitions in APL vector editor)

#### 6. Struct return values (hidden-pointer transform)

```
⍝ User writes — struct as return type:
'GetDec' bsx∆NA '{U1 U1 U8 I8} duckdb|get_decimal P'

⍝ BSX-Light detects struct return > 8 bytes, transforms to:
⎕NA 'P duckdb|get_decimal >{U1 U1 U8 I8} P'
```

Automatic for structs > 8 bytes on Windows x64 (hidden first pointer argument per ABI convention). Transparent to the user.

### Reserved for Future

- **Field names**: likely `={P hwnd; U4 wFunc; <00T pFrom}` — deferred for v1
- **Unions**: `(I4 | F4 | {U2 U2})` — useful for `LARGE_INTEGER`, `VARIANT`
- **Bypass ⎕NA entirely**: use `libffi` for full ABI coverage including two-register struct returns (System V)

### Architecture

```
BSX-Light spec string
        │
        ▼
┌──────────────────┐
│  BSX-Light Lexer │    Tokenizes ⎕NA superset syntax
│  & Parser        │    (new, lightweight)
└────────┬─────────┘
         │ AST (reuses BinScript AST nodes where possible)
         ▼
┌──────────────────┐
│  BytecodeEmitter │    Existing BinScript emitter
│  (reused)        │
└────────┬─────────┘
         │ BytecodeProgram
         ▼
┌──────────────────┐
│  ParseEngine /   │    Existing BinScript runtime
│  ProduceEngine   │    (with dual-mode ParseContext from ADR-005)
│  (reused)        │
└──────────────────┘
```

### APL Integration (`bsx∆NA`)

```apl
⍝ Definition time:
'SHFileOp' bsx∆NA 'I4 shell32|SHFileOperation* ={P U4 <00T <00T U2 I4 P <0T}'

⍝ Under the hood, bsx∆NA is a tradfn that:
⍝   1. Calls binscript C-ABI to compile the BSX-Light spec → script handle
⍝   2. Creates thin ⎕NA: ⎕NA 'I4 shell32|SHFileOperationW <{P U4 P P U2 I4 P P}'
⍝   3. Returns:  (handle thin_fn metadata) ∘ worker
⍝   The composed left arg is invisible; the result looks monadic

⍝ Call time — follows ⎕NA conventions exactly:
SHFileOp ,⊂(0 1 ('C:\a' 'C:\b') ('C:\dest') 0 0 0 'Moving...')

⍝ Inside worker (dyadic, left arg composed via ∘):
⍝   1. bs_produce handle data → contiguous buffer (struct + trailing pointer targets)
⍝   2. thin_fn ,⊂buffer       → DLL call
⍝   3. bs_parse_live handle buffer_addr → structured APL result (pointer chasing)
```

### Memory Model

- **Produce**: single contiguous buffer. Struct body at offset 0, pointer targets appended in trailing data region. `P` values in the struct are patched to `base_address + offset`. Caller allocates one block, frees one block.
- **Parse**: dual-mode `ParseContext` in live mode (`base_ptr = 0`). Pointer fields are chased directly at their absolute addresses in process memory. No copying.

### Compiled Handle Caching

The `HandleTable` in `BinScript.Interop` already provides pinned opaque handles via `GCHandle`. The BSX-Light spec is compiled once at definition time; the handle is stored in the composed left argument of the APL function. Subsequent calls use the handle directly — zero recompilation or deserialization overhead.

## Implementation Components

### New: BSX-Light Parser (`BinScript.NALight`)
- Lexer for ⎕NA superset tokens (types, directions, `00`, `*`, `@aligned`, `@pack`, `/* */`)
- Parser producing AST compatible with `BytecodeEmitter`
- Thin-⎕NA generator (strips extensions, produces plain ⎕NA string)
- Hidden-pointer transform for struct returns

### Modified: BinScript.Core
- Depends on: Dual-mode ParseContext (ADR-005, separate feature)
- Depends on: `@last_size` sentinel (separate feature)

### Modified: BinScript.Interop
- New C-ABI functions: `binscript_na_compile`, `binscript_na_get_plain_na`
- `binscript_parse_live` (from dual-mode ParseContext feature)

### New: APL Integration (`tools/bsxna/`)
- `bsx∆NA` tradfn
- `bsx∆worker` dyadic helper
- Setup/installation utilities
- Examples and tests

## Testing Strategy

- **Round-trip tests**: BSX-Light produce → thin ⎕NA call → BSX-Light parse for known Win32 structs
- **Backward compatibility**: plain ⎕NA definitions parsed by BSX-Light produce identical bytecode to hand-written `.bsx` equivalents
- **Alignment tests**: compare BSX-Light auto-aligned structs against C `sizeof`/`offsetof`
- **PCZZSTR tests**: produce/parse double-null-terminated string lists
- **Counted array tests**: produce/parse `{count, items[count]}` patterns
- **APL integration tests**: end-to-end `bsx∆NA` definition and invocation via dyalogscript

## Dependencies

- Dual-mode ParseContext (FEATURE-dual-mode-parse-context.md) — must be implemented first
- `@last_size` sentinel (FEATURE-last-size-sentinel.md) — required for `00T` support
