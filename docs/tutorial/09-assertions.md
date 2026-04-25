# 9. Assertions and Validation

Binary formats have structural constraints — magic numbers, checksum fields, size limits. BinScript provides several mechanisms to validate data during parsing.

## Magic Value Assertions

The simplest form — assert a field has a specific value:

```
magic: u32 = 0x00004550
```

If the parsed value doesn't match, parsing fails with an error. This is syntactic sugar for reading the field and checking its value.

## `@assert` Directive

For more complex validations, use `@assert` as a standalone directive:

```
@default_endian(little)

@root struct SafeHeader {
    version: u8,
    @assert(version >= 1 && version <= 5, "unsupported version")
    section_count: u16,
    @assert(section_count <= 256, "too many sections")
    sections: Section[section_count],
}
```

### 🔑 `@assert(condition, message)`

- `condition` — any boolean expression
- `message` — a string literal describing the failure

If the condition is false, parsing stops with the given error message.

## `@assert` as a Field Modifier

Assertions can also be attached to specific fields:

```
struct BmpHeader {
    width: i32 @assert(width > 0, "width must be positive"),
    height: i32 @assert(height != 0, "height cannot be zero"),
    bit_count: u16 @assert(
        bit_count == 1 || bit_count == 4 || bit_count == 8 ||
        bit_count == 16 || bit_count == 24 || bit_count == 32,
        "invalid bit count"),
}
```

## Checksum Validation

Use built-in checksum functions with `@assert` or `@derived`:

```
@root struct PngChunk {
    length: u32be,
    chunk_type: u32be,
    data: bytes[length],
    crc: u32be,
    @assert(@crc32(chunk_type, data) == crc, "CRC mismatch"),
}
```

Available checksum built-ins:
- `@crc32(field1, field2, ...)` — CRC-32/ISO-HDLC
- `@adler32(field1, field2, ...)` — Adler-32

## `@coverage(partial)`

When your script intentionally doesn't describe the entire input, use `@coverage(partial)` to suppress "unconsumed bytes" warnings:

```
@coverage(partial)
@root struct PeQuickHeader {
    dos: DosHeader,
    @seek(dos.e_lfanew)
    pe_sig: u32 = 0x00004550,
    coff: CoffHeader,
    // We're only reading the headers, not the full PE
}
```

Without `@coverage(partial)`, the parser would warn about all the unread bytes after the COFF header.

## Practical Example: ZIP Local File Header

ZIP files have multiple validation points:

```
@default_endian(little)

const ZIP_LOCAL_MAGIC = 0x04034B50;

@root struct ZipLocalFile {
    signature: u32 = ZIP_LOCAL_MAGIC,
    version_needed: u16,
    flags: u16,
    compression: u16,
    mod_time: u16,
    mod_date: u16,
    crc32: u32,
    compressed_size: u32,
    uncompressed_size: u32,
    filename_length: u16,
    extra_length: u16,
    @assert(filename_length <= 65535, "filename too long")
    filename: fixed_string[filename_length],
    extra: bytes[extra_length],
    data: bytes[compressed_size],
}
```

## What You Learned

- `field: type = value` for magic number assertions
- `@assert(condition, "message")` for general validation
- `@assert` as a field modifier for per-field checks
- `@crc32()` and `@adler32()` for checksum validation
- `@coverage(partial)` to suppress incomplete parsing warnings

**Next**: [Reusable Expressions with @map →](10-map-expressions.md)
