# 12. Walkthrough: TCP/IP Packet

This walkthrough builds a BinScript definition for parsing a raw Ethernet frame containing an IPv4 TCP packet. It exercises byte order mixing, bitfields, computed fields, and match expressions.

## Layer Model

```
┌──────────────────────────────┐
│  Ethernet Frame (14 bytes)   │  Big-endian
├──────────────────────────────┤
│  IPv4 Header (20+ bytes)     │  Big-endian
├──────────────────────────────┤
│  TCP Header (20+ bytes)      │  Big-endian
├──────────────────────────────┤
│  Payload (variable)          │
└──────────────────────────────┘
```

Network protocols are big-endian ("network byte order").

## The Complete Script

```
@default_endian(big)

// ─── Ethernet ───────────────────────────────────────────────────

@root struct EthernetFrame {
    dst_mac: bytes[6],
    src_mac: bytes[6],
    ether_type: u16,
    payload: match(ether_type) {
        0x0800 => Ipv4Packet,
        _ => bytes[@remaining],
    },
}

// ─── IPv4 ───────────────────────────────────────────────────────

struct Ipv4Packet {
    version_ihl: u8,
    @derived version: u8 = (version_ihl >> 4) & 0x0F @hidden,
    @derived ihl: u8 = version_ihl & 0x0F @hidden,
    @derived header_length: u8 = ihl * 4 @hidden,
    dscp_ecn: u8,
    total_length: u16,
    identification: u16,
    flags_fragment: u16,
    @derived flags: u8 = (flags_fragment >> 13) & 0x07 @hidden,
    @derived fragment_offset: u16 = flags_fragment & 0x1FFF @hidden,
    ttl: u8,
    protocol: u8,
    header_checksum: u16,
    src_ip: u32,
    dst_ip: u32,
    @skip(header_length - 20)
    body: match(protocol) {
        6 => TcpSegment,
        17 => UdpDatagram,
        _ => bytes[@remaining],
    },
}

// ─── TCP ────────────────────────────────────────────────────────

struct TcpSegment {
    src_port: u16,
    dst_port: u16,
    sequence_number: u32,
    ack_number: u32,
    data_offset_flags: u16,
    @derived data_offset: u8 = (data_offset_flags >> 12) & 0x0F @hidden,
    @derived header_length: u8 = data_offset * 4 @hidden,
    @derived tcp_flags: TcpFlags = data_offset_flags @hidden,
    window_size: u16,
    checksum: u16,
    urgent_pointer: u16,
    @let options_len = header_length - 20
    options: bytes[options_len],
    payload: bytes[@remaining],
}

bits struct TcpFlags : u16 {
    fin: bit,
    syn: bit,
    rst: bit,
    psh: bit,
    ack: bit,
    urg: bit,
    ece: bit,
    cwr: bit,
    _reserved: bits[4],
    _data_offset: bits[4],
}

// ─── UDP ────────────────────────────────────────────────────────

struct UdpDatagram {
    src_port: u16,
    dst_port: u16,
    length: u16,
    checksum: u16,
    @let payload_length = length - 8
    payload: bytes[payload_length],
}
```

## Key Techniques Used

### Mixed-Level Bitfield Unpacking

IPv4's first byte packs version (4 bits) and IHL (4 bits). We read the raw byte, then use `@derived @hidden` to extract sub-fields:

```
version_ihl: u8,
@derived version: u8 = (version_ihl >> 4) & 0x0F @hidden,
@derived ihl: u8 = version_ihl & 0x0F @hidden,
```

### Computed Skip

IPv4 options are variable-length. We compute how many bytes to skip:

```
@derived header_length: u8 = ihl * 4 @hidden,
@skip(header_length - 20)
```

⚠️ Wait — `@skip` only accepts literals! So we'd actually use:

```
@seek(@offset + header_length - 20)
```

This is exactly the pattern the `@skip` limitation anticipates. The `@seek` workaround is clean and explicit.

### Protocol Dispatch

The IPv4 `protocol` field determines the next layer:

```
body: match(protocol) {
    6 => TcpSegment,
    17 => UdpDatagram,
    _ => bytes[@remaining],
}
```

### `@let` for Intermediate Values

TCP's data offset field is in 32-bit words. We compute the actual byte length:

```
@let options_len = header_length - 20
options: bytes[options_len],
```

## What This Demonstrates

- Big-endian throughout (network byte order)
- Bitfield extraction via shift/mask on `@derived` fields
- `match` for protocol multiplexing
- `@seek` as a dynamic alternative to `@skip`
- `@let` for readable intermediate computations
- Nested struct composition (Ethernet → IP → TCP/UDP)

**Next**: [Walkthrough: WAV Audio →](13-walkthrough-wav.md)
