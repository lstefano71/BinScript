# ADR-004: Reserved Keywords and Identifier Escaping

## Status

Proposed

## Context

BinScript reserves 17 keywords (`struct`, `enum`, `bits`, `const`, `match`, `when`, `if`, `else`, `bit`, `bool`, `bytes`, `cstring`, `string`, `fixed_string`, `ptr`, `relptr`, `null`, `true`, `false`) and 31 directive tokens (`@root`, `@seek`, etc.). The lexer unconditionally promotes matching identifiers to keyword tokens — there is no escaping mechanism.

This means that if a binary format has a field naturally named `string`, `match`, `type` (not reserved, fortunately), `if`, or `null`, the user must rename it. This is a minor but real friction point.

Two reserved keywords — `if` and `else` — have no syntactic production today. They are reserved for potential future conditional field support.

## Current Limitation

```
// This does NOT work — `string` is a keyword
struct Record {
    string: cstring,       // ERROR: unexpected token 'string'
}

// Workaround: rename the field
struct Record {
    str_val: cstring,
}
```

## Proposed Solution (Future)

Add backtick escaping, similar to Kotlin:

```
struct Record {
    `string`: cstring,     // OK — backticks escape the keyword
    `if`: u8,              // OK
}
```

### Implementation Sketch

1. **Lexer**: When encountering `` ` ``, read until the closing `` ` ``. Emit as `TokenType.Identifier` regardless of keyword table match.
2. **Parser**: No changes needed — backtick-escaped tokens are plain identifiers.
3. **AST / Bytecode**: Field name stored without backticks.
4. **JSON output**: Field name appears without backticks (e.g., `"string": "hello"`).

### Alternatives Considered

| Approach | Pros | Cons |
|----------|------|------|
| Backticks (`` `if` ``) | Familiar (Kotlin), minimal parser impact | New lexer state |
| Raw identifier (`r#if`) | Familiar (Rust) | Clashes with potential hex literal syntax |
| At-prefix (`@if` as identifier) | Already used for directives | Ambiguous with directive tokens |
| Unreserve unused keywords | Zero implementation cost | Prevents future use of those keywords |

## Decision

**Deferred.** The keyword set is small enough and collisions are rare enough that renaming fields is an acceptable workaround today. When the need arises (or when `if`/`else` get a syntactic role), implement backtick escaping.

## Consequences

- Users must avoid keyword names for fields and parameters
- The limitation is documented in the language specification
- When implemented, backtick escaping will be backward-compatible (existing scripts unaffected)
