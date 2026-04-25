# 11. Modules and Imports

As your scripts grow, you'll want to split them into reusable modules. BinScript supports a simple import system.

## Importing Modules

```
@import "pe/dos_header"
@import "pe/coff_header"

@root struct PeFile {
    dos: DosHeader,       // defined in pe/dos_header
    @seek(dos.e_lfanew)
    pe_sig: u32 = 0x00004550,
    coff: CoffHeader,     // defined in pe/coff_header
}
```

### 🔑 How Imports Work

1. Module names are strings (e.g., `"pe/dos_header"`)
2. The host application provides a **module resolver** that maps names to source text
3. All imported definitions (structs, enums, bits structs, constants, maps) are available in the importing file
4. Imported bytecode is bundled into the final compiled program — no runtime dependency

## Module Organization

A good convention is to organize modules by format:

```
stdlib/
├── pe.bsx              // Full PE parser (imports sub-modules)
├── pe/
│   ├── dos_header.bsx
│   ├── coff_header.bsx
│   └── optional_header.bsx
├── png.bsx
├── zip.bsx
└── elf.bsx
```

## Import Rules

- **No circular imports** — Module A cannot import B if B imports A
- **Name uniqueness** — If two modules define the same name (struct, enum, etc.), it's a compile error
- **Import chains** — If A imports B and B imports C, A can use types from both B and C
- **No selective imports** — You get everything from an imported module (no `import { X } from`)

## Structuring Reusable Types

Put commonly used types in shared modules:

```
// common/checksums.bsx
@map verify_crc32(data: bytes, expected: u32): bool =
    @crc32(data) == expected

// common/types.bsx
enum Endianness : u8 {
    LITTLE = 0,
    BIG = 1,
}
```

Then import where needed:

```
@import "common/checksums"
@import "common/types"

@root struct MyFormat {
    endian: Endianness,
    data: bytes[16],
    checksum: u32,
    @assert(verify_crc32(data, checksum), "checksum failed"),
}
```

## The Standard Library

BinScript includes a standard library of real-world format descriptions in `stdlib/`:

| Module | Format | Description |
|--------|--------|-------------|
| `pe.bsx` | Portable Executable | Windows executables and DLLs |
| `elf.bsx` | ELF | Linux/Unix executables |
| `png.bsx` | PNG | Portable Network Graphics |
| `zip.bsx` | ZIP | Archive format |
| `bmp.bsx` | BMP | Bitmap images |
| `gif.bsx` | GIF | Graphics Interchange Format |
| `jpg.bsx` | JPEG | JPEG/JFIF images |

These serve as both documentation and test fixtures — they're compiled and tested against real binaries.

## What You Learned

- `@import "module"` brings in types from other files
- Module resolution is handled by the host application
- All definitions are merged at compile time
- No circular imports; names must be unique across modules
- Standard library provides ready-made format descriptions

---

**Congratulations!** You've covered all core language features. Continue with the real-world walkthroughs to see everything come together.

**Next**: [Walkthrough: TCP/IP Packet →](12-walkthrough-tcp.md)
