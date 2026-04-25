#!/usr/bin/env python3
"""
bsxtool — CLI exercising the BinScript C-ABI DLL.

Compile .bsx scripts, parse/produce binary data, save/load bytecode,
disassemble bytecode, and inspect .bsc files — all via the NativeAOT
C-ABI DLL.

Usage:
    python bsxtool.py <command> [options]

Commands:
    parse      Compile a .bsx script and parse a binary file → JSON
    produce    Compile a .bsx script and produce binary from JSON
    compile    Compile a .bsx script and save bytecode to .bsc
    disasm     Disassemble a .bsx script or .bsc file
    hexdump    Annotated hex dump of a .bsc file
    run        Load pre-compiled .bsc and parse a binary file → JSON
    info       Print DLL version and path

Requires the published NativeAOT DLL (dotnet publish src/BinScript.Interop -c Release).
"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import platform
import sys
from pathlib import Path

# Local modules
from disasm import parse_bsc, disassemble, disassemble_bsc_bytes
from hexdump import hexdump_bsc


# ═══════════════════════════════════════════════════════════════════════════════
#  DLL Discovery
# ═══════════════════════════════════════════════════════════════════════════════

def _find_dll() -> Path | None:
    """Auto-discover the BinScript NativeAOT DLL from the publish output."""
    # Walk up from this script to the repo root
    tool_dir = Path(__file__).resolve().parent       # tools/bsxtool/
    repo_root = tool_dir.parent.parent               # repo root

    interop_dir = repo_root / "src" / "BinScript.Interop"
    if not interop_dir.is_dir():
        return None

    system = platform.system()
    if system == "Windows":
        lib_name = "BinScript.Interop.dll"
    elif system == "Darwin":
        lib_name = "BinScript.Interop.dylib"
    else:
        lib_name = "BinScript.Interop.so"

    # Search in publish output for common RIDs
    for config in ("Release", "Debug"):
        publish_base = interop_dir / "bin" / config
        if not publish_base.is_dir():
            continue
        for child in publish_base.rglob(lib_name):
            if "publish" in str(child):
                return child

    # Fallback: any match under bin/
    for child in (interop_dir / "bin").rglob(lib_name):
        return child

    return None


def load_dll(dll_path: str | None) -> ctypes.CDLL:
    """Load the BinScript DLL, auto-discovering if no explicit path given."""
    if dll_path:
        path = Path(dll_path)
    else:
        path = _find_dll()
        if path is None:
            print(
                "Error: Could not find BinScript DLL.\n"
                "Publish it first:  dotnet publish src/BinScript.Interop -c Release\n"
                "Or specify --dll <path>",
                file=sys.stderr,
            )
            sys.exit(1)

    if not path.exists():
        print(f"Error: DLL not found at {path}", file=sys.stderr)
        sys.exit(1)

    lib = ctypes.CDLL(str(path))
    _setup_signatures(lib)
    return lib


# ═══════════════════════════════════════════════════════════════════════════════
#  ctypes Signatures
# ═══════════════════════════════════════════════════════════════════════════════

def _setup_signatures(lib: ctypes.CDLL) -> None:
    """Declare all C-ABI function signatures."""

    # Compiler lifecycle
    lib.binscript_compiler_new.restype = ctypes.c_void_p
    lib.binscript_compiler_new.argtypes = []

    lib.binscript_compiler_add_module.restype = ctypes.c_int
    lib.binscript_compiler_add_module.argtypes = [
        ctypes.c_void_p,  # compiler
        ctypes.c_char_p,  # name
        ctypes.c_char_p,  # script
    ]

    lib.binscript_compiler_compile.restype = ctypes.c_void_p
    lib.binscript_compiler_compile.argtypes = [
        ctypes.c_void_p,  # compiler
        ctypes.c_char_p,  # script
        ctypes.c_char_p,  # params_json
    ]

    lib.binscript_compiler_free.restype = None
    lib.binscript_compiler_free.argtypes = [ctypes.c_void_p]

    # Program persistence
    lib.binscript_save.restype = ctypes.c_int64
    lib.binscript_save.argtypes = [
        ctypes.c_void_p,                    # script
        ctypes.POINTER(ctypes.c_uint8),     # buf
        ctypes.c_size_t,                    # cap
    ]

    lib.binscript_load.restype = ctypes.c_void_p
    lib.binscript_load.argtypes = [
        ctypes.POINTER(ctypes.c_uint8),     # data
        ctypes.c_size_t,                    # len
    ]

    lib.binscript_free.restype = None
    lib.binscript_free.argtypes = [ctypes.c_void_p]

    # Parse: Binary → JSON
    lib.binscript_to_json.restype = ctypes.c_void_p  # char* we must free
    lib.binscript_to_json.argtypes = [
        ctypes.c_void_p,  # script
        ctypes.c_void_p,  # data
        ctypes.c_size_t,  # len
        ctypes.c_char_p,  # params_json
    ]

    lib.binscript_to_json_entry.restype = ctypes.c_void_p
    lib.binscript_to_json_entry.argtypes = [
        ctypes.c_void_p,  # script
        ctypes.c_char_p,  # entry
        ctypes.c_void_p,  # data
        ctypes.c_size_t,  # len
        ctypes.c_char_p,  # params_json
    ]

    # Produce: JSON → Binary
    lib.binscript_from_json_static_size.restype = ctypes.c_int64
    lib.binscript_from_json_static_size.argtypes = [
        ctypes.c_void_p,  # script
        ctypes.c_char_p,  # entry
    ]

    lib.binscript_from_json_calc_size.restype = ctypes.c_int64
    lib.binscript_from_json_calc_size.argtypes = [
        ctypes.c_void_p,  # script
        ctypes.c_char_p,  # json
        ctypes.c_char_p,  # params_json
    ]

    lib.binscript_from_json_into.restype = ctypes.c_int64
    lib.binscript_from_json_into.argtypes = [
        ctypes.c_void_p,                    # script
        ctypes.POINTER(ctypes.c_uint8),     # buf
        ctypes.c_size_t,                    # buf_len
        ctypes.c_char_p,                    # json
        ctypes.c_char_p,                    # params_json
    ]

    lib.binscript_from_json.restype = ctypes.c_void_p  # uint8_t* we must free
    lib.binscript_from_json.argtypes = [
        ctypes.c_void_p,                    # script
        ctypes.c_char_p,                    # json
        ctypes.POINTER(ctypes.c_size_t),    # out_len
        ctypes.c_char_p,                    # params_json
    ]

    # Memory management
    lib.binscript_mem_free.restype = None
    lib.binscript_mem_free.argtypes = [ctypes.c_void_p]

    # Error reporting
    lib.binscript_last_error.restype = ctypes.c_char_p
    lib.binscript_last_error.argtypes = []

    # Version
    lib.binscript_version.restype = ctypes.c_char_p
    lib.binscript_version.argtypes = []


# ═══════════════════════════════════════════════════════════════════════════════
#  Helper: check errors
# ═══════════════════════════════════════════════════════════════════════════════

def _check_error(lib: ctypes.CDLL, context: str) -> None:
    err = lib.binscript_last_error()
    if err:
        msg = err.decode("utf-8")
        print(f"Error ({context}): {msg}", file=sys.stderr)
        sys.exit(1)


# ═══════════════════════════════════════════════════════════════════════════════
#  Helper: compile a .bsx script → program handle
# ═══════════════════════════════════════════════════════════════════════════════

def _compile_script(
    lib: ctypes.CDLL,
    script_path: str,
    modules: list[tuple[str, str]] | None = None,
    stdlib_dir: str | None = None,
    params_json: str | None = None,
) -> ctypes.c_void_p:
    """Compile a .bsx script and return the BinScript* handle."""
    compiler = lib.binscript_compiler_new()
    if not compiler:
        _check_error(lib, "compiler_new")
        sys.exit(1)

    try:
        # Register modules from --stdlib-dir
        if stdlib_dir:
            stdlib_path = Path(stdlib_dir)
            if not stdlib_path.is_dir():
                print(f"Error: stdlib dir not found: {stdlib_dir}", file=sys.stderr)
                sys.exit(1)
            for bsx_file in sorted(stdlib_path.rglob("*.bsx")):
                # Module name = relative path without .bsx, using / as separator
                mod_name = bsx_file.relative_to(stdlib_path).with_suffix("").as_posix()
                mod_source = bsx_file.read_text(encoding="utf-8")
                rc = lib.binscript_compiler_add_module(
                    compiler,
                    mod_name.encode("utf-8"),
                    mod_source.encode("utf-8"),
                )
                if rc != 0:
                    _check_error(lib, f"add_module({mod_name})")

        # Register explicit modules from --module
        if modules:
            for mod_name, mod_path in modules:
                mod_source = Path(mod_path).read_text(encoding="utf-8")
                rc = lib.binscript_compiler_add_module(
                    compiler,
                    mod_name.encode("utf-8"),
                    mod_source.encode("utf-8"),
                )
                if rc != 0:
                    _check_error(lib, f"add_module({mod_name})")

        # Compile main script
        source = Path(script_path).read_text(encoding="utf-8")
        params_bytes = params_json.encode("utf-8") if params_json else None

        prog = lib.binscript_compiler_compile(compiler, source.encode("utf-8"), params_bytes)
        if not prog:
            _check_error(lib, "compile")
            sys.exit(1)

        return prog
    finally:
        lib.binscript_compiler_free(compiler)


# ═══════════════════════════════════════════════════════════════════════════════
#  Helper: save program to bytes
# ═══════════════════════════════════════════════════════════════════════════════

def _save_program(lib: ctypes.CDLL, prog: ctypes.c_void_p) -> bytes:
    """Serialize a compiled program to bytes."""
    size = lib.binscript_save(prog, None, 0)
    if size < 0:
        _check_error(lib, "save (query size)")
        sys.exit(1)

    buf = (ctypes.c_uint8 * size)()
    written = lib.binscript_save(prog, buf, size)
    if written < 0:
        _check_error(lib, "save")
        sys.exit(1)

    return bytes(buf[:written])


# ═══════════════════════════════════════════════════════════════════════════════
#  Helper: load program from .bsc file
# ═══════════════════════════════════════════════════════════════════════════════

def _load_program(lib: ctypes.CDLL, bsc_path: str) -> ctypes.c_void_p:
    """Load a compiled program from a .bsc file."""
    data = Path(bsc_path).read_bytes()
    buf = (ctypes.c_uint8 * len(data))(*data)
    prog = lib.binscript_load(buf, len(data))
    if not prog:
        _check_error(lib, "load")
        sys.exit(1)
    return prog


# ═══════════════════════════════════════════════════════════════════════════════
#  Helper: parse modules from --module args
# ═══════════════════════════════════════════════════════════════════════════════

def _parse_module_args(module_args: list[str] | None) -> list[tuple[str, str]]:
    """Parse --module name=path arguments."""
    if not module_args:
        return []
    result = []
    for m in module_args:
        if "=" not in m:
            print(f"Error: --module must be name=path, got: {m}", file=sys.stderr)
            sys.exit(1)
        name, path = m.split("=", 1)
        result.append((name, path))
    return result


# ═══════════════════════════════════════════════════════════════════════════════
#  Commands
# ═══════════════════════════════════════════════════════════════════════════════

def cmd_parse(args: argparse.Namespace) -> None:
    """Compile .bsx, parse binary → JSON."""
    lib = load_dll(args.dll)
    modules = _parse_module_args(args.module)
    prog = _compile_script(lib, args.script, modules, args.stdlib_dir, args.params)

    try:
        data = Path(args.input).read_bytes()
        buf = (ctypes.c_uint8 * len(data))(*data)

        params_bytes = args.params.encode("utf-8") if args.params else None

        if args.entry:
            json_ptr = lib.binscript_to_json_entry(
                prog,
                args.entry.encode("utf-8"),
                ctypes.cast(buf, ctypes.c_void_p),
                len(data),
                params_bytes,
            )
        else:
            json_ptr = lib.binscript_to_json(
                prog,
                ctypes.cast(buf, ctypes.c_void_p),
                len(data),
                params_bytes,
            )

        if not json_ptr:
            _check_error(lib, "to_json")
            sys.exit(1)

        result = ctypes.cast(json_ptr, ctypes.c_char_p).value.decode("utf-8")
        lib.binscript_mem_free(json_ptr)

        # Pretty-print JSON
        parsed = json.loads(result)
        print(json.dumps(parsed, indent=2))
    finally:
        lib.binscript_free(prog)


def cmd_produce(args: argparse.Namespace) -> None:
    """Compile .bsx, produce binary from JSON."""
    lib = load_dll(args.dll)
    modules = _parse_module_args(args.module)
    prog = _compile_script(lib, args.script, modules, args.stdlib_dir, args.params)

    try:
        json_text = Path(args.json_file).read_text(encoding="utf-8")
        params_bytes = args.params.encode("utf-8") if args.params else None

        out_len = ctypes.c_size_t(0)
        data_ptr = lib.binscript_from_json(
            prog,
            json_text.encode("utf-8"),
            ctypes.byref(out_len),
            params_bytes,
        )

        if not data_ptr:
            _check_error(lib, "from_json")
            sys.exit(1)

        result = ctypes.string_at(data_ptr, out_len.value)
        lib.binscript_mem_free(data_ptr)

        output_path = args.output or "output.bin"
        Path(output_path).write_bytes(result)
        print(f"Produced {len(result)} bytes → {output_path}")
    finally:
        lib.binscript_free(prog)


def cmd_compile(args: argparse.Namespace) -> None:
    """Compile .bsx → .bsc bytecode file."""
    lib = load_dll(args.dll)
    modules = _parse_module_args(args.module)
    prog = _compile_script(lib, args.script, modules, args.stdlib_dir, args.params)

    try:
        bsc_data = _save_program(lib, prog)

        output_path = args.output or Path(args.script).with_suffix(".bsc")
        Path(output_path).write_bytes(bsc_data)
        print(f"Compiled {len(bsc_data)} bytes → {output_path}")
    finally:
        lib.binscript_free(prog)


def cmd_disasm(args: argparse.Namespace) -> None:
    """Disassemble a .bsx script or .bsc file."""
    input_path = Path(args.input)

    if input_path.suffix.lower() == ".bsc":
        # Direct disassembly of .bsc file (no DLL needed)
        data = input_path.read_bytes()
        prog = parse_bsc(data)
        disassemble(prog)
    elif input_path.suffix.lower() == ".bsx":
        # Compile via DLL, save to bytes, then disassemble
        lib = load_dll(args.dll)
        modules = _parse_module_args(args.module)
        handle = _compile_script(lib, str(input_path), modules, args.stdlib_dir, args.params)
        try:
            bsc_data = _save_program(lib, handle)
            disassemble_bsc_bytes(bsc_data)
        finally:
            lib.binscript_free(handle)
    else:
        print(f"Error: expected .bsx or .bsc file, got: {input_path.suffix}", file=sys.stderr)
        sys.exit(1)


def cmd_hexdump(args: argparse.Namespace) -> None:
    """Hex dump of a .bsc file."""
    input_path = Path(args.input)

    if input_path.suffix.lower() == ".bsx":
        # Compile first, then hexdump
        lib = load_dll(args.dll)
        modules = _parse_module_args(args.module)
        handle = _compile_script(lib, str(input_path), modules, args.stdlib_dir, args.params)
        try:
            bsc_data = _save_program(lib, handle)
            hexdump_bsc(bsc_data)
        finally:
            lib.binscript_free(handle)
    else:
        data = input_path.read_bytes()
        hexdump_bsc(data)


def cmd_run(args: argparse.Namespace) -> None:
    """Load pre-compiled .bsc, parse binary → JSON."""
    lib = load_dll(args.dll)
    prog = _load_program(lib, args.bsc)

    try:
        data = Path(args.input).read_bytes()
        buf = (ctypes.c_uint8 * len(data))(*data)

        params_bytes = args.params.encode("utf-8") if args.params else None

        if args.entry:
            json_ptr = lib.binscript_to_json_entry(
                prog,
                args.entry.encode("utf-8"),
                ctypes.cast(buf, ctypes.c_void_p),
                len(data),
                params_bytes,
            )
        else:
            json_ptr = lib.binscript_to_json(
                prog,
                ctypes.cast(buf, ctypes.c_void_p),
                len(data),
                params_bytes,
            )

        if not json_ptr:
            _check_error(lib, "to_json")
            sys.exit(1)

        result = ctypes.cast(json_ptr, ctypes.c_char_p).value.decode("utf-8")
        lib.binscript_mem_free(json_ptr)

        parsed = json.loads(result)
        print(json.dumps(parsed, indent=2))
    finally:
        lib.binscript_free(prog)


def cmd_info(args: argparse.Namespace) -> None:
    """Print DLL version and path."""
    lib = load_dll(args.dll)
    version = lib.binscript_version()
    if version:
        print(f"BinScript version: {version.decode('utf-8')}")
    else:
        print("BinScript version: <unknown>")

    # Show discovered DLL path
    if args.dll:
        print(f"DLL path: {args.dll}")
    else:
        path = _find_dll()
        print(f"DLL path: {path}" if path else "DLL path: <auto-discovered>")


# ═══════════════════════════════════════════════════════════════════════════════
#  Argument Parser
# ═══════════════════════════════════════════════════════════════════════════════

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="bsxtool",
        description="CLI tool exercising the BinScript C-ABI DLL",
    )
    parser.add_argument(
        "--dll", metavar="PATH",
        help="Path to BinScript NativeAOT DLL (auto-discovered if omitted)",
    )

    subs = parser.add_subparsers(dest="command", required=True)

    # ── parse ────────────────────────────────────────────
    p_parse = subs.add_parser("parse", help="Compile .bsx and parse binary → JSON")
    p_parse.add_argument("script", help="Path to .bsx script")
    p_parse.add_argument("input", help="Path to binary input file")
    p_parse.add_argument("--entry", help="Named entry point (struct name)")
    p_parse.add_argument("--params", help="Runtime parameters as JSON string")
    p_parse.add_argument("--module", action="append", metavar="NAME=PATH",
                         help="Register a module (can repeat)")
    p_parse.add_argument("--stdlib-dir", metavar="DIR",
                         help="Auto-register all .bsx files as modules")
    p_parse.set_defaults(func=cmd_parse)

    # ── produce ──────────────────────────────────────────
    p_produce = subs.add_parser("produce", help="Compile .bsx and produce binary from JSON")
    p_produce.add_argument("script", help="Path to .bsx script")
    p_produce.add_argument("json_file", help="Path to JSON input file")
    p_produce.add_argument("-o", "--output", metavar="PATH", help="Output binary path")
    p_produce.add_argument("--params", help="Runtime parameters as JSON string")
    p_produce.add_argument("--module", action="append", metavar="NAME=PATH",
                           help="Register a module (can repeat)")
    p_produce.add_argument("--stdlib-dir", metavar="DIR",
                           help="Auto-register all .bsx files as modules")
    p_produce.set_defaults(func=cmd_produce)

    # ── compile ──────────────────────────────────────────
    p_compile = subs.add_parser("compile", help="Compile .bsx → .bsc bytecode")
    p_compile.add_argument("script", help="Path to .bsx script")
    p_compile.add_argument("-o", "--output", metavar="PATH", help="Output .bsc path")
    p_compile.add_argument("--params", help="Compile-time parameters as JSON string")
    p_compile.add_argument("--module", action="append", metavar="NAME=PATH",
                           help="Register a module (can repeat)")
    p_compile.add_argument("--stdlib-dir", metavar="DIR",
                           help="Auto-register all .bsx files as modules")
    p_compile.set_defaults(func=cmd_compile)

    # ── disasm ───────────────────────────────────────────
    p_disasm = subs.add_parser("disasm", help="Disassemble .bsx or .bsc to readable form")
    p_disasm.add_argument("input", help="Path to .bsx script or .bsc bytecode")
    p_disasm.add_argument("--params", help="Compile-time parameters as JSON string")
    p_disasm.add_argument("--module", action="append", metavar="NAME=PATH",
                          help="Register a module (can repeat)")
    p_disasm.add_argument("--stdlib-dir", metavar="DIR",
                          help="Auto-register all .bsx files as modules")
    p_disasm.set_defaults(func=cmd_disasm)

    # ── hexdump ──────────────────────────────────────────
    p_hexdump = subs.add_parser("hexdump", help="Annotated hex dump of .bsc or .bsx file")
    p_hexdump.add_argument("input", help="Path to .bsc file (or .bsx to compile first)")
    p_hexdump.add_argument("--params", help="Compile-time parameters as JSON string")
    p_hexdump.add_argument("--module", action="append", metavar="NAME=PATH",
                           help="Register a module (can repeat)")
    p_hexdump.add_argument("--stdlib-dir", metavar="DIR",
                           help="Auto-register all .bsx files as modules")
    p_hexdump.set_defaults(func=cmd_hexdump)

    # ── run ──────────────────────────────────────────────
    p_run = subs.add_parser("run", help="Load .bsc and parse binary → JSON")
    p_run.add_argument("bsc", help="Path to compiled .bsc file")
    p_run.add_argument("input", help="Path to binary input file")
    p_run.add_argument("--entry", help="Named entry point (struct name)")
    p_run.add_argument("--params", help="Runtime parameters as JSON string")
    p_run.set_defaults(func=cmd_run)

    # ── info ─────────────────────────────────────────────
    p_info = subs.add_parser("info", help="Print DLL version and path")
    p_info.set_defaults(func=cmd_info)

    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
