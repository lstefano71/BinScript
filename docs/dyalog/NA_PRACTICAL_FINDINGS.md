# Dyalog APL `⎕NA` — Practical Findings from Live Testing

> Tested on Dyalog APL v20.0, 64-bit Unicode, Windows.
> Runtime DLL: `dyalog64.dll`

## Key Gotcha: Single-Argument Enclosure

**The most important lesson**: `⎕NA` functions receive a **nested vector** where each item corresponds to one C parameter. For single-argument functions, you must enclose the argument with `,⊂` to create a 1-element vector.

```apl
⍝ WRONG — 'Hello' is 5 chars → LENGTH ERROR (5 ≠ 1 declared param)
'strlen' ⎕NA 'P msvcrt|strlen <0C[]'
strlen 'Hello'          ⍝ LENGTH ERROR!

⍝ CORRECT — ,⊂ creates a 1-element nested vector
strlen ,⊂'Hello'        ⍝ → 5
```

Multi-argument functions work naturally because APL sees the right number of items:
```apl
'cpy' ⎕NA 'dyalog64|MEMCPY >U1[] <U1[] P'
cpy 5 (72 101 108 108 111) 5    ⍝ 3 args → 3-element vector → OK
```

## Verified Working Patterns

### dyalog64.dll — MEMCPY

All of these work correctly:

```apl
⍝ Byte copy
'cpy' ⎕NA 'dyalog64|MEMCPY >U1[] <U1[] P'
cpy 5 (72 101 108 108 111) 5           ⍝ → 72 101 108 108 111

⍝ Double copy
'cpyF' ⎕NA 'dyalog64|MEMCPY >F8[] <F8[] P'
cpyF 3 (3.14 2.718 1.414) (3×8)        ⍝ → 3.14 2.718 1.414

⍝ Signed ints (including negatives and max value)
'cpyI' ⎕NA 'dyalog64|MEMCPY >I4[] <I4[] P'
cpyI 4 (¯1 0 42 2147483647) (4×4)      ⍝ → ¯1 0 42 2147483647

⍝ Struct {I4 F8} round-trip
'cpyS' ⎕NA 'dyalog64|MEMCPY >{I4 F8}[] <{I4 F8}[] P'
cpyS 2 ((7 3.14)(42 2.718)) (2×16)     ⍝ → (7 3.14)(42 2.718)

⍝ Input/output (=) direction — buffer gets overwritten
'cpy2' ⎕NA 'dyalog64|MEMCPY =U1[] <U1[] P'
cpy2 (1 2 3 4 5) (10 20 30 40 50) 5    ⍝ → 10 20 30 40 50
```

### dyalog64.dll — STRNCPYW

```apl
'scpy' ⎕NA 'dyalog64|STRNCPYW >0T[] <0T[] P'
scpy 50 'Hello ⎕NA!' 50                ⍝ → 'Hello ⎕NA!'
```

### C Runtime — strlen / wcslen

```apl
⍝ Byte strlen via msvcrt
'strlen' ⎕NA 'P msvcrt|strlen <0C[]'
strlen ,⊂'Hello'                        ⍝ → 5

⍝ Wide wcslen (counts APL characters correctly)
'wcslen' ⎕NA 'P msvcrt|wcslen <0T[]'
wcslen ,⊂'Hello World'                  ⍝ → 11
wcslen ,⊂'⍳⍴⍵⍺∊'                       ⍝ → 5  (APL glyphs!)
```

### kernel32.dll — System Information

```apl
⍝ Simple scalar return, no args
⎕NA 'U4 kernel32|GetCurrentProcessId'
GetCurrentProcessId                      ⍝ → 50584

⍝ 64-bit integer return
⎕NA 'U8 kernel32|GetTickCount64'
GetTickCount64                           ⍝ → 1140887406

⍝ Output string + return value (result vector)
⎕NA 'U4 kernel32|GetWindowsDirectoryW >0T U4'
GetWindowsDirectoryW 260 260             ⍝ → (length 'C:\WINDOWS')

⍝ Environment variables
⎕NA 'U4 kernel32|GetEnvironmentVariableW <0T >0T U4'
GetEnvironmentVariableW 'USERNAME' 256 256
⍝ → (length 'stf')

⍝ Temp path
⎕NA 'U4 kernel32|GetTempPathW U4 >0T'
GetTempPathW 260 260
⍝ → (length 'C:\Users\stf\AppData\Local\Temp\')

⍝ System directory
⎕NA 'U4 kernel32|GetSystemDirectoryW >0T U4'
GetSystemDirectoryW 260 260
⍝ → (length 'C:\WINDOWS\system32')

⍝ Current directory
⎕NA 'U4 kernel32|GetCurrentDirectoryW U4 >0T'
GetCurrentDirectoryW 260 260
```

### kernel32.dll — GlobalMemoryStatusEx (Structure with `=`)

```apl
⎕NA 'I4 kernel32|GlobalMemoryStatusEx ={U4 U4 U8 U8 U8 U8 U8 U8}'
r ← GlobalMemoryStatusEx ,⊂(64 0 0 0 0 0 0 0)
info ← 2⊃r
⍝ 2⊃info → memory load %
⍝ 3⊃info → total physical bytes
⍝ 4⊃info → available physical bytes
```

Result: `60% used, 32706MB total, 12995MB free`

### kernel32.dll — GetDiskFreeSpaceExW (Multiple `>` Outputs)

```apl
⎕NA 'I4 kernel32|GetDiskFreeSpaceExW <0T >U8 >U8 >U8'
r ← GetDiskFreeSpaceExW 'C:\' 1 1 1
⍝ 1⊃r → success
⍝ 2⊃r → free bytes available to caller
⍝ 3⊃r → total bytes
⍝ 4⊃r → total free bytes
```

Result: `17 GB free / 223 GB total`

### kernel32.dll — GetSystemInfo (Nested Structure Output)

```apl
⎕NA 'kernel32|GetSystemInfo >{  {U2 U2} U4 P P P U4 U4 U4 U2 U2}'
r ← GetSystemInfo ,⊂1
⍝ 1⊃1⊃r → processor architecture (9 = x64)
⍝ 2⊃r   → page size (4096)
⍝ 7⊃r   → number of processors
```

### kernel32.dll — High-Resolution Timer (⎕FR←1287 Required)

```apl
⎕FR ← 1287   ⍝ 128-bit decimal for 64-bit integer precision
⎕NA 'I4 kernel32|QueryPerformanceCounter >I8'
⎕NA 'I4 kernel32|QueryPerformanceFrequency >I8'
f ← 2⊃QueryPerformanceFrequency ,⊂1    ⍝ freq = 10000000
c1 ← 2⊃QueryPerformanceCounter ,⊂1
⎕DL 0.1
c2 ← 2⊃QueryPerformanceCounter ,⊂1
elapsed ← (c2-c1)÷f                     ⍝ → ~0.117s
```

### user32.dll — Screen Metrics

```apl
⎕NA 'I4 user32|GetSystemMetrics I4'
GetSystemMetrics ,⊂0     ⍝ → 1920 (screen width)
GetSystemMetrics ,⊂1     ⍝ → 1080 (screen height)
GetSystemMetrics ,⊂80    ⍝ → 1 (monitor count)
```

### advapi32.dll — GetUserNameW

```apl
⎕NA 'I4 advapi32|GetUserNameW >0T =U4'
GetUserNameW 256 256     ⍝ → (1 'stf' 4)
```

### Full Memory Lifecycle: HeapAlloc → MEMCPY → HeapFree

```apl
⎕NA 'P kernel32|HeapAlloc P U4 P'
⎕NA 'I4 kernel32|HeapFree P U4 P'
⎕NA 'P kernel32|GetProcessHeap'
'wr' ⎕NA 'P dyalog64|MEMCPY P <U1[] P'
'rd' ⎕NA 'dyalog64|MEMCPY >U1[] P P'

heap ← GetProcessHeap
addr ← HeapAlloc heap 0 16        ⍝ Allocate 16 bytes
wr addr (⍳16) 16                  ⍝ Write 1..16
data ← rd 16 addr 16              ⍝ Read back → 1 2 3 ... 16
ok ← HeapFree heap 0 addr         ⍝ Free → 1 (success)
```

### Threaded `&` Suffix

```apl
⎕NA 'U8 kernel32|GetTickCount64&'
GetTickCount64    ⍝ Runs in separate OS thread, other APL threads can proceed
```

## OS Version Note

`GetVersionExW` returns `6.2 build 9200` due to Windows manifest compatibility — this is expected on modern Windows (returns Win 8 compatibility level unless the app has a proper manifest).

## Summary Table

| Pattern | Declaration | Key Insight |
|---------|-------------|-------------|
| Scalar by value | `I4`, `U8`, `F8` | No direction prefix |
| Input pointer | `<U1[]`, `<0T[]` | Pass data array directly |
| Output pointer | `>0T`, `>I4[]` | Pass element count to reserve |
| Input/output | `={U4 U4 ...}` | Pass data, get modified copy |
| Null-terminated string | `<0T`, `>0T` | `0` implies array, `[]` optional |
| Structure | `{I4 F8}` | Maps to C struct, model padding explicitly |
| Null pointer | `P` + value `0` | Passes raw pointer by value |
| Threaded call | `fn&` | Runs in separate OS thread |
| Wide string portability | `<0T` (no width) | Defaults to T2 on Windows, T4 on Unix |
