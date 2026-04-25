# 8. Pointers

Many binary formats use pointer fields — integer values that hold the offset of another structure. BinScript provides first-class pointer types that automatically dereference during parsing and produce correct offsets during producing.

## Basic Pointers

```
@default_endian(little)

struct Header {
    name_offset: ptr<cstring, u32>,
}
```

### 🔑 `ptr<T, width>`

- `T` — the type at the pointed-to location
- `width` — the integer type storing the offset (default: `u64`)

The parser reads a `u32` value (the offset), seeks to that position, parses a `cstring`, then returns. In JSON output, the pointer is transparently dereferenced:

```json
{
    "name_offset": "hello world"
}
```

## Relative Pointers

`relptr<T>` treats the stored value as an offset relative to the pointer field's own position, not the file start:

```
struct Entry {
    name_ptr: relptr<cstring, u32>,
    data_ptr: relptr<DataBlock, u32>,
}
```

If `name_ptr` is at file offset 100 and contains the value 50, the target is at offset 150 (100 + 50).

## Pointer Width

The width parameter controls how many bytes the pointer value occupies:

```
small_ptr: ptr<Target, u16>,    // 2-byte pointer (max 64K offset)
normal_ptr: ptr<Target, u32>,   // 4-byte pointer (max 4GB offset)
wide_ptr: ptr<Target>,          // 8-byte pointer (default u64)
```

## Nullable Pointers

Add `?` to make a pointer nullable — a zero value means "no target":

```
optional_data: ptr<ExtraInfo, u32>?,
```

If the stored offset is 0, the output is `null` instead of trying to dereference:

```json
{
    "optional_data": null
}
```

## Inner Modifiers

Pointer types support `@encoding` and `@hidden` on the inner type:

```
name: ptr<cstring @encoding(utf16le), u32>,
```

This reads a UTF-16LE string at the pointed-to offset.

## Showing Raw Pointer Values

By default, pointers are transparently dereferenced. Use `@show_ptr` to also include the raw offset:

```
name_ptr: ptr<cstring, u32> @show_ptr,
```

Output:

```json
{
    "name_ptr": "hello",
    "name_ptr$ptr": 256
}
```

## Practical Example: ELF String Table

ELF binaries store section names as offsets into a string table:

```
@default_endian(little)

struct ElfSectionHeader {
    sh_name: u32,       // offset into string table
    sh_type: u32,
    sh_flags: u64,
    sh_addr: u64,
    sh_offset: u64,
    sh_size: u64,
    sh_link: u32,
    sh_info: u32,
    sh_addralign: u64,
    sh_entsize: u64,
}
```

In a full ELF parser, `sh_name` would be used with `@at` to read from the string table:

```
@at(string_table_offset + section.sh_name) {
    section_name: cstring,
}
```

Or, with pointer types and a base offset:

```
section_name: ptr<cstring, u32>,
```

## Bidirectional Pointers

Pointer types work in both directions:
- **Parse**: Read the offset integer, seek to that position, parse the target
- **Produce**: Accept the target data in JSON, serialize it to a trailing data region, write the computed offset

This bidirectionality is automatic — the same script handles both directions.

## What You Learned

- `ptr<T, width>` for absolute pointers
- `relptr<T, width>` for relative pointers
- `?` suffix for nullable pointers (zero = null)
- Default pointer width is `u64`
- `@show_ptr` to include raw offset values in output
- Inner modifiers like `@encoding` on pointed-to types
- Pointers are bidirectional — same script for parse and produce

**Next**: [Assertions and Validation →](09-assertions.md)
