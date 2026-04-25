# 13. Walkthrough: WAV Audio

This walkthrough parses a WAV audio file — a RIFF-based chunk format with nested structures, alignment padding, and conditional fields based on audio format.

## WAV Structure

```
┌─────────────────────────────┐
│  RIFF Header (12 bytes)     │
├─────────────────────────────┤
│  "fmt " Chunk               │  Format description
├─────────────────────────────┤
│  ["fact" Chunk]              │  Optional (compressed audio)
├─────────────────────────────┤
│  "data" Chunk               │  Audio samples
└─────────────────────────────┘
```

WAV is little-endian with chunk IDs stored as 4-byte ASCII strings.

## The Complete Script

```
@default_endian(little)

// ─── RIFF Container ─────────────────────────────────────────────

@root
@coverage(partial)
struct WavFile {
    riff_id: fixed_string[4] = "RIFF",
    file_size: u32,
    wave_id: fixed_string[4] = "WAVE",
    chunks: WavChunk[] @until(@remaining == 0),
}

// ─── Chunk Dispatch ─────────────────────────────────────────────

struct WavChunk {
    chunk_id: fixed_string[4],
    chunk_size: u32,
    body: match(chunk_id) {
        s when s == "fmt " => FmtChunk,
        s when s == "data" => DataChunk,
        s when s == "fact" => FactChunk,
        _ => UnknownChunk,
    },
    @align(2)
}

// ─── Format Chunk ───────────────────────────────────────────────

enum AudioFormat : u16 {
    PCM         = 0x0001,
    IEEE_FLOAT  = 0x0003,
    ALAW        = 0x0006,
    MULAW       = 0x0007,
    EXTENSIBLE  = 0xFFFE,
}

struct FmtChunk {
    audio_format: AudioFormat,
    num_channels: u16,
    sample_rate: u32,
    byte_rate: u32,
    block_align: u16,
    bits_per_sample: u16,
    @derived bytes_per_sample: u16 = bits_per_sample / 8 @hidden,
    @derived is_stereo: bool = num_channels == 2,
}

// ─── Data Chunk ─────────────────────────────────────────────────

struct DataChunk {
    // chunk_size was read in WavChunk, but the data chunk
    // just contains raw samples — we read what's available
    samples: bytes[@remaining],
}

// ─── Fact Chunk ─────────────────────────────────────────────────

struct FactChunk {
    sample_count: u32,
}

// ─── Unknown Chunk ──────────────────────────────────────────────

struct UnknownChunk {
    data: bytes[@remaining],
}
```

## Key Techniques Used

### String Matching with Guards

WAV chunk IDs are 4-byte strings (note the space in `"fmt "`). We use guard arms to match:

```
body: match(chunk_id) {
    s when s == "fmt " => FmtChunk,
    s when s == "data" => DataChunk,
    ...
}
```

### Enum for Audio Format

The `AudioFormat` enum gives meaningful names to format codes:

```json
{
    "audio_format": "PCM",
    "num_channels": 2,
    "sample_rate": 44100
}
```

### `@align(2)` for Chunk Padding

RIFF requires chunks to start at even byte offsets. The `@align(2)` after each chunk's body ensures proper alignment:

```
struct WavChunk {
    ...
    body: match(...) { ... },
    @align(2)    // pad to 2-byte boundary
}
```

### `@coverage(partial)`

We're not parsing every possible WAV chunk type, so `@coverage(partial)` suppresses incomplete-parsing warnings.

### Derived Fields for Readability

```
@derived bytes_per_sample: u16 = bits_per_sample / 8 @hidden,
@derived is_stereo: bool = num_channels == 2,
```

`bytes_per_sample` is hidden (internal use only), while `is_stereo` is included in the output for convenience.

## Sample Output

Parsing a typical WAV file:

```json
{
    "riff_id": "RIFF",
    "file_size": 176444,
    "wave_id": "WAVE",
    "chunks": [
        {
            "chunk_id": "fmt ",
            "chunk_size": 16,
            "body": {
                "audio_format": "PCM",
                "num_channels": 2,
                "sample_rate": 44100,
                "byte_rate": 176400,
                "block_align": 4,
                "bits_per_sample": 16,
                "is_stereo": true
            }
        },
        {
            "chunk_id": "data",
            "chunk_size": 176400,
            "body": {
                "samples": "..."
            }
        }
    ]
}
```

## What This Demonstrates

- Chunk-based formats with string ID dispatch
- Guard arms for string matching
- `@align` for format-specific padding rules
- `@coverage(partial)` for incomplete parsers
- `@derived @hidden` for internal computations
- `@until(@remaining == 0)` for reading all chunks
- Enums for human-readable output

**Next**: [Walkthrough: PNG Image →](14-walkthrough-png.md)
