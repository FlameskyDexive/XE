# Prowl Agent Guide

These instructions apply repository-wide. Prowl targets .NET 10/CoreCLR and uses
measured frame time, throughput, allocation, and GPU cost—not intuition—as the basis
for performance decisions.

Read the relevant project skill before changing or reviewing the corresponding area:

- [LINQ performance](skills/linq-performance/SKILL.md) — LINQ use, PR0001,
  collection queries, materialization, and the benchmark-backed allowlist.
- [Hot-path performance](skills/hot-path-performance/SKILL.md) — `[HotPath]`,
  PR0002–PR0007, per-frame CPU code, allocation, pooling, iteration, and caching.
- [Rendering and GPU performance](skills/rendering-gpu-performance/SKILL.md) —
  render pipelines, passes, command buffers, GPU resources, viewports, shadows, and RHI.
- [Performance validation](skills/performance-validation/SKILL.md) —
  benchmarks, analyzer changes, runtime configuration, regression verification, and
  documentation of performance evidence.

When a change spans multiple areas, apply every relevant skill. Do not weaken an
analyzer rule or broaden an allowlist without benchmark evidence and matching tests.
