# BinScript Integration Examples

Working examples showing how to use the BinScript C-ABI from C, Python, and Go.

Each example is a standalone, runnable program that demonstrates parsing and/or producing binary data using the BinScript library. These examples accompany the [integration tutorial](../../docs/tutorial/15-integration-c.md).

## Prerequisites

### Build the BinScript DLL

```bash
dotnet publish src/BinScript.Interop -c Release -r <your-rid>
```

Common RIDs: `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`.

### DLL Discovery

All examples auto-discover the DLL by walking up from their location to find the publish output. To override, set the `BINSCRIPT_DLL` environment variable:

```bash
# Windows
set BINSCRIPT_DLL=C:\path\to\BinScript.Interop.dll

# Linux/macOS
export BINSCRIPT_DLL=/path/to/BinScript.Interop.so
```

## Examples

### C — BMP Parse + Round-Trip Produce

```bash
cd tools/examples/c
# Windows (MSVC)
cl /nologo parse_bmp.c /I ..\..\..\src\BinScript.Interop /Fe:parse_bmp.exe
parse_bmp.exe ..\..\..\tests\samples\bmp\4x4_24bit.bmp

# Linux/macOS
gcc -o parse_bmp parse_bmp.c -I ../../../src/BinScript.Interop -ldl
./parse_bmp ../../../tests/samples/bmp/4x4_24bit.bmp
```

See [Tutorial Chapter 15: C Integration](../../docs/tutorial/15-integration-c.md).

### Python — GIF Parse

```bash
cd tools/examples/python
python parse_gif.py ../../../tests/samples/gif/2frame.gif
```

See [Tutorial Chapter 16: Python Integration](../../docs/tutorial/16-integration-python.md).

### Go — WAV Produce (Twinkle Twinkle Little Star 🎵)

```bash
cd tools/examples/go
go run produce_wav.go twinkle.wav
# Now play twinkle.wav!
```

See [Tutorial Chapter 17: Go Integration](../../docs/tutorial/17-integration-go.md).

## Maintenance

These examples use a subset of the C-ABI. When the API changes, verify they still compile and run. See `docs/C_ABI.md` for the full maintenance checklist.
