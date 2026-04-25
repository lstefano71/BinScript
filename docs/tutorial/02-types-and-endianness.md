# 2. Types and Endianness

Binary formats mix byte orders constantly — a network protocol might use big-endian while the embedded payload uses little-endian. BinScript gives you fine-grained control.

## Default Endianness

Most formats use a single byte order. Set it once at the top:

```
@default_endian(little)    // Intel, ARM, most file formats
@default_endian(big)       // Network protocols, Java class files, TIFF (sometimes)
```

Fields declared with unqualified types (`u16`, `i32`, `f64`) use this default.

## Explicit Endian Types

When a format mixes byte orders, use endian-qualified types:

```
@default_endian(little)

@root struct MixedHeader {
    // These use the default (little-endian)
    local_value: u32,
    local_offset: u16,

    // These override to big-endian (e.g., embedded network data)
    network_port: u16be,
    ipv4_addr: u32be,
}
```

Available suffixes:

| Little-endian | Big-endian | Size |
|---------------|------------|------|
| `u16le` | `u16be` | 2 bytes |
| `u32le` | `u32be` | 4 bytes |
| `u64le` | `u64be` | 8 bytes |
| `i16le` | `i16be` | 2 bytes |
| `i32le` | `i32be` | 4 bytes |
| `i64le` | `i64be` | 8 bytes |
| `f32le` | `f32be` | 4 bytes |
| `f64le` | `f64be` | 8 bytes |

`u8` and `i8` have no endian variants — single bytes have no byte order.

## 🔑 When to Use What

- **Set `@default_endian`** for the dominant byte order in your format
- **Use qualified types** for the exceptions (e.g., a big-endian checksum in an otherwise little-endian file)
- **Omit `@default_endian`** and use only qualified types when the format genuinely mixes byte orders throughout

⚠️ **Common pitfall**: Forgetting `@default_endian` and using bare `u16`. The compiler will reject bare unqualified multi-byte types if no default endian is set.

## Integer Literals

BinScript supports multiple bases for constants:

```
const MAGIC = 0x5A4D;       // hexadecimal
const FLAGS = 0b1010_0001;  // binary
const PERMS = 0o755;        // octal
const SIZE  = 65536;        // decimal
```

All integer literals are 64-bit internally, so there's no risk of overflow in expressions.

## Practical Example: DNS Header

A DNS packet header is big-endian with tightly packed fields:

```
@default_endian(big)

@root struct DnsHeader {
    transaction_id: u16,
    flags: u16,
    question_count: u16,
    answer_count: u16,
    authority_count: u16,
    additional_count: u16,
}
```

Given `[0x12, 0x34, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]`:

```json
{
    "transaction_id": 4660,
    "flags": 256,
    "question_count": 1,
    "answer_count": 0,
    "authority_count": 0,
    "additional_count": 0
}
```

## What You Learned

- `@default_endian` sets the file-wide byte order
- Endian-qualified types (`u16le`, `u32be`) override the default per-field
- Integer literals support hex, binary, octal, and decimal
- Single-byte types have no endian variants

**Next**: [Enums and Flags →](03-enums-and-flags.md)
