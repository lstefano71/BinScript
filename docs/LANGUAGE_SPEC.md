# BinScript Language Specification

Version: 0.2 (Draft)

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
cstring   string    fixed_string        ptr       relptr
null
```

Keywords and directives cannot be used as field or parameter names. There is currently no
escaping mechanism (see [ADR-004](adr/ADR-004-keyword-escaping.md) for a proposed backtick
syntax). If a binary format uses a name that collides with a keyword, choose an alternative
(e.g., `str_val` instead of `string`).

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
@sizeof         @offsetof           @count
@strlen         @crc32              @adler32
@map            @max_depth          @inline
@show_ptr
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

### 3.4 Pointer Types

Pointer types describe fields whose binary value is an address (or offset) pointing to data stored elsewhere in the buffer.

#### `ptr<T, width>` — Absolute Pointer

The pointer value is an absolute address. The engine converts between buffer offsets and absolute addresses using a base pointer provided via `@param`.

```
@param base_ptr: u64

struct MyStruct {
    name: ptr<cstring, u64>,     // 8-byte pointer to a nul-terminated string
    data: ptr<u32[10], u32>,     // 4-byte pointer to an array of 10 u32s
    next: ptr<Node, u64>?,       // nullable 8-byte pointer (null = 0)
}
```

The width parameter (`u32` or `u64`) specifies the binary size of the pointer value itself. If omitted, defaults to `u64`:

```
name: ptr<cstring>,              // equivalent to ptr<cstring, u64>
```

**Parse**: Read `width` bytes → interpret as unsigned integer → subtract `base_ptr` → seek to resulting buffer offset → read inner type `T` → restore cursor.

**Produce**: Lay out inner data in a trailing data region → compute pointer value as `base_ptr + data_offset` → write pointer value as `width` bytes.

A script using `ptr<T>` without a `@param base_ptr` declaration produces a compile error.

#### `relptr<T, width>` — Relative Pointer

The pointer value is an offset relative to the pointer field's own position in the buffer. No base pointer is needed.

```
struct FlatBufferTable {
    name_offset: relptr<cstring, u32>,   // offset from this field's position
}
```

**Parse**: Read `width` bytes → interpret as signed integer → add to the field's own buffer offset → seek to resulting position → read inner type `T` → restore cursor.

**Produce**: Compute `ptr_value = target_offset - field_offset` → write as `width` bytes.

#### Nullable Pointers

Appending `?` to a pointer type makes it nullable. A zero pointer value represents null:

```
next: ptr<Node, u64>?,           // null if pointer value is 0
```

**JSON representation**: `null` when the pointer is zero, otherwise the dereferenced value:

```json
{ "next": null }
{ "next": { "value": 42, "next": null } }
```

Nullable pointers serve as recursion termination guards (see §4.5).

#### JSON Representation

By default, pointers are **transparently dereferenced** — the pointer value is hidden, and only the pointed-to content appears in the JSON output:

```json
{ "name": "hello", "data": [1, 2, 3] }
```

To include the raw pointer value (for debugging or forensics), use `@show_ptr`:

```
name: ptr<cstring, u64> @show_ptr,
```

Produces:

```json
{ "name": { "_ptr": 140698832470040, "value": "hello" } }
```

In produce mode with `@show_ptr`, the `_ptr` field is ignored — the engine always computes pointer values from the layout.

#### Produce Layout

**Trailing data region (default):** All pointer targets within a struct are appended after the struct's fixed-size fields, in field declaration order. The two-pass produce engine computes:

1. **Size pass**: Total size = fixed fields + all pointed-to data sizes
2. **Write pass**: Pointer values = `base_ptr + offset_of_data_in_trailing_region`

**`@inline` annotation:** Places the pointed-to data immediately after the pointer field, rather than in a trailing region:

```
name: ptr<cstring, u64> @inline,   // data follows the pointer directly
```

This is useful for packed record streams where cache locality matters. Note that `@inline` makes the containing struct variable-sized, which moves it from Tier 1 to Tier 2 bytecode.

#### Array Compositions

Pointer types compose with arrays in two ways:

```
// Array of pointers — each element is a separate pointer
items: ptr<cstring, u64>[count],

// Pointer to array — single pointer to a contiguous array
data: ptr<u32[count], u64>,
```

Both forms support all array termination strategies:

```
names: ptr<cstring, u64>[] @until(@remaining == 0),
records: ptr<Record[] @until(r => r.type == 0), u64>,
```

### 3.5 Nullable Types

Any struct or pointer type can be made nullable with the `?` suffix:

```
field: SomeStruct?,                  // may be absent
field: ptr<T, u64>?,                 // null pointer
```

Nullable fields require a guard condition (`when`) to control when they are present during parsing:

```
@at(next_offset) when next_offset != 0 {
    next: TiffIfd?,
}
```

**JSON representation**: `null` when absent. In produce mode, the engine checks `IDataSource.HasField` to determine presence.

### 3.6 Bit Types (within `bits struct` only)

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

#### Recursive Structs (Guarded Recursion)

Structs may reference themselves (directly or mutually) provided every recursive path passes through a **termination guard**. Valid guards are:

- `when condition` on an `@at` block or field
- A non-default `match` arm
- A nullable pointer `ptr<T>?` or nullable field `T?`

```
struct TiffIfd {
    entry_count: u16be,
    entries: IfdEntry[entry_count],
    next_offset: u32be,
    @at(next_offset) when next_offset != 0 {
        next: TiffIfd,               // guarded by 'when'
    },
}

struct LinkedNode {
    value: u32,
    next: ptr<LinkedNode, u64>?,     // guarded by nullable '?'
}
```

The compiler performs cycle detection on the struct reference graph. Any cycle without a guard on every edge produces a compile error:

```
Error E030: Recursive reference from A → B → A has no termination guard.
            Add a 'when' condition, match arm, or nullable 'ptr<T>?'.
```

#### `@max_depth(N)` — Recursion Depth Limit

An optional annotation that limits the maximum recursion depth for a struct:

```
struct ResourceDirectory @max_depth(4) {
    ...
    entries: ResourceEntry[count],   // ResourceEntry may recurse back
}
```

If not specified, a default runtime depth limit of 256 applies. When the limit is reached, the engine produces a runtime error.

In produce mode, recursion depth is determined by the data source (JSON nesting), not by guard expressions.

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

### 4.5 Map Declarations

`@map` declarations define **named pure expressions** that can be reused at multiple call sites. They are compile-time inlined — the compiler substitutes the body at each call site, so there is no runtime function call overhead.

```
@map name(param1: type1, param2: type2, ...): return_type = expression
```

#### Example — PE RVA Resolution

```
@map rva_to_offset(rva: u64, sections: SectionHeader[]): u64 = {
    @let s = sections.find(s => rva >= s.virtual_address
                              && rva < s.virtual_address + s.virtual_size),
    s.pointer_to_raw_data + (rva - s.virtual_address)
}

// Usage:
@at(rva_to_offset(import_rva, sections)) {
    import_dir: ImportDirectory,
}
```

#### Constraints

- **Pure**: Bodies consist only of `@let` bindings and a final expression. No side effects, no field reads, no `@seek`/`@at`, no emitter calls.
- **No recursion**: A `@map` cannot reference itself or form cycles with other `@map` declarations. The compiler detects and rejects such cycles.
- **Type-checked**: All parameters and the return type are verified at compile time.
- **Bidirectional**: `@map` expressions work identically in both parse and produce directions since they are pure computations.

See [ADR-001](adr/ADR-001-map-pure-expressions.md) for design rationale.

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

`@skip(N)` accepts only a literal integer — it compiles to the `SkipFixed` opcode with a compact
`u16` operand for zero-overhead seeking. For dynamic skip amounts, use `@seek(@offset + expr)`.

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

### 6.6 Constant-Index Element Access

Previously parsed arrays support constant-index field access in expressions. The index must be a compile-time integer literal:

```
// Access a specific element's field
@let debug_rva = data_directories[6].virtual_address,

// Use in @at / when guards
@at(data_directories[6].virtual_address) when data_directories[6].rva != 0 {
    debug_directory: DebugDirectory,
},

// Cross-struct access (cascades through CopyChildField chain)
@let picked = header.body.items[2].value,
data: u8[picked],
```

Constant-index access works across struct boundaries at any nesting depth. The compiler propagates the required path suffixes through the struct hierarchy via a fixpoint pre-pass, so `a.b.array[i].field` works even when `a`, `b`, and the array are in different structs.

**Limitations:**
- The index must be an integer literal (variables and computed indices are not supported at compile time)
- The array must have been fully parsed before the indexed access is evaluated (no forward references)

### 6.7 Array Methods

Previously parsed arrays support search methods in expressions. These methods iterate the array and return a single value — they do NOT return new arrays.

| Method | Return Type | Description |
|--------|-------------|-------------|
| `array.find(e => pred)` | element | First element where `pred` is true. Runtime error if none found. |
| `array.find_or(e => pred, default)` | element | First matching element, or `default` if none found. |
| `array.any(e => pred)` | `bool` | True if any element matches. |
| `array.all(e => pred)` | `bool` | True if all elements match. |

Predicates are lambda expressions with a single parameter. The parameter has access to all fields of the array element type:

```
@let debug_section = sections.find(s =>
    debug_rva >= s.virtual_address &&
    debug_rva < s.virtual_address + s.virtual_size
),

has_code: bool = sections.any(s => s.characteristics & 0x20 != 0),
```

Array methods work in both parse and produce directions:
- **Parse**: iterates the in-memory parsed array
- **Produce**: iterates the data source array (e.g., JSON input)

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
| `@offsetof(field)` | Byte offset of a field from its containing struct's start |
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
- **`ptr<T>` fields**: The engine lays out pointed-to data in a trailing data region (or inline if `@inline` is annotated), computes pointer values as `base_ptr + data_offset`, and writes them. The data source provides the dereferenced values directly (e.g., `"s1": "hello"`), not pointer addresses.
- **Nullable fields (`T?`, `ptr<T>?`)**: The engine checks `IDataSource.HasField` to determine if the field is present. If absent or null, writes a zero/null value and does not recurse.
- **Recursive structs**: Recursion depth in produce mode is governed by the data source nesting (JSON depth), not by guard expressions. The `@max_depth` limit still applies as a safety net.

### 11.3 Round-Trip Fidelity

BinScript guarantees **semantic** round-trip fidelity:

```
parse(produce(parse(data))) == parse(data)
```

The binary output of `produce` may differ from the original binary input (e.g., pointer layouts may be canonicalized to trailing order, padding may differ), but the structured representation is preserved.

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
- Conditional compilation (`@if`)
- Bitwise field ordering options (MSB-first vs LSB-first)
- Circular structure support (graphs with cycles) — see [ADR-002](adr/ADR-002-circular-structures-deferred.md)
- `@max_visits` annotation for graph-mode parsing
- User-defined functions (if `@map` proves insufficient)
