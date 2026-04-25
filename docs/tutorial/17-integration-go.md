# Chapter 17 — Integrating BinScript from Go

> Load the BinScript DLL with `syscall.LoadDLL` and produce a playable WAV file.

## What You'll Build

A Go program that:
1. Loads the BinScript NativeAOT DLL via Go's `syscall` package
2. Compiles a WAV structure script at runtime
3. Generates a melody (*Twinkle Twinkle Little Star*) as 8-bit PCM audio
4. Produces a valid `.wav` file through BinScript's produce engine
5. Outputs a file you can double-click to hear 🎵

Full source: [`tools/examples/go/produce_wav.go`](../../tools/examples/go/produce_wav.go)

## Prerequisites

| Requirement | Notes |
|---|---|
| **Go 1.21+** | Uses `unsafe.Slice` (Go 1.17+) and generics-era stdlib |
| **Published BinScript DLL** | `dotnet publish src/BinScript.Interop -c Release` |
| **Windows** | `syscall.LoadDLL` is Windows-only — see [Platform Notes](#platform-notes) for Linux/macOS |

## Step 1: Loading the DLL

Go's `syscall` package provides `LoadDLL` to load a Windows DLL and `FindProc` to
resolve exported functions — no cgo required.

```go
dll, err := syscall.LoadDLL(dllPath)
if err != nil {
    log.Fatalf("Load DLL: %v", err)
}
defer dll.Release()
```

Each C-ABI function is resolved by its exported name:

```go
func loadProcs(dll *syscall.DLL) map[string]*syscall.Proc {
    names := map[string]string{
        "version":          "binscript_version",
        "compiler_new":     "binscript_compiler_new",
        "compiler_compile": "binscript_compiler_compile",
        "compiler_free":    "binscript_compiler_free",
        "calc_size":        "binscript_from_json_calc_size",
        "produce_into":     "binscript_from_json_into",
        "free":             "binscript_free",
        "last_error":       "binscript_last_error",
        "mem_free":         "binscript_mem_free",
    }
    procs := make(map[string]*syscall.Proc, len(names))
    for k, v := range names {
        p, err := dll.FindProc(v)
        if err != nil {
            log.Fatalf("Missing export %s: %v", v, err)
        }
        procs[k] = p
    }
    return procs
}
```

The map gives us short, readable names (`p["version"]`) while mapping to the
full `binscript_*` C symbol. If any export is missing, the program fails fast.

## Step 2: DLL Discovery

The example finds the DLL automatically. It checks the `BINSCRIPT_DLL` environment
variable first, then walks the repo's build output:

```go
func findDLL() string {
    if env := os.Getenv("BINSCRIPT_DLL"); env != "" {
        if _, err := os.Stat(env); err == nil {
            return env
        }
    }
    root := repoRoot()
    base := filepath.Join(root, "src", "BinScript.Interop", "bin", "Release")
    var found string
    filepath.WalkDir(base, func(p string, d os.DirEntry, err error) error {
        if err != nil || found != "" {
            return filepath.SkipAll
        }
        if !d.IsDir() && d.Name() == "BinScript.Interop.dll" &&
            strings.Contains(filepath.Dir(p), "native") {
            found = p
            return filepath.SkipAll
        }
        return nil
    })
    return found
}
```

⚠️ The NativeAOT DLL lives under a `native/` subdirectory in the publish output — the
`strings.Contains(..."native")` check ensures we pick the native binary, not the managed
assembly.

## Step 3: Calling C Functions from Go

This is the Go-specific challenge. Every `proc.Call()` speaks in `uintptr`:

```go
r1, r2, err := proc.Call(arg1, arg2, ...)
```

- **Return values** are `uintptr` — an integer the size of a pointer.
- **Pointers** must be cast via `unsafe.Pointer` → `uintptr`.
- **Strings** need null-termination (C expects `\0`). Go's `string` type does not include one.

🔑 **String conversion** — the example pins Go byte slices to prevent GC collection:

```go
var pins [][]byte // prevent GC from collecting backing arrays

func cStr(s string) uintptr {
    b := append([]byte(s), 0)       // null-terminate
    pins = append(pins, b)          // pin — Go GC must not move this
    return uintptr(unsafe.Pointer(&b[0]))
}
```

The reverse — reading a C string back into Go — walks memory until `\0`:

```go
func goStr(ptr uintptr) string {
    if ptr == 0 {
        return ""
    }
    p := ptr
    for *(*byte)(unsafe.Pointer(p)) != 0 {
        p++
    }
    n := int(p - ptr)
    return string(unsafe.Slice((*byte)(unsafe.Pointer(ptr)), n))
}
```

⚠️ **Why pin?** Go's garbage collector can relocate heap objects. If the GC moves a
byte slice between the moment you take its address and the moment the DLL reads it,
the pointer becomes dangling. Keeping a reference in the `pins` slice ensures the
backing array stays alive and in place for the duration of the call.

## Step 4: Compiling the WAV Script

The BinScript lifecycle is: **create compiler → compile → free compiler → use program → free program**.

```go
script := readFile("stdlib/wav.bsx")

compiler, _, _ := p["compiler_new"].Call()
if compiler == 0 {
    fatal("Failed to create compiler")
}

prog, _, _ := p["compiler_compile"].Call(compiler, cStr(script), 0)
p["compiler_free"].Call(compiler)     // compiler no longer needed

if prog == 0 {
    fatal("Compile error: %s", lastErr(p))
}
defer p["free"].Call(prog)            // free program on exit
```

The third argument to `compiler_compile` is an optional filename (for diagnostics) — we
pass `0` (null) here. The WAV script being compiled is `stdlib/wav.bsx`:

```
@default_endian(little)

@root struct WavFile {
    chunk_id: fixed_string[4] = "RIFF",
    chunk_size: u32,
    format: fixed_string[4] = "WAVE",
    chunks: WavChunk[] @until(@remaining == 0),
}

struct WavChunk {
    chunk_id: fixed_string[4],
    chunk_size: u32,
    body: match(chunk_id) {
        "fmt " => FmtBody(chunk_size),
        _ => bytes[chunk_size],
    },
}

struct FmtBody(data_length) {
    audio_format: u16,
    num_channels: u16,
    sample_rate: u32,
    byte_rate: u32,
    block_align: u16,
    bits_per_sample: u16,
    @hidden _extra: bytes[data_length - 16],
}
```

## Step 5: Generating the Melody

Here's where it gets fun. We're generating *Twinkle Twinkle Little Star* as raw PCM audio.

**Note frequencies** (Hz) for one octave:

```go
var freqs = map[string]float64{
    "C4": 261.63, "D4": 293.66, "E4": 329.63,
    "F4": 349.23, "G4": 392.00, "A4": 440.00,
}
```

**The melody** — each string is a note, `"-"` is a rest between phrases:

```go
var melody = []string{
    "C4", "C4", "G4", "G4", "A4", "A4", "G4", "-",
    "F4", "F4", "E4", "E4", "D4", "D4", "C4", "-",
    "G4", "G4", "F4", "F4", "E4", "E4", "D4", "-",
    "G4", "G4", "F4", "F4", "E4", "E4", "D4", "-",
    "C4", "C4", "G4", "G4", "A4", "A4", "G4", "-",
    "F4", "F4", "E4", "E4", "D4", "D4", "C4", "-",
}
```

**Tone generation** — a sine wave with fade-in/fade-out to avoid clicks:

```go
const (
    sampleRate = 8000  // Hz — telephone quality, keeps the file small
    noteDur    = 0.3   // seconds per note
    amplitude  = 100   // 0-127 range, centered at 128 for unsigned 8-bit
)

func makeTone(freq, dur float64) []byte {
    n := int(dur * sampleRate)
    buf := make([]byte, n)
    fade := int(0.005 * sampleRate) // 5ms fade to avoid clicks
    for i := range buf {
        t := float64(i) / sampleRate
        env := 1.0
        if i < fade {
            env = float64(i) / float64(fade)
        } else if i > n-fade {
            env = float64(n-i) / float64(fade)
        }
        buf[i] = byte(128 + amplitude*env*math.Sin(2*math.Pi*freq*t))
    }
    return buf
}
```

The math: `128 + A·sin(2πft)` generates an unsigned 8-bit PCM sample centered at 128.
The 5ms fade envelope prevents audible pops at note boundaries.

Silence is simply the center value (128) repeated:

```go
func makeSilence(dur float64) []byte {
    buf := make([]byte, int(dur*sampleRate))
    for i := range buf {
        buf[i] = 128
    }
    return buf
}
```

## Step 6: Building the JSON Structure

BinScript's produce engine takes structured data as JSON. The WAV structure maps directly
to the `wav.bsx` script:

```go
func buildWavJSON(pcm []byte) string {
    wav := map[string]any{
        "chunk_id":   "RIFF",
        "chunk_size": 36 + len(pcm),
        "format":     "WAVE",
        "chunks": []map[string]any{
            {
                "chunk_id":   "fmt ",
                "chunk_size": 16,
                "body": map[string]any{
                    "audio_format":    1, // PCM
                    "num_channels":    1,
                    "sample_rate":     sampleRate,
                    "byte_rate":       sampleRate,
                    "block_align":     1,
                    "bits_per_sample": 8,
                    "_extra":          "",
                },
            },
            {
                "chunk_id":   "data",
                "chunk_size": len(pcm),
                "body":       base64.StdEncoding.EncodeToString(pcm),
            },
        },
    }
    j, _ := json.Marshal(wav)
    return string(j)
}
```

🔑 **Key insight: base64 encoding for `bytes[]` fields.** When BinScript's JSON
representation encounters a `bytes[N]` field, it expects the value as a **base64-encoded
string**, not a JSON array of numbers. This is how raw binary data round-trips through JSON
efficiently. Use Go's `encoding/base64` package:

```go
import "encoding/base64"

encoded := base64.StdEncoding.EncodeToString(pcmSamples)
```

Notice `"_extra": ""` for the `@hidden` field — the produce engine still needs a value
for every field, even hidden ones. An empty base64 string means zero bytes.

## Step 7: Producing Binary from JSON

The produce pipeline is two steps:

1. **Calculate size** — ask BinScript how many bytes the output will need
2. **Produce into buffer** — write the binary data into a pre-allocated Go slice

```go
js := cStr(wavJSON)

// Step 1: calculate required buffer size
size, _, _ := p["calc_size"].Call(prog, js, 0)
if int64(size) < 0 {
    fatal("Size calc failed: %s", lastErr(p))
}

// Step 2: allocate and produce
buf := make([]byte, int(size))
written, _, _ := p["produce_into"].Call(
    prog,
    uintptr(unsafe.Pointer(&buf[0])),  // pointer to Go slice backing array
    size,                               // buffer capacity
    js,                                 // JSON input
    0,                                  // options (null)
)
if int64(written) < 0 {
    fatal("Produce failed: %s", lastErr(p))
}
```

The `unsafe.Pointer(&buf[0])` trick passes the address of the Go slice's backing array
directly to C. This is safe because `buf` stays alive for the duration of the call.

The third argument to both `calc_size` and `produce_into` is an options pointer — `0` (null)
for defaults.

## Step 8: Writing the Output

With the binary produced, writing is straightforward:

```go
if err := os.WriteFile("twinkle.wav", buf[:int(written)], 0644); err != nil {
    fatal("Write: %v", err)
}
fmt.Printf("✓ Wrote twinkle.wav (%d bytes, %.1fs)\n", int(written),
    float64(len(pcm))/sampleRate)
```

On Windows, play it immediately:

```
start twinkle.wav
```

## Step 9: Error Handling and Memory

### Checking for errors

BinScript C-ABI functions signal failure by returning `NULL` (0) for pointers or
negative values for sizes. Always check:

```go
prog, _, _ := p["compiler_compile"].Call(compiler, cStr(script), 0)
if prog == 0 {
    fmt.Fprintf(os.Stderr, "Error: %s\n", lastErr(p))
}
```

The `lastErr` helper reads the thread-local error string:

```go
func lastErr(p map[string]*syscall.Proc) string {
    r, _, _ := p["last_error"].Call()
    return goStr(r)
}
```

⚠️ The string from `binscript_last_error()` is thread-local and must **NOT** be freed.
It's overwritten on the next error.

### Memory management

Go's garbage collector knows nothing about C allocations. You must free BinScript
objects explicitly:

| Object | Free with |
|---|---|
| `BinCompiler*` | `binscript_compiler_free` |
| `BinScript*` (program) | `binscript_free` |
| Strings returned by `binscript_to_json` etc. | `binscript_mem_free` |
| `binscript_last_error()` result | **Do NOT free** (thread-local) |

Go's `defer` is perfect for this:

```go
compiler, _, _ := p["compiler_new"].Call()
// ... use compiler ...
p["compiler_free"].Call(compiler)

prog, _, _ := p["compiler_compile"].Call(compiler, cStr(script), 0)
defer p["free"].Call(prog) // freed when main() returns
```

## Platform Notes

This example uses `syscall.LoadDLL`, which is **Windows-only**. For Linux and macOS,
use **cgo** with `dlopen`:

```go
// +build linux darwin

/*
#cgo LDFLAGS: -ldl
#include <dlfcn.h>
#include <stdlib.h>

// Declare the BinScript functions you need:
typedef void* (*compiler_new_fn)(void);
typedef void* (*compiler_compile_fn)(void*, const char*, const char*);
// ... etc.
*/
import "C"

import "unsafe"

func loadLibrary(path string) unsafe.Pointer {
    cpath := C.CString(path)
    defer C.free(unsafe.Pointer(cpath))
    return C.dlopen(cpath, C.RTLD_LAZY)
}
```

The cgo approach requires a C compiler toolchain. The `syscall.LoadDLL` approach on
Windows requires only the Go toolchain — no gcc, no MSVC.

## Building and Running

```bash
# 1. Publish the NativeAOT DLL (one-time)
dotnet publish src/BinScript.Interop -c Release

# 2. Run from the repo root
cd tools/examples/go
go run produce_wav.go

# 3. Or specify an output path
go run produce_wav.go my_song.wav
```

Expected output:

```
Loading BinScript DLL...
  Version: 1.0.0

Compiling WAV script...
  ✓ Compiled successfully

Generating "Twinkle Twinkle Little Star" 🎵
  Sample rate: 8000 Hz, 8-bit mono
  C4 C4 G4 G4 A4 A4 G4 - F4 F4 E4 E4 D4 D4 C4 -

Building JSON structure...
Producing WAV binary...
  ✓ Wrote twinkle.wav (15648 bytes, 1.6s)
  Play it: start twinkle.wav

Done.
```

## Summary

You produced a playable WAV file entirely from Go — no cgo, no C compiler, just
`syscall.LoadDLL` and BinScript's C-ABI.

Key takeaways:

- **`syscall.LoadDLL` + `FindProc`** gives Go direct access to any C-ABI DLL on Windows
- **`proc.Call`** works in `uintptr` — cast pointers with `unsafe.Pointer`, null-terminate strings manually
- **Pin Go memory** that crosses the FFI boundary to prevent GC relocation
- **`bytes[]` fields use base64** in BinScript's JSON representation — use `encoding/base64`
- **Two-step produce**: `calc_size` first, then `produce_into` a pre-allocated buffer
- **Free everything**: Go's GC won't clean up C-side allocations — use `defer` liberally
