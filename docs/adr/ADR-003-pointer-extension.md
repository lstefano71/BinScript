# ADR-003: Pointer Extension Design

## Status

Accepted

## Context

BinScript's existing syntax can express pointer indirection using `@at`, `@hidden`, and `@derived`, but the approach is verbose (two fields per pointer), fragile (name mapping between hidden pointer and visible data), and broken in the produce direction (no single bidirectional script). A first-class pointer type is needed to support in-memory C structure descriptions.

## Decision

Introduce `ptr<T, width>` and `relptr<T, width>` as first-class types in the BinScript type system.

### Syntax

```
field: ptr<InnerType, u32|u64>      // absolute pointer (requires @param base_ptr)
field: relptr<InnerType, u32|u64>   // relative pointer (offset from field position)
field: ptr<InnerType>               // shorthand — defaults to u64 width
field: ptr<InnerType>?              // nullable pointer — null = zero
```

### Absolute Pointers (`ptr<T>`)

The pointer value is an absolute address. During parse, the engine computes the buffer offset as `ptr_value - base_ptr`. During produce, it computes the pointer value as `base_ptr + buffer_offset`.

The base pointer is provided via the existing `@param` mechanism:
```
@param base_ptr: u64
```

### Relative Pointers (`relptr<T>`)

The pointer value is an offset from the pointer field's own position in the buffer. No base pointer is needed. During parse: `target_offset = field_offset + ptr_value`. During produce: `ptr_value = target_offset - field_offset`.

### JSON Representation

**Transparent dereferencing** by default — the pointer is invisible in JSON output. Only the pointed-to value appears:
```json
{ "s1": "hello", "s2": "world" }
```

An optional `@show_ptr` annotation exposes the raw pointer value:
```json
{ "s1": { "_ptr": 140698832470040, "value": "hello" } }
```

### Produce-Direction Layout

**Trailing data region (default):** All pointer targets are appended after the fixed-size struct fields, in field declaration order.

Given `{ s1: ptr<cstring, u64>, s2: ptr<cstring, u64> }` and input `{"s1": "hello", "s2": "world"}`:
```
Offset 0:  [ptr_to_s1 = base+16] (8 bytes)
Offset 8:  [ptr_to_s2 = base+22] (8 bytes)
Offset 16: [hello\0]             (6 bytes)
Offset 22: [world\0]             (6 bytes)
```

**`@inline` annotation:** Data placed immediately after its pointer field. Opt-in per field:
```
name: ptr<cstring, u64> @inline
```

### Nullable Pointers

`ptr<T>?` — a zero pointer value represents null. In JSON, emits `null`. Serves as a recursion termination guard for linked structures.

### Array Compositions

```
items: ptr<cstring, u64>[count]     // array of pointers
data: ptr<u32[count], u64>          // pointer to array
```

Inner types follow all existing array termination strategies.

### Bidirectional Semantics

One script, both directions:
- **Parse**: Read pointer width bytes → subtract base (or add field offset for relptr) → seek to target → read inner type → emit value
- **Produce**: Size pass computes data region → layout pass assigns offsets → write pass writes pointer values and data

Round-trip fidelity: `parse(produce(parse(data))) == parse(data)`. The binary layout may differ (produce uses canonical trailing layout), but the structured representation is preserved.

### Bytecode Opcodes

> **Note (2026-04-25):** The original opcodes below were superseded during implementation.
> The current opcodes split `READ_PTR` by width (`READ_PTR_U32` 0x74, `READ_PTR_U64` 0x75),
> replace `DEREF_BEGIN`/`DEREF_END` with existing `SEEK_PUSH`/`SEEK_POP`,
> replace `WRITE_PTR` with `COPY_CHILD_FIELD` (0x77),
> and replace `NULL_CHECK` with `EMIT_NULL` (0x76).
> See `docs/BYTECODE.md` for the current instruction set.

*Original design (for historical context):*

| Opcode | Hex | Operands | Description |
|--------|-----|----------|-------------|
| ~~`READ_PTR`~~ | 0x74 | field_id:u16, width:u8, mode:u8 | Read pointer, compute buffer offset |
| ~~`DEREF_BEGIN`~~ | 0x75 | — | Push position, seek to deref target |
| ~~`DEREF_END`~~ | 0x76 | — | Pop position, restore cursor |
| ~~`WRITE_PTR`~~ | 0x77 | field_id:u16, width:u8, mode:u8 | Write computed pointer value |
| ~~`NULL_CHECK`~~ | 0x7A | — | Pop value, push bool (true if zero/null) |

`mode`: 0 = absolute (ptr), 1 = relative (relptr).

## Alternatives Considered

### Manual `@at`/`@hidden`/`@derived`
The current approach. Requires two fields per pointer, verbose, not bidirectional. See the plan for a detailed breakdown of why this is insufficient.

### Wrapper JSON Representation
`{ "_ptr": addr, "value": content }` for every pointer field. Adds noise to every pointer in the output. Most users want to see the data, not the address. Rejected as the default; available via `@show_ptr`.

### Relative-Only Pointers
Some formats (FlatBuffers) use only relative offsets. Insufficient for the primary use case of C struct memory dumps where absolute addresses are the norm. Both modes are supported.

## Consequences

- The two-pass produce engine must track a "data region cursor" separate from the struct cursor, to place trailing pointer targets.
- `@param base_ptr` is required for absolute pointers — scripts using `ptr<T>` without it produce a compile error.
- The compiler must verify that `ptr<T>?` nullable pointers are properly handled (null check before dereference).
- `@inline` creates variable-sized struct fields, which may push structs from Tier 1 to Tier 2 bytecode.
