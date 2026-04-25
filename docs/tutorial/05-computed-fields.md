# 5. Computed Fields

Not everything in a binary format is stored directly. Sometimes you need to compute values, create intermediate variables, or define file-wide constants.

## `const` — File-Level Constants

Use `const` for magic numbers and well-known values:

```
const PE_MAGIC = 0x00004550;
const MZ_MAGIC = 0x5A4D;

@default_endian(little)

@root struct PeFile {
    dos_magic: u16 = MZ_MAGIC,
    // ...
}
```

🔑 Constants are replaced at compile time. They improve readability and prevent typos.

⚠️ `const` declarations use a **semicolon** terminator, unlike struct members which use commas:

```
const MAX_SECTIONS = 96;     // semicolon
struct Header {
    count: u16,              // comma (or newline)
}
```

## `@let` — Local Bindings

`@let` creates a named value within a struct, available to subsequent fields but not emitted in the output:

```
@default_endian(little)

@root struct DataBlock {
    header_size: u16,
    total_size: u32,
    @let payload_size = total_size - header_size
    payload: bytes[payload_size],
}
```

### 🔑 Why Not Just Inline the Expression?

You could write `bytes[total_size - header_size]`, but `@let` helps when:
- The same computation is needed in multiple places
- The expression is complex and deserves a name
- You want to make the logic readable

```
@let data_offset = header.pointer_to_raw_data + (rva - section.virtual_address)
```

## `@derived` — Computed Output Fields

Unlike `@let` (hidden from output), `@derived` computes a value and **includes it** in the parsed result:

```
@default_endian(little)

@root struct Rectangle {
    width: u16,
    height: u16,
    @derived area: u32 = width * height,
    @derived perimeter: u32 = 2 * (width + height),
}
```

Parsing `[0x0A, 0x00, 0x05, 0x00]` (width=10, height=5) produces:

```json
{
    "width": 10,
    "height": 5,
    "area": 50,
    "perimeter": 30
}
```

### `@derived` with `@hidden`

Sometimes you need a computed value for internal logic but don't want it in the output:

```
@derived adjusted_size: u32 = raw_size - overhead @hidden
```

This computes `adjusted_size` (available to later fields) but excludes it from JSON output.

## `@hidden` — Suppressing Fields

Any field can be hidden from the output while still being available for expressions:

```
@root struct Container {
    @hidden _reserved: u32,
    flags: u16,
    @hidden _padding: bytes[2],
    data_size: u32,
}
```

Hidden fields are parsed (bytes are consumed) but omitted from the output.

## Expressions

BinScript supports a full expression language for computed values:

```
// Arithmetic
@let total = base + offset * stride

// Bitwise
@let flags_masked = flags & 0xFF00

// Comparison (returns bool)
@derived is_compressed: bool = method != 0

// Logical
@derived is_valid: bool = size > 0 && offset < total_size

// Built-in functions
@derived file_crc: u32 = @crc32(data)
@derived payload_len: u32 = @sizeof(payload)
@derived field_pos: u32 = @offsetof(data)
@derived item_count: u32 = @count(items)
@derived name_len: u32 = @strlen(name)
```

### Operator Precedence (tightest binding last)

| Priority | Operators | Description |
|----------|-----------|-------------|
| 10 | `*` `/` `%` | Multiplicative |
| 9 | `+` `-` | Additive |
| 8 | `<<` `>>` | Bit shift |
| 7 | `<` `>` `<=` `>=` | Comparison |
| 6 | `==` `!=` | Equality |
| 5 | `&` | Bitwise AND |
| 4 | `^` | Bitwise XOR |
| 3 | `\|` | Bitwise OR |
| 2 | `&&` | Logical AND |
| 1 | `\|\|` | Logical OR |

## What You Learned

- `const` for file-level named constants (semicolon-terminated)
- `@let` for hidden intermediate computations
- `@derived` for computed fields included in output
- `@hidden` to suppress any field from output
- Full expression language with arithmetic, bitwise, logical, and built-in functions

**Next**: [Match Expressions →](06-match-expressions.md)
