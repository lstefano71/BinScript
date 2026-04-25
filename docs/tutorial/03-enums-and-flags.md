# 3. Enums and Flags

Binary formats are full of magic numbers — machine types, compression methods, permission flags. BinScript provides `enum` for named constants and `bits struct` for bitfields.

## Enums

An `enum` maps integer values to meaningful names:

```
enum CompressionMethod : u16 {
    NONE    = 0,
    DEFLATE = 8,
    BZIP2   = 12,
    LZMA    = 14,
    ZSTD    = 93,
}
```

### 🔑 Backing Type

The `: u16` after the enum name specifies the storage size — how many bytes to read from the binary data. Any primitive integer type works: `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, plus their endian-qualified variants.

### Using Enums

Use an enum like any other type:

```
@default_endian(little)

enum CompressionMethod : u16 {
    NONE    = 0,
    DEFLATE = 8,
}

@root struct FileEntry {
    compression: CompressionMethod,
    compressed_size: u32,
    uncompressed_size: u32,
}
```

Parsing produces named values in the output:

```json
{
    "compression": "DEFLATE",
    "compressed_size": 1024,
    "uncompressed_size": 4096
}
```

If the value doesn't match any variant, the raw integer is emitted instead:

```json
{
    "compression": 99
}
```

### Referencing Enum Values in Expressions

Enum variants can be used in match expressions and comparisons:

```
body: match(compression) {
    CompressionMethod.NONE => RawData,
    CompressionMethod.DEFLATE => DeflateData,
    _ => bytes[compressed_size],
}
```

## Bits Structs (Bitfields)

Many formats pack multiple flags into a single integer. `bits struct` unpacks them:

```
bits struct TcpFlags : u8 {
    fin: bit,
    syn: bit,
    rst: bit,
    psh: bit,
    ack: bit,
    urg: bit,
    ece: bit,
    cwr: bit,
}
```

### 🔑 `bit` vs `bits[N]`

- `bit` — a single boolean flag (1 bit)
- `bits[N]` — a multi-bit integer field (N bits)

```
bits struct IpFlags : u8 {
    _reserved: bit,
    dont_fragment: bit,
    more_fragments: bit,
    fragment_offset_high: bits[5],
}
```

The fields are read from the **least significant bit** upward. The total bit count must equal the backing type's bit width (e.g., 8 bits for `u8`, 16 for `u16`).

### Practical Example: File Permissions (Unix-style)

```
bits struct Permissions : u16 {
    other_exec: bit,
    other_write: bit,
    other_read: bit,
    group_exec: bit,
    group_write: bit,
    group_read: bit,
    owner_exec: bit,
    owner_write: bit,
    owner_read: bit,
    sticky: bit,
    setgid: bit,
    setuid: bit,
    _padding: bits[4],
}
```

Parsing the value `0o0755` (= `0x01ED`) produces:

```json
{
    "other_exec": true,
    "other_write": false,
    "other_read": true,
    "group_exec": true,
    "group_write": false,
    "group_read": true,
    "owner_exec": true,
    "owner_write": true,
    "owner_read": true,
    "sticky": false,
    "setgid": false,
    "setuid": false
}
```

⚠️ **Common pitfall**: Bits are read LSB-first. The first field in the declaration is the *lowest* bit, not the highest. Check your format's documentation for bit numbering.

## What You Learned

- `enum` maps integers to names with explicit backing types
- Unknown enum values pass through as raw integers
- `bits struct` unpacks individual bits from an integer
- `bit` is a single flag; `bits[N]` is a multi-bit field
- Fields are ordered from LSB to MSB

**Next**: [Arrays and Strings →](04-arrays-and-strings.md)
