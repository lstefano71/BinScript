# 7. Seeking and Alignment

Binary formats rarely lay data out sequentially. Headers point to distant offsets, structures are padded to alignment boundaries, and reserved regions must be skipped. BinScript provides several directives for navigating the byte stream.

## `@seek` — Jump to an Absolute Offset

```
@default_endian(little)

@root struct PeFile {
    dos: DosHeader,
    @seek(dos.e_lfanew)
    pe_signature: u32 = 0x00004550,
}

struct DosHeader {
    e_magic: u16 = 0x5A4D,
    @skip(58)
    e_lfanew: u32,
}
```

### 🔑 How `@seek` Works

`@seek(expr)` moves the read cursor to an absolute byte offset. The expression can reference any previously parsed field. This is essential for formats like PE where the DOS header contains a pointer to the PE header at a variable offset.

After the seek, parsing continues sequentially from the new position.

## `@at` Blocks — Seek, Read, Return

Sometimes you need to read data at a remote offset without permanently moving the cursor:

```
@root struct FileWithIndex {
    index_offset: u32,
    data_size: u32,
    data: bytes[data_size],
    @at(index_offset) {
        index: IndexTable,
    }
}
```

### 🔑 `@at` vs `@seek`

| | `@seek` | `@at { }` |
|---|---------|-----------|
| Cursor after | Stays at new position | Returns to original position |
| Use when | Reading sequentially from a new offset | Peeking at a remote structure |

### `@at` with Guards

`@at` blocks can be conditional:

```
@at(extra_offset) when extra_offset != 0 {
    extra_data: ExtraInfo,
}
```

The block is only entered if the guard expression is true.

## `@skip` — Skip a Fixed Number of Bytes

```
struct DosHeader {
    e_magic: u16,
    @skip(58)
    e_lfanew: u32,
}
```

`@skip(N)` advances the cursor by exactly N bytes without reading them. The argument must be a literal integer (not an expression) — this compiles to a single fast opcode.

⚠️ For dynamic skip amounts, use `@seek(@offset + expr)` instead.

## `@align` — Pad to an Alignment Boundary

```
struct PaddedRecord {
    tag: u8,
    @align(4)
    value: u32,
}
```

`@align(N)` skips bytes until the current offset is a multiple of N. If already aligned, no bytes are skipped. Unlike `@skip`, the argument can be any expression.

## Built-in Offset Functions

```
@root struct Header {
    magic: u32,
    @let current_pos = @offset           // current cursor position
    @let bytes_left = @remaining         // bytes remaining in input
    @let total_input = @input_size       // total input size
    @derived magic_offset: u32 = @offsetof(magic),  // offset of the magic field
}
```

| Built-in | Returns |
|----------|---------|
| `@offset` | Current cursor position (byte offset from start) |
| `@remaining` | Bytes remaining in the input |
| `@input_size` | Total size of the input data |
| `@offsetof(field)` | Byte offset where a field was read |
| `@sizeof(field)` | Size in bytes of a parsed field |

## Practical Example: RIFF Container

RIFF (used by WAV, AVI) has chunks at variable offsets, each padded to 2-byte alignment:

```
@default_endian(little)

@root struct RiffFile {
    riff_magic: u32 = 0x46464952,      // "RIFF"
    file_size: u32,
    form_type: u32,
    chunks: RiffChunk[] @until(@remaining == 0),
}

struct RiffChunk {
    chunk_id: u32,
    chunk_size: u32,
    data: bytes[chunk_size],
    @align(2)
}
```

Each chunk's data is followed by alignment padding so the next chunk starts at an even offset.

## What You Learned

- `@seek(offset)` jumps to an absolute position
- `@at(offset) { }` reads at a remote offset then returns
- `@at ... when guard` for conditional remote reads
- `@skip(N)` advances by a fixed literal count
- `@align(N)` pads to an alignment boundary
- `@offset`, `@remaining`, `@input_size`, `@offsetof`, `@sizeof` for position queries

**Next**: [Pointers →](08-pointers.md)
