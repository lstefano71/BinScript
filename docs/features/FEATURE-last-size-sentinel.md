# Feature: `@last_size` Sentinel for Array Termination

## Summary

Add a new built-in variable `@last_size` usable in `@until` expressions, returning the byte-length of the most recently read array element. This enables sentinel-terminated array patterns where the termination condition depends on the element's content or size rather than a fixed count or remaining bytes.

## Motivation

Many C APIs use sentinel-terminated lists:

- **PCZZSTR / REG_MULTI_SZ**: double-null-terminated string lists — an array of null-terminated strings where the last "string" is empty (size 0). Common in Win32 shell APIs (`SHFileOperation`), registry (`REG_MULTI_SZ`), and INI file functions (`GetPrivateProfileSection`).
- **Null-pointer-terminated pointer arrays**: `char **argv` style — array of pointers terminated by a NULL entry.
- **Zero-entry-terminated tables**: struct arrays where a zeroed-out entry marks the end (e.g., PE import tables).

BinScript currently supports `@until(@remaining == 0)` and `@until(field == value)` but has no way to express "stop when the last element was empty/zero-sized."

## Design

### New Built-in Variable

`@last_size` — available inside `@until()` expressions on array fields. Returns the byte size consumed by the most recently read array element, as tracked by the ParseEngine.

### Usage in Full BinScript

```bsx
struct MultiString {
    items: cstring[] @until(@last_size == 1)
}
```

This reads null-terminated strings until it encounters an empty one (just a null terminator — 1 byte consumed). For UTF-16 cstrings, use `@until(@last_size == 2)` since the wide null terminator is 2 bytes.

### Semantic Precision

`@last_size` returns the **bytes consumed** by the last element (position delta). For `cstring`, this includes the null terminator — an empty cstring (`"\0"`) has `@last_size == 1`. For UTF-16 cstrings, an empty string (`\0\0`) has `@last_size == 2`.

### Relationship to BSX-Light

In BSX-Light, the `00T` type specifier compiles to `cstring[] @until(@last_size == 1)` (or `@last_size == 2` for wide strings). The `00T` is syntactic sugar; `@last_size` is the primitive.

## Implementation

### ParseEngine

Track the size of the last-read element in the array loop state:

```csharp
// In ArrayLoopState or similar
public long LastElementSize { get; set; }
```

Updated after each element read. The `@last_size` built-in variable reads from this field.

### BytecodeEmitter

New opcode or extension to existing `PushBuiltinVar`:
- `PushLastSize` — pushes the byte size of the last array element onto the eval stack

### Compiler

- Lexer: recognize `@last_size` as a built-in variable token
- Parser: allow in `@until()` expressions
- SemanticAnalyzer: validate that `@last_size` is only used inside array `@until` contexts

## Scope

### In Scope
- `@last_size` built-in variable in `@until()` expressions
- ParseEngine tracking of last element size
- Bytecode support (new opcode or PushBuiltinVar extension)
- Compiler support (lexer, parser, semantic analysis, emitter)
- Tests: cstring arrays terminated by empty element, struct arrays terminated by zero-sized entry
- Documentation: `LANGUAGE_SPEC.md`, `BYTECODE.md`

### Out of Scope
- `00T` syntax (BSX-Light feature, depends on this primitive)
- ProduceEngine support for `@last_size` (produce direction uses the input data length)
- `@last_value` or other element-inspection built-ins (future extension)

## Testing Strategy

- **cstring sentinel**: array of cstrings terminated by empty string
- **UTF-16 cstring sentinel**: same with `@encoding(utf16le)` — validates wide-char handling
- **struct sentinel**: array of structs terminated by all-zero struct
- **nested arrays**: `@last_size` in inner array doesn't leak to outer
- **regression**: all existing `@until` tests pass unchanged

## Dependencies

None — this is a standalone enhancement to BinScript.Core.

## Cherry-Pick Notes

This feature modifies only:
- `BinScript.Core/Compiler/Lexer.cs` (new token)
- `BinScript.Core/Compiler/Parser.cs` (parse @last_size)
- `BinScript.Core/Compiler/SemanticAnalyzer.cs` (validation)
- `BinScript.Core/Compiler/BytecodeEmitter.cs` (emit opcode)
- `BinScript.Core/Bytecode/Opcode.cs` (new opcode or variant)
- `BinScript.Core/Runtime/ParseEngine.cs` (track last element size)
- `BinScript.Tests/`
- Docs: `LANGUAGE_SPEC.md`, `BYTECODE.md`

It can be cherry-picked to `main` independently of BSX-Light work.
