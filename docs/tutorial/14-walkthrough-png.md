# 14. Walkthrough: PNG Image

This final walkthrough parses a PNG image file — showcasing CRC validation, big-endian chunk iteration, and conditional IHDR-based logic. This is a simplified version of the full PNG parser in `stdlib/png.bsx`.

## PNG Structure

```
┌─────────────────────────────┐
│  Signature (8 bytes)        │  Magic: 89 50 4E 47 0D 0A 1A 0A
├─────────────────────────────┤
│  IHDR Chunk (25 bytes)      │  Image header (always first)
├─────────────────────────────┤
│  [Optional chunks...]       │  tEXt, gAMA, pHYs, etc.
├─────────────────────────────┤
│  IDAT Chunk(s)              │  Compressed image data
├─────────────────────────────┤
│  IEND Chunk (12 bytes)      │  End marker (always last)
└─────────────────────────────┘
```

PNG is fully big-endian.

## The Complete Script

```
@default_endian(big)

// ─── PNG File ───────────────────────────────────────────────────

@root struct PngFile {
    signature: bytes[8] = "\x89PNG\r\n\x1A\n",
    chunks: PngChunk[] @until(@remaining == 0),
}

// ─── Chunk Container ────────────────────────────────────────────

struct PngChunk {
    length: u32,
    chunk_type: fixed_string[4],
    body: match(chunk_type) {
        s when s == "IHDR" => IhdrChunk,
        s when s == "tEXt" => TextChunk,
        s when s == "pHYs" => PhysChunk,
        _ => bytes[length],
    },
    crc: u32,
}

// ─── IHDR — Image Header ───────────────────────────────────────

enum ColorType : u8 {
    GRAYSCALE       = 0,
    RGB             = 2,
    INDEXED         = 3,
    GRAYSCALE_ALPHA = 4,
    RGBA            = 6,
}

enum InterlaceMethod : u8 {
    NONE  = 0,
    ADAM7 = 1,
}

struct IhdrChunk {
    width: u32 @assert(width > 0, "width must be positive"),
    height: u32 @assert(height > 0, "height must be positive"),
    bit_depth: u8,
    color_type: ColorType,
    compression_method: u8 = 0,
    filter_method: u8 = 0,
    interlace_method: InterlaceMethod,
    @derived channels: u8 = match(color_type) {
        ColorType.GRAYSCALE => 1,
        ColorType.RGB => 3,
        ColorType.INDEXED => 1,
        ColorType.GRAYSCALE_ALPHA => 2,
        ColorType.RGBA => 4,
    },
}

// ─── tEXt — Text Metadata ──────────────────────────────────────

struct TextChunk {
    keyword: cstring,
    text: fixed_string[@remaining],
}

// ─── pHYs — Physical Dimensions ────────────────────────────────

enum PhysUnit : u8 {
    UNKNOWN = 0,
    METER   = 1,
}

struct PhysChunk {
    pixels_per_unit_x: u32,
    pixels_per_unit_y: u32,
    unit: PhysUnit,
    @derived dpi_x: u32 = pixels_per_unit_x * 254 / 10000 @hidden,
    @derived dpi_y: u32 = pixels_per_unit_y * 254 / 10000 @hidden,
}
```

## Key Techniques Used

### Big-Endian Throughout

PNG uses network byte order for all multi-byte fields. A single `@default_endian(big)` covers everything.

### Magic Signature Validation

```
signature: bytes[8] = "\x89PNG\r\n\x1A\n"
```

The 8-byte PNG signature is validated as a magic value. If the file doesn't start with these exact bytes, parsing fails immediately.

### String-Based Chunk Dispatch

PNG chunk types are 4-character ASCII strings:

```
body: match(chunk_type) {
    s when s == "IHDR" => IhdrChunk,
    s when s == "tEXt" => TextChunk,
    ...
}
```

### Assertions as Field Modifiers

```
width: u32 @assert(width > 0, "width must be positive"),
```

The assertion is attached directly to the field — compact and clear.

### Magic Values for Fixed Constants

```
compression_method: u8 = 0,   // Only deflate is valid
filter_method: u8 = 0,        // Only adaptive filtering
```

These fields must always be zero in valid PNG files.

### Enums for Readability

Color types and interlace methods are named:

```json
{
    "color_type": "RGBA",
    "interlace_method": "NONE"
}
```

### CRC Validation

The full `stdlib/png.bsx` validates CRC-32 for each chunk:

```
@assert(@crc32(chunk_type, body) == crc, "CRC mismatch")
```

### `@remaining` in Dynamic Contexts

The `tEXt` chunk reads a null-terminated keyword, then treats the rest as text:

```
keyword: cstring,
text: fixed_string[@remaining],
```

`@remaining` gives the bytes left *within the current chunk's data section*, not the whole file.

## Sample Output

```json
{
    "signature": "89504e470d0a1a0a",
    "chunks": [
        {
            "length": 13,
            "chunk_type": "IHDR",
            "body": {
                "width": 256,
                "height": 256,
                "bit_depth": 8,
                "color_type": "RGBA",
                "compression_method": 0,
                "filter_method": 0,
                "interlace_method": "NONE",
                "channels": 4
            },
            "crc": 1828764025
        },
        {
            "length": 0,
            "chunk_type": "IEND",
            "body": "",
            "crc": 2923585666
        }
    ]
}
```

## What This Demonstrates

- Big-endian format handling
- Magic signature validation with byte literals
- String-based match dispatch for chunk types
- CRC-32 checksum validation
- Enums for named constants
- `@remaining` for reading remaining data in a context
- Field-level `@assert` for structural validation
- `@derived @hidden` for internal computations

---

**🎉 Tutorial complete!** You've learned every major feature of BinScript through practical examples.

For more, explore:
- [Language Specification](../LANGUAGE_SPEC.md) — Complete reference
- [Formal Grammar](../GRAMMAR.md) — EBNF syntax definition
- [Standard Library](../STDLIB.md) — Real-world format parsers
- [Bytecode Reference](../BYTECODE.md) — How scripts compile
