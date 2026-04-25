# BinScript

**A high-performance binary structure parser and producer with a human-readable description language.**

BinScript lets you describe binary formats — PE executables, ZIP archives, PNG images, ELF binaries, and more — using a concise, declarative script language. Scripts compile to fast bytecode that can **parse** binary data into structured output (JSON) and **produce** binary data from structured input, using the same script for both directions.

## Key Features

- **Declarative `.bsx` script language** with expressions, enums, bitfields, pointers, match, arrays, and imports
- **Compile-once, run-many**: persist bytecode to `.bsc` files for fast repeated execution
- **Bidirectional**: parse binary → JSON, produce JSON → binary with the same script
- **C-ABI NativeAOT DLL**: call from C, C++, Python, Go, Rust, or any FFI-capable language
- **Pluggable emitters**: implement `IResultEmitter` / `IDataSource` for custom output formats
- **Standard library**: ready-to-use `.bsx` definitions for PE, ELF, ZIP, PNG, BMP, GIF, WAV, JPEG

## Quick Example

```bsx
// pe_quick.bsx — parse the DOS + COFF headers
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
    e_cblp:  u16,
    e_cp:    u16,
    // ... more fields ...
    e_lfanew: u32,
}

struct CoffHeader {
    machine:                Machine,
    number_of_sections:     u16,
    time_date_stamp:        u32,
    pointer_to_symbol_table: u32,
    number_of_symbols:      u32,
    size_of_optional_header: u16,
    characteristics:        CoffCharacteristics,
}

enum Machine : u16 {
    I386  = 0x014C,
    AMD64 = 0x8664,
    ARM64 = 0xAA64,
}

bits struct CoffCharacteristics : u16 {
    relocs_stripped:      bit,
    executable_image:     bit,
    line_nums_stripped:   bit,
    local_syms_stripped:  bit,
    aggressive_ws_trim:   bit,
    large_address_aware:  bit,
    _reserved:            bit,
    bytes_reversed_lo:    bit,
    _32bit_machine:       bit,
    debug_stripped:       bit,
    removable_run_from_swap: bit,
    net_run_from_swap:    bit,
    system:               bit,
    dll:                  bit,
    up_system_only:       bit,
    bytes_reversed_hi:    bit,
}
```

## Architecture

```
C# (.NET 10, C# 14, NativeAOT)
├── BinScript.Core          — compiler pipeline, bytecode VM (Parse + Produce engines)
├── BinScript.Emitters.Json — JSON IResultEmitter / IDataSource implementations
├── BinScript.Interop       — NativeAOT C-ABI DLL (flat C functions via [UnmanagedCallersOnly])
└── BinScript.Tests         — unit + integration tests with real binary samples
```

Compilation pipeline: `Source → Lexer → Parser → TypeResolver → SemanticAnalyzer → BytecodeEmitter → BytecodeProgram`

The runtime has two engines: **ParseEngine** (binary → structured output via `IResultEmitter`) and **ProduceEngine** (structured input via `IDataSource` → binary). Bytecode programs can be serialized to `.bsc` files and reloaded without recompilation.

## Tools

- **[bsxtool](tools/bsxtool/README.md)** — Python CLI: compile `.bsx`, parse binaries, produce from JSON, disassemble bytecode, hex-dump `.bsc` files.
- **[tools/examples/](tools/examples/)** — Standalone integration examples:
  - **C** — parse + round-trip produce BMP via dynamic DLL loading
  - **Python** — parse GIF via ctypes with bytecode caching
  - **Go** — produce a playable WAV file via `syscall.LoadDLL`

## Documentation

| Document | Description |
|---|---|
| [Language Specification](docs/LANGUAGE_SPEC.md) | Full `.bsx` syntax and semantics |
| [Grammar](docs/GRAMMAR.md) | Formal EBNF grammar |
| [Architecture & Design](docs/ARCHITECTURE.md) | System design, project layout, data flow |
| [Bytecode Format](docs/BYTECODE.md) | Instruction set and VM semantics |
| [C-ABI Reference](docs/C_ABI.md) | NativeAOT DLL exports and calling conventions |
| [Standard Library](docs/STDLIB.md) | Bundled format definitions |
| [Future Extensions](docs/FUTURE_EXTENSIONS.md) | Planned language features |
| [Tutorial](docs/tutorial/README.md) | 17-chapter hands-on guide (language → integration) |
| [PRD](docs/PRD.md) | Product motivation and goals |
| [ADRs](docs/adr/) | Architecture decision records |

## Building

Requires **.NET 10 SDK** with C# 14 (`LangVersion preview`).

```bash
dotnet build src/BinScript.slnx          # build everything
dotnet test  src/BinScript.Tests          # run all tests
dotnet publish src/BinScript.Interop -c Release  # NativeAOT DLL
```

## License

TBD
