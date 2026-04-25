# Chapter 16 — Integrating BinScript from Python

> Use Python's built-in `ctypes` to call the BinScript C-ABI — zero dependencies beyond the standard library.

## What You'll Build

A program that:

1. Loads the BinScript NativeAOT DLL via `ctypes`
2. Compiles a GIF parser script at runtime
3. Parses a GIF file → JSON
4. Prints a human-friendly summary (dimensions, frames, extensions)
5. Saves compiled bytecode for reuse

Full source: [`tools/examples/python/parse_gif.py`](../../tools/examples/python/parse_gif.py)

## Prerequisites

- **Python 3.10+** (for `Path | None` union syntax; 3.8+ works with `Optional[Path]`)
- **Published BinScript DLL** — build it once with:
  ```
  dotnet publish src/BinScript.Interop -c Release
  ```
- A GIF file to parse (any `.gif` will do)

---

## Step 1: Loading the DLL with ctypes

Python's `ctypes.CDLL` loads a shared library and exposes its exported C functions. The only trick is finding the right file — the extension varies by platform:

| Platform | Extension |
|----------|-----------|
| Windows  | `.dll`    |
| macOS    | `.dylib`  |
| Linux    | `.so`     |

Here's the discovery logic from the example:

```python
import ctypes
import os
import platform
from pathlib import Path

def find_dll() -> Path | None:
    """Auto-discover BinScript DLL: env var → walk repo tree."""
    # 1. Check explicit env var first
    env = os.environ.get("BINSCRIPT_DLL")
    if env and Path(env).exists():
        return Path(env)

    # 2. Walk the repo's publish output
    repo = Path(__file__).resolve().parent.parent.parent.parent
    interop = repo / "src" / "BinScript.Interop"
    if not interop.is_dir():
        return None

    ext = {"Windows": ".dll", "Darwin": ".dylib"}.get(platform.system(), ".so")
    name = f"BinScript.Interop{ext}"

    for config in ("Release", "Debug"):
        base = interop / "bin" / config
        if not base.is_dir():
            continue
        for f in base.rglob(name):
            if "publish" in str(f) or "native" in str(f):
                return f

    for f in (interop / "bin").rglob(name):
        return f
    return None
```

Load it:

```python
dll_path = find_dll()
lib = ctypes.CDLL(str(dll_path))
```

> 💡 `BINSCRIPT_DLL` is the escape hatch — set it when the DLL lives outside the repo tree.

---

## Step 2: Declaring Function Signatures

ctypes needs to know each function's return type (`restype`) and parameter types (`argtypes`). Without these declarations, ctypes guesses `int` for everything — which silently truncates 64-bit pointers on most platforms.

```python
def setup_lib(lib: ctypes.CDLL) -> None:
    """Declare ctypes signatures for all functions we use."""
    # Compiler lifecycle
    lib.binscript_compiler_new.restype = ctypes.c_void_p
    lib.binscript_compiler_new.argtypes = []

    lib.binscript_compiler_compile.restype = ctypes.c_void_p
    lib.binscript_compiler_compile.argtypes = [
        ctypes.c_void_p,   # compiler handle
        ctypes.c_char_p,   # script source (UTF-8)
        ctypes.c_char_p,   # filename (nullable)
    ]

    lib.binscript_compiler_free.restype = None
    lib.binscript_compiler_free.argtypes = [ctypes.c_void_p]

    # Parse → JSON
    lib.binscript_to_json.restype = ctypes.c_void_p     # ← NOT c_char_p!
    lib.binscript_to_json.argtypes = [
        ctypes.c_void_p,   # program handle
        ctypes.c_void_p,   # data pointer
        ctypes.c_size_t,   # data length
        ctypes.c_char_p,   # options (nullable)
    ]

    # Bytecode save
    lib.binscript_save.restype = ctypes.c_int64
    lib.binscript_save.argtypes = [
        ctypes.c_void_p,                        # program handle
        ctypes.POINTER(ctypes.c_uint8),         # buffer (nullable for size query)
        ctypes.c_size_t,                        # buffer size
    ]

    # Cleanup
    lib.binscript_free.restype = None
    lib.binscript_free.argtypes = [ctypes.c_void_p]

    lib.binscript_mem_free.restype = None
    lib.binscript_mem_free.argtypes = [ctypes.c_void_p]

    # Error & version
    lib.binscript_last_error.restype = ctypes.c_char_p
    lib.binscript_last_error.argtypes = []

    lib.binscript_version.restype = ctypes.c_char_p
    lib.binscript_version.argtypes = []
```

### ⚠️ The `c_void_p` vs `c_char_p` Gotcha

`binscript_to_json` returns an engine-allocated string that you **must free** with `binscript_mem_free`. If you declare its `restype` as `c_char_p`, ctypes helpfully auto-converts it to a Python `bytes` object — and you lose the raw pointer. You can never free it. Memory leak.

The fix: declare it as `c_void_p`, read the string with `ctypes.string_at(ptr)`, then call `binscript_mem_free(ptr)`.

In contrast, `binscript_last_error` returns a thread-local string that you must **not** free — `c_char_p` is correct for that one.

---

## Step 3: Error Checking Helper

Every BinScript C-ABI call that can fail sets a thread-local error message retrievable via `binscript_last_error()`. The pattern is: call the function, check for `NULL`/negative return, then read the error:

```python
def check_error(lib: ctypes.CDLL, context: str) -> None:
    """Check binscript_last_error and raise if set."""
    err = lib.binscript_last_error()
    if err:
        raise RuntimeError(f"{context}: {err.decode()}")
```

Use it after any failable call:

```python
prog = lib.binscript_compiler_compile(compiler, script.encode(), None)
if not prog:
    check_error(lib, "Compile failed")
```

> 🔑 `binscript_last_error()` returns `NULL` when no error is pending, so it doubles as a "did it work?" check.

---

## Step 4: Compiling the GIF Script

BinScript compiles `.bsx` source text into a bytecode program at runtime. The compiler handle is short-lived — create it, compile, free it:

```python
# Read the GIF script
script_path = repo / "stdlib" / "gif.bsx"
script = script_path.read_text(encoding="utf-8")

# Compile
compiler = lib.binscript_compiler_new()
prog = lib.binscript_compiler_compile(compiler, script.encode(), None)
lib.binscript_compiler_free(compiler)    # compiler is done, free immediately

if not prog:
    err = lib.binscript_last_error()
    msg = err.decode() if err else "unknown error"
    print(f"Compile error: {msg}", file=sys.stderr)
    sys.exit(1)
```

Note that `script.encode()` produces UTF-8 bytes — ctypes passes this to the C function as a null-terminated `const char*`.

The second argument to `binscript_compiler_compile` is an optional filename (for error messages). We pass `None` here.

---

## Step 5: Parsing → JSON

With a compiled program, parsing is a single call — pass in the binary data, get back a JSON string:

```python
data = gif_path.read_bytes()

json_ptr = lib.binscript_to_json(prog, data, len(data), None)
if not json_ptr:
    err = lib.binscript_last_error()
    msg = err.decode() if err else "unknown error"
    print(f"Parse error: {msg}", file=sys.stderr)
    sys.exit(1)

# Read the string, then free the engine-allocated buffer
json_str = ctypes.string_at(json_ptr).decode()
lib.binscript_mem_free(json_ptr)

parsed = json.loads(json_str)
```

The three-step dance — `string_at` → `decode` → `mem_free` — is the standard pattern for any engine-allocated string. Get comfortable with it; you'll use it every time.

---

## Step 6: Working with GIF Output

This is where Python shines. The parsed JSON is a nested dict that maps directly to the GIF structure defined in `gif.bsx`. Here's what the script defines:

```
GifFile
├── header          → { signature, version }
├── screen          → { width, height, packed, ... }
├── global_color_table → [ { red, green, blue }, ... ]
└── blocks[]        → each has { introducer, body }
    ├── 0x2C: ImageData   → { width, height, local_color_table, ... }
    ├── 0x21: Extension   → { label, body }
    │   ├── 0xF9: GraphicsControlExtension → { delay_time, ... }
    │   ├── 0xFF: ApplicationExtension     → { identifier, ... }
    │   ├── 0xFE: CommentExtension
    │   └── 0x01: PlainTextExtension
    └── 0x3B: Trailer     → (empty)
```

The example walks this structure to print a rich summary:

```python
def print_summary(parsed: dict) -> None:
    """Print a human-friendly summary of the parsed GIF structure."""
    version = parsed["header"]["version"]
    screen = parsed["screen"]
    gct = parsed.get("global_color_table", [])
    gct_count = len(gct) if isinstance(gct, list) else 0
    blocks = parsed.get("blocks", [])

    print(f"\nGIF{version} Image Summary:")
    print(f"  Dimensions: {screen['width']} × {screen['height']}")
    if gct_count:
        print(f"  Global Colors: {gct_count}")
    print(f"  Blocks: {len(blocks)}")

    for i, block in enumerate(blocks, 1):
        intro = block.get("introducer", 0)
        if intro == 0x2C:  # Image descriptor
            b = block["body"]
            w, h = b.get("width", "?"), b.get("height", "?")
            left, top = b.get("left", 0), b.get("top", 0)
            lct = b.get("local_color_table")
            has_lct = "Yes" if lct and lct != [] else "No"
            print(f"\n  Block {i}: Image ({w}×{h} at {left},{top})")
            print(f"    Local Color Table: {has_lct}")
        elif intro == 0x21:  # Extension
            ext = block["body"]
            label = ext.get("label", 0)
            if label == 0xF9:
                gce = ext["body"]
                delay = gce.get("delay_time", 0)
                print(f"\n  Block {i}: Extension (Graphics Control)")
                print(f"    Delay: {delay} ({delay * 10}ms)")
            elif label == 0xFF:
                ident = ext["body"].get("identifier", "?")
                print(f"\n  Block {i}: Extension (Application: {ident})")
            elif label == 0xFE:
                print(f"\n  Block {i}: Extension (Comment)")
            elif label == 0x01:
                print(f"\n  Block {i}: Extension (Plain Text)")
            else:
                print(f"\n  Block {i}: Extension (0x{label:02X})")
        elif intro == 0x3B:  # Trailer
            print(f"\n  Block {i}: Trailer")
```

The `introducer` byte tells you the block type, and the `match` in the `.bsx` script ensures the JSON body is already the right shape. Python's dict access makes it trivial to destructure — no deserialization classes needed.

---

## Step 7: Bytecode Caching

Compilation isn't free. For repeated use (e.g., a server that parses many GIFs), save the compiled bytecode to a `.bsc` file and load it next time:

```python
# Save: first call with NULL buffer to get size, then allocate and fill
size = lib.binscript_save(prog, None, 0)
if size > 0:
    buf = (ctypes.c_uint8 * size)()
    lib.binscript_save(prog, buf, size)
    bsc_path = gif_path.with_suffix(".bsc")
    bsc_path.write_bytes(bytes(buf))
    print(f"Saved {size} bytes → {bsc_path.name}")
```

The two-pass pattern — `save(prog, NULL, 0)` to query the size, then `save(prog, buf, size)` to fill it — is a common C-ABI idiom. It avoids guessing buffer sizes.

To load a cached `.bsc` later, use `binscript_load` instead of `binscript_compiler_compile`:

```python
# Load pre-compiled bytecode (skips compilation entirely)
bsc_data = bsc_path.read_bytes()
buf = (ctypes.c_uint8 * len(bsc_data)).from_buffer_copy(bsc_data)
prog = lib.binscript_load(buf, len(bsc_data))
if not prog:
    check_error(lib, "Load failed")
```

---

## Step 8: Memory Management in ctypes

BinScript's C-ABI has clear ownership rules. Here's the cheat sheet:

| Returned by | Free with | Notes |
|---|---|---|
| `binscript_compiler_new()` | `binscript_compiler_free()` | Compiler handle |
| `binscript_compiler_compile()` | `binscript_free()` | Program handle |
| `binscript_to_json()` | `binscript_mem_free()` | Engine-allocated string |
| `binscript_last_error()` | **Do NOT free** | Thread-local, overwritten on next call |
| `binscript_load()` | `binscript_free()` | Program handle |

The golden rules:

1. **Use `c_void_p`** for any return value you need to free. If ctypes auto-converts it (via `c_char_p`), you lose the pointer.
2. **Call `binscript_mem_free`** for engine-allocated strings and buffers — never Python's `free` or `del`.
3. **Use `try/finally`** to ensure handles are freed even on error:

```python
prog = lib.binscript_compiler_compile(compiler, script.encode(), None)
lib.binscript_compiler_free(compiler)

try:
    # ... use prog ...
finally:
    lib.binscript_free(prog)
```

---

## Building and Running

```bash
# 1. Build the DLL (one-time)
dotnet publish src/BinScript.Interop -c Release

# 2. Run the example
python tools/examples/python/parse_gif.py path/to/image.gif
```

Expected output:

```
Loading BinScript DLL...
  Path: .../BinScript.Interop.dll
  Version: 1.0.0

Compiling GIF script...
  Script: .../stdlib/gif.bsx
  ✓ Compiled successfully

Parsing image.gif (12345 bytes)...

GIF89a Image Summary:
  Dimensions: 320 × 240
  Global Colors: 256
  Blocks: 5

  Block 1: Extension (Application: NETSCAPE)

  Block 2: Extension (Graphics Control)
    Delay: 10 (100ms)

  Block 3: Image (320×240 at 0,0)
    Local Color Table: No

  Block 4: Extension (Graphics Control)
    Delay: 10 (100ms)

  Block 5: Trailer

Saving compiled bytecode → image.bsc
  ✓ Saved 284 bytes

Done.
```

> 💡 You can also set the `BINSCRIPT_DLL` environment variable to point directly at the DLL if auto-discovery doesn't find it.

---

## Summary

| Concept | Takeaway |
|---|---|
| DLL loading | `ctypes.CDLL` + platform-aware file extension |
| Signatures | Always set `restype` and `argtypes` — silent pointer truncation otherwise |
| `c_void_p` vs `c_char_p` | Use `c_void_p` for strings you must free; `c_char_p` for strings you must not |
| Error handling | Check for `NULL`/negative return, then `binscript_last_error()` |
| String ownership | `string_at()` → `decode()` → `mem_free()` — the three-step dance |
| Bytecode caching | `save(NULL, 0)` for size, `save(buf, size)` to fill — skip recompilation |
| Handle cleanup | `compiler_free` for compilers, `free` for programs, `mem_free` for strings |

**Next:** [Chapter 17 — Integrating BinScript from Go](17-integration-go.md) shows the same C-ABI from Go's `cgo`.
