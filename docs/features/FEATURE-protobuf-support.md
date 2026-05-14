# Feature: Protocol Buffers Wire Format Support

## Status

Proposed (future development)

## Summary

Add first-class support for parsing and producing Protocol Buffers (protobuf) wire-format encoded data. The primary motivating use case is OpenTelemetry (OTEL) signal payloads — logs, traces, and metrics — which are transmitted as protobuf-encoded messages.

This feature requires both new primitive types (varints) and a new struct execution mode (tag-based field dispatch) that fundamentally extends BinScript beyond positional binary formats into self-describing TLV (Tag-Length-Value) formats.

## Motivation

### Why Protobuf?

Protocol Buffers is the dominant binary serialization format for modern infrastructure tooling. OpenTelemetry — the industry-standard observability framework — uses protobuf exclusively for its wire protocol (OTLP). Being able to parse and produce OTEL payloads would allow BinScript to:

- Inspect OTLP traffic on the wire (packet captures, proxies)
- Transform telemetry data between formats
- Generate synthetic OTEL payloads for testing
- Serve as a lightweight alternative to full protobuf SDK integration in constrained environments

### Why Not Just Use a Protobuf Library?

BinScript's value proposition is a single declarative script that handles both directions (parse and produce) without code generation or language-specific SDKs. A `.bsx` script for OTEL would be:

- **Portable** — works from any language via the C-ABI
- **Inspectable** — the script IS the documentation of the wire format
- **Bidirectional** — parse and produce from the same definition
- **Versionable** — update the script when the proto schema evolves

---

## Protobuf Wire Format — Technical Background

### Wire Types

Protobuf defines 6 wire types (3 bits):

| ID | Name | Payload | Used For |
|---|---|---|---|
| `0` | VARINT | Variable-length (1–10 bytes) | int32, int64, uint32, uint64, sint32, sint64, bool, enum |
| `1` | I64 | Fixed 8 bytes, little-endian | fixed64, sfixed64, double |
| `2` | LEN | Varint length + N bytes | string, bytes, embedded messages, packed repeated fields |
| `3` | SGROUP | None (deprecated) | Group start |
| `4` | EGROUP | None (deprecated) | Group end |
| `5` | I32 | Fixed 4 bytes, little-endian | fixed32, sfixed32, float |

### Encoding Fundamentals

**Field tags**: Every field starts with a varint-encoded tag = `(field_number << 3) | wire_type`. The parser reads the tag, extracts wire_type (low 3 bits) to determine payload size, and field_number (remaining bits) to route to the correct field.

**Varints (Base-128, MSB continuation)**:
```
Encoding:
  while (value > 127):
    emit byte: (value & 0x7F) | 0x80   // MSB = "more bytes follow"
    value >>= 7
  emit byte: value & 0x7F              // MSB = 0, final byte

Decoding:
  result = 0; shift = 0
  loop:
    byte = read_byte()
    result |= (byte & 0x7F) << shift
    if (byte & 0x80) == 0: break
    shift += 7
```

Range: 1 byte for 0–127, up to 10 bytes for full 64-bit values.

**ZigZag encoding** (sint32/sint64): Maps signed integers to unsigned for efficient varint encoding of small negative values:
```
Encode: (n << 1) ^ (n >> 31)   // sint32
        (n << 1) ^ (n >> 63)   // sint64
Decode: (n >>> 1) ^ -(n & 1)   // unsigned right shift
```

**Nested messages**: Wire type LEN — `[tag][length varint][serialized child message bytes]`. Children are complete, self-contained protobuf streams.

**Packed repeated fields**: A single LEN record containing concatenated values (varint/i32/i64) with no separators — read until the length-delimited payload is exhausted.

**Maps**: Syntactic sugar for `repeated Entry { key=1; value=2; }` — no special wire encoding.

**Oneofs**: No special wire encoding — at most one field from the set is present; "last value wins" on decode.

### Key Structural Properties

| Property | Implication for BinScript |
|---|---|
| **Tag-driven, not positional** | Fields arrive in any order, identified by field_number |
| **Self-describing** | Wire type alone determines payload size (schema-free skipping) |
| **Non-contiguous field numbers** | Field numbers have gaps (reserved fields, evolution) |
| **Repeated = multiple records** | Same field_number appears N times; values accumulate |
| **Merge semantics** | Duplicate scalar = overwrite; duplicate message = merge; duplicate repeated = concatenate |
| **Forward-compatible** | Unknown fields are skipped (not rejected) based on wire type |

---

## OpenTelemetry Signal Complexity

All three OTEL signals share a common envelope:

```
<Signal>Data
  └── repeated Resource<Signal>
       ├── Resource (repeated KeyValue attributes)
       ├── string schema_url
       └── repeated Scope<Signal>
            ├── InstrumentationScope
            ├── string schema_url
            └── repeated <SignalRecord>
```

### Per-Signal Requirements

| Signal | Max Nesting | Special Features |
|---|---|---|
| **Logs** | 4 + recursive AnyValue | `fixed64` timestamps, `fixed32` flags, recursive `AnyValue` body |
| **Traces** | 5 + recursive AnyValue | Nested `Event[]` and `Link[]` inside `Span`, `bytes` trace/span IDs |
| **Metrics** | 6–7 + recursive AnyValue | Multiple oneofs (`Metric.data`, `NumberDataPoint.value`), ZigZag `sint32`, packed `repeated fixed64/double` arrays |

### AnyValue — Recursive Oneof

The most challenging type: `AnyValue` is a **mutually recursive** oneof used for all attribute values and log bodies:

```protobuf
message AnyValue {
  oneof value {
    string string_value = 1;
    bool bool_value = 2;
    int64 int_value = 3;
    double double_value = 4;
    ArrayValue array_value = 5;      // → repeated AnyValue
    KeyValueList kvlist_value = 6;   // → repeated KeyValue → AnyValue
    bytes bytes_value = 7;
  }
}
```

This requires arbitrary-depth recursion at runtime — already supported by BinScript's guarded recursion mechanism.

### Wire Feature Usage Matrix

| Feature | Logs | Traces | Metrics |
|---|---|---|---|
| Varints (uint32/uint64/enum) | ✅ | ✅ | ✅ |
| ZigZag varints (sint32) | ❌ | ❌ | ✅ |
| Fixed32 (flags) | ✅ | ✅ | ❌ |
| Fixed64 (timestamps, doubles) | ✅ | ✅ | ✅ |
| LEN strings | ✅ | ✅ | ✅ |
| LEN bytes | ✅ | ✅ | ✅ |
| LEN nested messages | ✅ | ✅ | ✅ |
| Repeated (unpacked messages) | ✅ | ✅ | ✅ |
| Packed repeated (fixed64/double/uint64) | ❌ | ❌ | ✅ |
| Oneofs | ❌ | ❌ | ✅ |
| Recursive messages (AnyValue) | ✅ | ✅ | ✅ |
| Maps | ❌ | ❌ | ❌ |

**No OTEL signal uses**: maps, groups (deprecated), or packed varint arrays (all packed fields are fixed-width).

---

## Gap Analysis — BinScript vs. Protobuf Requirements

### What Already Works ✅

| Protobuf Need | BinScript Feature |
|---|---|
| Fixed32 / Fixed64 reads | `u32le`, `i32le`, `u64le`, `i64le`, `f32le`, `f64le` |
| Nested messages | Nested structs, parameterized structs |
| Strings and bytes | `string[N]`, `bytes[N]` |
| Bitwise tag decomposition | `@let tag_wt = tag & 0x07`, `@let tag_fn = tag >> 3` |
| Recursive structs (AnyValue) | Guarded recursion + `@max_depth` |
| Computed/derived fields | `@derived`, `@let`, `@map` |
| Reading until buffer exhausted | `@until(@remaining == 0)` |
| Discriminated unions | `match(expr) { ... }` |

### What's Missing 🔴

#### P0 — Critical Blockers (no workaround)

##### 1. Varint / LEB128 Primitive Type

**The fundamental blocker.** Protobuf's entire wire format is built on varints — every field tag, every length prefix, every integer value. BinScript has only fixed-width integer types.

**Requirement**: A built-in type that reads 1–10 bytes using base-128 MSB-continuation encoding, producing a `u64` value.

**Proposed syntax**:
```
tag: uvarint           // unsigned LEB128 → u64
length: uvarint        // varint for length prefixes
value: ivarint         // signed (two's complement) varint → i64
signed_val: svarint    // ZigZag-decoded signed varint → i64
```

**Implementation scope**:
- Lexer: new type keywords (`uvarint`, `ivarint`, `svarint`)
- Parser/AST: new `FieldType` variants
- TypeResolver: size = variable (like `cstring`)
- SemanticAnalyzer: valid in all contexts where variable-size types are allowed
- BytecodeEmitter: new opcodes (`READ_UVARINT`, `READ_IVARINT`, `READ_SVARINT`)
- ParseEngine: byte-level decode loop (1–10 bytes, MSB continuation)
- ProduceEngine: encode loop (value → base-128 bytes, with ZigZag for `svarint`)
- `@sizeof(varint_field)` must return the actual encoded byte count

##### 2. Tag-Based Field Dispatch (TLV Struct Mode)

**Architectural gap.** BinScript structs execute fields sequentially in declaration order. Protobuf fields arrive in arbitrary order, identified by tag. The same field_number may appear multiple times (repeated fields).

**Requirement**: A struct mode where the runtime:
1. Reads a tag (varint)
2. Dispatches to the matching field by `field_number`
3. Accumulates repeated field occurrences into arrays
4. Skips unknown fields based on wire_type
5. Loops until data is exhausted

**Proposed syntax** (option A — annotation-driven):
```
@encoding(protobuf)
struct LogRecord {
    @field(1)  time_unix_nano: u64le,           // wire_type inferred from field type
    @field(11) observed_time_unix_nano: u64le,
    @field(2)  severity_number: uvarint,
    @field(3)  severity_text: pb_string,
    @field(5)  body: AnyValue,
    @field(6)  attributes: KeyValue[],          // repeated (unpacked message)
    @field(7)  dropped_attributes_count: uvarint,
    @field(8)  flags: u32le,
    @field(9)  trace_id: pb_bytes,
    @field(10) span_id: pb_bytes,
}
```

**Proposed syntax** (option B — manual TLV loop with existing constructs + varint):
```
struct ProtobufMessage {
    fields: ProtobufField[] @until(@remaining == 0),
}

struct ProtobufField {
    tag: uvarint,
    @let wire_type = tag & 0x07,
    @let field_number = tag >> 3,
    match(wire_type) {
        0 => { value: uvarint },
        1 => { value: u64le },
        2 => { @let len = uvarint, payload: bytes[len] },
        5 => { value: u32le },
    }
}
```

Option B is more general but produces unstructured output (flat list of tag+value pairs rather than named fields). Option A requires more compiler support but produces ergonomic, schema-aware output.

**A hybrid approach** may be best: implement varint + a `@tagged` struct mode that automates TLV dispatch while preserving BinScript's declarative field model.

**Implementation scope** (Option A):
- New `@encoding(protobuf)` annotation or `@tagged` struct modifier
- New `@field(N)` annotation on struct members
- New array accumulation mode for repeated fields (non-contiguous)
- Runtime TLV dispatch loop in both ParseEngine and ProduceEngine
- Unknown-field skip logic (wire_type → skip size)
- Produce mode: emit fields in field_number order with proper tags

#### P1 — Required for Full OTEL Coverage

##### 3. ZigZag Decoding

Needed for `sint32`/`sint64` fields (ExponentialHistogram bucket offsets/scale in OTEL metrics).

**Options**:
- A dedicated type: `svarint` (reads varint, applies ZigZag decode automatically)
- A built-in function: `@zigzag(uvarint_field)` that applies `(n >>> 1) ^ -(n & 1)`

The dedicated `svarint` type (listed under P0.1) is the cleanest approach.

##### 4. Length-Scoped Sub-Buffer Parsing

Protobuf nested messages are length-prefixed: read the length varint, then parse exactly that many bytes as a child message. The child must see `@remaining` relative to its own length, not the parent buffer.

**Requirement**: A mechanism to "scope" parsing into a sub-buffer of known length.

**Possible approach**: Already partially supported — a parameterized struct reading `bytes[len]` then interpreting it. But `bytes[N]` produces raw bytes; we need to parse them as a struct. This could be:
```
// New: inline sub-buffer scoping
@field(5) body: AnyValue @length(len),    // parse AnyValue within next `len` bytes
```

Or leveraging existing pointer mechanics:
```
@let len: uvarint,
@let payload_start = @offset,
body: AnyValue,                           // parse within [offset, offset+len)
@seek(payload_start + len),               // skip any unparsed remainder
```

##### 5. Non-Contiguous Repeated Field Accumulation

In positional BinScript, arrays are contiguous elements. In protobuf, repeated fields can be interleaved:
```
field 1 (attribute), field 2 (name), field 1 (attribute), field 1 (attribute)
```

All three `field 1` values must be collected into a single array.

**Handled by**: The TLV struct mode (P0.2) would inherently solve this — the dispatch loop accumulates same-field_number records.

##### 6. Conditional Fields (`if`/`else`)

Already tracked in `FUTURE_EXTENSIONS.md`. Useful for optional protobuf fields (emit field only if value != default in produce mode).

---

## Phased Delivery Plan

### Phase 1: Varint Primitives

Add `uvarint`, `ivarint`, and `svarint` types to the language. This is foundational — every subsequent phase depends on it.

**Deliverables**:
- Three new built-in types with variable-length encoding
- New opcodes: `READ_UVARINT`, `READ_IVARINT`, `READ_SVARINT`, `WRITE_UVARINT`, `WRITE_IVARINT`, `WRITE_SVARINT`
- `@sizeof(field)` returns actual encoded byte count for varint fields
- Full round-trip tests

**Validation**: Write a manual protobuf parser using varints + existing `match`/`@until` constructs. It won't be ergonomic (Option B above), but it proves the primitive works.

### Phase 2: TLV Struct Mode

Add `@encoding(protobuf)` struct annotation with `@field(N)` dispatch.

**Deliverables**:
- Tag-based field dispatch loop in ParseEngine
- Field-number-ordered tag emission in ProduceEngine
- Repeated field accumulation (non-contiguous)
- Unknown field skipping
- Wire type inference from field types

**Validation**: Parse a simple protobuf message (e.g., OTEL `Resource`) end-to-end.

### Phase 3: Nested Messages & Packed Arrays

Add length-scoped sub-buffer parsing and packed repeated field support.

**Deliverables**:
- `@length(expr)` annotation for sub-buffer scoping
- Packed array syntax: `field: Type[] @packed` (reads elements until sub-buffer exhausted)
- Nested message composition (structs within TLV structs)

**Validation**: Parse OTEL `LogRecord` with nested `AnyValue` body and repeated `KeyValue` attributes.

### Phase 4: Full OTEL Coverage

Integration testing with real-world OTEL payloads across all three signals.

**Deliverables**:
- `stdlib/otel/logs.bsx` — parse/produce LogsData
- `stdlib/otel/traces.bsx` — parse/produce TracesData
- `stdlib/otel/metrics.bsx` — parse/produce MetricsData (including ExponentialHistogram with ZigZag)
- `stdlib/otel/common.bsx` — shared types (AnyValue, KeyValue, Resource, InstrumentationScope)
- Round-trip tests against captured OTEL payloads

---

## Open Design Questions

1. **Naming**: `uvarint`/`ivarint`/`svarint` vs. `leb128u`/`leb128s`/`zigzag` vs. `pb_uint`/`pb_int`/`pb_sint`?
2. **Generality**: Should TLV dispatch be protobuf-specific or general enough for other TLV formats (ASN.1 BER, CBOR, MessagePack)?
3. **Schema evolution**: How to handle `@field` number conflicts when composing modules?
4. **Default values**: Protobuf fields absent from wire have default values (0, "", empty). Should BinScript emit these or omit them from JSON output?
5. **Produce ordering**: Should produce mode emit fields in declaration order or field_number order? (Protobuf spec says field_number order is canonical but not required.)
6. **Wire type validation**: Should the parser enforce that `@field(N)` type matches the observed wire_type, or silently skip mismatches (forward-compat)?

---

## Alternatives Considered

### A. "Just Use Protobuf SDK"

Rejected. Defeats the purpose of BinScript — we want a single declarative script, not generated code. Also, the C-ABI boundary makes SDK integration impractical for the APL use case.

### B. "Implement Only Varints, Do TLV Manually"

Feasible with existing `match` + `@until`, but produces unstructured output (array of generic field records). Loses BinScript's core value of named, typed, documented fields mapping directly to format semantics. Acceptable as a Phase 1 validation step but not as the final solution.

### C. "External Preprocessor"

Have a tool that converts `.proto` files to `.bsx` scripts. Interesting for automation but doesn't solve the fundamental runtime gaps (still needs varint + TLV dispatch). Could be a valuable companion tool once the runtime supports protobuf.

---

## References

- [Protocol Buffers Encoding Specification](https://protobuf.dev/programming-guides/encoding/)
- [OpenTelemetry Proto Repository](https://github.com/open-telemetry/opentelemetry-proto)
- [ADR-002: Circular Structures Deferred](../adr/ADR-002-circular-structures-deferred.md) — recursion depth limits apply to AnyValue
- [FUTURE_EXTENSIONS: Conditional Fields](../FUTURE_EXTENSIONS.md) — `if`/`else` needed for optional fields
- [BYTECODE.md](../BYTECODE.md) — new opcodes will be added here
- [LANGUAGE_SPEC.md](../LANGUAGE_SPEC.md) — varint types and annotations defined here
