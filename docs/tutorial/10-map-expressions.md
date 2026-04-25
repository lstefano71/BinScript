# 10. Reusable Expressions with `@map`

When the same computation appears in multiple places, `@map` lets you define a named, reusable expression with parameters.

## The Problem

In a PE file, every data directory entry stores a Relative Virtual Address (RVA) that must be converted to a file offset. The formula is:

```
pointer_to_raw_data + (rva - virtual_address)
```

Without `@map`, you'd copy-paste this at every data directory access.

## Defining a Map

```
@map rva_to_offset(rva: u32, sections: SectionHeader[]): u32 =
    sections
        .find(s => s.virtual_address <= rva && rva < s.virtual_address + s.virtual_size)
        .pointer_to_raw_data + (rva - sections
            .find(s => s.virtual_address <= rva && rva < s.virtual_address + s.virtual_size)
            .virtual_address)
```

### 🔑 `@map` Syntax

```
@map name(param: type, ...): return_type = expression
```

- **Parameters** have explicit types (including array types with `[]`)
- **Return type** is declared after the `:`
- **Body** is a single expression (no statements, no side effects)
- Maps are **inlined at compile time** — there is no function call overhead

## Using a Map

```
@let import_offset = rva_to_offset(import_table_rva, sections)
@at(import_offset) {
    import_table: ImportDirectory,
}
```

## Array Search Methods

Maps often use array search methods with lambda predicates:

| Method | Description |
|--------|-------------|
| `.find(pred)` | First element where predicate is true |
| `.find_or(pred, default)` | First match, or default if none |
| `.any(pred)` | True if any element matches |
| `.all(pred)` | True if all elements match |

```
// Find the section containing an RVA
sections.find(s => s.virtual_address <= rva && rva < s.virtual_address + s.virtual_size)

// Check if any section is executable
sections.any(s => s.characteristics & 0x20000000 != 0)
```

### Lambda Syntax

```
parameter => expression
```

The parameter is bound to each element in turn. The expression must return a boolean for search methods.

## Block Expressions

For complex computations, use block expressions with `@let` bindings:

```
@map classify(flags: u32): u32 = {
    @let is_exe = flags & 0x0002 != 0,
    @let is_dll = flags & 0x2000 != 0,
    is_exe + is_dll * 2
}
```

A block expression is `{ @let bindings..., result_expression }`. The last expression is the value of the block.

## Design Constraints

🔑 Maps are **not functions**:
- No recursion (a map cannot call itself)
- No side effects (cannot modify the parse state)
- No control flow (no `if`/`else`, only expressions)
- Inlined at every call site at compile time

This keeps the language simple and fully compilable to efficient bytecode. See [ADR-001](../adr/ADR-001-map-pure-expressions.md) for the design rationale.

## Practical Example: PE RVA Resolution

```
@default_endian(little)

@map rva_to_offset(rva: u32, sections: SectionHeader[]): u32 = {
    @let section = sections.find(s =>
        s.virtual_address <= rva &&
        rva < s.virtual_address + s.virtual_size),
    section.pointer_to_raw_data + (rva - section.virtual_address)
}

struct SectionHeader {
    name: fixed_string[8],
    virtual_size: u32,
    virtual_address: u32,
    size_of_raw_data: u32,
    pointer_to_raw_data: u32,
    @skip(16)
}
```

## What You Learned

- `@map` defines reusable inlined expressions with typed parameters
- Array methods `.find()`, `.any()`, `.all()` with lambda predicates
- Block expressions `{ @let ..., result }` for complex computations
- Maps are compile-time inlined, not runtime functions

**Next**: [Modules and Imports →](11-modules-and-imports.md)
