/* parse_bmp.c — BinScript C-ABI tutorial: parse a BMP, round-trip produce it.
 *
 * Dynamically loads the BinScript NativeAOT DLL and exercises the full
 * compile → parse → produce → save-bytecode cycle.
 *
 * Build (Windows/MSVC):  cl /nologo /W3 parse_bmp.c /Fe:parse_bmp.exe
 * Build (Linux/macOS):   gcc -o parse_bmp parse_bmp.c -ldl
 * Run:                   parse_bmp ..\..\..\tests\samples\bmp\4x4_24bit.bmp
 */

#ifdef _MSC_VER
  #define _CRT_SECURE_NO_WARNINGS
#endif
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

/* ── Platform abstraction ────────────────────────────────────────────────── */

#ifdef _WIN32
  #include <windows.h>
  #include <direct.h>
  typedef HMODULE lib_t;
  #define lib_open(p)   LoadLibraryA(p)
  #define lib_sym(h, n) ((void*)(uintptr_t)GetProcAddress(h, n))
  #define lib_close(h)  FreeLibrary(h)
  #define SEP '\\'
  #define DLL_NAME "BinScript.Interop.dll"
  #define get_cwd(b, n) _getcwd(b, n)
#else
  #include <dlfcn.h>
  #include <unistd.h>
  typedef void* lib_t;
  #define lib_open(p)   dlopen(p, RTLD_LAZY)
  #define lib_sym(h, n) dlsym(h, n)
  #define lib_close(h)  dlclose(h)
  #define SEP '/'
  #ifdef __APPLE__
    #define DLL_NAME "BinScript.Interop.dylib"
  #else
    #define DLL_NAME "BinScript.Interop.so"
  #endif
  #define get_cwd(b, n) getcwd(b, n)
#endif

/* ── Function pointer types (mirrors binscript.h) ────────────────────────── */

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

static fn_compiler_new        bs_compiler_new;
static fn_compiler_compile    bs_compiler_compile;
static fn_compiler_free       bs_compiler_free;
static fn_save                bs_save;
static fn_free                bs_free;
static fn_to_json             bs_to_json;
static fn_from_json_calc_size bs_calc_size;
static fn_from_json_into      bs_produce;
static fn_mem_free            bs_mem_free;
static fn_last_error          bs_last_error;
static fn_version             bs_version;
static lib_t                  g_lib;

/* ── Helpers ─────────────────────────────────────────────────────────────── */

static uint8_t *read_file(const char *path, size_t *out_len) {
    FILE *f = fopen(path, "rb");
    if (!f) return NULL;
    fseek(f, 0, SEEK_END);
    long len = ftell(f);
    fseek(f, 0, SEEK_SET);
    uint8_t *buf = (uint8_t *)malloc((size_t)len + 1);
    if (!buf) { fclose(f); return NULL; }
    *out_len = fread(buf, 1, (size_t)len, f);
    buf[*out_len] = 0; /* null-terminate so text reads work too */
    fclose(f);
    return buf;
}

static int write_file(const char *path, const uint8_t *data, size_t len) {
    FILE *f = fopen(path, "wb");
    if (!f) return 0;
    int ok = fwrite(data, 1, len, f) == len;
    fclose(f);
    return ok;
}

static int file_exists(const char *p) {
    FILE *f = fopen(p, "rb");
    if (f) { fclose(f); return 1; }
    return 0;
}

static const char *basename_of(const char *path) {
    const char *s1 = strrchr(path, '\\');
    const char *s2 = strrchr(path, '/');
    const char *s = s1 > s2 ? s1 : s2;
    return s ? s + 1 : path;
}

/* ── DLL loading ─────────────────────────────────────────────────────────── */

#define LOAD(var, type, name) do {                           \
    var = (type)lib_sym(g_lib, name);                        \
    if (!var) { fprintf(stderr, "  Missing: %s\n", name);   \
                return 0; }                                  \
} while (0)

static int load_binscript(const char *dll_path) {
    g_lib = lib_open(dll_path);
    if (!g_lib) { fprintf(stderr, "  Cannot load: %s\n", dll_path); return 0; }
    LOAD(bs_compiler_new,    fn_compiler_new,        "binscript_compiler_new");
    LOAD(bs_compiler_compile,fn_compiler_compile,    "binscript_compiler_compile");
    LOAD(bs_compiler_free,   fn_compiler_free,       "binscript_compiler_free");
    LOAD(bs_save,            fn_save,                "binscript_save");
    LOAD(bs_free,            fn_free,                "binscript_free");
    LOAD(bs_to_json,         fn_to_json,             "binscript_to_json");
    LOAD(bs_calc_size,       fn_from_json_calc_size, "binscript_from_json_calc_size");
    LOAD(bs_produce,         fn_from_json_into,      "binscript_from_json_into");
    LOAD(bs_mem_free,        fn_mem_free,            "binscript_mem_free");
    LOAD(bs_last_error,      fn_last_error,          "binscript_last_error");
    LOAD(bs_version,         fn_version,             "binscript_version");
    return 1;
}

/* ── Repo / DLL discovery ────────────────────────────────────────────────── */

/* Walk up from `start` looking for src/BinScript.Interop/binscript.h.
   Writes repo root into `root`. Returns 1 on success. */
static int find_repo_root(const char *start, char *root, size_t cap) {
    char dir[1024];
    strncpy(dir, start, sizeof(dir) - 1);
    dir[sizeof(dir) - 1] = '\0';
    for (int i = 0; i < 8; i++) {
        char probe[1024];
        snprintf(probe, sizeof(probe), "%s%csrc%cBinScript.Interop%cbinscript.h",
                 dir, SEP, SEP, SEP);
        if (file_exists(probe)) {
            strncpy(root, dir, cap - 1);
            root[cap - 1] = '\0';
            return 1;
        }
        char *sep = strrchr(dir, SEP);
        if (!sep) break;
        *sep = '\0';
    }
    return 0;
}

static const char *find_dll(const char *repo_root) {
    /* 1. Environment variable */
    const char *env = getenv("BINSCRIPT_DLL");
    if (env && file_exists(env)) return env;

    /* 2. Search publish output under repo root */
    static char buf[1024];
    static const char *cfgs[] = { "Release", "Debug" };
    static const char *subs[] = { "native", "publish" };
#ifdef _WIN32
    static const char *rids[] = { "win-x64", "win-arm64" };
#elif __APPLE__
    static const char *rids[] = { "osx-x64", "osx-arm64" };
#else
    static const char *rids[] = { "linux-x64", "linux-arm64" };
#endif
    int nrids = (int)(sizeof(rids) / sizeof(rids[0]));
    for (int c = 0; c < 2; c++)
      for (int r = 0; r < nrids; r++)
        for (int s = 0; s < 2; s++) {
            snprintf(buf, sizeof(buf),
                "%s%csrc%cBinScript.Interop%cbin%c%s%cnet10.0%c%s%c%s%c%s",
                repo_root, SEP, SEP, SEP, SEP,
                cfgs[c], SEP, SEP,
                rids[r], SEP,
                subs[s], SEP, DLL_NAME);
            if (file_exists(buf)) return buf;
        }
    return NULL;
}

/* ── Main ────────────────────────────────────────────────────────────────── */

int main(int argc, char *argv[]) {
    if (argc < 2) {
        fprintf(stderr, "Usage: %s <bmp-file>\n", argv[0]);
        return 1;
    }
    const char *bmp_path = argv[1];

#ifdef _WIN32
    SetConsoleOutputCP(65001); /* enable UTF-8 console output */
#endif

    /* Locate repo root (walk up from CWD) */
    char cwd[1024];
    get_cwd(cwd, sizeof(cwd));
    char repo_root[1024];
    if (!find_repo_root(cwd, repo_root, sizeof(repo_root))) {
        fprintf(stderr, "Error: cannot find repo root from %s\n", cwd);
        return 1;
    }

    /* ── 1. Load DLL ──────────────────────────────────────────── */
    printf("Loading BinScript DLL...\n");
    const char *dll_path = find_dll(repo_root);
    if (!dll_path) {
        fprintf(stderr, "Error: DLL not found. Build with:\n");
        fprintf(stderr, "  dotnet publish src/BinScript.Interop -c Release\n");
        fprintf(stderr, "Or set BINSCRIPT_DLL environment variable.\n");
        return 1;
    }
    if (!load_binscript(dll_path)) return 1;
    printf("  Version: %s\n", bs_version());

    /* ── 2. Compile BMP script ────────────────────────────────── */
    char script_path[1024];
    snprintf(script_path, sizeof(script_path), "%s%cstdlib%cbmp.bsx",
             repo_root, SEP, SEP);
    size_t src_len;
    uint8_t *src = read_file(script_path, &src_len);
    if (!src) { fprintf(stderr, "Error: cannot read %s\n", script_path); return 1; }

    printf("\nCompiling BMP script...\n");
    printf("  Script: %s\n", script_path);

    void *compiler = bs_compiler_new();
    void *prog = bs_compiler_compile(compiler, (const char *)src, NULL);
    bs_compiler_free(compiler);
    free(src);

    if (!prog) {
        const char *e = bs_last_error();
        fprintf(stderr, "  \xc3\x97 Compile error: %s\n", e ? e : "unknown");
        return 1;
    }
    printf("  \xe2\x9c\x93 Compiled successfully\n");

    /* ── 3. Parse the BMP file ────────────────────────────────── */
    size_t bmp_len;
    uint8_t *bmp_data = read_file(bmp_path, &bmp_len);
    if (!bmp_data) {
        fprintf(stderr, "Error: cannot read %s\n", bmp_path);
        bs_free(prog);
        return 1;
    }

    printf("\nParsing %s (%zu bytes)...\n", basename_of(bmp_path), bmp_len);

    const char *json = bs_to_json(prog, bmp_data, bmp_len, NULL);
    if (!json) {
        const char *e = bs_last_error();
        fprintf(stderr, "  \xc3\x97 Parse error: %s\n", e ? e : "unknown");
        free(bmp_data); bs_free(prog); return 1;
    }
    printf("%s\n", json);

    /* ── 4. Round-trip: JSON → binary ─────────────────────────── */
    printf("\nProducing round-trip copy \xe2\x86\x92 roundtrip.bmp\n");

    int64_t need = bs_calc_size(prog, json, NULL);
    if (need < 0) {
        const char *e = bs_last_error();
        fprintf(stderr, "  \xc3\x97 Size calc error: %s\n", e ? e : "unknown");
    } else {
        uint8_t *rt = (uint8_t *)malloc((size_t)need);
        int64_t wrote = bs_produce(prog, rt, (size_t)need, json, NULL);
        if (wrote < 0) {
            const char *e = bs_last_error();
            fprintf(stderr, "  \xc3\x97 Produce error: %s\n", e ? e : "unknown");
        } else {
            write_file("roundtrip.bmp", rt, (size_t)wrote);
            if ((size_t)wrote == bmp_len
                    && memcmp(rt, bmp_data, bmp_len) == 0) {
                printf("  \xe2\x9c\x93 Round-trip matches original "
                       "(%lld bytes identical)\n", (long long)wrote);
            } else if ((size_t)wrote <= bmp_len
                    && memcmp(rt, bmp_data, (size_t)wrote) == 0) {
                printf("  \xe2\x9c\x93 Produced %lld of %zu bytes "
                       "(header matches, @coverage partial)\n",
                       (long long)wrote, bmp_len);
            } else {
                printf("  Produced %lld bytes "
                       "(differs from original %zu bytes)\n",
                       (long long)wrote, bmp_len);
            }
        }
        free(rt);
    }
    bs_mem_free((void *)json);

    /* ── 5. Save compiled bytecode ────────────────────────────── */
    int64_t bc_size = bs_save(prog, NULL, 0);
    if (bc_size > 0) {
        uint8_t *bc = (uint8_t *)malloc((size_t)bc_size);
        bs_save(prog, bc, (size_t)bc_size);
        printf("\nSaving compiled bytecode \xe2\x86\x92 bmp.bsc\n");
        write_file("bmp.bsc", bc, (size_t)bc_size);
        printf("  \xe2\x9c\x93 Saved %lld bytes of bytecode\n",
               (long long)bc_size);
        free(bc);
    }

    /* ── 6. Cleanup ───────────────────────────────────────────── */
    printf("\nCleaning up...\n");
    free(bmp_data);
    bs_free(prog);
    lib_close(g_lib);
    printf("Done.\n");
    return 0;
}
