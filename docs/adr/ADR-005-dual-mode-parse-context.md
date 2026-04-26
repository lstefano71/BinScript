# ADR-005: Dual-Mode ParseContext for Live Memory Access

## Status

Proposed

## Context

BinScript's `ParseContext` currently operates exclusively over a `ReadOnlyMemory<byte>` buffer. All data reads go through `ctx.Input.Span.Slice((int)ctx.Position)`, meaning pointer targets must reside within the same contiguous buffer as the struct being parsed.

This is adequate for file-format parsing (the original use case), but insufficient for FFI scenarios where a DLL returns a struct containing pointers to arbitrary process memory. In these cases, `ptr<T>` cannot chase pointers outside the buffer — a `SeekAbs` to an address beyond `Input.Length` throws `ArgumentOutOfRangeException`.

The existing `base_ptr` parameter already provides the abstraction: `buffer_offset = ptr_value - base_ptr`. When `base_ptr = 0`, the buffer offset equals the raw pointer value, which is exactly "the process address space starting at 0."

## Decision

Extend `ParseContext` to support two modes:

### Buffer Mode (existing behavior, unchanged)

- Constructed with `ReadOnlyMemory<byte>`
- `Position` is an offset into the buffer
- `input()` helper: `ctx.Input.Span.Slice((int)ctx.Position)`
- Bounds-checked — out-of-range throws clean exceptions
- `base_ptr` provided by the caller (typically the buffer's actual address in memory)

### Live Mode (new)

- Constructed with `nint baseAddress` (typically 0 for process-wide pointer chasing)
- `Position` is an absolute memory address
- `input()` helper: `new ReadOnlySpan<byte>((void*)ctx.Position, length)` — reads directly from process memory
- **No bounds checking** — reading invalid addresses causes access violations (documented as caller's risk)
- `base_ptr` is implicitly 0 (or explicitly set to 0), so `ptr<T>` pointer arithmetic is a no-op: `offset = ptr_value - 0 = ptr_value`

### Unified Semantics

The key insight: live mode is equivalent to "buffer mode with `base_ptr = 0` and the buffer being all of addressable memory." No changes needed to:
- Bytecode (same opcodes)
- `SeekAbs` / `SeekPush` / `SeekPop` semantics
- `ptr<T>` / `relptr<T>` offset arithmetic
- `BytecodeEmitter` or compiler pipeline

The only change is in the `input()` and `inputSpan()` helper methods in `ParseEngine`, which dispatch based on `ctx.IsLiveMode`.

### Performance

A single `bool IsLiveMode` branch in the hot-path `input()` helpers. Branch prediction makes this effectively free after the first read in either mode. Buffer-mode performance is unchanged — no new allocations, no interface dispatch.

## Consequences

- **BinScript.Core** gains `unsafe` code in `ParseEngine` for live-mode span construction
- `ParseContext` constructor gets a new overload: `ParseContext(nint baseAddress)`
- `AllowUnsafeBlocks` is already enabled on all projects
- The C-ABI surface (`BinScript.Interop`) gains new parse functions that accept a raw address + size instead of a byte buffer
- All existing tests and buffer-mode behavior are unaffected
- Safety documentation must clearly state that live mode is inherently unsafe and pointer validity is the caller's responsibility

## Related

- ADR-003: Pointer Extension Design (introduced `ptr<T>` and `base_ptr`)
- Feature: BSX-Light compiler (primary consumer of live mode)
