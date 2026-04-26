# Dyalog APL `⎕NA` (Name Association) — Comprehensive Reference

## Executive Summary

`⎕NA` is Dyalog APL's Foreign Function Interface (FFI) system function that bridges APL code to compiled C/C++ functions in shared libraries (DLLs on Windows, `.so`/`.dylib` on Linux/macOS). It establishes a callable APL function (name class 3) that maps APL data types to C types, handles pointer semantics (input/output/in-out), supports structures, arrays, null-terminated strings, callbacks, and multi-threading. The type declaration mini-language uses a compact `[direction][special]type[width][array]` syntax to describe the C calling convention entirely from APL. This document is an exhaustive technical reference synthesized from the official Dyalog documentation[^1].

## How `⎕NA` Works

### Basic Syntax

```
{R} ← {X} ⎕NA Y
```

| Part | Description |
|------|-------------|
| `Y` (right argument) | Character vector: `[result] library\|function [arg1] [arg2] ...` |
| `X` (left argument) | Optional character vector — APL name to associate. If omitted, the C function name is used. |
| `R` (shy result) | Character vector containing the name that was fixed. |

The established function is **always monadic or niladic** (never dyadic). All C arguments are passed as items of a (possibly nested) right-argument vector[^1].

### Minimal Example

Given a C function:
```c
double divide(int32_t, int32_t);
```

The APL association and call:
```apl
'div' ⎕NA 'F8 math|divide I4 I4'
div 10 4    ⍝ → 2.5
```

The mapping is:

| C | ⎕NA |
|---|-----|
| `double` (return) | `F8` |
| `int32_t` (arg 1) | `I4` |
| `int32_t` (arg 2) | `I4` |

---

## Locating the Library

APL uses `LoadLibrary()` (Windows) or `dlopen()` (Unix/macOS) to load the library[^1].

| Platform | Default extension | Search paths |
|----------|-------------------|--------------|
| Windows | `.dll` (assumed if omitted; `.exe` must be explicit) | OS search order + Dyalog install dir |
| Linux/macOS | `.so` / `.dylib` | `$DYALOG/lib` + OS `LD_LIBRARY_PATH` etc. |

**Critical constraint**: A 32-bit interpreter can only load 32-bit libraries; 64-bit can only load 64-bit[^1].

### Errors

| Condition | Error |
|-----------|-------|
| Library (or dependency) not found | `FILE ERROR 2 No such file or directory` |
| Library loads but function not found | `VALUE ERROR` |

Missing-dependency errors are indistinguishable from missing-library errors at the OS level[^1].

---

## Data Type Coding Scheme

The full syntax for each type specifier is:

```
[direction] [special] type [width] [array][[count]]
```

### Direction Prefixes

Direction indicators are **required** whenever the C function expects a pointer argument (`*` or `[]` in C).

| Symbol | Meaning | APL caller provides | APL receives back |
|--------|---------|--------------------|--------------------|
| `<` | Input pointer | The data array itself | Nothing (input only) |
| `>` | Output pointer | The **number of elements** to reserve | The filled array |
| `=` | Input/output pointer | The data array (duplicated into output buffer) | The modified copy |

When no direction prefix is present, the value is passed **by value** (not by pointer)[^1].

### Special Qualifiers

Inserted between direction and type:

| Symbol | Meaning |
|--------|---------|
| `0` | Null-terminated (C string convention). Implies array; `[]` may be omitted. |
| `#` | Byte-counted string. Implies array; `[]` may be omitted. |

Both work with all types except `A` and `Z`[^1].

### Types

| Code | C Equivalent | Description |
|------|-------------|-------------|
| `I` | `int` (signed) | 2's complement signed integer |
| `U` | `unsigned int` | Unsigned integer |
| `C` | `char` | Character (untranslated in Classic; same as `T` in Unicode, but default width = 1) |
| `T` | `char` / `wchar_t` | Translated character. Default width = "wide" (2 bytes Windows, 4 bytes Unix). **Recommended for portable code.** |
| `UTF` | UTF-8/16 | `>0UTF8[]` for UTF-8, `<0UTF16[]` for UTF-16LE |
| `F` | `float` / `double` | IEEE 754 binary floating point |
| `D` | `_Decimal128` | IEEE 754-2008 decimal128 (DPD on AIX, BID elsewhere) |
| `J` | complex | Complex number |
| `P` | `uintptr_t` | Platform-sized pointer. Equivalent to `U4` (32-bit) or `U8` (64-bit). |
| `∇` | function pointer | Callback — pass APL function name or `⎕OR`. Limited to NAG library pattern. |
| `A` | APL array (AP format) | Same format as Auxiliary Processor transmission |
| `Z` | APL array + header | Same format as TCP/IP socket transmission |

### Widths

| Type | Valid widths | Default |
|------|-------------|---------|
| `I` | 1, 2, 4, 8 | 4 |
| `U` | 1, 2, 4, 8 | 4 |
| `C` | 1, 2, 4 | 1 |
| `T` | 1, 2, 4 | wide (2 on Win, 4 on Unix) |
| `UTF` | 8, 16 | none (must specify) |
| `F` | 4, 8 | 8 |
| `D` | 16 | 16 |
| `J` | 16 | 16 |

**Note**: 32-bit versions support 64-bit integer *arguments* but not 64-bit integer *results*[^1].

### Arrays

| Syntax | Meaning |
|--------|---------|
| `[n]` | Fixed-length array of `n` elements |
| `[]` | Variable-length array (size determined at call time) |

C deals only in scalars and rank-1 arrays. From APL's perspective, a pointer to a scalar (`<T`) and a pointer to an array (`<T[]`) are distinct declarations — you may define multiple associations for different calling patterns[^1].

### Structures

Curly braces `{}` define nested C structures:

```apl
⎕NA 'mydll.foo U <{F8 I2}[]'
```

corresponds to:
```c
typedef struct { double f; short i; } mystruct;
void foo(unsigned count, mystruct *str);
```

**Padding**: If the C compiler adds alignment padding, you must explicitly model it:
```apl
⎕NA 'mydll.foo U <{I2 {I1[6]} F8}[]'
```
This inserts 6 bytes of padding between the `I2` and `F8` fields[^1].

### Count Shorthand

Repeat adjacent identical items:
- `>I8[3]` instead of `>I8 >I8 >I8`
- `{I8 U8 I8 P}[2]` instead of repeating the struct twice[^1]

---

## Result Vector

The result of calling a `⎕NA` function is always a **nested vector**:

```
[explicit_result, output_arg_1, output_arg_2, ...]
```

| Component | Included when |
|-----------|--------------|
| Explicit result | Function declares a return type |
| Output arguments | Arguments declared with `>` or `=` |

If the result would be a 1-element vector, it is **automatically disclosed** (unwrapped) as a convenience[^1].

A `void` function with no output parameters returns `⍬` (zilde)[^1].

### 64-bit Integer Results

64-bit integer results are converted to 128-bit decimal floating point (`⎕DR` type) because it is the only APL data type that preserves all 64 bits. You must set `⎕FR←1287` for arithmetic to maintain full precision[^1].

---

## Advanced Features

### Multi-Threading

Appending `&` to the function name runs the external function in its own system thread:

```apl
⎕NA '... mydll|foo& ...'
```

This allows other APL threads to execute concurrently[^1].

### Name Mangling

C++ and other languages mangle exported names by default. The `⎕NA` function name must match the actual exported symbol — either match the mangling or ensure unmangled exports (e.g., `extern "C"`)[^1].

### Call by Ordinal (Windows only)

```apl
⎕NA '... mydll|57 ...'
```

Calls the function exported at ordinal 57 rather than by name[^1].

### ANSI/Unicode Variants (`*` Suffix)

Under Windows, many API functions have `A` (ANSI) and `W` (Wide/Unicode) forms. Using `*` at the end of the function name auto-selects:

| Edition | `*` resolves to |
|---------|----------------|
| Classic | `A` |
| Unicode | `W` |

The default APL name omits the trailing letter (e.g., `MessageBox*` → APL name `MessageBox`)[^1].

### Callbacks (`∇`)

Limited to the NAG library pattern. Declares a callback that receives up to 16 pointer arguments:

```apl
∇f8←(P P P P)
```

The callback argument can be an APL function name or `⎕OR` of a function. The callback decodes pointer arguments using `MEMCPY`[^1].

### Passing Null Pointers

Use type `P` to pass a raw pointer value (including null/0) by value:

```apl
'fun_null' ⎕NA 'mydll|fun I P'
fun_null 42 0    ⍝ passes null pointer as second arg
```

Using `P` ensures portability between 32-bit and 64-bit[^1].

---

## The Dyalog DLL Built-in Functions

The Dyalog runtime DLL (`dyalog32.dll` / `dyalog64.dll`) provides three utility functions for memory manipulation:

### MEMCPY

```c
void *MEMCPY(void *to, void *fm, size_t size);
```

Copies `size` bytes from `fm` to `to`. Extremely versatile — can be associated with many different type declarations to move data between memory buffers and APL arrays[^1].

**Example** — copy floating-point numbers from a global buffer:
```apl
'doubles' ⎕NA 'dyalog64|MEMCPY >F8[] P P'
doubles numb addr (numb×8)
```

### STRNCPY / STRNCPYA / STRNCPYW

```c
void *STRNCPY(char *to, char *fm, size_t size);
```

Copies up to `size` characters from null-terminated source. `STRNCPYA` is a synonym; `STRNCPYW` covers `wcsncpy()`. Using `STRNCPY*` auto-selects on Windows[^1].

### STRLEN

```c
size_t STRLEN(const char *s);
```

Returns the length of a null-terminated string in memory[^1].

---

## Common Windows Type Mappings

| Windows typedef | `⎕NA` equivalent | Notes |
|----------------|------------------|-------|
| `HWND`, `HANDLE`, `HDC`, `HBITMAP`, `HBRUSH`, `HFONT`, `HICON`, `HMENU`, `HPALETTE`, `HMETAFILE`, `HMODULE`, `HINSTANCE` | `P` | All handle types |
| `DWORD`, `ULONG` | `U4` | |
| `WORD` | `U2` | |
| `BYTE` | `U1` | |
| `BOOL` | `I` | |
| `UINT` | `U` | |
| `ATOM` | `U2` | |
| `LRESULT` | `I4` | |
| `LPSTR` | `=0T[]` | Safest; use `<0T[]` if input-only |
| `LPCSTR` | `<0T[]` | Const pointer → input only |
| `WPARAM` | `U` | 32-bit in 32-bit APL, 64-bit in 64-bit |
| `LPARAM` | `U4` | Same note as WPARAM |
| `COLORREF` | `{U1[4]}` | |
| `POINT` | `{I I}` | |
| `POINTS` | `{I2 I2}` | |
| `RECT` | `{I I I I}` | |

---

## Footnotes

[^1]: [Dyalog/documentation — `language-reference-guide/docs/system-functions/na.md`](https://github.com/Dyalog/documentation/blob/main/language-reference-guide/docs/system-functions/na.md) (SHA: `1227e3469aa88496037b637e0e6cb5eff15ea6a0`)
