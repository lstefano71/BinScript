# Feature: Dual-Mode ParseContext (Live Memory Access)

## Summary

Extend `ParseContext` to support reading from arbitrary process memory addresses in addition to the existing `ReadOnlyMemory<byte>` buffer mode. This enables `ptr<T>` to chase pointers outside the parse buffer — critical for FFI scenarios where DLLs return structs containing pointers to data elsewhere in process memory.

## Motivation

Currently, `ParseContext` holds a single `ReadOnlyMemory<byte> Input` and all reads go through `ctx.Input.Span.Slice((int)ctx.Position)`. If `SeekAbs` targets a position beyond `Input.Length`, the engine throws `ArgumentOutOfRangeException`. This means `ptr<T>` can only dereference pointers that point within the same buffer.

In FFI scenarios (e.g., calling Win32 APIs via `⎕NA` or the C-ABI), a DLL may return a struct where pointer fields reference data anywhere in the process address space. Today, the only way to handle this is to manually copy all pointer targets into a contiguous buffer before parsing — defeating the purpose of a declarative parser.

The insight is that live mode is equivalent to `base_ptr = 0` with the "buffer" being all addressable memory. No changes are needed to bytecode, pointer arithmetic, or the compiler.

## Design

See ADR-005 for the full architectural decision.

### ParseContext Changes

```csharp
public sealed class ParseContext
{
    // Existing buffer mode
    public ReadOnlyMemory<byte> Input { get; }

    // New live mode
    public bool IsLiveMode { get; }
    private readonly nint _baseAddress;  // 0 for live mode typically

    // Existing constructor (buffer mode)
    public ParseContext(ReadOnlyMemory<byte> input) { ... }

    // New constructor (live mode)
    public ParseContext(nint baseAddress) { ... }
}
```

### ParseEngine Changes

The `input()` and `inputSpan()` helper methods dispatch based on `IsLiveMode`:

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static ReadOnlySpan<byte> inputSpan(ParseContext ctx, int length) =>
    ctx.IsLiveMode
        ? new ReadOnlySpan<byte>((void*)ctx.Position, length)
        : ctx.Input.Span.Slice((int)ctx.Position, length);
```

### Runtime Variables in Live Mode

- `@remaining` — meaningless in live mode (no buffer end). Returns `long.MaxValue` or requires a caller-provided limit.
- `@offset` — returns `Position` (which is an absolute address in live mode).
- `@size` — meaningless in live mode. Returns `long.MaxValue` or caller-provided limit.

### C-ABI Surface

New interop functions:

```c
// Parse from a raw memory address (live mode, base_ptr implicitly 0)
const char* binscript_parse_live(
    intptr_t script_handle,
    uintptr_t address,        // start address of the struct
    uintptr_t size_hint,      // advisory size (0 = unknown)
    const char* params_json   // runtime parameters (base_ptr will be forced to 0)
);
```

## Scope

### In Scope
- `ParseContext` dual-mode construction
- `ParseEngine` input helper dispatch
- C-ABI `binscript_parse_live` function
- Unit tests for live-mode parsing (using pinned managed arrays to simulate)
- Documentation updates to `ARCHITECTURE.md`, `C_ABI.md`

### Out of Scope
- ProduceEngine changes (produce always writes to a caller-provided buffer)
- Any changes to the compiler pipeline
- BSX-Light (separate feature, depends on this one)

## Testing Strategy

- **Unit tests**: Pin a managed `byte[]` via `GCHandle`, get its `IntPtr`, construct `ParseContext` in live mode, verify reads work identically to buffer mode for the same data.
- **Pointer chasing tests**: Create a buffer with embedded absolute pointers (pointing elsewhere in the same pinned allocation), verify `ptr<T>` resolves correctly with `base_ptr = 0`.
- **Regression**: All existing buffer-mode tests must pass unchanged.
- **Interop tests**: Call `binscript_parse_live` via C-ABI, verify results.

## Dependencies

None — this is a standalone enhancement to BinScript.Core.

## Cherry-Pick Notes

This feature modifies only:
- `BinScript.Core/Runtime/ParseContext.cs`
- `BinScript.Core/Runtime/ParseEngine.cs`
- `BinScript.Interop/NativeExports.cs`
- `BinScript.Tests/Runtime/ParseEngineTests.cs`
- Docs: `ARCHITECTURE.md`, `C_ABI.md`, ADR-005

It can be cherry-picked to `main` independently of BSX-Light work.
