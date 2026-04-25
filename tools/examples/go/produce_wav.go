//go:build windows

// produce_wav generates a WAV file playing "Twinkle Twinkle Little Star"
// using the BinScript C-ABI DLL via syscall.LoadDLL (Windows-only).
//
// Usage:
//
//	go run produce_wav.go [output.wav]
//
// Prerequisites:
//
//	dotnet publish src/BinScript.Interop -c Release
package main

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"math"
	"os"
	"path/filepath"
	"strings"
	"syscall"
	"unsafe"
)

const (
	sampleRate = 8000 // Hz
	noteDur    = 0.3  // seconds per note
	restDur    = 0.15 // seconds per rest
	amplitude  = 100  // 0-127, centered at 128 for unsigned 8-bit PCM
)

var freqs = map[string]float64{
	"C4": 261.63, "D4": 293.66, "E4": 329.63,
	"F4": 349.23, "G4": 392.00, "A4": 440.00,
}

// Twinkle Twinkle Little Star — full melody ("-" = rest between phrases)
var melody = []string{
	"C4", "C4", "G4", "G4", "A4", "A4", "G4", "-", "F4", "F4", "E4", "E4", "D4", "D4", "C4", "-",
	"G4", "G4", "F4", "F4", "E4", "E4", "D4", "-", "G4", "G4", "F4", "F4", "E4", "E4", "D4", "-",
	"C4", "C4", "G4", "G4", "A4", "A4", "G4", "-", "F4", "F4", "E4", "E4", "D4", "D4", "C4", "-",
}

// pins prevents GC from collecting C string backing arrays.
var pins [][]byte

func main() {
	out := "twinkle.wav"
	if len(os.Args) > 1 {
		out = os.Args[1]
	}

	// ── Load DLL ────────────────────────────────────────
	fmt.Println("Loading BinScript DLL...")
	dllPath := findDLL()
	if dllPath == "" {
		fatal("DLL not found. Set BINSCRIPT_DLL or run:\n  dotnet publish src/BinScript.Interop -c Release")
	}
	dll, err := syscall.LoadDLL(dllPath)
	if err != nil {
		fatal("Load DLL: %v", err)
	}
	defer dll.Release()

	p := loadProcs(dll)
	r, _, _ := p["version"].Call()
	fmt.Printf("  Version: %s\n\n", goStr(r))

	// ── Compile WAV script ──────────────────────────────
	fmt.Println("Compiling WAV script...")
	script := readRepoFile("stdlib", "wav.bsx")

	compiler, _, _ := p["compiler_new"].Call()
	if compiler == 0 {
		fatal("Failed to create compiler")
	}
	prog, _, _ := p["compiler_compile"].Call(compiler, cStr(script), 0)
	p["compiler_free"].Call(compiler)
	if prog == 0 {
		fatal("Compile error: %s", lastErr(p))
	}
	defer p["free"].Call(prog)
	fmt.Println("  ✓ Compiled successfully")
	fmt.Println()

	// ── Generate melody ─────────────────────────────────
	fmt.Printf("Generating \"Twinkle Twinkle Little Star\" 🎵\n")
	fmt.Printf("  Sample rate: %d Hz, 8-bit mono\n", sampleRate)
	fmt.Printf("  %s\n\n", strings.Join(melody[:16], " "))

	pcm := makeMelody()

	// ── Produce WAV binary ──────────────────────────────
	fmt.Println("Building JSON structure...")
	wavJSON := buildWavJSON(pcm)

	fmt.Println("Producing WAV binary...")
	js := cStr(wavJSON)
	size, _, _ := p["calc_size"].Call(prog, js, 0)
	if int64(size) < 0 {
		fatal("Size calc failed: %s", lastErr(p))
	}

	buf := make([]byte, int(size))
	written, _, _ := p["produce_into"].Call(
		prog, uintptr(unsafe.Pointer(&buf[0])), size, js, 0,
	)
	if int64(written) < 0 {
		fatal("Produce failed: %s", lastErr(p))
	}

	if err := os.WriteFile(out, buf[:int(written)], 0644); err != nil {
		fatal("Write: %v", err)
	}
	dur := float64(len(pcm)) / sampleRate
	fmt.Printf("  ✓ Wrote %s (%d bytes, %.1fs)\n", out, int(written), dur)
	fmt.Printf("  Play it: start %s\n\n", out)
	fmt.Println("Done.")
}

// ── Audio generation ────────────────────────────────────

func makeMelody() []byte {
	var pcm []byte
	for _, n := range melody {
		if n == "-" {
			pcm = append(pcm, makeSilence(restDur)...)
		} else {
			pcm = append(pcm, makeTone(freqs[n], noteDur)...)
		}
	}
	return pcm
}

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

func makeSilence(dur float64) []byte {
	buf := make([]byte, int(dur*sampleRate))
	for i := range buf {
		buf[i] = 128 // center value for unsigned 8-bit PCM
	}
	return buf
}

// ── WAV JSON builder ────────────────────────────────────

func buildWavJSON(pcm []byte) string {
	wav := map[string]any{
		"chunk_id":   "RIFF",
		"chunk_size": 36 + len(pcm), // file_size - 8
		"format":     "WAVE",
		"chunks": []map[string]any{
			{
				"chunk_id":   "fmt ",
				"chunk_size": 16,
				"body": map[string]any{
					"audio_format":    1, // PCM
					"num_channels":    1,
					"sample_rate":     sampleRate,
					"byte_rate":       sampleRate, // sampleRate * channels * bitsPerSample/8
					"block_align":     1,           // channels * bitsPerSample/8
					"bits_per_sample": 8,
					"_extra":          "", // @hidden bytes[0] — required by produce engine
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

// ── DLL discovery ───────────────────────────────────────

func findDLL() string {
	if env := os.Getenv("BINSCRIPT_DLL"); env != "" {
		if _, err := os.Stat(env); err == nil {
			return env
		}
	}
	root := repoRoot()
	if root == "" {
		return ""
	}
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

func repoRoot() string {
	dir, _ := os.Getwd()
	for {
		if _, err := os.Stat(filepath.Join(dir, ".git")); err == nil {
			return dir
		}
		parent := filepath.Dir(dir)
		if parent == dir {
			return ""
		}
		dir = parent
	}
}

func readRepoFile(parts ...string) string {
	root := repoRoot()
	if root == "" {
		fatal("Cannot find repo root (no .git directory)")
	}
	data, err := os.ReadFile(filepath.Join(append([]string{root}, parts...)...))
	if err != nil {
		fatal("Read file: %v", err)
	}
	return string(data)
}

// ── FFI helpers ─────────────────────────────────────────

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
			fatal("Missing export %s: %v", v, err)
		}
		procs[k] = p
	}
	return procs
}

// cStr converts a Go string to a null-terminated C string pointer.
// The backing memory is pinned in the pins slice to prevent GC collection.
func cStr(s string) uintptr {
	b := append([]byte(s), 0)
	pins = append(pins, b)
	return uintptr(unsafe.Pointer(&b[0]))
}

// goStr reads a null-terminated C string from a pointer.
//
//go:nosplit
func goStr(ptr uintptr) string {
	if ptr == 0 {
		return ""
	}
	// Walk the C string to find the null terminator.
	p := ptr
	for *(*byte)(unsafe.Pointer(p)) != 0 { //nolint: gosec
		p++
	}
	n := int(p - ptr)
	return string(unsafe.Slice((*byte)(unsafe.Pointer(ptr)), n)) //nolint: gosec
}

func lastErr(p map[string]*syscall.Proc) string {
	r, _, _ := p["last_error"].Call()
	return goStr(r)
}

func fatal(format string, args ...any) {
	fmt.Fprintf(os.Stderr, "Error: "+format+"\n", args...)
	os.Exit(1)
}
