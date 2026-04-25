# 1. Your First Script

In this chapter, you'll write a BinScript script that parses a simple binary file header.

## The Problem

Imagine a custom image format called "SimpleImg" with this header layout:

| Offset | Size | Field | Description |
|--------|------|-------|-------------|
| 0 | 2 | magic | Magic number `0x5349` ("SI") |
| 2 | 2 | width | Image width in pixels |
| 4 | 2 | height | Image height in pixels |
| 6 | 1 | channels | Number of color channels (1, 3, or 4) |

All multi-byte values are little-endian.

## The Script

```
@default_endian(little)

@root struct SimpleImgHeader {
    magic: u16 = 0x5349,
    width: u16,
    height: u16,
    channels: u8,
}
```

Let's break this down:

### 🔑 `@default_endian(little)`

Sets the byte order for all multi-byte fields. Without this, you'd need to use explicit types like `u16le` everywhere.

### 🔑 `@root struct`

The `@root` annotation marks the entry point — this is where parsing starts. Every script needs exactly one `@root struct`.

### 🔑 Magic Values

`magic: u16 = 0x5349` declares a field *and* asserts its value. If the binary data doesn't contain `0x5349` at offset 0, parsing fails with an error. This is how you validate file signatures.

### 🔑 Primitive Types

`u16` is an unsigned 16-bit integer. BinScript provides:
- **Unsigned integers**: `u8`, `u16`, `u32`, `u64`
- **Signed integers**: `i8`, `i16`, `i32`, `i64`
- **Floating point**: `f32`, `f64`
- **Boolean**: `bool` (1 byte, 0 = false, non-zero = true)

## Running It

Given the bytes `[0x49, 0x53, 0x80, 0x00, 0x60, 0x00, 0x03]`, parsing produces:

```json
{
    "magic": 21321,
    "width": 128,
    "height": 96,
    "channels": 3
}
```

## Adding a Second Struct

Real formats have more than one struct. Let's add a pixel data descriptor:

```
@default_endian(little)

@root struct SimpleImg {
    header: SimpleImgHeader,
    pixel_count: u32,
}

struct SimpleImgHeader {
    magic: u16 = 0x5349,
    width: u16,
    height: u16,
    channels: u8,
}
```

Structs can reference each other by name. The parser reads them sequentially — first the `SimpleImgHeader`, then the `pixel_count`.

## What You Learned

- `@default_endian` sets byte order for the whole file
- `@root struct` marks the parsing entry point
- Fields are declared as `name: type`
- Magic values validate expected constants
- Structs compose by referencing each other

**Next**: [Types and Endianness →](02-types-and-endianness.md)
