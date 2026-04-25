#!/usr/bin/env python3
"""Parse a GIF file using BinScript C-ABI — integration example.

Pure stdlib (ctypes, json, pathlib) — no pip dependencies.

Usage:
    python parse_gif.py <gif-file>
    BINSCRIPT_DLL=/path/to/dll python parse_gif.py <gif-file>
"""

import io, sys as _sys
# Ensure stdout handles Unicode on Windows consoles (cp1252 etc.)
if _sys.stdout and hasattr(_sys.stdout, "reconfigure"):
    try:
        _sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        _sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

import ctypes
import json
import os
import platform
import sys
from pathlib import Path


# ─── DLL discovery ────────────────────────────────────────────────────────────

def find_dll() -> Path | None:
    """Auto-discover BinScript DLL: env var → walk repo tree."""
    env = os.environ.get("BINSCRIPT_DLL")
    if env and Path(env).exists():
        return Path(env)

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


# ─── ctypes signatures ───────────────────────────────────────────────────────

def setup_lib(lib: ctypes.CDLL) -> None:
    """Declare ctypes signatures for all functions we use."""
    lib.binscript_compiler_new.restype = ctypes.c_void_p
    lib.binscript_compiler_new.argtypes = []

    lib.binscript_compiler_compile.restype = ctypes.c_void_p
    lib.binscript_compiler_compile.argtypes = [
        ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p,
    ]

    lib.binscript_compiler_free.restype = None
    lib.binscript_compiler_free.argtypes = [ctypes.c_void_p]

    lib.binscript_to_json.restype = ctypes.c_void_p
    lib.binscript_to_json.argtypes = [
        ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_char_p,
    ]

    lib.binscript_save.restype = ctypes.c_int64
    lib.binscript_save.argtypes = [
        ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint8), ctypes.c_size_t,
    ]

    lib.binscript_free.restype = None
    lib.binscript_free.argtypes = [ctypes.c_void_p]

    lib.binscript_mem_free.restype = None
    lib.binscript_mem_free.argtypes = [ctypes.c_void_p]

    lib.binscript_last_error.restype = ctypes.c_char_p
    lib.binscript_last_error.argtypes = []

    lib.binscript_version.restype = ctypes.c_char_p
    lib.binscript_version.argtypes = []


def check_error(lib: ctypes.CDLL, context: str) -> None:
    """Check binscript_last_error and raise if set."""
    err = lib.binscript_last_error()
    if err:
        raise RuntimeError(f"{context}: {err.decode()}")


# ─── GIF summary printer ─────────────────────────────────────────────────────

def print_summary(parsed: dict) -> None:
    """Print a human-friendly summary of the parsed GIF structure."""
    version = parsed["header"]["version"]
    screen = parsed["screen"]
    gct = parsed.get("global_color_table", [])
    gct_count = len(gct) if isinstance(gct, list) else 0
    blocks = parsed.get("blocks", [])

    print(f"\nGIF{version} Image Summary:")
    print(f"  Version: {version}")
    print(f"  Dimensions: {screen['width']} \u00d7 {screen['height']}")
    if gct_count:
        print(f"  Global Colors: {gct_count}")
    print(f"  Blocks: {len(blocks)}")

    for i, block in enumerate(blocks, 1):
        intro = block.get("introducer", 0)
        if intro == 0x2C:  # Image
            b = block["body"]
            w, h = b.get("width", "?"), b.get("height", "?")
            left, top = b.get("left", 0), b.get("top", 0)
            lct = b.get("local_color_table")
            has_lct = "Yes" if lct and lct != [] else "No"
            print(f"\n  Block {i}: Image ({w}\u00d7{h} at {left},{top})")
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
        else:
            print(f"\n  Block {i}: Unknown (0x{intro:02X})")


# ─── Main ─────────────────────────────────────────────────────────────────────

def main() -> None:
    if len(sys.argv) < 2:
        print("Usage: parse_gif.py <gif-file>", file=sys.stderr)
        sys.exit(1)

    gif_path = Path(sys.argv[1])
    if not gif_path.exists():
        print(f"Error: file not found: {gif_path}", file=sys.stderr)
        sys.exit(1)

    # 1. Find and load DLL
    dll_path = find_dll()
    if not dll_path:
        print("Error: BinScript DLL not found. Build with:", file=sys.stderr)
        print("  dotnet publish src/BinScript.Interop -c Release", file=sys.stderr)
        print("Or set BINSCRIPT_DLL environment variable.", file=sys.stderr)
        sys.exit(1)

    lib = ctypes.CDLL(str(dll_path))
    setup_lib(lib)

    print("Loading BinScript DLL...")
    print(f"  Path: {dll_path}")
    print(f"  Version: {lib.binscript_version().decode()}")

    # 2. Compile the GIF script
    repo = Path(__file__).resolve().parent.parent.parent.parent
    script_path = repo / "stdlib" / "gif.bsx"
    script = script_path.read_text(encoding="utf-8")

    print(f"\nCompiling GIF script...")
    print(f"  Script: {script_path}")

    compiler = lib.binscript_compiler_new()
    prog = lib.binscript_compiler_compile(compiler, script.encode(), None)
    lib.binscript_compiler_free(compiler)

    if not prog:
        err = lib.binscript_last_error()
        msg = err.decode() if err else "unknown error"
        print(f"  \u2717 Compile error: {msg}", file=sys.stderr)
        sys.exit(1)
    print("  \u2713 Compiled successfully")

    try:
        # 3. Parse the GIF binary
        data = gif_path.read_bytes()
        print(f"\nParsing {gif_path.name} ({len(data)} bytes)...")

        json_ptr = lib.binscript_to_json(prog, data, len(data), None)
        if not json_ptr:
            err = lib.binscript_last_error()
            msg = err.decode() if err else "unknown error"
            print(f"  \u2717 Parse error: {msg}", file=sys.stderr)
            sys.exit(1)

        json_str = ctypes.string_at(json_ptr).decode()
        lib.binscript_mem_free(json_ptr)

        parsed = json.loads(json_str)

        # 4. Print summary
        print_summary(parsed)

        # 5. Full JSON
        print(f"\nFull JSON:")
        print(json.dumps(parsed, indent=2))

        # 6. Save compiled bytecode
        size = lib.binscript_save(prog, None, 0)
        if size > 0:
            buf = (ctypes.c_uint8 * size)()
            lib.binscript_save(prog, buf, size)
            bsc_path = gif_path.with_suffix(".bsc")
            bsc_path.write_bytes(bytes(buf))
            print(f"\nSaving compiled bytecode \u2192 {bsc_path.name}")
            print(f"  \u2713 Saved {size} bytes")
    finally:
        lib.binscript_free(prog)
        print("\nDone.")


if __name__ == "__main__":
    main()
