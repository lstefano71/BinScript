# BinScript

**A high-performance binary structure parser and producer with a human-readable description language.**

BinScript lets you describe binary formats — PE executables, ZIP archives, PNG images, ELF binaries, Parquet files, and more — using a concise, declarative script language. It compiles scripts into a fast bytecode that can parse binary data into structured output (JSON initially) and produce binary data from structured input.

## Key Features

- **Declarative script language** (`.bsx`) with embedded expressions for complex formats
- **Two-phase architecture**: compile once, parse many times (like a prepared statement)
- **Bidirectional**: parse binary → JSON, produce JSON → binary with the same script
- **High performance**: tight bytecode, caller-owns-buffer, persistable compiled scripts
- **C-ABI DLL** (NativeAOT): usable from any language with an FFI
- **Extensible emitters**: JSON first, pluggable interface for future formats
- **Standard library**: ships with `.bsx` definitions for PE, ZIP, PNG, ELF, BMP, GIF

## Architecture

```
C# Core Library (.NET 10, C# 14, NativeAOT)
├── BinScript.Core         — compiler, bytecode VM, IR
├── BinScript.Emitters.Json — IResultEmitter / IDataSource for JSON
├── BinScript.Interop       — C-ABI shim (NativeAOT exported functions)
└── BinScript.Tests         — unit + integration tests with real binary samples
```

## Quick Example

```
// pe_quick.bsx — parse just the DOS + COFF headers
@default_endian(little)
@coverage(partial)

@root struct PeQuickHeader {
    dos: DosHeader,
    @seek(dos.e_lfanew)
    pe_sig: u32 = 0x00004550,
    coff: CoffHeader,
}

struct DosHeader {
    e_magic: u16 = 0x5A4D,
    e_cblp: u16,
    e_cp: u16,
    // ... more fields ...
    e_lfanew: u32,
}

struct CoffHeader {
    machine: Machine,
    number_of_sections: u16,
    time_date_stamp: u32,
    pointer_to_symbol_table: u32,
    number_of_symbols: u32,
    size_of_optional_header: u16,
    characteristics: Characteristics,
}

enum Machine : u16 {
    I386  = 0x014C,
    AMD64 = 0x8664,
    ARM64 = 0xAA64,
}

bits struct Characteristics : u16 {
    relocs_stripped: bit,
    executable_image: bit,
    line_nums_stripped: bit,
    local_syms_stripped: bit,
    aggressive_ws_trim: bit,
    large_address_aware: bit,
    _reserved: bit,
    bytes_reversed_lo: bit,
    _32bit_machine: bit,
    debug_stripped: bit,
    removable_run_from_swap: bit,
    net_run_from_swap: bit,
    system: bit,
    dll: bit,
    up_system_only: bit,
    bytes_reversed_hi: bit,
}
```

## Documentation

- [Product Requirements Document](docs/PRD.md)
- [Language Specification](docs/LANGUAGE_SPEC.md)
- [Architecture & Design](docs/ARCHITECTURE.md)
- [C-ABI Reference](docs/C_ABI.md)
- [Bytecode Format](docs/BYTECODE.md)
- [Standard Library](docs/STDLIB.md)
- [Implementation Plan](docs/IMPLEMENTATION_PLAN.md)

## Building

Requires .NET 10 SDK with C# 14 support.

```bash
dotnet build src/BinScript.sln
dotnet test src/BinScript.Tests
dotnet publish src/BinScript.Interop -c Release  # produces NativeAOT DLL
```

## License

TBD
