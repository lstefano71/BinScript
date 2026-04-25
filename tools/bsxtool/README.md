# bsxtool — BinScript C-ABI CLI Tool

A Python CLI that exercises every function in the BinScript NativeAOT C-ABI DLL. Use it to compile `.bsx` scripts, parse binary files, save/load compiled bytecode, disassemble bytecode, and inspect `.bsc` files.

> **This tool is part of the BinScript project.** When the C ABI, bytecode format, or opcode set changes, this tool must be updated accordingly. See [Maintenance](#maintenance) below.

## Prerequisites

- **Python 3.10+** (uses `match`, `|` type unions, `from __future__ import annotations`)
- **Published BinScript NativeAOT DLL** — build it with:
  ```bash
  dotnet publish src/BinScript.Interop -c Release
  ```
- No external Python packages — uses only the standard library (`ctypes`, `argparse`, `json`, `struct`)

## Quick Start

```bash
# From the repo root:

# 1. Publish the DLL
dotnet publish src/BinScript.Interop -c Release

# 2. Parse a PNG file using the stdlib script
python tools/bsxtool/bsxtool.py parse stdlib/png.bsx tests/samples/png/rgb_8x8.png

# 3. Disassemble the PNG script's bytecode
python tools/bsxtool/bsxtool.py disasm stdlib/png.bsx

# 4. Compile to .bsc and inspect it
python tools/bsxtool/bsxtool.py compile stdlib/png.bsx -o png.bsc
python tools/bsxtool/bsxtool.py hexdump png.bsc

# 5. Run from pre-compiled bytecode
python tools/bsxtool/bsxtool.py run png.bsc tests/samples/png/rgb_8x8.png
```

## Commands

### `parse` — Compile & parse binary → JSON

```
bsxtool.py parse <script.bsx> <input-file> [options]
```

Compiles a `.bsx` script, parses the binary input, and prints pretty-printed JSON to stdout.

| Option | Description |
|--------|-------------|
| `--entry NAME` | Use a named struct as entry point instead of `@root` |
| `--params JSON` | Runtime parameters as a JSON object string |
| `--module NAME=PATH` | Register a module for `@import` (repeatable) |
| `--stdlib-dir DIR` | Auto-register all `.bsx` files in a directory as modules |

**Example:**
```bash
python bsxtool.py parse stdlib/zip.bsx tests/samples/zip/single.zip
```

### `produce` — Compile & produce binary from JSON

```
bsxtool.py produce <script.bsx> <input.json> [-o output.bin] [options]
```

Compiles a `.bsx` script, reads structured JSON input, and produces binary output.

**Example:**
```bash
python bsxtool.py produce my_format.bsx data.json -o output.bin
```

### `compile` — Save compiled bytecode

```
bsxtool.py compile <script.bsx> [-o output.bsc] [options]
```

Compiles a `.bsx` script and saves the bytecode to a `.bsc` file for later use with `run` or `disasm`.

**Example:**
```bash
python bsxtool.py compile stdlib/pe.bsx -o pe.bsc
```

### `disasm` — Disassemble bytecode

```
bsxtool.py disasm <script.bsx | file.bsc> [options]
```

Prints a human-readable disassembly of the compiled bytecode, including:
- **Global summary**: version, root struct, string/struct counts
- **String table**: all interned strings with indices
- **Per-struct listing**: field table, flags, and full instruction disassembly
- **Resolved names**: field IDs, string IDs, struct IDs shown as names, not raw numbers

Accepts either a `.bsx` file (compiles first via DLL) or a `.bsc` file (direct, no DLL needed).

**Example:**
```bash
# Disassemble from source (needs DLL)
python bsxtool.py disasm stdlib/png.bsx

# Disassemble pre-compiled bytecode (no DLL needed)
python bsxtool.py disasm png.bsc
```

### `hexdump` — Annotated hex dump

```
bsxtool.py hexdump <file.bsc | script.bsx> [options]
```

Prints an annotated hex dump of a `.bsc` file with:
- Decoded header fields (version, flags, section sizes)
- Section boundaries (header, parameters, string table, struct table, bytecode, source)
- Classic hex + ASCII display

Accepts `.bsx` files too (compiles first via DLL).

**Example:**
```bash
python bsxtool.py hexdump png.bsc
```

### `run` — Load bytecode & parse

```
bsxtool.py run <file.bsc> <input-file> [options]
```

Loads pre-compiled bytecode from a `.bsc` file and parses binary input. Faster than `parse` when running the same script repeatedly.

| Option | Description |
|--------|-------------|
| `--entry NAME` | Named struct entry point |
| `--params JSON` | Runtime parameters |

### `info` — Print DLL info

```
bsxtool.py info
```

Prints the BinScript library version and the auto-discovered DLL path.

## Global Options

| Option | Description |
|--------|-------------|
| `--dll PATH` | Explicit path to the BinScript NativeAOT DLL. If omitted, auto-discovers from `src/BinScript.Interop/bin/Release/.../publish/` |

## Module Resolution

For scripts that use `@import`, you need to register modules:

**Explicit modules** (`--module`):
```bash
python bsxtool.py parse main.bsx input.bin --module "pe/common=stdlib/memory/pe_common.bsx"
```

**Auto-register from directory** (`--stdlib-dir`):
```bash
# Registers all .bsx files under stdlib/ as modules using their relative paths
python bsxtool.py parse main.bsx input.bin --stdlib-dir stdlib
```

## Architecture

```
tools/bsxtool/
├── bsxtool.py     # CLI entry point, ctypes wrapper, DLL discovery, all commands
├── disasm.py      # BSC parser + bytecode disassembler (opcode table, decoders)
├── hexdump.py     # Annotated hex dump of .bsc binary format
└── README.md      # This file
```

### Key Design Decisions

- **Pure Python stdlib**: no `pip install` needed — just Python 3.10+ and the DLL
- **ctypes FFI**: wraps every C-ABI function with proper type signatures
- **Standalone disassembler**: `disasm.py` parses the BSC binary format directly in Python (doesn't need the DLL for `.bsc` files)
- **DLL auto-discovery**: searches the standard NativeAOT publish output paths

## Maintenance

This tool is tightly coupled to the BinScript C ABI and bytecode format. When these change, the tool **must** be updated:

### When opcodes change (`src/BinScript.Core/Bytecode/Opcode.cs`)
→ Update the `Opcode` enum and `_OPCODE_DECODERS` table in **`disasm.py`**. Each opcode needs a mnemonic string and a decoder function that knows the operand layout.

### When the BSC binary format changes (`BytecodeSerializer.cs` / `BytecodeDeserializer.cs`)
→ Update `parse_bsc()` in **`disasm.py`** and the section boundary logic in **`hexdump.py`**. The header layout, section order, and per-entry formats must match.

### When C-ABI functions are added/removed/changed (`NativeExports.cs`, `docs/C_ABI.md`)
→ Update `_setup_signatures()` in **`bsxtool.py`** and add/update the corresponding command implementation.

### When struct/field metadata changes (`BytecodeProgram.cs`)
→ Update the `StructMeta`/`FieldMeta` dataclasses and `parse_bsc()` in **`disasm.py`**.

### Checklist for C-ABI changes
1. Update ctypes signatures in `bsxtool.py` → `_setup_signatures()`
2. Add/update command implementation in `bsxtool.py`
3. Update opcode table in `disasm.py` (if bytecode changed)
4. Update BSC parser in `disasm.py` (if serialization format changed)
5. Update section layout in `hexdump.py` (if serialization format changed)
6. Run smoke tests (see below)

## Smoke Testing

After changes, verify with these commands:

```bash
# Info
python tools/bsxtool/bsxtool.py info

# Compile + disasm stdlib scripts
for script in stdlib/*.bsx; do
    echo "=== $script ==="
    python tools/bsxtool/bsxtool.py disasm "$script"
done

# Parse test samples
python tools/bsxtool/bsxtool.py parse stdlib/png.bsx tests/samples/png/rgb_8x8.png
python tools/bsxtool/bsxtool.py parse stdlib/zip.bsx tests/samples/zip/single.zip
python tools/bsxtool/bsxtool.py parse stdlib/bmp.bsx tests/samples/bmp/4x4_24bit.bmp

# Compile + run roundtrip
python tools/bsxtool/bsxtool.py compile stdlib/png.bsx -o /tmp/png.bsc
python tools/bsxtool/bsxtool.py run /tmp/png.bsc tests/samples/png/rgb_8x8.png
python tools/bsxtool/bsxtool.py hexdump /tmp/png.bsc
```
