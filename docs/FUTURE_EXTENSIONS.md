# Future Extensions

This document tracks language and tooling extensions that have been discussed, designed at a high level, but deferred from the current implementation. Each entry includes motivation and a rough implementation sketch.

## Const-Folding

**Motivation**: Several constructs require compile-time constant literals where a `const`-declared value would be natural:

```
const HEADER_BITS = 4;
bits struct Flags : bits[HEADER_BITS] { ... }   // ERROR today: bits[N] requires INT_LITERAL

const RESERVED = 16;
@skip(RESERVED)                                  // ERROR today: @skip(N) requires INT_LITERAL

const MAX_DEPTH = 128;
struct Node @max_depth(MAX_DEPTH) { ... }        // ERROR today: @max_depth(N) requires INT_LITERAL
```

**Affected constructs**: `bits[N]`, `@skip(N)`, `@max_depth(N)`.

**Implementation sketch**:
1. After parsing, resolve `const` declarations to their literal values
2. Add a const-folding pass that evaluates constant expressions (arithmetic on literals and other consts)
3. Store folded values in a const table accessible during parsing of literal-only positions
4. The parser would accept `Expression` in these positions; the semantic analyzer would verify the expression is const-foldable

**Complexity**: Medium. Requires a new compiler pass between parsing and bytecode emission.

---

## String Interpolation

**Motivation**: Assert messages are currently string literals only. Interpolated strings would make diagnostics far more useful:

```
// Today
@assert(magic == 0x5A4D, "bad DOS magic")

// With interpolation
@assert(magic == 0x5A4D, $"expected 0x5A4D, got {magic}")
@assert(size <= max_size, $"size {size} exceeds max {max_size}")
```

Could also be used in `@derived` fields for computed labels:
```
@derived label: string = $"section_{index}"
```

**Requirements**:
- Must support full expression paths including multi-segment references: `{hdr.data_dirs[6].va}`
- Must support integer formatting (hex, decimal)
- Must handle all `StackValue` types (Int, Float, String, Bytes)

**Implementation sketch**:
1. **Lexer**: Recognize `$"..."` as interpolated string token; parse `{expr}` holes
2. **Parser**: Parse each interpolation hole as a full `Expression`
3. **AST**: New `InterpolatedStringExpr` node containing alternating literal segments and expression nodes
4. **Emitter**: Emit expression evaluation for each hole, then a `FormatString` opcode that pops N values + a template
5. **Runtime**: Format values to string representation, concatenate segments

**Complexity**: High. Cross-cutting change touching lexer → parser → AST → emitter → runtime. The expression evaluator already handles all the expression types; the new work is string formatting and concatenation.

---

## Keyword Escaping

**Motivation**: Reserved keywords cannot be used as field names. See [ADR-004](adr/ADR-004-keyword-escaping.md).

**Proposed syntax**: Backtick escaping (`` `keyword` ``), similar to Kotlin.

**Complexity**: Low. Lexer-only change.

---

## Conditional Fields (`if`/`else`)

**Motivation**: The keywords `if` and `else` are reserved but unused. They could enable conditional field inclusion:

```
struct OptionalHeader(arch) {
    standard_fields: u32,
    if (arch == 0x20B) {
        extra_fields: u64,
    }
}
```

**Open questions**:
- Should this be a compile-time or runtime construct?
- How does it interact with produce mode? (The data source would need to know which branch was taken)
- Is `@at ... when guard { }` sufficient for most use cases?

**Complexity**: High. Affects parser, AST, emitter, both runtime engines, and data source/emitter interfaces.
