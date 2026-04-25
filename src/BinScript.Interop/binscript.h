/**
 * BinScript C-ABI Header
 * 
 * Canonical header for the BinScript NativeAOT shared library.
 * This is the single source of truth — docs/C_ABI.md references this file.
 *
 * Conventions:
 *   - All strings are UTF-8, null-terminated.
 *   - Opaque handles (BinCompiler*, BinScript*) must not be dereferenced.
 *   - On failure: pointers return NULL, integers return negative values.
 *   - Call binscript_last_error() for error details.
 *   - Free engine-allocated memory with binscript_mem_free().
 *   - Do NOT free binscript_last_error() or binscript_version() return values.
 */
#ifndef BINSCRIPT_H
#define BINSCRIPT_H

#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ═══════════════════════════════════════════════════════
 *  Opaque handles
 * ═══════════════════════════════════════════════════════ */

typedef struct BinCompiler BinCompiler;
typedef struct BinScript   BinScript;

/* ═══════════════════════════════════════════════════════
 *  Compiler lifecycle
 * ═══════════════════════════════════════════════════════ */

/**
 * Create a new compiler instance.
 * Returns NULL on allocation failure.
 */
BinCompiler* binscript_compiler_new(void);

/**
 * Register a named module for import resolution.
 * Must be called before binscript_compiler_compile.
 *
 * @param compiler    Compiler instance.
 * @param name_utf8   Module name (e.g., "pe/dos_header"). UTF-8, null-terminated.
 * @param script_utf8 Module source text. UTF-8, null-terminated.
 * @return 0 on success, -1 on error (check binscript_last_error).
 */
int binscript_compiler_add_module(BinCompiler* compiler,
                                   const char* name_utf8,
                                   const char* script_utf8);

/**
 * Compile a BinScript source into an executable program.
 *
 * @param compiler        Compiler instance (with modules pre-registered).
 * @param script_utf8     Main script source. UTF-8, null-terminated.
 * @param params_json_utf8 Compile-time parameters as JSON object.
 *                         NULL or "{}" if none.
 * @return Compiled program handle, or NULL on error.
 */
BinScript* binscript_compiler_compile(BinCompiler* compiler,
                                       const char* script_utf8,
                                       const char* params_json_utf8);

/**
 * Free a compiler instance. Safe to call with NULL.
 */
void binscript_compiler_free(BinCompiler* compiler);

/* ═══════════════════════════════════════════════════════
 *  Program persistence (save / load compiled bytecode)
 * ═══════════════════════════════════════════════════════ */

/**
 * Serialize a compiled program to a byte buffer.
 *
 * @param script  Compiled program.
 * @param buf     Output buffer (caller-provided). NULL to query required size.
 * @param cap     Buffer capacity in bytes.
 * @return Number of bytes written, or required size if buf is NULL.
 *         Returns -1 on error.
 */
int64_t binscript_save(BinScript* script, uint8_t* buf, size_t cap);

/**
 * Deserialize a compiled program from a byte buffer.
 *
 * @param data  Serialized bytecode.
 * @param len   Length in bytes.
 * @return Compiled program handle, or NULL on error.
 */
BinScript* binscript_load(const uint8_t* data, size_t len);

/**
 * Free a compiled program. Safe to call with NULL.
 */
void binscript_free(BinScript* script);

/* ═══════════════════════════════════════════════════════
 *  Parse: Binary → JSON
 * ═══════════════════════════════════════════════════════ */

/**
 * Parse binary data into a JSON string using the default @root entry point.
 *
 * @param script          Compiled program.
 * @param data            Input binary data.
 * @param len             Input length in bytes.
 * @param params_json_utf8 Runtime parameters as JSON. NULL or "{}" if none.
 * @return JSON string (caller must free with binscript_mem_free), or NULL on error.
 */
const char* binscript_to_json(BinScript* script,
                               const uint8_t* data, size_t len,
                               const char* params_json_utf8);

/**
 * Parse binary data into a JSON string using a named entry point.
 *
 * @param script          Compiled program.
 * @param entry_utf8      Struct name to use as entry point. UTF-8, null-terminated.
 * @param data            Input binary data.
 * @param len             Input length in bytes.
 * @param params_json_utf8 Runtime parameters as JSON. NULL or "{}" if none.
 * @return JSON string (caller must free with binscript_mem_free), or NULL on error.
 */
const char* binscript_to_json_entry(BinScript* script,
                                     const char* entry_utf8,
                                     const uint8_t* data, size_t len,
                                     const char* params_json_utf8);

/* ═══════════════════════════════════════════════════════
 *  Produce: JSON → Binary
 * ═══════════════════════════════════════════════════════ */

/**
 * Query the compile-time-known output size for a struct.
 * Only returns a positive value for structs with entirely fixed-size fields.
 *
 * @param script      Compiled program.
 * @param entry_utf8  Struct name. NULL for @root.
 * @return Size in bytes, or -1 if the size is dynamic.
 */
int64_t binscript_from_json_static_size(BinScript* script,
                                         const char* entry_utf8);

/**
 * Calculate the exact output size for producing binary from JSON data.
 * Performs a size-calculation pass without writing any bytes.
 *
 * @param script          Compiled program.
 * @param json_utf8       Input JSON. UTF-8, null-terminated.
 * @param params_json_utf8 Runtime parameters. NULL or "{}" if none.
 * @return Size in bytes, or -1 on error.
 */
int64_t binscript_from_json_calc_size(BinScript* script,
                                       const char* json_utf8,
                                       const char* params_json_utf8);

/**
 * Produce binary data from JSON into a caller-provided buffer.
 *
 * @param script          Compiled program.
 * @param buf             Output buffer (caller-allocated).
 * @param buf_len         Buffer capacity in bytes.
 * @param json_utf8       Input JSON. UTF-8, null-terminated.
 * @param params_json_utf8 Runtime parameters. NULL or "{}" if none.
 * @return Bytes written (>= 0), or negative error code:
 *         -1: general error (check binscript_last_error)
 *         -2: buffer too small (call binscript_from_json_calc_size first)
 */
int64_t binscript_from_json_into(BinScript* script,
                                  uint8_t* buf, size_t buf_len,
                                  const char* json_utf8,
                                  const char* params_json_utf8);

/**
 * Convenience: produce binary from JSON with engine-allocated buffer.
 * Use binscript_from_json_into for performance-critical paths.
 *
 * @param script          Compiled program.
 * @param json_utf8       Input JSON. UTF-8, null-terminated.
 * @param out_len         [out] Length of produced binary data.
 * @param params_json_utf8 Runtime parameters. NULL or "{}" if none.
 * @return Binary data (caller must free with binscript_mem_free), or NULL on error.
 */
const uint8_t* binscript_from_json(BinScript* script,
                                    const char* json_utf8,
                                    size_t* out_len,
                                    const char* params_json_utf8);

/* ═══════════════════════════════════════════════════════
 *  Memory management
 * ═══════════════════════════════════════════════════════ */

/**
 * Free memory allocated by BinScript functions.
 * Must be used for all pointers returned by binscript_to_json,
 * binscript_from_json, and binscript_save (when buf is NULL).
 * Safe to call with NULL.
 */
void binscript_mem_free(void* ptr);

/* ═══════════════════════════════════════════════════════
 *  Error reporting
 * ═══════════════════════════════════════════════════════ */

/**
 * Get the last error message for the current thread.
 * Returns a thread-local string. Do NOT free this pointer.
 * The string is valid until the next BinScript call on the same thread.
 *
 * @return Error message (UTF-8, null-terminated), or NULL if no error.
 */
const char* binscript_last_error(void);

/* ═══════════════════════════════════════════════════════
 *  Version information
 * ═══════════════════════════════════════════════════════ */

/**
 * Get the BinScript library version string.
 * @return Version string (e.g., "1.0.0"). Do NOT free.
 */
const char* binscript_version(void);

#ifdef __cplusplus
}
#endif

#endif /* BINSCRIPT_H */
