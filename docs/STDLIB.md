# BinScript Standard Library

## Overview

BinScript ships with a standard library of `.bsx` definitions for common binary formats. These serve three purposes:

1. **User utility** — ready-to-use format definitions for common tasks.
2. **Test suite** — each definition is tested against real binary samples.
3. **Language validation** — the formats collectively exercise every language feature.

## Formats

### PE/COFF (`pe.bsx`)

**Portable Executable** format used by Windows executables and DLLs.
Full parse path from DOS header to PDB path via CodeView debug data.

**Features exercised:**
- Nested structs (DosHeader → PE signature → CoffHeader → OptionalHeader → Sections)
- `@seek(dos_header.e_lfanew)` for PE header location
- `@let num_sections = coff_header.number_of_sections` for cross-struct reference
- `@map rva_to_offset(rva, sections)` — RVA-to-file-offset conversion via `sections.find()`
- `enum Machine : u16` for machine types (30 variants)
- `bits struct CoffCharacteristics : u16`, `DllCharacteristics : u16`, `SectionCharacteristics : u32`
- `match(magic)` for PE32 vs PE32+ optional header
- `@at(rva_to_offset(...))` with `when` guards for optional data directories
- `@until_sentinel(e => e.import_lookup_table_rva == 0)` for import table termination
- `@max_depth(4)` on recursive `ResourceDirectory`
- `@coverage(partial)` — covers headers, exports, imports, resources, debug; not raw section data
- Parameterized structs: `ImportDescriptor(magic, sections)`, `ResourceDirectory(base_offset)`

**Test samples:**
- `tests/samples/pe/tiny_x64.exe` — minimal valid x64 PE
- `tests/samples/pe/tiny_x86.exe` — minimal valid x86 PE
- `tests/samples/pe/tiny_dll.dll` — minimal valid DLL

**Structs defined:**
```
PeFile (root), DosHeader, CoffHeader, OptionalHeader, OptionalHeader32,
OptionalHeader64, DataDirectory, SectionHeader, ExportDirectory,
ImportDescriptor, ImportLookupEntry32, ImportLookupEntry64,
ResourceDirectory, ResourceEntry, ResourceDataEntry,
DebugDirectoryEntry, CvInfoPdb70
```

**Bits structs defined:**
```
CoffCharacteristics, DllCharacteristics, SectionCharacteristics
```

**Enums defined:**
```
Machine, Subsystem, DebugType
```

**Maps defined:**
```
rva_to_offset(rva: u32, sections: SectionHeader[]): u32
```

---

### ZIP (`zip.bsx`)

**ZIP archive** format.

**Features exercised:**
- `@at(@input_size - 22)` for end-of-central-directory record
- `@at(eocd.central_dir_offset)` for central directory
- `@at(central_dir[_index].local_header_offset)` for local file entries
- `@input_size` built-in
- `_index` for indexed array access
- `@derived crc32` and size fields
- Count-based arrays from EOCD's `total_entries`
- Magic value assertions (`0x04034b50`, `0x02014b50`, `0x06054b50`)
- `bytes[compressed_size]` for opaque data blobs

**Test samples:**
- `tests/samples/zip/hello.zip` — 2-3 small text files
- `tests/samples/zip/single.zip` — single file, no compression (stored)

**Structs defined:**
```
ZipFile, EndOfCentralDir, CentralDirEntry, LocalFileHeader, DataDescriptor
```

**Enums defined:**
```
CompressionMethod
```

---

### PNG (`png.bsx`)

**Portable Network Graphics** image format.
Full chunk coverage with typed body structs for all common chunk types.

**Features exercised:**
- Condition-based arrays: `chunks: PngChunk[] @until(@remaining == 0)`
- `match(chunk_type)` for 17 chunk-type dispatch arms
- `enum ColorType : u8`, `InterlaceMethod : u8`, `SrgbRenderingIntent : u8`
- Parameterized structs: `PlteBody(data_length)`, `TextBody(data_length)`, etc.
- `fixed_string[4]` for chunk type
- `bytes[length]` for raw data blobs (IDAT, eXIf)
- CRC-32 field per chunk
- Nested IHDR, PLTE, tEXt, zTXt, iTXt, pHYs, tIME, gAMA, cHRM, sRGB, iCCP structures

**Test samples:**
- `tests/samples/png/rgb_8x8.png` — tiny 8×8 RGB PNG
- `tests/samples/png/indexed_4x4.png` — 4×4 with palette

**Structs defined:**
```
PngFile (root), PngChunk, IhdrBody, PlteBody, PaletteEntry,
TextBody, ZtxtBody, ItxtBody, PhysBody, TimeBody, GamaBody,
ChrmBody, SrgbBody, IccpBody
```

**Enums defined:**
```
ColorType, InterlaceMethod, SrgbRenderingIntent
```

---

### ELF (`elf.bsx`)

**Executable and Linkable Format** used by Linux/Unix.
Supports both ELF32 and ELF64 via `match(ei_class)` dispatch. Covers headers,
program headers, section headers, string tables, symbol tables, dynamic sections,
notes, and relocations.

**Features exercised:**
- `match(ident.ei_class)` for 32-bit vs 64-bit body dispatch (ELFCLASS64 listed first)
- `@at(e_shoff)` and `@at(e_phoff)` for section/program header tables
- `@at(section_headers[e_shstrndx].sh_offset)` for string table resolution
- Count-based arrays from `e_shnum` and `e_phnum`
- `bits struct PhdrFlags : u32`, `ShFlags32 : u32`, `ShFlags64 : u64` for permission flags
- `enum ElfMachine : u16` (10 variants), `PhdrType : u32`, `ShType : u32`, `DynTag : i64`
- `@coverage(partial)` — covers all headers and metadata; not raw section contents

**Test samples:**
- `tests/samples/elf/hello_x64.elf` — minimal x64 ELF

**Structs defined:**
```
ElfFile (root), ElfIdent, ElfBody32, ElfBody64,
ProgramHeader32, ProgramHeader64, SectionHeader32, SectionHeader64,
DynamicEntry32, DynamicEntry64, Symbol32, Symbol64,
NoteEntry, Rel32, Rela32, Rel64, Rela64
```

**Bits structs defined:**
```
PhdrFlags, ShFlags32, ShFlags64
```

**Enums defined:**
```
ElfClass, ElfData, ElfOsAbi, ElfType, ElfMachine,
PhdrType, ShType, DynTag
```

---

### BMP (`bmp.bsx`)

**Windows Bitmap** format.

**Features exercised:**
- **Tier 1 fast-path validation**: the BMP file header and DIB header are flat, fixed-size structs with no conditionals — perfect for testing the Tier 1 bytecode path.
- Simple `@seek(bfOffBits)` to pixel data
- `match(biSize)` for different DIB header versions (BITMAPINFOHEADER vs BITMAPV4HEADER vs BITMAPV5HEADER)
- `bytes[pixel_data_size]` for raw pixel data
- Simple computed sizes: `pixel_data_size = biSizeImage` or calculated from dimensions

**Test samples:**
- `tests/samples/bmp/4x4_24bit.bmp` — tiny 24-bit BMP
- `tests/samples/bmp/4x4_8bit.bmp` — tiny 8-bit indexed BMP with palette

**Structs defined:**
```
BmpFile, BmpFileHeader, BitmapInfoHeader, BitmapV4Header,
RgbQuad, PixelData
```

---

### GIF (`gif.bsx`)

**Graphics Interchange Format**.

**Features exercised:**
- `match(version)` on "87a" vs "89a"
- Sub-block arrays with sentinel terminator (`0x00` block terminator)
- `@until_sentinel(block => block.size == 0)` for sub-block chains
- Extension blocks dispatched by `match(extension_label)`
- `bits struct PackedFields : u8` for packed GIF descriptors
- `cstring`-like patterns (nul-terminated sub-blocks)
- Count-based palette: `palette: RgbEntry[1 << (packed.color_table_size + 1)]` — expression in array count

**Test samples:**
- `tests/samples/gif/2frame.gif` — minimal 2-frame animated GIF
- `tests/samples/gif/single.gif` — single-frame GIF87a

**Structs defined:**
```
GifFile, GifHeader, LogicalScreenDescriptor, GlobalColorTable, RgbEntry,
ImageDescriptor, LocalColorTable, ImageData, SubBlock,
GraphicControlExtension, ApplicationExtension, CommentExtension,
PackedScreenDescriptor (bits), PackedImageDescriptor (bits)
```

**Enums defined:**
```
GifVersion, DisposalMethod, BlockLabel
```

---

### JPG/JPEG (`jpg.bsx`)

**JPEG/JFIF** image format.
Covers SOI marker, all standard segment types, JFIF APP0, EXIF APP1, frame
headers, Huffman/quantization tables, and comments.

**Features exercised:**
- `@default_endian(big)` — JPEG is big-endian throughout
- `@until(s => s.marker == 0xFFD9 || s.marker == 0xFFDA)` for segment termination
- `match(marker)` with 10 dispatch arms for marker types
- `enum DensityUnit : u8` for JFIF density units
- `fixed_string[5]` for JFIF identifier, `fixed_string[6]` for EXIF header
- Parameterized length for variable-size segments: `bytes[length - 2]`
- Count-based component arrays in SOF and SOS markers
- `@coverage(partial)` — covers all markers/headers; not entropy-coded scan data

**Test samples:**
- *(test samples to be added)*

**Structs defined:**
```
JpgFile (root), JpgSegment, JfifApp0, ExifApp1, SofBaseline,
SofProgressive, FrameComponent, DhtMarker, DqtMarker, DriMarker,
SosMarker, ScanComponent, ComMarker, GenericMarkerBody
```

**Enums defined:**
```
DensityUnit
```

---

### PCAP (`pcap.bsx`)

**Packet capture** format used by tcpdump, Wireshark, and network analysis tools.
Parses the little-endian variant (magic `0xA1B2C3D4`).

**Features exercised:**
- `@greedy` arrays — `packets: PacketRecord[] @greedy` reads packet records until the input is exhausted or a truncated record causes a parse error (the only stdlib example of `@greedy`)
- `enum LinkType : u32` for link-layer types (Ethernet, Raw IP, etc.)
- `bytes[incl_len]` for variable-length packet payloads
- Graceful handling of truncated captures via `@greedy` error recovery

**Test samples:**
- `tests/samples/pcap/TNS_Oracle1.pcap` — 78 Ethernet packets (Oracle TNS traffic)

**Structs defined:**
```
PcapFile (root), PcapHeader, PacketRecord
```

**Enums defined:**
```
LinkType
```

---

### Memory Examples (`memory/`)

In-memory structure definitions demonstrating `ptr<T>` pointer support.
These require `@param base_ptr: u64` at runtime.

#### `c_strings.bsx` — Two-String Struct
The canonical `{ char *s1; char *s2; }` example. Two `ptr<cstring, u64>` fields
that dereference to nul-terminated strings. Demonstrates transparent pointer
dereferencing and trailing data region layout.

#### `linked_list.bsx` — Singly Linked List
Recursive `struct Node { i32 value; ptr<Node, u64>? next; }`. Demonstrates
guarded recursion via nullable `ptr<T>?` — null pointer terminates the chain.
JSON output is nested objects with `null` leaf.

#### `win32_startup.bsx` — STARTUPINFOW
Windows `STARTUPINFOW` structure with `ptr<cstring @encoding(utf16le), u64>?`
for wide-string fields. Exercises pointer types, nullable, encoding annotations,
and `bits struct StartupFlags : u32`.

**Features exercised (collectively):**
- `ptr<T, u64>` — first-class pointer types
- `ptr<T>?` — nullable pointers (null = zero)
- `@param base_ptr: u64` — runtime base pointer
- `@encoding(utf16le)` — wide string encoding on pointed-to type
- `bits struct` for Windows flags
- Guarded recursion via nullable pointer termination

## Feature Coverage Matrix

| Feature | PE | ZIP | PNG | ELF | BMP | GIF | JPG | PCAP | mem/ |
|---------|:--:|:---:|:---:|:---:|:---:|:---:|:---:|:----:|:----:|
| Nested structs | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `@seek` / `@at` | ✓ | ✓ | | ✓ | ✓ | | | | |
| `@let` cross-ref | ✓ | | | | | | | | |
| `@param` runtime | | | | | | | | | ✓ |
| `@map` pure expr | ✓ | | | | | | | | |
| `enum` | ✓ | ✓ | ✓ | ✓ | | ✓ | ✓ | ✓ | |
| `bits struct` | ✓ | ✓ | | ✓ | | ✓ | | | ✓ |
| `match` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | | |
| `match` with guards | ✓ | | | | | | | | |
| `when` guards | ✓ | ✓ | | | | | | | |
| Count-based arrays | ✓ | ✓ | | ✓ | | | ✓ | | |
| `@until` arrays | | | ✓ | | | | ✓ | | |
| `@until_sentinel` | ✓ | | | | | ✓ | | | |
| `@greedy` arrays | | | | | | | | ✓ | |
| `@derived` / `@crc32` | | ✓ | ✓ | | | | | | |
| `@align` | | | | | | | | | |
| `@coverage(partial)` | ✓ | | ✓ | ✓ | | | ✓ | | |
| `@input_size` | | ✓ | | | | | | | |
| `_index` | | ✓ | | | | | | | |
| `fixed_string` | ✓ | | ✓ | ✓ | | | ✓ | | |
| `cstring` | ✓ | | | | | | | | ✓ |
| `bytes[]` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| `ptr<T>` | | | | | | | | | ✓ |
| `ptr<T>?` nullable | | | | | | | | | ✓ |
| `@encoding` | | | | | | | | | ✓ |
| `@max_depth` | ✓ | | | | | | | | |
| Parameterized structs | ✓ | | ✓ | | | | | | |
| Tier 1 flat-path | | | | | ✓ | | | | |
| Expressions in counts | | | | | | ✓ | | | |
| Guarded recursion | ✓ | | | | | | | | ✓ |
| `.find()` array method | ✓ | | | | | | | | |

## Test Strategy

For each format:

1. **Parse test**: Read the binary sample, parse with the `.bsx` script, compare JSON output against the expected JSON in `tests/expected/`.
2. **Round-trip test**: Parse binary → JSON → produce binary → parse again → compare JSON outputs. They must be identical.
3. **Error test**: Feed malformed data (truncated, wrong magic) and verify appropriate error messages.
4. **Coverage test**: Verify that `CoverageReport` matches expectations (full coverage for simple formats, declared partial for quick-header scripts).
