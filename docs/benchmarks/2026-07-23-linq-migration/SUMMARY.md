# LINQ migration summary

This file is a stub for older links.

**Canonical report:** [README.md](./README.md)

PR0001 allowlist (from README):

1. `ToList()` on statically typed arrays / `List<T>` (not on `[HotPath]`)
2. `Any(static ...)` and `FirstOrDefault(static ...)` on arrays / `List<T>`
3. `All(static ...)` and predicate `Count(static ...)` on `List<T>` only
