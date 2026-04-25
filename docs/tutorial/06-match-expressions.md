# 6. Match Expressions

Binary formats often have tagged unions — a discriminator field determines what type follows. BinScript uses `match` to express this cleanly.

## Basic Match

```
@default_endian(little)

struct TextPayload { text: cstring }
struct BinaryPayload { data: bytes[16] }

@root struct Message {
    msg_type: u8,
    body: match(msg_type) {
        1 => TextPayload,
        2 => BinaryPayload,
        _ => bytes[0],
    },
}
```

### 🔑 How Match Works

1. The discriminator expression (`msg_type`) is evaluated
2. Each arm's pattern is tested in order
3. The first matching arm determines the type to parse
4. `_` is the wildcard — matches anything (default case)

### Pattern Types

```
match(tag) {
    1 => TypeA,                     // Value pattern — exact match
    2..5 => TypeB,                  // Range pattern — inclusive range
    s when s > 100 => TypeC,        // Identifier + guard — bind and test
    _ => TypeD,                     // Wildcard — default fallback
}
```

## Value Patterns

The simplest form — match an exact value:

```
enum PacketType : u8 {
    HANDSHAKE = 0x01,
    DATA      = 0x02,
    CLOSE     = 0x03,
}

@root struct Packet {
    ptype: PacketType,
    body: match(ptype) {
        PacketType.HANDSHAKE => Handshake,
        PacketType.DATA => DataPacket,
        PacketType.CLOSE => ClosePacket,
        _ => bytes[0],
    },
}
```

## Range Patterns

Match a contiguous range of values (inclusive on both ends):

```
match(version) {
    1..3 => LegacyFormat,
    4..7 => ModernFormat,
    _ => bytes[0],
}
```

## Guard Arms

Sometimes the discriminator value isn't enough — you need to check additional conditions:

```
@root struct SectionEntry {
    name: fixed_string[8],
    body: match(name) {
        s when s.starts_with(".debug") => DebugSection,
        s when s.starts_with(".text") => CodeSection,
        _ => GenericSection,
    },
}
```

### 🔑 Identifier Patterns with Guards

`s when condition` does two things:
1. **Binds** the discriminant value to the name `s`
2. **Evaluates** the guard expression — the arm matches only if the guard is true

Available string methods for guards:
- `.starts_with(prefix)` — true if string starts with prefix
- `.ends_with(suffix)` — true if string ends with suffix
- `.contains(substring)` — true if string contains substring

### Value Pattern + Guard

You can also guard a specific value:

```
match(version) {
    1 when flags > 0 => ExtendedV1,
    1 => BasicV1,
    2 => V2Format,
    _ => bytes[0],
}
```

Here, version 1 is parsed as `ExtendedV1` only when `flags > 0`; otherwise it falls through to `BasicV1`.

## Arrays in Match Arms

Match arms can produce arrays:

```
match(compression) {
    0 => u8[uncompressed_size],
    8 => bytes[compressed_size],
    _ => bytes[0],
}
```

## ⚠️ Common Pitfalls

**No exhaustiveness check (warning only)**: If you omit the wildcard `_` arm, the compiler warns but doesn't error. At runtime, an unmatched value produces an empty result for that field.

**Arm order matters**: The first matching arm wins. Put specific patterns before general ones:

```
// ❌ Wrong — wildcard catches everything
match(tag) {
    _ => Default,
    1 => Special,     // never reached!
}

// ✅ Right — specific before general
match(tag) {
    1 => Special,
    _ => Default,
}
```

## Practical Example: USB Descriptor

```
@default_endian(little)

@root struct UsbDescriptor {
    length: u8,
    descriptor_type: u8,
    body: match(descriptor_type) {
        1 => DeviceDescriptor,
        2 => ConfigDescriptor,
        4 => InterfaceDescriptor,
        5 => EndpointDescriptor,
        _ => bytes[length - 2],
    },
}

struct DeviceDescriptor {
    usb_version: u16,
    device_class: u8,
    device_subclass: u8,
    protocol: u8,
    max_packet_size: u8,
    vendor_id: u16,
    product_id: u16,
}

struct ConfigDescriptor {
    total_length: u16,
    num_interfaces: u8,
    config_value: u8,
    config_string: u8,
    attributes: u8,
    max_power: u8,
}

struct InterfaceDescriptor {
    interface_number: u8,
    alternate_setting: u8,
    num_endpoints: u8,
    interface_class: u8,
    interface_subclass: u8,
    interface_protocol: u8,
    interface_string: u8,
}

struct EndpointDescriptor {
    endpoint_address: u8,
    attributes: u8,
    max_packet_size: u16,
    interval: u8,
}
```

## What You Learned

- `match(expr) { pattern => Type }` for tagged unions
- Value patterns for exact matches, range patterns for ranges
- `s when guard` for conditional matching with bound identifiers
- String methods: `.starts_with()`, `.ends_with()`, `.contains()`
- Wildcard `_` as the default fallback
- Arm order matters — first match wins

**Next**: [Seeking and Alignment →](07-seeking-and-alignment.md)
