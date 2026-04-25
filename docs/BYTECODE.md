# BinScript Bytecode Format

## 1. Overview

BinScript compiles `.bsx` scripts into a flat bytecode format optimized for:

1. **Speed** — minimal interpretation overhead, especially for flat struct reads (Tier 1).
2. **Persistence** — the bytecode is a plain `byte[]` that can be saved to disk and reloaded without recompilation.
3. **Future JIT** — the instruction set maps cleanly to .NET IL opcodes for a future JIT tier.

## 2. Serialized File Format

```
┌──────────────────────────────────────────────────────────────┐
│  Header (fixed size)                                          │
├──────────────────────────────────────────────────────────────┤
│  Magic:         4 bytes   "BSC\x01"                           │
│  Version:       u16le     format version (currently 1)        │
│  Flags:         u16le     bit 0: has source, bit 1-15: reserved│
│  BytecodeLen:   u32le     length of bytecode section          │
│  StringTableLen:u32le     length of string table              │
│  StructTableLen:u32le     length of struct metadata table     │
│  SourceLen:     u32le     length of embedded source (0 if stripped)│
│  RootStructId:  u16le     index of the @root struct           │
│  ParamCount:    u16le     number of baked compile-time params │
├──────────────────────────────────────────────────────────────┤
│  Compile-Time Parameters (ParamCount × entry)                 │
│    NameIdx:     u16le     index into string table             │
│    Type:        u8        parameter type tag                  │
│    ValueLen:    u16le     length of value                     │
│    Value:       bytes     parameter value                     │
├──────────────────────────────────────────────────────────────┤
│  String Table                                                 │
│    Count:       u32le     number of strings                   │
│    For each string:                                           │
│      Length:    u16le     byte length                         │
│      Data:     bytes     UTF-8 string data (no null terminator)│
├──────────────────────────────────────────────────────────────┤
│  Struct Metadata Table                                        │
│    Count:       u16le     number of struct definitions        │
│    For each struct:                                           │
│      NameIdx:   u16le     index into string table             │
│      ParamCnt:  u8        number of parameters                │
│      FieldCnt:  u16le     number of fields                    │
│      BytecodeOffset: u32le  offset into bytecode section      │
│      BytecodeLen:    u32le  length of bytecode for this struct│
│      StaticSize:     i32le  fixed size in bytes (-1 if dynamic)│
│      Flags:     u16le     bit 0: is_root, bit 1: is_bits,    │
│                           bit 2: is_partial_coverage          │
│    For each field:                                            │
│      NameIdx:   u16le     index into string table             │
│      Flags:     u8        bit 0: derived, bit 1: hidden       │
├──────────────────────────────────────────────────────────────┤
│  Bytecode Section                                             │
│    Raw bytecode instructions (see §3)                         │
├──────────────────────────────────────────────────────────────┤
│  Source Section (optional, present if Flags bit 0 set)        │
│    Raw UTF-8 source text                                      │
└──────────────────────────────────────────────────────────────┘
```

## 3. Instruction Set

Each instruction is encoded as a 1-byte opcode followed by operands. Operands are packed in little-endian format.

### 3.0 Tagged Inline Literals

Several instructions (`ASSERT_VALUE`, `MATCH_ARM_EQ`, `MATCH_ARM_RANGE`) use a **tagged inline literal** encoding for values that can be different types. The encoding is:

| Tag | Type | Size | Description |
|-----|------|------|-------------|
| 0 | i64 | 8 bytes | Signed 64-bit integer (little-endian) |
| 2 | string_id | 2 bytes | Index into the string table (u16le) |
| 3 | bool | 1 byte | Boolean (0 or 1) |

A tagged inline literal is encoded as `type_tag:u8` followed by the value bytes. The total size varies by type tag.

### 3.1 Tier 1 — Fast-Path Instructions

These instructions handle flat, sequential struct reads with no control flow. A struct composed entirely of Tier 1 instructions can be executed as a tight loop.

| Opcode | Hex | Operands | Description |
|--------|-----|----------|-------------|
| `READ_U8` | 0x01 | field_id:u16 | Read u8, emit as uint |
| `READ_I8` | 0x02 | field_id:u16 | Read i8, emit as int |
| `READ_U16_LE` | 0x03 | field_id:u16 | Read u16 little-endian |
| `READ_U16_BE` | 0x04 | field_id:u16 | Read u16 big-endian |
| `READ_I16_LE` | 0x05 | field_id:u16 | |
| `READ_I16_BE` | 0x06 | field_id:u16 | |
| `READ_U32_LE` | 0x07 | field_id:u16 | |
| `READ_U32_BE` | 0x08 | field_id:u16 | |
| `READ_I32_LE` | 0x09 | field_id:u16 | |
| `READ_I32_BE` | 0x0A | field_id:u16 | |
| `READ_U64_LE` | 0x0B | field_id:u16 | |
| `READ_U64_BE` | 0x0C | field_id:u16 | |
| `READ_I64_LE` | 0x0D | field_id:u16 | |
| `READ_I64_BE` | 0x0E | field_id:u16 | |
| `READ_F32_LE` | 0x0F | field_id:u16 | |
| `READ_F32_BE` | 0x10 | field_id:u16 | |
| `READ_F64_LE` | 0x11 | field_id:u16 | |
| `READ_F64_BE` | 0x12 | field_id:u16 | |
| `READ_BOOL` | 0x13 | field_id:u16 | Read 1 byte as boolean |
| `READ_FIXED_STR` | 0x14 | field_id:u16, len:u16, encoding:u8 | Read fixed-length string |
| `READ_CSTRING` | 0x15 | field_id:u16, encoding:u8 | Read null-terminated string |
| `READ_BYTES_FIXED` | 0x16 | field_id:u16, len:u32 | Read fixed byte count |
| `SKIP_FIXED` | 0x17 | len:u16 | Advance cursor, no emit |
| `ASSERT_VALUE` | 0x18 | field_id:u16, tagged_literal | Assert last-read field equals value. Uses tagged inline literal encoding (see §3.0) |

### 3.2 Tier 2 — Complex-Path Instructions

| Opcode | Hex | Operands | Description |
|--------|-----|----------|-------------|
| **Control flow** | | | |
| `CALL_STRUCT` | 0x40 | struct_id:u16, param_count:u8 | Call a nested struct |
| `RETURN` | 0x41 | — | Return from struct |
| `JUMP` | 0x42 | offset:i32 | Unconditional jump |
| `JUMP_IF_FALSE` | 0x43 | offset:i32 | Pop bool, jump if false |
| `JUMP_IF_TRUE` | 0x44 | offset:i32 | Pop bool, jump if true |
| **Seeking** | | | |
| `SEEK_ABS` | 0x50 | — | Pop u64, set cursor to value |
| `SEEK_PUSH` | 0x51 | — | Push current cursor to position stack |
| `SEEK_POP` | 0x52 | — | Pop position stack, restore cursor |
| **Emitter events** | | | |
| `EMIT_STRUCT_BEGIN` | 0x60 | name_id:u16, field_id:u16 | Begin struct: name_id is type name, field_id is the containing field |
| `EMIT_STRUCT_END` | 0x61 | — | |
| `EMIT_ARRAY_BEGIN` | 0x62 | name_id:u16, field_id:u16 | Begin array |
| `EMIT_ARRAY_END` | 0x63 | — | |
| `EMIT_VARIANT_BEGIN` | 0x64 | name_id:u16, field_id:u16 | Begin variant (match arm body) |
| `EMIT_VARIANT_END` | 0x65 | — | |
| `EMIT_BITS_BEGIN` | 0x66 | name_id:u16, field_id:u16 | Begin bits struct |
| `EMIT_BITS_END` | 0x67 | — | |
| **Dynamic reads** | | | |
| `READ_BYTES_DYN` | 0x70 | field_id:u16 | Pop u64 length, read bytes |
| `READ_STRING_DYN` | 0x71 | field_id:u16, encoding:u8 | Pop u64 length, read string |
| `READ_BITS` | 0x72 | field_id:u16, bit_count:u8 | Read N bits from bit accumulator |
| `READ_BIT` | 0x73 | field_id:u16 | Read 1 bit from bit accumulator |
| **Pointer operations** | | | |
| `READ_PTR_U32` | 0x74 | field_id:u16 | Read 4-byte pointer, seek to target. For `ptr<T, u32>` and `relptr<T, u32>`. Compiler emits surrounding `SEEK_PUSH`/`SEEK_POP` and `CALL_STRUCT` to dereference |
| `READ_PTR_U64` | 0x75 | field_id:u16 | Read 8-byte pointer, seek to target. Same pattern as `READ_PTR_U32` but for u64 width |
| `EMIT_NULL` | 0x76 | field_id:u16 | Emit null value for a nullable pointer field (`ptr<T>?` where value is 0) |
| **Cross-struct field access** | | | |
| `COPY_CHILD_FIELD` | 0x77 | child_field_name_idx:u16, dst_field_id:u16 | Copy a field from the last child struct's field table to the parent. Uses **name-based** lookup (string table index), not field ID, because field IDs are per-struct |
| **Array store forwarding** | | | |
| `FORWARD_ARRAY_STORE` | 0x78 | field_id:u16, dst_param_idx:u16 | Forward a field's stored array elements as a struct call parameter |
| `FORWARD_PARAM_ARRAY_STORE` | 0x79 | src_param_idx:u16, dst_param_idx:u16 | Forward a received param's array store to another struct call parameter |
| `ARRAY_SEARCH_BEGIN_PARAM` | 0x7A | param_idx:u16, mode:u8 | Begin search over array received via parameter. Same modes as `ARRAY_SEARCH_BEGIN` |
| **Expression VM** | | | |
| `PUSH_CONST_I64` | 0x80 | value:i64 | Push integer constant |
| `PUSH_CONST_F64` | 0x81 | value:f64 | Push float constant |
| `PUSH_CONST_STR` | 0x82 | string_id:u16 | Push string constant |
| `PUSH_FIELD_VAL` | 0x83 | field_id:u16 | Push a previously parsed field's value |
| `PUSH_PARAM` | 0x84 | param_idx:u16 | Push struct parameter by index |
| `PUSH_RUNTIME_VAR` | 0x85 | var_id:u8 | Push runtime var (0=@input_size, 1=@offset, 2=@remaining) |
| `PUSH_INDEX` | 0x86 | — | Push current array `_index` |
| `STORE_FIELD_VAL` | 0x87 | field_id:u16 | Pop stack, store into field value table. Used for `@map` derived fields and array search results |
| `PUSH_FILE_PARAM` | 0x88 | name_idx:u16 | Push a file-level runtime parameter by name (string table index). Used for `base_ptr` in pointer computations |
| **Arithmetic/logic** | | | |
| `OP_ADD` | 0x90 | — | Pop 2, push sum |
| `OP_SUB` | 0x91 | — | |
| `OP_MUL` | 0x92 | — | |
| `OP_DIV` | 0x93 | — | |
| `OP_MOD` | 0x94 | — | |
| `OP_AND` | 0x95 | — | Bitwise AND |
| `OP_OR` | 0x96 | — | Bitwise OR |
| `OP_XOR` | 0x97 | — | |
| `OP_NOT` | 0x98 | — | Bitwise NOT (unary) |
| `OP_SHL` | 0x99 | — | |
| `OP_SHR` | 0x9A | — | |
| `OP_EQ` | 0x9B | — | |
| `OP_NE` | 0x9C | — | |
| `OP_LT` | 0x9D | — | |
| `OP_GT` | 0x9E | — | |
| `OP_LE` | 0x9F | — | |
| `OP_GE` | 0xA0 | — | |
| `OP_LOGICAL_AND` | 0xA1 | — | |
| `OP_LOGICAL_OR` | 0xA2 | — | |
| `OP_LOGICAL_NOT` | 0xA3 | — | (unary) |
| `OP_NEG` | 0xA4 | — | Arithmetic negate (unary) |
| **Built-in functions** | | | |
| `FN_SIZEOF` | 0xB0 | field_id:u16 | Push byte size of field |
| `FN_OFFSET_OF` | 0xB1 | field_id:u16 | Push byte offset of field |
| `FN_COUNT` | 0xB2 | field_id:u16 | Push array element count |
| `FN_STRLEN` | 0xB3 | field_id:u16 | Push string byte length |
| `FN_CRC32` | 0xB4 | field_count:u8 | Pop N field_ids, compute CRC-32 |
| `FN_ADLER32` | 0xB5 | field_count:u8 | Pop N field_ids, compute Adler-32 |
| **String methods** | | | |
| `STR_STARTS_WITH` | 0xC0 | — | Pop string + pattern, push bool |
| `STR_ENDS_WITH` | 0xC1 | — | |
| `STR_CONTAINS` | 0xC2 | — | |
| **Array control** | | | |
| `ARRAY_BEGIN_COUNT` | 0xD0 | — | Pop count, begin counted array loop |
| `ARRAY_BEGIN_UNTIL` | 0xD1 | — | Begin condition-terminated array. The termination condition is evaluated inline before `ARRAY_END` (not embedded in this opcode) |
| `ARRAY_BEGIN_SENTINEL` | 0xD2 | — | Begin sentinel-terminated array. Uses `SENTINEL_SAVE`/`SENTINEL_CHECK` for element rollback on match |
| `ARRAY_BEGIN_GREEDY` | 0xD3 | — | Begin greedy array (read until error) |
| `ARRAY_NEXT` | 0xD4 | — | Advance to next element |
| `ARRAY_END` | 0xD5 | — | End array loop. For `@until` arrays, pops the condition bool from stack |
| **Array search** *(parse-only; see §3.3)* | | | |
| `ARRAY_STORE_ELEM` | 0xD6 | array_field_id:u16 | Snapshot current element's field table into the array element store for later search |
| `ARRAY_SEARCH_BEGIN` | 0xD7 | array_field_id:u16, mode:u8 | Begin iteration over stored elements. mode: 0=find, 1=find_or_default, 2=any, 3=all |
| `PUSH_ELEM_FIELD` | 0xD8 | field_name_idx:u16 | Push a field value from the current search element. Uses **name-based** lookup (string table index) |
| `ARRAY_SEARCH_CHECK` | 0xD9 | loop_target:i32, not_found_target:i32 | Pop predicate bool. If match found (per mode), jump to not_found_target to exit. Otherwise advance and jump to loop_target |
| `ARRAY_SEARCH_COPY` | 0xDA | src_field_name_idx:u16, dst_field_id:u16 | Copy a field from the matched element to the parent's field table. src uses name-based lookup |
| `ARRAY_SEARCH_END` | 0xDB | — | End search, pop search state. For find (mode 0), throws if no match found |
| **Sentinel control** *(parse-only)* | | | |
| `SENTINEL_SAVE` | 0xDC | — | Save emitter checkpoint before reading a sentinel-guarded element |
| `SENTINEL_CHECK` | 0xDD | — | Pop bool; if true, rollback emitter to checkpoint and break the enclosing array loop |
| **Match** | | | |
| `MATCH_BEGIN` | 0xE0 | — | Begin match block. Discriminant must already be on the stack |
| `MATCH_ARM_EQ` | 0xE1 | tagged_literal, jump:i32 | If discriminant equals the tagged literal value, jump to the arm body. Uses tagged inline literal encoding (see §3.0) |
| `MATCH_ARM_RANGE` | 0xE2 | lo:tagged_literal, hi:tagged_literal, jump:i32 | If lo ≤ discriminant ≤ hi, jump. Both bounds use tagged inline literals |
| `MATCH_ARM_GUARD` | 0xE3 | jump:i32 | Guard arm — currently acts as unconditional jump (guard expression evaluation is not yet implemented) |
| `MATCH_DEFAULT` | 0xE4 | jump:i32 | Default arm |
| `MATCH_END` | 0xE5 | — | End match block |
| **Alignment** | | | |
| `ALIGN` | 0xF0 | — | Pop alignment value, skip to boundary |
| `ALIGN_FIXED` | 0xF1 | alignment:u16 | Skip to next multiple of alignment |

### 3.3 Parse-Only vs Full-VM Support

Most opcodes are supported by both `ParseEngine` and `ProduceEngine`. The following opcodes are currently **parse-only** — the produce engine knows their operand sizes (for bytecode traversal) but does not execute them:

- Array search opcodes (0xD6–0xDB): `ARRAY_STORE_ELEM`, `ARRAY_SEARCH_BEGIN`, `PUSH_ELEM_FIELD`, `ARRAY_SEARCH_CHECK`, `ARRAY_SEARCH_COPY`, `ARRAY_SEARCH_END`
- Sentinel opcodes (0xDC–0xDD): `SENTINEL_SAVE`, `SENTINEL_CHECK`
- Array store forwarding (0x78–0x7A): `FORWARD_ARRAY_STORE`, `FORWARD_PARAM_ARRAY_STORE`, `ARRAY_SEARCH_BEGIN_PARAM`

## 4. Execution Model

### 4.1 Registers / State

The VM has no general-purpose registers. Instead it maintains:

- **Evaluation stack**: arbitrary depth, holds `i64`, `u64`, `f64`, `string`, `bool` values
- **Position cursor**: current byte offset in the input buffer (`u64`)
- **Position stack**: for `SEEK_PUSH` / `SEEK_POP` (implements `@at` blocks, pointer dereferencing)
- **Field value table**: per-struct, indexed by `field_id`, stores parsed values for back-references
- **Last child field table**: the field value table from the most recently called child struct, used by `COPY_CHILD_FIELD` for dotted field access (`a.b`)
- **Parameter stack**: for nested struct calls with parameters
- **Array index**: current `_index` value (stack of indices for nested arrays)
- **Bit accumulator**: for bit-level reads within `bits struct` (byte value + bit position)
- **Array element stores**: per-field snapshots of child struct field tables, built by `ARRAY_STORE_ELEM` during array iteration for later use by `.find()` / `.any()` / `.all()`
- **Search state stack**: for `ARRAY_SEARCH_BEGIN` / `ARRAY_SEARCH_CHECK` / `ARRAY_SEARCH_END` — tracks iteration position and matched element during array searches
- **Pending param array stores**: for `FORWARD_ARRAY_STORE` / `FORWARD_PARAM_ARRAY_STORE` — array element stores being forwarded through struct call parameters
- **File-level runtime parameters**: key-value pairs (e.g., `base_ptr`) passed at parse time, accessed by `PUSH_FILE_PARAM`
- **Data region cursor** (produce only): tracks the next available offset in the trailing data region for pointer target placement

### 4.2 Tier 1 Execution

For structs that only use Tier 1 instructions, the parse engine executes a tight loop:

```
while (ip < end) {
    opcode = code[ip++];
    field_id = ReadU16(code, ip); ip += 2;
    switch (opcode) {
        case READ_U32_LE:
            value = BinaryPrimitives.ReadUInt32LittleEndian(input[pos..]);
            pos += 4;
            fieldValues[field_id] = value;
            emitter.EmitUInt(fieldNames[field_id], value, null);
            break;
        // ... similar for other READ_* ...
    }
}
```

This is as close to a C struct cast as an interpreter can get.

### 4.3 Tier 2 Execution

For complex structs, the full VM runs with stack operations, control flow, and nested struct calls. The evaluation stack handles expression computation.

## 5. Versioning

The file format version (in the header) allows forward compatibility:
- **Version 1**: Current format as specified in this document.
- Loaders MUST reject files with unknown versions.
- New opcodes can be added in minor version bumps (loaders skip unknown opcodes with known sizes).
- Structural changes to the file format require a major version bump.

> **Note:** During early development (pre-1.0), opcode assignments at 0x74–0x7A and 0xD6–0xDD were reassigned without a version bump. Bytecode files persisted before this change are incompatible with the current runtime. Since there are no external users yet, this is acceptable — see project instructions on backward compatibility.

## 6. Design Notes

### Why not .NET IL directly?

- IL is not trivially serializable/persistable.
- The bytecode is simpler to debug and introspect.
- The bytecode instruction set is designed so that a future JIT pass can mechanically translate it to IL.

### Why a stack machine for expressions?

- Matches the precedence-climbing expression parser naturally.
- Compact encoding (no register allocation needed).
- Tier 1 instructions bypass the stack entirely — the stack is only used in Tier 2.

### Field IDs

`field_id` is a `u16` index into the struct's field table. This limits structs to 65,535 fields, which is more than sufficient. The field table is part of the struct metadata in the serialized format.
