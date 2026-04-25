# 4. Arrays and Strings

Most binary formats contain repeated elements — pixel rows, section tables, string pools. BinScript provides several array and string types.

## Fixed-Count Arrays

When the count is known statically or from a prior field:

```
@default_endian(little)

@root struct SectionTable {
    section_count: u16,
    sections: SectionHeader[section_count],
}

struct SectionHeader {
    name: fixed_string[8],
    virtual_size: u32,
    virtual_address: u32,
}
```

### 🔑 Array Syntax

`Type[count]` where `count` is any expression:
- `u8[16]` — 16 bytes
- `SectionHeader[section_count]` — count from a prior field
- `u32[header.num_entries]` — count from a nested field

## Dynamic Arrays

When you don't know the count ahead of time:

### `@until` — Read Until a Condition

```
@root struct ChunkList {
    chunks: Chunk[] @until(@remaining == 0),
}

struct Chunk {
    tag: u32,
    size: u32,
    data: bytes[size],
}
```

🔑 `@remaining` is a built-in that returns how many bytes are left in the input. The array keeps reading elements until the condition is true.

### `@until_sentinel` — Read Until an Element Matches

```
@root struct StringTable {
    entries: Entry[] @until_sentinel(e => e.length == 0),
}

struct Entry {
    length: u16,
    text: fixed_string[length],
}
```

The lambda `e => e.length == 0` receives each element after it's parsed. When it returns true, that element is included and the array stops.

### `@greedy` — Read As Many As Possible

```
@root struct PixelData {
    pixels: Pixel[] @greedy,
}

struct Pixel {
    r: u8,
    g: u8,
    b: u8,
}
```

Reads elements until the data runs out or an element fails to parse. Useful when the format doesn't specify a count.

## String Types

### `cstring` — Null-Terminated String

```
name: cstring
```

Reads bytes until a `0x00` null terminator. The terminator is consumed but not included in the value.

### `fixed_string[N]` — Fixed-Width String

```
section_name: fixed_string[8]
```

Reads exactly N bytes as a string. Trailing null bytes are stripped. Use this for fields like ELF section names or PE section headers where the field has a fixed size.

### `bytes[N]` — Raw Byte Array

```
raw_data: bytes[256]
```

Reads N bytes as an opaque blob. Emitted as a hex string in JSON output.

### String Encoding

By default, strings are interpreted as ASCII/UTF-8. Use `@encoding` for other encodings:

```
@default_encoding(utf8)   // file-level default

struct UnicodeRecord {
    name: cstring @encoding(utf16le),
    description: fixed_string[64] @encoding(ascii),
}
```

## Practical Example: TGA Image Header

```
@default_endian(little)

@root struct TgaFile {
    id_length: u8,
    colormap_type: u8,
    image_type: u8,
    colormap_origin: u16,
    colormap_length: u16,
    colormap_depth: u8,
    x_origin: u16,
    y_origin: u16,
    width: u16,
    height: u16,
    pixel_depth: u8,
    image_descriptor: u8,
    image_id: fixed_string[id_length],
}
```

Note how `image_id` uses `id_length` as its size — BinScript fields can reference any previously parsed field.

## What You Learned

- `Type[count]` for fixed-count arrays with static or dynamic counts
- `@until`, `@until_sentinel`, `@greedy` for dynamic arrays
- `cstring` for null-terminated strings, `fixed_string[N]` for fixed-width
- `bytes[N]` for raw data
- `@encoding` for non-UTF-8 strings

**Next**: [Computed Fields →](05-computed-fields.md)
