"""
BinScript bytecode disassembler.

Decodes the BSC binary format and prints human-readable assembly listings.
The opcode table mirrors src/BinScript.Core/Bytecode/Opcode.cs — when opcodes
change there, they MUST be updated here too.
"""

from __future__ import annotations

import struct
import sys
from dataclasses import dataclass, field
from enum import IntEnum
from typing import BinaryIO

# ── Opcode enum (mirrors Opcode.cs) ─────────────────────────────────────────

class Opcode(IntEnum):
    # Tier 1 — Fast-Path Instructions
    READ_U8         = 0x01
    READ_I8         = 0x02
    READ_U16_LE     = 0x03
    READ_U16_BE     = 0x04
    READ_I16_LE     = 0x05
    READ_I16_BE     = 0x06
    READ_U32_LE     = 0x07
    READ_U32_BE     = 0x08
    READ_I32_LE     = 0x09
    READ_I32_BE     = 0x0A
    READ_U64_LE     = 0x0B
    READ_U64_BE     = 0x0C
    READ_I64_LE     = 0x0D
    READ_I64_BE     = 0x0E
    READ_F32_LE     = 0x0F
    READ_F32_BE     = 0x10
    READ_F64_LE     = 0x11
    READ_F64_BE     = 0x12
    READ_BOOL       = 0x13
    READ_FIXED_STR  = 0x14
    READ_CSTRING    = 0x15
    READ_BYTES_FIXED = 0x16
    SKIP_FIXED      = 0x17
    ASSERT_VALUE    = 0x18

    # Control flow
    CALL_STRUCT     = 0x40
    RETURN          = 0x41
    JUMP            = 0x42
    JUMP_IF_FALSE   = 0x43
    JUMP_IF_TRUE    = 0x44

    # Seeking
    SEEK_ABS        = 0x50
    SEEK_PUSH       = 0x51
    SEEK_POP        = 0x52

    # Emitter events
    EMIT_STRUCT_BEGIN  = 0x60
    EMIT_STRUCT_END    = 0x61
    EMIT_ARRAY_BEGIN   = 0x62
    EMIT_ARRAY_END     = 0x63
    EMIT_VARIANT_BEGIN = 0x64
    EMIT_VARIANT_END   = 0x65
    EMIT_BITS_BEGIN    = 0x66
    EMIT_BITS_END      = 0x67

    # Dynamic reads
    READ_BYTES_DYN  = 0x70
    READ_STRING_DYN = 0x71
    READ_BITS       = 0x72
    READ_BIT        = 0x73

    # Pointer operations
    READ_PTR_U32    = 0x74
    READ_PTR_U64    = 0x75
    EMIT_NULL       = 0x76
    COPY_CHILD_FIELD = 0x77
    FORWARD_ARRAY_STORE = 0x78
    FORWARD_PARAM_ARRAY_STORE = 0x79
    ARRAY_SEARCH_BEGIN_PARAM = 0x7A
    EXTRACT_ARRAY_ELEM_FIELD = 0x7B

    # Expression VM
    PUSH_CONST_I64  = 0x80
    PUSH_CONST_F64  = 0x81
    PUSH_CONST_STR  = 0x82
    PUSH_FIELD_VAL  = 0x83
    PUSH_PARAM      = 0x84
    PUSH_RUNTIME_VAR = 0x85
    PUSH_INDEX      = 0x86
    STORE_FIELD_VAL = 0x87
    PUSH_FILE_PARAM = 0x88

    # Arithmetic/logic
    OP_ADD          = 0x90
    OP_SUB          = 0x91
    OP_MUL          = 0x92
    OP_DIV          = 0x93
    OP_MOD          = 0x94
    OP_AND          = 0x95
    OP_OR           = 0x96
    OP_XOR          = 0x97
    OP_NOT          = 0x98
    OP_SHL          = 0x99
    OP_SHR          = 0x9A
    OP_EQ           = 0x9B
    OP_NE           = 0x9C
    OP_LT           = 0x9D
    OP_GT           = 0x9E
    OP_LE           = 0x9F
    OP_GE           = 0xA0
    OP_LOGICAL_AND  = 0xA1
    OP_LOGICAL_OR   = 0xA2
    OP_LOGICAL_NOT  = 0xA3
    OP_NEG          = 0xA4

    # Built-in functions
    FN_SIZEOF       = 0xB0
    FN_OFFSET_OF    = 0xB1
    FN_COUNT        = 0xB2
    FN_STRLEN       = 0xB3
    FN_CRC32        = 0xB4
    FN_ADLER32      = 0xB5

    # String methods
    STR_STARTS_WITH = 0xC0
    STR_ENDS_WITH   = 0xC1
    STR_CONTAINS    = 0xC2

    # Array control
    ARRAY_BEGIN_COUNT    = 0xD0
    ARRAY_BEGIN_UNTIL    = 0xD1
    ARRAY_BEGIN_SENTINEL = 0xD2
    ARRAY_BEGIN_GREEDY   = 0xD3
    ARRAY_NEXT           = 0xD4
    ARRAY_END            = 0xD5

    # Array search
    ARRAY_STORE_ELEM     = 0xD6
    ARRAY_SEARCH_BEGIN   = 0xD7
    PUSH_ELEM_FIELD      = 0xD8
    ARRAY_SEARCH_CHECK   = 0xD9
    ARRAY_SEARCH_COPY    = 0xDA
    ARRAY_SEARCH_END     = 0xDB
    SENTINEL_SAVE        = 0xDC
    SENTINEL_CHECK       = 0xDD

    # Match
    MATCH_BEGIN     = 0xE0
    MATCH_ARM_EQ    = 0xE1
    MATCH_ARM_RANGE = 0xE2
    MATCH_ARM_GUARD = 0xE3
    MATCH_DEFAULT   = 0xE4
    MATCH_END       = 0xE5

    # Alignment
    ALIGN           = 0xF0
    ALIGN_FIXED     = 0xF1


# ── String encoding names ────────────────────────────────────────────────────

_ENCODING_NAMES = {0: "utf8", 1: "ascii", 2: "utf16le", 3: "utf16be", 4: "latin1"}

# ── Runtime variable names ───────────────────────────────────────────────────

_RUNTIME_VAR_NAMES = {0: "@input_size", 1: "@offset", 2: "@remaining"}

# ── Struct flag names ────────────────────────────────────────────────────────

_STRUCT_FLAG_NAMES = {1: "root", 2: "bits", 4: "partial_coverage"}

# ── Field flag names ─────────────────────────────────────────────────────────

_FIELD_FLAG_NAMES = {1: "derived", 2: "hidden"}


# ── BSC parsed structures ───────────────────────────────────────────────────

@dataclass
class FieldMeta:
    name_index: int
    flags: int


@dataclass
class StructMeta:
    name_index: int
    param_count: int
    fields: list[FieldMeta]
    bytecode_offset: int
    bytecode_length: int
    static_size: int
    flags: int


@dataclass
class BscProgram:
    version: int
    has_source: bool
    bytecode: bytes
    string_table: list[str]
    structs: list[StructMeta]
    root_struct_index: int
    parameters: dict[str, object]
    source: str | None


# ── BSC parser ───────────────────────────────────────────────────────────────

MAGIC = b"BSC\x01"


def parse_bsc(data: bytes) -> BscProgram:
    """Parse a .bsc binary into a BscProgram structure."""
    if len(data) < 28:
        raise ValueError(f"BSC data too short ({len(data)} bytes, need >= 28)")
    if data[:4] != MAGIC:
        raise ValueError(f"Invalid BSC magic: {data[:4]!r} (expected {MAGIC!r})")

    pos = 4
    version = struct.unpack_from("<H", data, pos)[0]; pos += 2
    flags = struct.unpack_from("<H", data, pos)[0]; pos += 2
    has_source = bool(flags & 1)
    bytecode_len = struct.unpack_from("<I", data, pos)[0]; pos += 4
    string_table_len = struct.unpack_from("<I", data, pos)[0]; pos += 4
    struct_table_len = struct.unpack_from("<I", data, pos)[0]; pos += 4
    source_len = struct.unpack_from("<I", data, pos)[0]; pos += 4
    root_struct_idx = struct.unpack_from("<H", data, pos)[0]; pos += 2
    root_index = -1 if root_struct_idx == 0xFFFF else root_struct_idx
    param_count = struct.unpack_from("<H", data, pos)[0]; pos += 2

    # Parameters (raw — need string table for names)
    raw_params: list[tuple[int, int, bytes]] = []
    for _ in range(param_count):
        name_idx = struct.unpack_from("<H", data, pos)[0]; pos += 2
        type_tag = data[pos]; pos += 1
        value_len = struct.unpack_from("<H", data, pos)[0]; pos += 2
        value = data[pos:pos + value_len]; pos += value_len
        raw_params.append((name_idx, type_tag, value))

    # String table
    str_count = struct.unpack_from("<I", data, pos)[0]; pos += 4
    string_table: list[str] = []
    for _ in range(str_count):
        s_len = struct.unpack_from("<H", data, pos)[0]; pos += 2
        string_table.append(data[pos:pos + s_len].decode("utf-8")); pos += s_len

    # Resolve parameters
    parameters: dict[str, object] = {}
    for name_idx, type_tag, value in raw_params:
        name = string_table[name_idx] if name_idx < len(string_table) else f"param_{name_idx}"
        if type_tag == 0:
            parameters[name] = struct.unpack_from("<q", value)[0]
        elif type_tag == 1:
            parameters[name] = struct.unpack_from("<d", value)[0]
        elif type_tag == 2:
            parameters[name] = value.decode("utf-8")
        elif type_tag == 3:
            parameters[name] = value[0] != 0
        else:
            parameters[name] = struct.unpack_from("<q", value)[0]

    # Struct metadata table
    struct_count = struct.unpack_from("<H", data, pos)[0]; pos += 2
    structs: list[StructMeta] = []
    for _ in range(struct_count):
        name_index = struct.unpack_from("<H", data, pos)[0]; pos += 2
        param_cnt = data[pos]; pos += 1
        field_cnt = struct.unpack_from("<H", data, pos)[0]; pos += 2
        bc_offset = struct.unpack_from("<I", data, pos)[0]; pos += 4
        bc_len = struct.unpack_from("<I", data, pos)[0]; pos += 4
        static_size = struct.unpack_from("<i", data, pos)[0]; pos += 4
        s_flags = struct.unpack_from("<H", data, pos)[0]; pos += 2

        fields: list[FieldMeta] = []
        for _ in range(field_cnt):
            f_name_idx = struct.unpack_from("<H", data, pos)[0]; pos += 2
            f_flags = data[pos]; pos += 1
            fields.append(FieldMeta(f_name_idx, f_flags))

        structs.append(StructMeta(
            name_index=name_index,
            param_count=param_cnt,
            fields=fields,
            bytecode_offset=bc_offset,
            bytecode_length=bc_len,
            static_size=static_size,
            flags=s_flags,
        ))

    # Bytecode section
    bytecode = data[pos:pos + bytecode_len]; pos += bytecode_len

    # Source section (optional)
    source = None
    if has_source and source_len > 0:
        source = data[pos:pos + source_len].decode("utf-8")

    return BscProgram(
        version=version,
        has_source=has_source,
        bytecode=bytecode,
        string_table=string_table,
        structs=structs,
        root_struct_index=root_index,
        parameters=parameters,
        source=source,
    )


# ── Opcode operand formats ──────────────────────────────────────────────────

# Each entry: (total_operand_bytes, decoder_function_name)
# Decoder functions return (formatted_string, bytes_consumed).

def _dec_field_id(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    name = _resolve_field(fid, prog)
    return f"{name} (fid={fid})", 2

def _dec_field_id_len_enc(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    length = struct.unpack_from("<H", bc, ip + 2)[0]
    enc = bc[ip + 4]
    name = _resolve_field(fid, prog)
    enc_name = _ENCODING_NAMES.get(enc, f"enc={enc}")
    return f"{name} (fid={fid}), len={length}, {enc_name}", 5

def _dec_field_id_enc(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    enc = bc[ip + 2]
    name = _resolve_field(fid, prog)
    enc_name = _ENCODING_NAMES.get(enc, f"enc={enc}")
    return f"{name} (fid={fid}), {enc_name}", 3

def _dec_field_id_len32(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    length = struct.unpack_from("<I", bc, ip + 2)[0]
    name = _resolve_field(fid, prog)
    return f"{name} (fid={fid}), len={length}", 6

def _decode_inline_value(bc: bytes, ip: int, type_tag: int, prog: BscProgram) -> tuple[str, int]:
    """Decode a tagged inline value (type_tag already read). Returns (display_str, bytes_consumed)."""
    if type_tag == 0:  # i64
        val = struct.unpack_from("<q", bc, ip)[0]
        return f"{val} (0x{val & 0xFFFFFFFFFFFFFFFF:X})", 8
    elif type_tag == 2:  # string
        sid = struct.unpack_from("<H", bc, ip)[0]
        s = _resolve_string(sid, prog)
        return f"{s!r} (sid={sid})", 2
    elif type_tag == 3:  # bool
        val = bc[ip] != 0
        return str(val), 1
    else:
        return f"<unknown type_tag={type_tag}>", 0

def _dec_skip(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    length = struct.unpack_from("<H", bc, ip)[0]
    return f"len={length}", 2

def _dec_assert(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    name = _resolve_field(fid, prog)
    type_tag = bc[ip + 2]
    consumed = 3  # field_id + type_tag
    val_str, val_size = _decode_inline_value(bc, ip + 3, type_tag, prog)
    consumed += val_size
    return f"{name} (fid={fid}), expected={val_str}", consumed

def _dec_none(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    return "", 0

def _dec_call_struct(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    sid = struct.unpack_from("<H", bc, ip)[0]
    param_count = bc[ip + 2]
    sname = _resolve_struct_name(sid, prog)
    return f"{sname} (sid={sid}), params={param_count}", 3

def _dec_jump(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    offset = struct.unpack_from("<i", bc, ip)[0]
    target = (ip + 4) + offset  # offset is relative to end of operand
    return f"offset={offset:+d} → @{target}", 4

def _dec_emit_struct_begin(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    name_id = struct.unpack_from("<H", bc, ip)[0]
    fid = struct.unpack_from("<H", bc, ip + 2)[0]
    sname = _resolve_string(name_id, prog)
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid}), type={sname!r}", 4

def _dec_emit_array_begin(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    name_id = struct.unpack_from("<H", bc, ip)[0]
    fid = struct.unpack_from("<H", bc, ip + 2)[0]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid})", 4

def _dec_read_bytes_dyn(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid})", 2

def _dec_read_string_dyn(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    enc = bc[ip + 2]
    fname = _resolve_field(fid, prog)
    enc_name = _ENCODING_NAMES.get(enc, f"enc={enc}")
    return f"{fname} (fid={fid}), {enc_name}", 3

def _dec_read_bits(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    bit_count = bc[ip + 2]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid}), bits={bit_count}", 3

def _dec_push_i64(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    val = struct.unpack_from("<q", bc, ip)[0]
    return f"{val} (0x{val & 0xFFFFFFFFFFFFFFFF:X})", 8

def _dec_push_f64(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    val = struct.unpack_from("<d", bc, ip)[0]
    return f"{val}", 8

def _dec_push_str(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    sid = struct.unpack_from("<H", bc, ip)[0]
    s = _resolve_string(sid, prog)
    return f"{s!r} (sid={sid})", 2

def _dec_push_field(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid})", 2

def _dec_push_param(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    idx = struct.unpack_from("<H", bc, ip)[0]
    return f"param[{idx}]", 2

def _dec_push_runtime_var(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    var_id = bc[ip]
    name = _RUNTIME_VAR_NAMES.get(var_id, f"var={var_id}")
    return name, 1

def _dec_fn_field(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid})", 2

def _dec_fn_multi(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    count = bc[ip]
    return f"field_count={count}", 1

def _dec_array_until(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    offset = struct.unpack_from("<i", bc, ip)[0]
    target = (ip + 4) + offset
    return f"cond_offset={offset:+d} → @{target}", 4

def _dec_match_begin(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    return "", 0

def _dec_match_arm_eq(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    type_tag = bc[ip]
    consumed = 1  # type_tag
    val_str, val_size = _decode_inline_value(bc, ip + 1, type_tag, prog)
    consumed += val_size
    jump = struct.unpack_from("<i", bc, ip + consumed)[0]
    target = (ip + consumed + 4) + jump
    consumed += 4
    return f"value={val_str}, jump={jump:+d} → @{target}", consumed

def _dec_match_arm_range(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    consumed = 0
    # low value (tagged)
    lo_tag = bc[ip + consumed]; consumed += 1
    lo_str, lo_size = _decode_inline_value(bc, ip + consumed, lo_tag, prog)
    consumed += lo_size
    # high value (tagged)
    hi_tag = bc[ip + consumed]; consumed += 1
    hi_str, hi_size = _decode_inline_value(bc, ip + consumed, hi_tag, prog)
    consumed += hi_size
    # jump
    jump = struct.unpack_from("<i", bc, ip + consumed)[0]
    target = (ip + consumed + 4) + jump
    consumed += 4
    return f"lo={lo_str}, hi={hi_str}, jump={jump:+d} → @{target}", consumed

def _dec_match_arm_guard(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    return f"binder_field={fid}", 2

def _dec_match_default(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    jump = struct.unpack_from("<i", bc, ip)[0]
    target = (ip + 4) + jump
    return f"jump={jump:+d} → @{target}", 4

def _dec_align_fixed(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    alignment = struct.unpack_from("<H", bc, ip)[0]
    return f"alignment={alignment}", 2

def _dec_emit_variant_begin(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    name_id = struct.unpack_from("<H", bc, ip)[0]
    fid = struct.unpack_from("<H", bc, ip + 2)[0]
    fname = _resolve_field(fid, prog)
    vname = _resolve_string(name_id, prog)
    return f"{fname} (fid={fid}), variant={vname!r}", 4

def _dec_emit_bits_begin(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    name_id = struct.unpack_from("<H", bc, ip)[0]
    fid = struct.unpack_from("<H", bc, ip + 2)[0]
    fname = _resolve_field(fid, prog)
    sname = _resolve_string(name_id, prog)
    return f"{fname} (fid={fid}), type={sname!r}", 4

def _dec_store_field(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid})", 2

def _dec_push_file_param(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    sid = struct.unpack_from("<H", bc, ip)[0]
    s = _resolve_string(sid, prog)
    return f"{s!r} (sid={sid})", 2

def _dec_copy_child(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    child_name_idx = struct.unpack_from("<H", bc, ip)[0]
    dst_fid = struct.unpack_from("<H", bc, ip + 2)[0]
    child_name = _resolve_string(child_name_idx, prog)
    dst_name = _resolve_field(dst_fid, prog)
    return f"child={child_name!r} (sid={child_name_idx}) → {dst_name} (fid={dst_fid})", 4

def _dec_extract_array_elem_field(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    arr_fid = struct.unpack_from("<H", bc, ip)[0]
    elem_idx = struct.unpack_from("<H", bc, ip + 2)[0]
    elem_fname_idx = struct.unpack_from("<H", bc, ip + 4)[0]
    dst_fid = struct.unpack_from("<H", bc, ip + 6)[0]
    arr_name = _resolve_field(arr_fid, prog)
    elem_fname = _resolve_string(elem_fname_idx, prog)
    dst_name = _resolve_field(dst_fid, prog)
    return f"{arr_name}[{elem_idx}].{elem_fname} → {dst_name} (fid={dst_fid})", 8

def _dec_forward_array_store(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    dst_param = struct.unpack_from("<H", bc, ip + 2)[0]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid}) → param[{dst_param}]", 4

def _dec_forward_param_array_store(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    src_param = struct.unpack_from("<H", bc, ip)[0]
    dst_param = struct.unpack_from("<H", bc, ip + 2)[0]
    return f"param[{src_param}] → param[{dst_param}]", 4

def _dec_array_search_begin_param(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    param_idx = struct.unpack_from("<H", bc, ip)[0]
    mode = bc[ip + 2]
    mode_names = {0: "find", 1: "find_or", 2: "any", 3: "all"}
    mode_name = mode_names.get(mode, f"mode={mode}")
    return f"param[{param_idx}], {mode_name}", 3

def _dec_array_store_elem(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid})", 2

def _dec_array_search_begin(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    mode = bc[ip + 2]
    fname = _resolve_field(fid, prog)
    mode_names = {0: "find", 1: "find_or", 2: "any", 3: "all"}
    mode_name = mode_names.get(mode, f"mode={mode}")
    return f"{fname} (fid={fid}), {mode_name}", 3

def _dec_push_elem_field(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    name_idx = struct.unpack_from("<H", bc, ip)[0]
    name = _resolve_string(name_idx, prog)
    return f"{name!r} (sid={name_idx})", 2

def _dec_array_search_check(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    loop_target = struct.unpack_from("<i", bc, ip)[0]
    not_found = struct.unpack_from("<i", bc, ip + 4)[0]
    lt = (ip + 4) + loop_target
    nf = (ip + 8) + not_found
    return f"loop → @{lt}, not_found → @{nf}", 8

def _dec_array_search_copy(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    src_name_idx = struct.unpack_from("<H", bc, ip)[0]
    dst_fid = struct.unpack_from("<H", bc, ip + 2)[0]
    src_name = _resolve_string(src_name_idx, prog)
    dst_name = _resolve_field(dst_fid, prog)
    return f"src={src_name!r} (sid={src_name_idx}) → dst={dst_name} (fid={dst_fid})", 4

def _dec_read_ptr_u32(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid}), width=u32", 2

def _dec_read_ptr_u64(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid}), width=u64", 2

def _dec_emit_null(bc: bytes, ip: int, prog: BscProgram) -> tuple[str, int]:
    fid = struct.unpack_from("<H", bc, ip)[0]
    fname = _resolve_field(fid, prog)
    return f"{fname} (fid={fid})", 2


# ── Opcode → decoder mapping ────────────────────────────────────────────────

_OPCODE_DECODERS: dict[int, tuple[str, callable]] = {
    # Tier 1
    Opcode.READ_U8:         ("READ_U8",         _dec_field_id),
    Opcode.READ_I8:         ("READ_I8",         _dec_field_id),
    Opcode.READ_U16_LE:     ("READ_U16_LE",     _dec_field_id),
    Opcode.READ_U16_BE:     ("READ_U16_BE",     _dec_field_id),
    Opcode.READ_I16_LE:     ("READ_I16_LE",     _dec_field_id),
    Opcode.READ_I16_BE:     ("READ_I16_BE",     _dec_field_id),
    Opcode.READ_U32_LE:     ("READ_U32_LE",     _dec_field_id),
    Opcode.READ_U32_BE:     ("READ_U32_BE",     _dec_field_id),
    Opcode.READ_I32_LE:     ("READ_I32_LE",     _dec_field_id),
    Opcode.READ_I32_BE:     ("READ_I32_BE",     _dec_field_id),
    Opcode.READ_U64_LE:     ("READ_U64_LE",     _dec_field_id),
    Opcode.READ_U64_BE:     ("READ_U64_BE",     _dec_field_id),
    Opcode.READ_I64_LE:     ("READ_I64_LE",     _dec_field_id),
    Opcode.READ_I64_BE:     ("READ_I64_BE",     _dec_field_id),
    Opcode.READ_F32_LE:     ("READ_F32_LE",     _dec_field_id),
    Opcode.READ_F32_BE:     ("READ_F32_BE",     _dec_field_id),
    Opcode.READ_F64_LE:     ("READ_F64_LE",     _dec_field_id),
    Opcode.READ_F64_BE:     ("READ_F64_BE",     _dec_field_id),
    Opcode.READ_BOOL:       ("READ_BOOL",       _dec_field_id),
    Opcode.READ_FIXED_STR:  ("READ_FIXED_STR",  _dec_field_id_len_enc),
    Opcode.READ_CSTRING:    ("READ_CSTRING",     _dec_field_id_enc),
    Opcode.READ_BYTES_FIXED:("READ_BYTES_FIXED", _dec_field_id_len32),
    Opcode.SKIP_FIXED:      ("SKIP_FIXED",       _dec_skip),
    Opcode.ASSERT_VALUE:    ("ASSERT_VALUE",     _dec_assert),

    # Control flow
    Opcode.CALL_STRUCT:     ("CALL_STRUCT",      _dec_call_struct),
    Opcode.RETURN:          ("RETURN",           _dec_none),
    Opcode.JUMP:            ("JUMP",             _dec_jump),
    Opcode.JUMP_IF_FALSE:   ("JUMP_IF_FALSE",    _dec_jump),
    Opcode.JUMP_IF_TRUE:    ("JUMP_IF_TRUE",     _dec_jump),

    # Seeking
    Opcode.SEEK_ABS:        ("SEEK_ABS",         _dec_none),
    Opcode.SEEK_PUSH:       ("SEEK_PUSH",        _dec_none),
    Opcode.SEEK_POP:        ("SEEK_POP",         _dec_none),

    # Emitter events
    Opcode.EMIT_STRUCT_BEGIN: ("EMIT_STRUCT_BEGIN", _dec_emit_struct_begin),
    Opcode.EMIT_STRUCT_END:   ("EMIT_STRUCT_END",   _dec_none),
    Opcode.EMIT_ARRAY_BEGIN:  ("EMIT_ARRAY_BEGIN",  _dec_emit_array_begin),
    Opcode.EMIT_ARRAY_END:    ("EMIT_ARRAY_END",    _dec_none),
    Opcode.EMIT_VARIANT_BEGIN:("EMIT_VARIANT_BEGIN", _dec_emit_variant_begin),
    Opcode.EMIT_VARIANT_END:  ("EMIT_VARIANT_END",  _dec_none),
    Opcode.EMIT_BITS_BEGIN:   ("EMIT_BITS_BEGIN",   _dec_emit_bits_begin),
    Opcode.EMIT_BITS_END:     ("EMIT_BITS_END",     _dec_none),

    # Dynamic reads
    Opcode.READ_BYTES_DYN:  ("READ_BYTES_DYN",   _dec_read_bytes_dyn),
    Opcode.READ_STRING_DYN: ("READ_STRING_DYN",  _dec_read_string_dyn),
    Opcode.READ_BITS:       ("READ_BITS",        _dec_read_bits),
    Opcode.READ_BIT:        ("READ_BIT",         _dec_field_id),

    # Pointer operations
    Opcode.READ_PTR_U32:    ("READ_PTR_U32",     _dec_read_ptr_u32),
    Opcode.READ_PTR_U64:    ("READ_PTR_U64",     _dec_read_ptr_u64),
    Opcode.EMIT_NULL:       ("EMIT_NULL",        _dec_emit_null),
    Opcode.COPY_CHILD_FIELD:("COPY_CHILD_FIELD", _dec_copy_child),
    Opcode.EXTRACT_ARRAY_ELEM_FIELD:("EXTRACT_ARRAY_ELEM_FIELD", _dec_extract_array_elem_field),
    Opcode.FORWARD_ARRAY_STORE:("FORWARD_ARRAY_STORE", _dec_forward_array_store),
    Opcode.FORWARD_PARAM_ARRAY_STORE:("FORWARD_PARAM_ARRAY_STORE", _dec_forward_param_array_store),
    Opcode.ARRAY_SEARCH_BEGIN_PARAM:("ARRAY_SEARCH_BEGIN_PARAM", _dec_array_search_begin_param),

    # Expression VM
    Opcode.PUSH_CONST_I64:  ("PUSH_CONST_I64",   _dec_push_i64),
    Opcode.PUSH_CONST_F64:  ("PUSH_CONST_F64",   _dec_push_f64),
    Opcode.PUSH_CONST_STR:  ("PUSH_CONST_STR",   _dec_push_str),
    Opcode.PUSH_FIELD_VAL:  ("PUSH_FIELD_VAL",   _dec_push_field),
    Opcode.PUSH_PARAM:      ("PUSH_PARAM",       _dec_push_param),
    Opcode.PUSH_RUNTIME_VAR:("PUSH_RUNTIME_VAR", _dec_push_runtime_var),
    Opcode.PUSH_INDEX:      ("PUSH_INDEX",       _dec_none),
    Opcode.STORE_FIELD_VAL: ("STORE_FIELD_VAL",  _dec_store_field),
    Opcode.PUSH_FILE_PARAM: ("PUSH_FILE_PARAM",  _dec_push_file_param),

    # Arithmetic/logic (all zero-operand)
    Opcode.OP_ADD:          ("OP_ADD",           _dec_none),
    Opcode.OP_SUB:          ("OP_SUB",           _dec_none),
    Opcode.OP_MUL:          ("OP_MUL",           _dec_none),
    Opcode.OP_DIV:          ("OP_DIV",           _dec_none),
    Opcode.OP_MOD:          ("OP_MOD",           _dec_none),
    Opcode.OP_AND:          ("OP_AND",           _dec_none),
    Opcode.OP_OR:           ("OP_OR",            _dec_none),
    Opcode.OP_XOR:          ("OP_XOR",           _dec_none),
    Opcode.OP_NOT:          ("OP_NOT",           _dec_none),
    Opcode.OP_SHL:          ("OP_SHL",           _dec_none),
    Opcode.OP_SHR:          ("OP_SHR",           _dec_none),
    Opcode.OP_EQ:           ("OP_EQ",            _dec_none),
    Opcode.OP_NE:           ("OP_NE",            _dec_none),
    Opcode.OP_LT:           ("OP_LT",            _dec_none),
    Opcode.OP_GT:           ("OP_GT",            _dec_none),
    Opcode.OP_LE:           ("OP_LE",            _dec_none),
    Opcode.OP_GE:           ("OP_GE",            _dec_none),
    Opcode.OP_LOGICAL_AND:  ("OP_LOGICAL_AND",   _dec_none),
    Opcode.OP_LOGICAL_OR:   ("OP_LOGICAL_OR",    _dec_none),
    Opcode.OP_LOGICAL_NOT:  ("OP_LOGICAL_NOT",   _dec_none),
    Opcode.OP_NEG:          ("OP_NEG",           _dec_none),

    # Built-in functions
    Opcode.FN_SIZEOF:       ("FN_SIZEOF",        _dec_fn_field),
    Opcode.FN_OFFSET_OF:    ("FN_OFFSET_OF",     _dec_fn_field),
    Opcode.FN_COUNT:        ("FN_COUNT",         _dec_fn_field),
    Opcode.FN_STRLEN:       ("FN_STRLEN",        _dec_fn_field),
    Opcode.FN_CRC32:        ("FN_CRC32",         _dec_fn_multi),
    Opcode.FN_ADLER32:      ("FN_ADLER32",       _dec_fn_multi),

    # String methods
    Opcode.STR_STARTS_WITH: ("STR_STARTS_WITH",  _dec_none),
    Opcode.STR_ENDS_WITH:   ("STR_ENDS_WITH",    _dec_none),
    Opcode.STR_CONTAINS:    ("STR_CONTAINS",     _dec_none),

    # Array control
    Opcode.ARRAY_BEGIN_COUNT:   ("ARRAY_BEGIN_COUNT",    _dec_none),
    Opcode.ARRAY_BEGIN_UNTIL:   ("ARRAY_BEGIN_UNTIL",    _dec_none),
    Opcode.ARRAY_BEGIN_SENTINEL:("ARRAY_BEGIN_SENTINEL", _dec_none),
    Opcode.ARRAY_BEGIN_GREEDY:  ("ARRAY_BEGIN_GREEDY",   _dec_none),
    Opcode.ARRAY_NEXT:          ("ARRAY_NEXT",           _dec_none),
    Opcode.ARRAY_END:           ("ARRAY_END",            _dec_none),

    # Array search
    Opcode.ARRAY_STORE_ELEM:    ("ARRAY_STORE_ELEM",    _dec_array_store_elem),
    Opcode.ARRAY_SEARCH_BEGIN:  ("ARRAY_SEARCH_BEGIN",  _dec_array_search_begin),
    Opcode.PUSH_ELEM_FIELD:     ("PUSH_ELEM_FIELD",     _dec_push_elem_field),
    Opcode.ARRAY_SEARCH_CHECK:  ("ARRAY_SEARCH_CHECK",  _dec_array_search_check),
    Opcode.ARRAY_SEARCH_COPY:   ("ARRAY_SEARCH_COPY",   _dec_array_search_copy),
    Opcode.ARRAY_SEARCH_END:    ("ARRAY_SEARCH_END",    _dec_none),
    Opcode.SENTINEL_SAVE:       ("SENTINEL_SAVE",       _dec_none),
    Opcode.SENTINEL_CHECK:      ("SENTINEL_CHECK",      _dec_none),

    # Match
    Opcode.MATCH_BEGIN:     ("MATCH_BEGIN",      _dec_match_begin),
    Opcode.MATCH_ARM_EQ:    ("MATCH_ARM_EQ",     _dec_match_arm_eq),
    Opcode.MATCH_ARM_RANGE: ("MATCH_ARM_RANGE",  _dec_match_arm_range),
    Opcode.MATCH_ARM_GUARD: ("MATCH_ARM_GUARD",  _dec_match_arm_guard),
    Opcode.MATCH_DEFAULT:   ("MATCH_DEFAULT",    _dec_match_default),
    Opcode.MATCH_END:       ("MATCH_END",        _dec_none),

    # Alignment
    Opcode.ALIGN:           ("ALIGN",            _dec_none),
    Opcode.ALIGN_FIXED:     ("ALIGN_FIXED",      _dec_align_fixed),
}


# ── Helpers ──────────────────────────────────────────────────────────────────

# Thread-local context for resolving field names during disassembly
_current_struct: StructMeta | None = None


def _get_utf8_stdout():
    """Return a stdout wrapper that handles Unicode on Windows."""
    import io
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        return sys.stdout
    return io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")


def _resolve_field(fid: int, prog: BscProgram) -> str:
    """Resolve a field ID to a name using the current struct's field table."""
    if _current_struct is not None and fid < len(_current_struct.fields):
        name_idx = _current_struct.fields[fid].name_index
        if name_idx < len(prog.string_table):
            return prog.string_table[name_idx]
    return f"field_{fid}"


def _resolve_string(sid: int, prog: BscProgram) -> str:
    if sid < len(prog.string_table):
        return prog.string_table[sid]
    return f"string_{sid}"


def _resolve_struct_name(sid: int, prog: BscProgram) -> str:
    if sid < len(prog.structs):
        name_idx = prog.structs[sid].name_index
        if name_idx < len(prog.string_table):
            return prog.string_table[name_idx]
    return f"struct_{sid}"


def _format_flags(flags: int, names: dict[int, str]) -> str:
    parts = [v for k, v in sorted(names.items()) if flags & k]
    return ", ".join(parts) if parts else "none"


# ── Disassembly output ──────────────────────────────────────────────────────

def disassemble(prog: BscProgram, out=None) -> None:
    """Print a full human-readable disassembly of a BscProgram."""
    global _current_struct
    if out is None:
        out = _get_utf8_stdout()

    # Global header
    out.write("═" * 72 + "\n")
    out.write(f"  BSC Program — version {prog.version}\n")
    if prog.root_struct_index >= 0:
        root_name = _resolve_struct_name(prog.root_struct_index, prog)
        out.write(f"  Root struct: {root_name} (index {prog.root_struct_index})\n")
    else:
        out.write("  Root struct: <none>\n")
    out.write(f"  Structs: {len(prog.structs)}\n")
    out.write(f"  Strings: {len(prog.string_table)}\n")
    out.write(f"  Bytecode: {len(prog.bytecode)} bytes\n")
    if prog.parameters:
        out.write(f"  Parameters: {prog.parameters}\n")
    if prog.has_source:
        out.write(f"  Source: embedded ({len(prog.source or '')} chars)\n")
    out.write("═" * 72 + "\n\n")

    # String table
    out.write("── String Table ──────────────────────────────────────────────────\n")
    for i, s in enumerate(prog.string_table):
        out.write(f"  [{i:4d}] {s!r}\n")
    out.write("\n")

    # Per-struct disassembly
    for si, sm in enumerate(prog.structs):
        _current_struct = sm
        sname = _resolve_struct_name(si, prog)
        flags_str = _format_flags(sm.flags, _STRUCT_FLAG_NAMES)

        out.write("── Struct ────────────────────────────────────────────────────────\n")
        out.write(f"  [{si}] {sname}")
        if flags_str != "none":
            out.write(f"  [{flags_str}]")
        out.write("\n")
        out.write(f"  params={sm.param_count}, fields={len(sm.fields)}")
        if sm.static_size >= 0:
            out.write(f", static_size={sm.static_size}")
        else:
            out.write(", dynamic_size")
        out.write(f"\n  bytecode: offset={sm.bytecode_offset}, length={sm.bytecode_length}\n")

        # Field table
        if sm.fields:
            out.write("  Fields:\n")
            for fi, fm in enumerate(sm.fields):
                fname = _resolve_string(fm.name_index, prog)
                fflags = _format_flags(fm.flags, _FIELD_FLAG_NAMES)
                flags_part = f"  [{fflags}]" if fflags != "none" else ""
                out.write(f"    [{fi:3d}] {fname}{flags_part}\n")

        # Instructions
        out.write("  Instructions:\n")
        _disassemble_range(prog, sm.bytecode_offset, sm.bytecode_length, out, indent="    ")
        out.write("\n")

    _current_struct = None


def _disassemble_range(prog: BscProgram, start: int, length: int, out, indent: str = "") -> None:
    """Disassemble a byte range within the bytecode."""
    bc = prog.bytecode
    ip = start
    end = start + length

    while ip < end:
        op_byte = bc[ip]
        ip += 1

        entry = _OPCODE_DECODERS.get(op_byte)
        if entry is None:
            out.write(f"{indent}{ip - 1:5d}: ??? (0x{op_byte:02X})\n")
            continue

        mnemonic, decoder = entry
        try:
            operands_str, consumed = decoder(bc, ip, prog)
        except (struct.error, IndexError) as e:
            out.write(f"{indent}{ip - 1:5d}: {mnemonic}  <truncated: {e}>\n")
            break

        ip += consumed

        if operands_str:
            out.write(f"{indent}{ip - 1 - consumed:5d}: {mnemonic:<28s} {operands_str}\n")
        else:
            out.write(f"{indent}{ip - 1 - consumed:5d}: {mnemonic}\n")


def disassemble_bsc_file(path: str, out=None) -> None:
    """Load a .bsc file and disassemble it."""
    with open(path, "rb") as f:
        data = f.read()
    prog = parse_bsc(data)
    disassemble(prog, out)


def disassemble_bsc_bytes(data: bytes, out=None) -> None:
    """Disassemble raw .bsc bytes."""
    prog = parse_bsc(data)
    disassemble(prog, out)
