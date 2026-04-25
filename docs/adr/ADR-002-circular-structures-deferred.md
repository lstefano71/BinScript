# ADR-002: Circular Structures — Deferred

## Status

Accepted (deferred to future version)

## Context

In-memory data structures can form graphs with cycles: node A points to node B, which points back to A. Examples include doubly-linked lists, graph adjacency lists, and self-referential trees with parent pointers.

BinScript v1 introduces guarded recursion with a runtime depth limit (default 256, overridable via `@max_depth`). When a circular reference is encountered, the depth limit is hit and a runtime error is produced.

The question is whether BinScript should support circular structures natively — detecting cycles, representing them in JSON, and reconstructing them in produce mode.

## Decision

**Defer circular structure support to a future version.** In v1, circular references are caught by the runtime depth limit and produce a clear error message.

### Rationale

1. **JSON cannot represent cycles.** JSON is a tree format. Representing cycles requires extensions like JSON Reference (`$ref`) which are non-standard and add complexity to every consumer of BinScript output.

2. **Real-world binary formats rarely have pointer cycles.** File formats (PE, ELF, ZIP, PNG, TIFF) use file offsets, not pointers, and are inherently acyclic. In-memory structures with cycles (doubly-linked lists, graphs) are uncommon targets for BinScript's primary use case of format description.

3. **Cycle detection adds runtime cost.** A visited-set check on every pointer dereference adds overhead to the fast path. For the vast majority of use cases (acyclic data), this cost is wasted.

4. **Produce direction is especially hard.** Reconstructing a cyclic graph from a tree representation requires a reference/identity system that doesn't exist in the current architecture.

## Future Options to Explore

When revisiting this decision, consider:

### Option A: `$ref`-Style Back-References
Emit pointer targets with IDs, and back-references as `{ "$ref": "#/path/to/node" }`. Requires changes to `IResultEmitter` and `IDataSource`, and every emitter implementation must support references.

### Option B: Visited-Set Tracking in the VM
Track visited (pointer_value, struct_type) pairs. When a cycle is detected, emit a sentinel value instead of recursing. Adds a hash set lookup per `ptr<T>` dereference.

### Option C: `@max_visits` Annotation
```
struct GraphNode @max_visits(1) {
    value: u32,
    neighbors: ptr<GraphNode, u64>[count],
}
```
A node visited more than N times emits a reference marker instead of full content. More explicit than a global visited set.

### Option D: Graph-Mode Parsing
A new parsing mode (`ParseStrictness.Graph`?) that enables cycle detection and reference tracking. Off by default, opt-in per parse call.

## Consequences

- Users with circular in-memory structures must pre-process their data (break cycles) before BinScript can parse them.
- The depth limit (default 256) provides a safety net — parsing circular data will terminate with a clear error rather than stack-overflowing.
- This is an acceptable limitation for v1. The stdlib formats are all acyclic, and the memory struct examples use singly-linked lists (acyclic with null termination).
