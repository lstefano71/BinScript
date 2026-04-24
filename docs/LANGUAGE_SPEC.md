# BinScript Language Specification

Version: 0.1 (Draft)

## 1. Overview

BinScript (`.bsx`) is a declarative language for describing binary data structures. A script defines types (structs, enums, bit structs) and constants that together describe how to interpret a sequence of bytes. The language is designed to be:

- **Human-readable** and writable by software developers
- **Compilable** to efficient bytecode for repeated application
- **Bidirectional** — the same script drives both parsing (binary → structured data) and producing (structured data → binary)

## 2. Lexical Structure

### 2.1 Comments

```
// This is a line comment

/* This is a
   block comment */
```

### 2.2 Identifiers

Identifiers start with a letter or underscore, followed by letters, digits, or underscores. Identifiers starting with `_` are reserved for internal use (e.g., `_variant`, `_index`).

```
my_field
CoffHeader
_reserved      // valid, commonly used for padding/unused fields
```

### 2.3 Numeric Literals

```
42             // decimal
0xFF           // hexadecimal
0b10110        // binary
0o755          // octal
```

### 2.4 String Literals

String literals use double quotes. Escape sequences: `\\`, `\"`, `\n`, `\r`, `\t`, `\0`, `\xHH`.

```
"IHDR"
"PE\0\0"
"\x50\x4B\x03\x04"
```

### 2.5 Keywords

```
struct    bits      enum      const     match     when
if        else      true      false     bit       bytes
cstring   string    fixed_string
```

### 2.6 Directives (Annotations)

All directives start with `@`:

```
@root           @default_endian     @default_encoding
@param          @import             @coverage
@let            @seek               @at
@align          @skip               @hidden
@derived        @assert             @encoding
@until          @until_sentinel     @greedy
@input_size     @offset             @remaining
@sizeof         @offset_of          @count
@strlen         @crc32              @adler32
```

## 3. Type System

### 3.1 Primitive Types

| Type | Size | Description |
|------|------|-------------|
| `u8` | 1 | Unsigned 8-bit integer |
| `u16` | 2 | Unsigned 16-bit integer |
| `u32` | 4 | Unsigned 32-bit integer |
| `u64` | 8 | Unsigned 64-bit integer |
| `i8` | 1 | Signed 8-bit integer |
| `i16` | 2 | Signed 16-bit integer |
| `i32` | 4 | Signed 32-bit integer |
| `i64` | 8 | Signed 64-bit integer |
| `f32` | 4 | IEEE 754 single-precision float |
| `f64` | 8 | IEEE 754 double-precision float |
| `bool` | 1 | Boolean (0 = false, non-zero = true) |

#### Endianness Suffixes

Any multi-byte primitive can have an explicit endianness suffix:

```
u32le       // little-endian u32
u32be       // big-endian u32
u16le       // little-endian u16
i64be       // big-endian i64
f32le       // little-endian f32
```

Without a suffix, the type uses the active default endianness (set by `@default_endian`).

### 3.2 String Types

| Type | Description |
|------|-------------|
| `cstring` | Nul-terminated string. Reads until `0x00` byte (inclusive). |
| `string[expr]` | Length-prefixed string. `expr` is the byte length. |
| `fixed_string[N]` | Fixed-width string. Always reads exactly `N` bytes. Trailing padding (usually `0x00`) is stripped on parse, added on produce. |

String encoding defaults to UTF-8. Override with `@encoding`:

```
value: string[length] @encoding(utf16le),
tag: cstring @encoding(ascii),
```

Supported encodings: `ascii`, `utf8`, `utf16le`, `utf16be`, `latin1`. More may be added.

### 3.3 Byte Blobs

```
data: bytes[expr]           // read exactly expr bytes
remaining_data: bytes[@remaining]  // read all remaining bytes
```

### 3.4 Bit Types (within `bits struct` only)

| Type | Description |
|------|-------------|
| `bit` | Single bit (parsed as boolean) |
| `bits[N]` | N-bit unsigned integer |

## 4. Declarations

### 4.1 Struct

The primary declaration. Describes a sequence of fields read from binary data.

```
struct Name {
    field_name: type,
    ...
}
```

#### Parameterized Structs

Structs can accept parameters from their parent context:

```
struct OptionalHeader(arch) {
    magic: u16,
    data: match(arch) {
        0x014C => OptionalHeader32,
        0x8664 => OptionalHeader64,
        _ => bytes[@remaining],
    },
}
```

Parameters are passed at the call site:

```
optional_header: OptionalHeader(coff_header.machine),
```

#### Root Struct

One struct should be marked as the default entry point:

```
@root struct PeFile {
    ...
}
```

The caller can override the entry point at parse time.

### 4.2 Bits Struct

Describes a bit-level layout within a fixed number of bytes. The total size is specified as a base type.

```
bits struct Characteristics : u16 {
    relocs_stripped: bit,        // bit 0
    executable_image: bit,       // bit 1
    large_address_aware: bit,    // bit 5 (etc.)
    ...
}
```

Bits are read from the least-significant bit (bit 0) to the most-significant bit, within the byte order determined by the base type's endianness. Use `_reserved: bit` or `_pad: bits[3]` for unused bits.

**JSON representation**: An object with named boolean/integer fields:
```json
{ "relocs_stripped": true, "executable_image": true, "large_address_aware": false }
```

### 4.3 Enum

Maps values to named constants. The base type determines the wire size.

```
enum Machine : u16 {
    I386   = 0x014C,
    AMD64  = 0x8664,
    ARM64  = 0xAA64,
}

enum PngChunkType : fixed_string[4] {
    IHDR = "IHDR",
    PLTE = "PLTE",
    IDAT = "IDAT",
    IEND = "IEND",
}
```

Use an enum as a field type:

```
machine: Machine,
```

**JSON representation**: The enum name if the value matches a defined variant, otherwise the raw numeric/string value. Both forms are accepted in produce mode:
```json
"machine": "AMD64"       // known value → name
"machine": 4919          // unknown value → raw
```

### 4.4 Constants

Standalone named values for use in expressions:

```
const PE_MAGIC = 0x00004550;
const DOS_MAGIC = 0x5A4D;
```

## 5. Field Modifiers & Directives

### 5.1 Default Endianness

Sets the default byte order for all multi-byte primitives without an explicit suffix:

```
@default_endian(little)     // or: big
```

Can be placed at file level (affects all structs) or struct level (affects that struct and its children).

### 5.2 Default Encoding

Sets the default string encoding:

```
@default_encoding(utf8)     // or: ascii, utf16le, utf16be, latin1
```

### 5.3 Magic Values (Implicit Assertions)

A field with an `= value` asserts that the parsed value must match:

```
signature: u32 = 0x00004550,    // parse fails if value doesn't match
```

In produce mode, the magic value is written regardless of input data.

### 5.4 Explicit Assertions

```
@assert(magic == 0x10B || magic == 0x20B, "Invalid PE optional header magic"),
```

### 5.5 Derived Fields

Fields recomputed during produce (not taken from structured input):

```
@derived length: u32 = @sizeof(data),
@derived crc: u32 = @crc32(chunk_type, data),
```

### 5.6 Cross-Struct References with `@let`

Bind a value from one branch of the parse tree for use in another:

```
@let arch = coff_header.machine,
optional_header: OptionalHeader(arch),
sections: Section(arch)[coff_header.number_of_sections],
```

Without `@let`, fields can reference their parent struct's fields and sibling fields directly by name:

```
struct Chunk {
    length: u32,
    data: bytes[length],    // references sibling field
}
```

### 5.7 Seeking

#### `@seek(expr)` — Jump and Stay

Sets the read cursor to an absolute offset. Subsequent fields continue from that position.

```
@seek(dos_header.e_lfanew)
pe_signature: u32 = 0x00004550,
```

#### `@at(expr) { ... }` — Jump, Read, Return

Reads fields at the specified offset, then restores the cursor to its previous position:

```
@at(file_size - 22) {
    eocd: EndOfCentralDir,
}
```

### 5.8 Alignment and Padding

```
@align(512)             // skip bytes until offset is a multiple of 512
@skip(4)                // skip exactly 4 bytes
reserved: bytes[8] @hidden,  // read 8 bytes but don't include in output
```

### 5.9 Coverage Annotation

Signals that the script intentionally does not describe the entire input:

```
@coverage(partial)
struct PeQuickHeader { ... }
```

Suppresses warnings in `WarnUnconsumed` strictness mode.

### 5.10 Imports

```
@import "pe/dos_header"
@import "pe/coff_header"
```

Modules are resolved via a host-provided registry at compile time. All imported definitions are bundled into the compiled bytecode.

### 5.11 Compile-Time Parameters

```
@param version: u32
@param arch: string
```

Values are provided by the host at compile time and baked into the bytecode.

## 6. Arrays

### 6.1 Count-Based

```
sections: SectionHeader[coff_header.number_of_sections],
```

The count expression can reference any previously parsed field or parameter.

### 6.2 Condition-Based

```
chunks: PngChunk[] @until(@remaining == 0),
```

After each element is parsed, the condition is evaluated. If true, the array ends.

### 6.3 Sentinel-Terminated

```
entries: DirEntry[] @until_sentinel(e => e.type == 0),
```

The sentinel element itself is NOT included in the array. After parsing each element, the predicate is applied — if it returns true, that element is discarded and the array ends.

### 6.4 Greedy

```
blocks: DataBlock[] @greedy,
```

Parses elements until a parse error occurs. The failing element is discarded; previously parsed elements are kept. Useful for recovery/forensic scenarios.

### 6.5 Indexed Access

Within arrays, `_index` is a built-in variable holding the zero-based index of the current element:

```
local_files: @at(central_dir[_index].local_header_offset)
             LocalFileEntry[eocd.total_entries],
```

## 7. Match Expressions (Tagged Unions)

Match selects a type based on a discriminant value:

```
body: match(chunk_type) {
    PngChunkType.IHDR => IhdrBody,
    PngChunkType.PLTE => PlteBody,
    PngChunkType.IDAT => IdatBody,
    _ => bytes[length],
}
```

### 7.1 Guards

Arms can have `when` guards for complex conditions:

```
content: match(section_name) {
    ".text" when arch == Machine.ARM64 => Arm64Code,
    ".text" when arch == Machine.AMD64 => X64Code,
    ".text" => GenericCode,
    s when s.starts_with(".debug") => DebugData,
    _ => bytes[@remaining],
}
```

Guards have access to all in-scope variables (parent fields, `@let` bindings, parameters).

### 7.2 Range Arms

```
value: match(tag) {
    0x00..0x1F => ControlRecord,
    0x20..0x7E => AsciiRecord,
    _ => BinaryRecord,
}
```

### 7.3 JSON Representation

Match results include a `_variant` field identifying the selected arm:

```json
{
    "body": {
        "_variant": "IhdrBody",
        "width": 800,
        "height": 600
    }
}
```

In produce mode, the `_variant` field is required to determine which arm to produce.

## 8. Built-in Pseudo-Variables

| Name | Type | Description |
|------|------|-------------|
| `@input_size` | `u64` | Total size of the input buffer in bytes |
| `@offset` | `u64` | Current read position (bytes from start) |
| `@remaining` | `u64` | Bytes remaining: `@input_size - @offset` |
| `_index` | `u64` | Current array element index (only valid inside arrays) |

## 9. Built-in Functions

| Function | Description |
|----------|-------------|
| `@sizeof(field)` | Byte size of a field's data as parsed/produced |
| `@offset_of(field)` | Byte offset of a field from its containing struct's start |
| `@count(array_field)` | Number of elements in an array field |
| `@strlen(string_field)` | Byte length of a string field's content (excluding terminator) |
| `@crc32(fields...)` | CRC-32 over the raw bytes of the specified fields |
| `@adler32(fields...)` | Adler-32 over the raw bytes of the specified fields |

## 10. Expressions

Expressions can appear in field type arguments, array counts, match conditions, `@assert`, `@derived`, `@at`, `@seek`, `@align`, and `@until`.

### 10.1 Operators (by precedence, lowest to highest)

| Precedence | Operators | Associativity |
|------------|-----------|---------------|
| 1 | `\|\|` | Left |
| 2 | `&&` | Left |
| 3 | `\|` | Left |
| 4 | `^` | Left |
| 5 | `&` | Left |
| 6 | `==` `!=` | Left |
| 7 | `<` `>` `<=` `>=` | Left |
| 8 | `<<` `>>` | Left |
| 9 | `+` `-` | Left |
| 10 | `*` `/` `%` | Left |
| 11 | `!` `~` `-` (unary) | Right |

### 10.2 Field Access

Dot notation for nested access:

```
dos_header.e_lfanew
central_dir[0].local_header_offset
parent_struct.child.field
```

### 10.3 String Methods

```
field.starts_with("text")
field.ends_with(".debug")
field.contains("rsrc")
```

## 11. Bidirectional Semantics

### 11.1 Parse Direction (Binary → Structured Data)

The engine reads bytes according to the struct layout, evaluating expressions as it goes, and emits field values to the active `IResultEmitter`.

### 11.2 Produce Direction (Structured Data → Binary)

The engine reads field values from the active `IDataSource` and writes bytes into a caller-provided buffer. Special behaviors:

- **Magic values**: Written as the constant, not read from input.
- **`@derived` fields**: Computed from the expression, not read from input. May require a two-pass layout (first pass to determine sizes/offsets, second pass to write).
- **`match` fields**: The `_variant` discriminator in the input data selects the arm.
- **Enum fields**: Both the name (string) and raw value (numeric) are accepted.
- **`@seek`/`@at`**: The engine writes at the specified offsets, leaving gaps filled with zeros (or as specified).

## 12. File Structure

A typical `.bsx` file:

```
// File-level directives
@default_endian(little)
@default_encoding(utf8)
@import "common/types"

// Compile-time parameters
@param version: u32

// Constants
const MAGIC = 0xDEADBEEF;

// Enums
enum RecordType : u8 {
    Header = 0x01,
    Data   = 0x02,
    Footer = 0xFF,
}

// Structs (order doesn't matter — forward references are resolved)
@root struct MyFormat {
    magic: u32 = MAGIC,
    version: u8,
    record_count: u32,
    records: Record[record_count],
}

struct Record {
    type: RecordType,
    length: u16,
    body: match(type) {
        RecordType.Header => HeaderBody,
        RecordType.Data => DataBody,
        RecordType.Footer => FooterBody,
        _ => bytes[length],
    },
}

struct HeaderBody { ... }
struct DataBody { ... }
struct FooterBody { ... }
```

## 13. Reserved for Future Specification

- Streaming parse/produce semantics
- User-defined functions
- Conditional compilation (`@if`)
- Bitwise field ordering options (MSB-first vs LSB-first)
