"""
Annotated hex dump of BSC (compiled BinScript bytecode) files.

Parses the BSC header to identify section boundaries, then prints a
hex dump with section labels and decoded header fields.
"""

from __future__ import annotations

import struct
import sys


MAGIC = b"BSC\x01"
HEADER_SIZE = 28


def _get_utf8_stdout():
    """Return a stdout wrapper that handles Unicode on Windows."""
    import io
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        return sys.stdout
    return io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")


def hexdump_bsc(data: bytes, out=None, width: int = 16) -> None:
    """Print an annotated hex dump of a BSC binary."""
    if out is None:
        out = _get_utf8_stdout()

    if len(data) < HEADER_SIZE:
        out.write(f"Error: data too short ({len(data)} bytes, need >= {HEADER_SIZE})\n")
        _raw_hexdump(data, 0, len(data), out, width)
        return

    # Parse header to get section sizes
    magic = data[0:4]
    if magic != MAGIC:
        out.write(f"Warning: bad magic {magic!r} (expected {MAGIC!r})\n\n")

    version = struct.unpack_from("<H", data, 4)[0]
    flags = struct.unpack_from("<H", data, 6)[0]
    has_source = bool(flags & 1)
    bytecode_len = struct.unpack_from("<I", data, 8)[0]
    string_table_len = struct.unpack_from("<I", data, 12)[0]
    struct_table_len = struct.unpack_from("<I", data, 16)[0]
    source_len = struct.unpack_from("<I", data, 20)[0]
    root_struct_idx = struct.unpack_from("<H", data, 24)[0]
    param_count = struct.unpack_from("<H", data, 26)[0]

    # Decoded header summary
    out.write("╔══════════════════════════════════════════════════════════════════╗\n")
    out.write("║  BSC File Hex Dump                                              ║\n")
    out.write("╠══════════════════════════════════════════════════════════════════╣\n")
    out.write(f"║  Version:         {version:<47d}║\n")
    out.write(f"║  Flags:           0x{flags:04X} ({'has_source' if has_source else 'no_source'}){' ' * (42 - len('has_source' if has_source else 'no_source'))}║\n")
    out.write(f"║  Bytecode:        {bytecode_len:<47d}║\n")
    out.write(f"║  String table:    {string_table_len:<47d}║\n")
    out.write(f"║  Struct table:    {struct_table_len:<47d}║\n")
    out.write(f"║  Source:          {source_len:<47d}║\n")
    out.write(f"║  Root struct:     {root_struct_idx:<47d}║\n")
    out.write(f"║  Param count:     {param_count:<47d}║\n")
    out.write("╚══════════════════════════════════════════════════════════════════╝\n\n")

    # Compute section boundaries
    pos = HEADER_SIZE
    sections: list[tuple[str, int, int]] = []

    # Parameters section — size not directly in header, must walk
    param_start = pos
    for _ in range(param_count):
        if pos + 5 > len(data):
            break
        pos += 2 + 1  # name_idx + type_tag
        val_len = struct.unpack_from("<H", data, pos)[0]
        pos += 2 + val_len
    if param_count > 0:
        sections.append(("Parameters", param_start, pos - param_start))

    sections.append(("String Table", pos, string_table_len))
    str_end = pos + string_table_len

    sections.append(("Struct Metadata", str_end, struct_table_len))
    struct_end = str_end + struct_table_len

    sections.append(("Bytecode", struct_end, bytecode_len))
    bc_end = struct_end + bytecode_len

    if has_source and source_len > 0:
        sections.append(("Source", bc_end, source_len))

    # Dump header
    _section_header(out, "Header", 0, HEADER_SIZE)
    _raw_hexdump(data, 0, min(HEADER_SIZE, len(data)), out, width)
    out.write("\n")

    # Dump each section
    for name, offset, length in sections:
        _section_header(out, name, offset, length)
        end = min(offset + length, len(data))
        if end > offset:
            _raw_hexdump(data, offset, end, out, width)
        else:
            out.write("  <empty>\n")
        out.write("\n")

    # Trailing bytes (if any)
    total_expected = HEADER_SIZE
    if sections:
        last = sections[-1]
        total_expected = last[1] + last[2]
    if total_expected < len(data):
        _section_header(out, "Trailing", total_expected, len(data) - total_expected)
        _raw_hexdump(data, total_expected, len(data), out, width)
        out.write("\n")


def _section_header(out, name: str, offset: int, length: int) -> None:
    out.write(f"── {name} (offset=0x{offset:04X}, length={length}) ──\n")


def _raw_hexdump(data: bytes, start: int, end: int, out, width: int = 16) -> None:
    """Print a classic hex dump with offset, hex bytes, and ASCII."""
    for row_start in range(start, end, width):
        row_end = min(row_start + width, end)
        row_bytes = data[row_start:row_end]

        hex_part = " ".join(f"{b:02X}" for b in row_bytes)
        # Pad hex part to consistent width
        hex_part = hex_part.ljust(width * 3 - 1)

        ascii_part = "".join(chr(b) if 32 <= b < 127 else "." for b in row_bytes)

        out.write(f"  {row_start:08X}  {hex_part}  |{ascii_part}|\n")


def hexdump_bsc_file(path: str, out=None, width: int = 16) -> None:
    """Load a .bsc file and print an annotated hex dump."""
    with open(path, "rb") as f:
        data = f.read()
    hexdump_bsc(data, out, width)
