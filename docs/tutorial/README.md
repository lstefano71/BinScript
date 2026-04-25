# BinScript Tutorial

Learn to describe binary formats with BinScript through practical, progressively complex examples. Each chapter is self-contained but builds on concepts from earlier chapters.

## Chapters

### Getting Started
1. **[Your First Script](01-first-script.md)** — Parse a simple file header with fixed fields
2. **[Types and Endianness](02-types-and-endianness.md)** — Primitive types, byte order, and explicit endian qualifiers

### Core Concepts
3. **[Enums and Flags](03-enums-and-flags.md)** — Named constants with `enum` and bitfields with `bits struct`
4. **[Arrays and Strings](04-arrays-and-strings.md)** — Fixed-count arrays, counted arrays, `cstring`, `fixed_string`, and `bytes`
5. **[Computed Fields](05-computed-fields.md)** — `@let` bindings, `@derived` fields, and `const` declarations

### Control Flow
6. **[Match Expressions](06-match-expressions.md)** — Discriminated unions, guard arms, and pattern matching
7. **[Seeking and Alignment](07-seeking-and-alignment.md)** — `@seek`, `@at` blocks, `@skip`, `@align`, and offset-based navigation

### Advanced
8. **[Pointers](08-pointers.md)** — `ptr<T>`, `relptr<T>`, pointer width, and nullable pointers
9. **[Assertions and Validation](09-assertions.md)** — Magic number checks, `@assert`, and `@coverage`
10. **[Reusable Expressions with @map](10-map-expressions.md)** — Pure inlined functions for common computations
11. **[Modules and Imports](11-modules-and-imports.md)** — Splitting scripts with `@import`

### Real-World Walkthroughs
12. **[Walkthrough: TCP/IP Packet](12-walkthrough-tcp.md)** — Network protocol headers with conditional fields
13. **[Walkthrough: WAV Audio](13-walkthrough-wav.md)** — RIFF chunk-based format with nested structures
14. **[Walkthrough: PNG Image](14-walkthrough-png.md)** — Chunk iteration, CRC validation, and standard library usage

---

## Prerequisites

- A compiled BinScript library (C-ABI DLL) or the `bsxtool` CLI
- Sample binary files (provided inline as hex where possible)

## Conventions

Throughout this tutorial:
- Code blocks show complete, compilable `.bsx` scripts
- Byte sequences are shown as hex arrays: `[0x89, 0x50, 0x4E, 0x47]`
- JSON output shows what parsing produces
- 🔑 marks key concepts introduced for the first time
- ⚠️ marks common pitfalls
