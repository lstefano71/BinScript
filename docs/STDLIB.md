# BinScript Standard Library

## Overview

BinScript ships with a standard library of `.bsx` definitions for common binary formats. These serve three purposes:

1. **User utility** — ready-to-use format definitions for common tasks.
2. **Test suite** — each definition is tested against real binary samples.
3. **Language validation** — the formats collectively exercise every language feature.

## Formats

### PE/COFF (`pe.bsx`)

**Portable Executable** format used by Windows executables and DLLs.

**Features exercised:**
- Nested structs (DOS header → PE signature → COFF header → Optional header → Sections)
- `@seek(dos_header.e_lfanew)` for PE header location
- `@let arch = coff_header.machine` for cross-struct reference
- `enum Machine : u16` for machine types
- `bits struct Characteristics : u16` for PE flags
- `match(arch)` for 32-bit vs 64-bit optional header
- `@align(file_alignment)` for section alignment
- `@coverage(partial)` for quick-header-only mode
- Parameterized structs: `OptionalHeader(arch)`, `Section(arch, file_alignment)`

**Test samples:**
- `tests/samples/pe/tiny_x64.exe` — minimal valid x64 PE
- `tests/samples/pe/tiny_x86.exe` — minimal valid x86 PE
- `tests/samples/pe/tiny_dll.dll` — minimal valid DLL

**Structs defined:**
```
DosHeader, PeFile, CoffHeader, OptionalHeader32, OptionalHeader64,
DataDirectory, SectionHeader, ImportDirectory, ExportDirectory,
ResourceDirectory, Characteristics (bits), DllCharacteristics (bits),
SectionFlags (bits)
```

**Enums defined:**
```
Machine, OptionalHeaderMagic, SubSystem, DataDirectoryIndex
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

**Features exercised:**
- Condition-based arrays: `chunks: PngChunk[] @until(@remaining == 0)`
- Alternative: sentinel-based: stop when chunk type is IEND
- `match(chunk_type)` for chunk-type dispatch
- `enum PngChunkType : fixed_string[4]`
- `@derived` CRC-32 field using `@crc32(chunk_type, data)`
- `@derived length` using `@sizeof(data)`
- `fixed_string[4]` for chunk type
- `bytes[length]` for chunk data
- Nested IHDR, PLTE, tEXt structures

**Test samples:**
- `tests/samples/png/rgb_8x8.png` — tiny 8×8 RGB PNG
- `tests/samples/png/indexed_4x4.png` — 4×4 with palette

**Structs defined:**
```
PngFile, PngSignature, PngChunk, IhdrBody, PlteBody, PlteEntry,
TextBody, PhysBody
```

**Enums defined:**
```
PngChunkType, ColorType, InterlaceMethod
```

---

### ELF (`elf.bsx`)

**Executable and Linkable Format** used by Linux/Unix.

**Features exercised:**
- `@param bitness: u32` (compile-time parameter: 32 or 64)
- Endian switching: `@default_endian` determined at runtime from `e_ident` byte
- `match(bitness)` for 32-bit vs 64-bit field sizes throughout
- `@at(e_shoff)` and `@at(e_phoff)` for section/program header tables
- Count-based arrays from `e_shnum` and `e_phnum`
- `fixed_string[16]` for `e_ident` magic
- `bytes[@remaining]` for section data

**Test samples:**
- `tests/samples/elf/hello_x64.elf` — minimal x64 ELF
- `tests/samples/elf/hello_arm64.elf` — minimal ARM64 ELF (if available)

**Structs defined:**
```
ElfFile, ElfIdent, Elf32Header, Elf64Header, Elf32ProgramHeader,
Elf64ProgramHeader, Elf32SectionHeader, Elf64SectionHeader
```

**Enums defined:**
```
ElfClass, ElfData, ElfOsAbi, ElfType, ElfMachine, ProgramHeaderType,
SectionHeaderType
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

## Feature Coverage Matrix

| Feature | PE | ZIP | PNG | ELF | BMP | GIF |
|---------|:--:|:---:|:---:|:---:|:---:|:---:|
| Nested structs | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `@seek` / `@at` | ✓ | ✓ | | ✓ | ✓ | |
| `@let` cross-ref | ✓ | | | | | |
| `@param` compile-time | | | | ✓ | | |
| `enum` | ✓ | ✓ | ✓ | ✓ | | ✓ |
| `bits struct` | ✓ | | | | | ✓ |
| `match` | ✓ | | ✓ | ✓ | ✓ | ✓ |
| `match` with guards | ✓ | | | ✓ | | |
| Count-based arrays | ✓ | ✓ | | ✓ | | |
| `@until` arrays | | | ✓ | | | |
| `@until_sentinel` | | | | | | ✓ |
| `@greedy` arrays | | | | | | |
| `@derived` / `@crc32` | | ✓ | ✓ | | | |
| `@align` | ✓ | | | | | |
| `@coverage(partial)` | ✓ | | | | | |
| `@input_size` | | ✓ | | | | |
| `_index` | | ✓ | | | | |
| `fixed_string` | | | ✓ | ✓ | | |
| `cstring` | | | | | | |
| `bytes[]` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Tier 1 flat-path | | | | | ✓ | |
| Expressions in counts | | | | | | ✓ |

## Test Strategy

For each format:

1. **Parse test**: Read the binary sample, parse with the `.bsx` script, compare JSON output against the expected JSON in `tests/expected/`.
2. **Round-trip test**: Parse binary → JSON → produce binary → parse again → compare JSON outputs. They must be identical.
3. **Error test**: Feed malformed data (truncated, wrong magic) and verify appropriate error messages.
4. **Coverage test**: Verify that `CoverageReport` matches expectations (full coverage for simple formats, declared partial for quick-header scripts).
