# ADR-001: `@map` — Pure Expression Functions

## Status

Accepted

## Context

BinScript needs reusable expressions for common computations, most urgently for PE RVA-to-file-offset resolution. The same formula — find the containing section, then compute `pointer_to_raw_data + (rva - virtual_address)` — must be applied at every data directory access. Without a reuse mechanism, users must copy-paste the expression at each call site, violating DRY and inviting bugs.

The PRD explicitly states: *"Fixed built-in functions, no UDFs — keeps the language simple and fully compilable."* Any solution must respect this constraint.

## Decision

Introduce `@map` declarations: **compile-time inlined pure expressions with named parameters**.

### Syntax

```
@map name(param1: type1, param2: type2, ...): return_type = expression
```

### Example

```
@map rva_to_offset(rva: u64, sections: SectionHeader[]): u64 = {
    @let s = sections.find(s => rva >= s.virtual_address
                              && rva < s.virtual_address + s.virtual_size),
    s.pointer_to_raw_data + (rva - s.virtual_address)
}
```

### Constraints

- **Pure**: No side effects, no field reads, no `@seek`/`@at`, no emitter calls.
- **No recursion**: A `@map` cannot call itself or form cycles with other `@map` declarations.
- **Expression-only body**: The body consists of `@let` bindings and a final expression. No `if`, no loops, no statements.
- **Compile-time inlining**: The compiler substitutes the `@map` body at each call site and compiles the result to normal bytecode. There is no runtime function call mechanism.
- **Type-checked**: Parameters and return type are verified at compile time.

### Call-site syntax

```
@at(rva_to_offset(import_rva, sections)) {
    import_dir: ImportDirectory,
}
```

## Alternatives Considered

### Full User-Defined Functions
Would provide maximum power (control flow, recursion, local variables) but violates the design philosophy of a simple, fully compilable declarative language. Opens the door to Turing-complete scripts, making static analysis and bidirectional reasoning much harder.

### `@lookup` — Domain-Specific Declaration
```
@lookup rva_to_offset(rva: u64)
    from sections
    where rva >= virtual_address && rva < virtual_address + virtual_size
    => pointer_to_raw_data + (rva - virtual_address);
```
SQL-inspired syntax. More readable for the RVA case but too narrow — only handles the "search array, compute result" pattern. Other reusable expressions (alignment calculations, checksum formulas) wouldn't fit this syntax.

### Bare `.find()` Repeated at Each Call Site
No language change needed. Users repeat the full expression at each `@at` or `@derived` site. Works but is verbose and error-prone. A 4-line expression repeated 10+ times in a PE script is a maintenance burden.

## Consequences

- `@map` is NOT a function in the traditional sense — no stack frame, no return address, no closure. It's syntactic sugar for expression substitution with type checking.
- If users need true control flow or recursion in expressions, we would need to revisit UDFs. This is explicitly deferred.
- The compiler must detect cycles in `@map` references and reject them.
- `@map` bodies work in both parse and produce directions since they are pure expressions.
