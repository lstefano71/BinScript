# 15. Integrating BinScript from C

> Use the BinScript NativeAOT DLL from plain C — no .NET runtime required.

## What You'll Build

A command-line program that:

1. Loads the BinScript DLL at runtime
2. Compiles the `stdlib/bmp.bsx` parser script
3. Parses a bitmap file into JSON
4. Produces a round-trip binary copy from that JSON
5. Saves compiled bytecode to a `.bsc` file for reuse

Full source: [`tools/examples/c/parse_bmp.c`](../../tools/examples/c/parse_bmp.c)

## Prerequisites

- A C compiler (MSVC, GCC, or Clang)
- The published BinScript NativeAOT DLL:

```bash
dotnet publish src/BinScript.Interop -c Release
```

This produces a self-contained native DLL with no .NET dependency. On Windows, look for `BinScript.Interop.dll` under the publish output directory.

## Step 1: Loading the Library

BinScript ships as a native shared library, so you load it at runtime with `LoadLibrary` (Windows) or `dlopen` (Linux/macOS). The example abstracts this behind platform macros:

```c
#ifdef _WIN32
  #include <windows.h>
  typedef HMODULE lib_t;
  #define lib_open(p)   LoadLibraryA(p)
  #define lib_sym(h, n) ((void*)(uintptr_t)GetProcAddress(h, n))
  #define lib_close(h)  FreeLibrary(h)
  #define DLL_NAME "BinScript.Interop.dll"
#else
  #include <dlfcn.h>
  typedef void* lib_t;
  #define lib_open(p)   dlopen(p, RTLD_LAZY)
  #define lib_sym(h, n) dlsym(h, n)
  #define lib_close(h)  dlclose(h)
  #ifdef __APPLE__
    #define DLL_NAME "BinScript.Interop.dylib"
  #else
    #define DLL_NAME "BinScript.Interop.so"
  #endif
#endif
```

Why dynamic loading instead of link-time binding? The DLL is a NativeAOT artifact whose path varies by build configuration and platform RID. Dynamic loading lets you discover the DLL at runtime — from an environment variable, a known build directory, or a user-provided path — without baking a path into your binary.

## Step 2: Declaring Function Pointers

Since we're loading dynamically, we need function pointer types for each API we use. These mirror the signatures in [`binscript.h`](../../src/BinScript.Interop/binscript.h):

```c
typedef void*       (*fn_compiler_new)(void);
typedef void*       (*fn_compiler_compile)(void*, const char*, const char*);
typedef void        (*fn_compiler_free)(void*);
typedef int64_t     (*fn_save)(void*, uint8_t*, size_t);
typedef void        (*fn_free)(void*);
typedef const char* (*fn_to_json)(void*, const uint8_t*, size_t, const char*);
typedef int64_t     (*fn_from_json_calc_size)(void*, const char*, const char*);
typedef int64_t     (*fn_from_json_into)(void*, uint8_t*, size_t,
                                         const char*, const char*);
typedef void        (*fn_mem_free)(void*);
typedef const char* (*fn_last_error)(void);
typedef const char* (*fn_version)(void);
```

Then a `LOAD` macro resolves each symbol and fails early if anything is missing:

```c
#define LOAD(var, type, name) do {                           \
    var = (type)lib_sym(g_lib, name);                        \
    if (!var) { fprintf(stderr, "  Missing: %s\n", name);   \
                return 0; }                                  \
} while (0)

static int load_binscript(const char *dll_path) {
    g_lib = lib_open(dll_path);
    if (!g_lib) { fprintf(stderr, "  Cannot load: %s\n", dll_path); return 0; }
    LOAD(bs_compiler_new,     fn_compiler_new,        "binscript_compiler_new");
    LOAD(bs_compiler_compile, fn_compiler_compile,    "binscript_compiler_compile");
    LOAD(bs_compiler_free,    fn_compiler_free,       "binscript_compiler_free");
    LOAD(bs_save,             fn_save,                "binscript_save");
    LOAD(bs_free,             fn_free,                "binscript_free");
    LOAD(bs_to_json,          fn_to_json,             "binscript_to_json");
    LOAD(bs_calc_size,        fn_from_json_calc_size, "binscript_from_json_calc_size");
    LOAD(bs_produce,          fn_from_json_into,      "binscript_from_json_into");
    LOAD(bs_mem_free,         fn_mem_free,            "binscript_mem_free");
    LOAD(bs_last_error,       fn_last_error,          "binscript_last_error");
    LOAD(bs_version,          fn_version,             "binscript_version");
    return 1;
}
```

This example uses a subset of the API. See [`binscript.h`](../../src/BinScript.Interop/binscript.h) for the complete set, which includes `binscript_load`, `binscript_to_json_entry`, `binscript_from_json`, and more.

## Step 3: Finding the DLL

The example uses a two-tier discovery strategy:

1. **Environment variable** — check `BINSCRIPT_DLL` for an explicit path
2. **Repo walk** — walk up from the current directory looking for the publish output

```c
static const char *find_dll(const char *repo_root) {
    /* 1. Environment variable */
    const char *env = getenv("BINSCRIPT_DLL");
    if (env && file_exists(env)) return env;

    /* 2. Search publish output under repo root */
    static char buf[1024];
    static const char *cfgs[] = { "Release", "Debug" };
    static const char *subs[] = { "native", "publish" };
    /* ... search loop over configs × RIDs × subdirs ... */
    return NULL;
}
```

For your own projects, you might embed the DLL alongside your executable or use a fixed install path instead.

## Step 4: Compiling a Script

The compiler lifecycle is: **create → compile → free**. Here we read `stdlib/bmp.bsx` from disk and compile it:

```c
void *compiler = bs_compiler_new();
void *prog = bs_compiler_compile(compiler, (const char *)src, NULL);
bs_compiler_free(compiler);
free(src);

if (!prog) {
    const char *e = bs_last_error();
    fprintf(stderr, "Compile error: %s\n", e ? e : "unknown");
    return 1;
}
```

Key points:

- The second argument to `compiler_compile` is the script source (UTF-8, null-terminated).
- The third argument is compile-time parameters as a JSON object — pass `NULL` when you don't need any.
- The compiler is freed immediately after use. The compiled `prog` handle is independent and long-lived.
- If compilation fails, `prog` is `NULL` and `bs_last_error()` has the diagnostic.

The BMP script being compiled is straightforward:

```
@default_endian(little)

@coverage(partial)
@root struct BmpFile {
    header: BmpFileHeader,
    dib_header: BitmapInfoHeader,
}

struct BmpFileHeader {
    signature: u16 = 0x4D42,
    file_size: u32,
    reserved1: u16,
    reserved2: u16,
    data_offset: u32,
}

struct BitmapInfoHeader {
    size: u32,
    width: i32,
    height: i32,
    planes: u16,
    bits_per_pixel: u16,
    compression: u32,
    image_size: u32,
    x_ppm: i32,
    y_ppm: i32,
    colors_used: u32,
    colors_important: u32,
}
```

Note the `@coverage(partial)` — this script only parses the headers, not the pixel data. This matters for round-trip fidelity (Step 6).

## Step 5: Parsing Binary → JSON

With the compiled program and a BMP file loaded into memory, one call converts binary to JSON:

```c
const char *json = bs_to_json(prog, bmp_data, bmp_len, NULL);
if (!json) {
    const char *e = bs_last_error();
    fprintf(stderr, "Parse error: %s\n", e ? e : "unknown");
    free(bmp_data); bs_free(prog); return 1;
}
printf("%s\n", json);
```

The returned JSON string is allocated by the BinScript engine. **You must free it with `bs_mem_free`** (not `free`) when done — see Step 9.

## Step 6: Producing JSON → Binary (Round-Trip)

The produce workflow is a two-step process: calculate the required size, then write into a caller-allocated buffer.

```c
int64_t need = bs_calc_size(prog, json, NULL);
if (need < 0) {
    const char *e = bs_last_error();
    fprintf(stderr, "Size calc error: %s\n", e ? e : "unknown");
} else {
    uint8_t *rt = (uint8_t *)malloc((size_t)need);
    int64_t wrote = bs_produce(prog, rt, (size_t)need, json, NULL);
    if (wrote < 0) {
        const char *e = bs_last_error();
        fprintf(stderr, "Produce error: %s\n", e ? e : "unknown");
    } else {
        write_file("roundtrip.bmp", rt, (size_t)wrote);
        /* Compare with original... */
    }
    free(rt);
}
bs_mem_free((void *)json);  /* now we're done with json */
```

Why two steps? The caller owns the output buffer, so you need to know how much to allocate. `binscript_from_json_calc_size` does a size-calculation pass without writing any bytes, then `binscript_from_json_into` writes the actual binary.

Because `bmp.bsx` is annotated `@coverage(partial)`, only the headers are round-tripped. The example detects this and reports accordingly:

```c
if ((size_t)wrote == bmp_len && memcmp(rt, bmp_data, bmp_len) == 0) {
    printf("Round-trip matches original (%lld bytes identical)\n",
           (long long)wrote);
} else if ((size_t)wrote <= bmp_len
        && memcmp(rt, bmp_data, (size_t)wrote) == 0) {
    printf("Produced %lld of %zu bytes (header matches, @coverage partial)\n",
           (long long)wrote, bmp_len);
}
```

## Step 7: Saving Bytecode

Compilation is the expensive step. You can serialize the compiled program to a `.bsc` file and reload it later with `binscript_load`, skipping the compiler entirely.

The save API uses a query-then-write pattern:

```c
int64_t bc_size = bs_save(prog, NULL, 0);   /* pass NULL to query size */
if (bc_size > 0) {
    uint8_t *bc = (uint8_t *)malloc((size_t)bc_size);
    bs_save(prog, bc, (size_t)bc_size);     /* actual serialization */
    write_file("bmp.bsc", bc, (size_t)bc_size);
    free(bc);
}
```

When to cache bytecode: if your script doesn't change between runs (e.g., a deployed parser), load from `.bsc` at startup. If scripts are user-editable, recompile each time.

## Step 8: Error Handling

Every BinScript API function follows the same error pattern:

1. **Check the return value** — `NULL` for pointer-returning functions, negative for integer-returning ones
2. **Call `binscript_last_error()`** — returns a UTF-8 string describing what went wrong
3. **The error string is thread-local** — valid until your next BinScript call on the same thread

```c
void *prog = bs_compiler_compile(compiler, src, NULL);
if (!prog) {
    const char *e = bs_last_error();
    fprintf(stderr, "Error: %s\n", e ? e : "unknown");
}
```

> **Important**: Do NOT free the pointer returned by `binscript_last_error()`. It points to a thread-local buffer managed by the engine.

## Step 9: Memory Management

The rules are simple:

| Returned by | Free with |
|---|---|
| `binscript_to_json` | `binscript_mem_free` |
| `binscript_from_json` | `binscript_mem_free` |
| `binscript_compiler_new` | `binscript_compiler_free` |
| `binscript_compiler_compile` | `binscript_free` |
| `binscript_load` | `binscript_free` |
| `binscript_last_error` | **Do NOT free** (thread-local) |
| `binscript_version` | **Do NOT free** (static) |

The core principle: anything BinScript allocates, BinScript frees. Use `binscript_mem_free` for data buffers (JSON strings, byte arrays). Use the specific `_free` functions for handles. Never pass BinScript pointers to your C runtime's `free()`.

## Building and Running

**Windows (MSVC)**:
```
cl /nologo /W3 parse_bmp.c /Fe:parse_bmp.exe
parse_bmp ..\..\..\tests\samples\bmp\4x4_24bit.bmp
```

**Linux/macOS (GCC)**:
```
gcc -o parse_bmp parse_bmp.c -ldl
./parse_bmp ../../../tests/samples/bmp/4x4_24bit.bmp
```

Expected output:

```
Loading BinScript DLL...
  Version: 1.0.0

Compiling BMP script...
  Script: .../stdlib/bmp.bsx
  ✓ Compiled successfully

Parsing 4x4_24bit.bmp (198 bytes)...
{"header":{"signature":19778,"file_size":198, ...}, "dib_header":{...}}

Producing round-trip copy → roundtrip.bmp
  ✓ Produced 54 of 198 bytes (header matches, @coverage partial)

Saving compiled bytecode → bmp.bsc
  ✓ Saved ... bytes of bytecode

Cleaning up...
Done.
```

## Summary

In this chapter you learned how to:

- **Load** the BinScript NativeAOT DLL dynamically from C
- **Compile** `.bsx` scripts via the compiler API
- **Parse** binary data to JSON with `binscript_to_json`
- **Produce** binary from JSON with the calc-size + write-into two-step workflow
- **Cache** compiled bytecode to skip recompilation
- **Handle errors** via the thread-local `binscript_last_error` pattern
- **Manage memory** correctly across the FFI boundary

For the complete API (including `binscript_load`, `binscript_to_json_entry`, `binscript_compiler_add_module`, and more), see [docs/C_ABI.md](../C_ABI.md).
