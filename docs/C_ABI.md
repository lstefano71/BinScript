# BinScript C-ABI Reference

## Overview

BinScript exposes a C-ABI interface via a NativeAOT-compiled DLL. This is the primary integration point for non-.NET host applications. The DLL exports plain C functions callable from any FFI-capable language.

All string parameters are UTF-8, null-terminated. All returned strings are UTF-8, null-terminated, and must be freed by the caller using `binscript_mem_free`.

## Conventions

- **Opaque handles**: `BinCompiler*` and `BinScript*` are opaque pointers. Do not dereference.
- **Error reporting**: On failure, functions return `NULL` (pointers) or negative values (integers). Call `binscript_last_error()` for a human-readable error message.
- **Memory ownership**: Any pointer returned by a `binscript_*` function that allocates memory must be freed with `binscript_mem_free()`. Exception: error strings from `binscript_last_error()` are thread-local and must NOT be freed.
- **Thread safety**: A `BinScript*` handle is immutable and safe to use from multiple threads. `BinCompiler*` is NOT thread-safe. `binscript_last_error()` is thread-local.

## Header File

The canonical C header is at [`src/BinScript.Interop/binscript.h`](../src/BinScript.Interop/binscript.h).

Include it in your project:
```c
#include "binscript.h"
```

## Usage Example (C)

```c
#include "binscript.h"
#include <stdio.h>
#include <stdlib.h>

int main(void) {
    // 1. Create compiler and register modules
    BinCompiler* compiler = binscript_compiler_new();
    binscript_compiler_add_module(compiler, "pe/common",
        "enum Machine : u16 { I386 = 0x014C, AMD64 = 0x8664, ARM64 = 0xAA64, }");

    // 2. Compile main script
    const char* script =
        "@default_endian(little)\n"
        "@import \"pe/common\"\n"
        "@root struct DosHeader {\n"
        "    e_magic: u16 = 0x5A4D,\n"
        "    e_cblp: u16,\n"
        "}\n";

    BinScript* prog = binscript_compiler_compile(compiler, script, NULL);
    binscript_compiler_free(compiler);

    if (!prog) {
        fprintf(stderr, "Compile error: %s\n", binscript_last_error());
        return 1;
    }

    // 3. Parse a binary buffer
    uint8_t data[] = { 0x4D, 0x5A, 0x90, 0x00 };  // "MZ" + e_cblp=144
    const char* json = binscript_to_json(prog, data, sizeof(data), NULL);

    if (json) {
        printf("%s\n", json);
        binscript_mem_free((void*)json);
    } else {
        fprintf(stderr, "Parse error: %s\n", binscript_last_error());
    }

    // 4. Save compiled bytecode
    int64_t size = binscript_save(prog, NULL, 0);
    uint8_t* bytecode = malloc(size);
    binscript_save(prog, bytecode, size);
    // ... write bytecode to file for later reuse ...
    free(bytecode);

    binscript_free(prog);
    return 0;
}
```

## Usage Example (Python ctypes)

```python
import ctypes
import json

lib = ctypes.CDLL("binscript.dll")  # or .so / .dylib

# Set up function signatures
lib.binscript_compiler_new.restype = ctypes.c_void_p
lib.binscript_compiler_compile.restype = ctypes.c_void_p
lib.binscript_compiler_compile.argtypes = [
    ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p
]
lib.binscript_to_json.restype = ctypes.c_char_p
lib.binscript_to_json.argtypes = [
    ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_char_p
]
lib.binscript_mem_free.argtypes = [ctypes.c_void_p]

# Compile
compiler = lib.binscript_compiler_new()
script = b'@root struct Test { magic: u32le, }'
prog = lib.binscript_compiler_compile(compiler, script, None)
lib.binscript_compiler_free(compiler)

# Parse
data = b'\x50\x4B\x03\x04'  # ZIP local file header magic
result = lib.binscript_to_json(prog, data, len(data), None)
print(json.loads(result))

lib.binscript_free(prog)
```

## Maintenance Checklist

When the C-ABI surface changes (new functions, changed signatures, removed functions), update these files:

| File | What to update |
|------|---------------|
| `src/BinScript.Interop/NativeExports.cs` | Implementation (source of truth) |
| `src/BinScript.Interop/binscript.h` | Canonical C header |
| `docs/C_ABI.md` | This documentation |
| `tools/bsxtool/bsxtool.py` | Python ctypes signatures (all 16 functions) |
| `tools/examples/c/parse_bmp.c` | C example (uses subset) |
| `tools/examples/python/parse_gif.py` | Python example (uses subset) |
| `tools/examples/go/produce_wav.go` | Go example (uses subset) |
| `src/BinScript.Tests/Interop/NativeExportTests.cs` | Interop tests |
