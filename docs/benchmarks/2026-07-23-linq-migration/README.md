# Runtime LINQ migration benchmark report

**Date:** 2026-07-23  
**Runtime:** .NET 10.0.10, x64 RyuJIT, TieredPGO  
**GC:** Concurrent Server GC  
**CPU:** Intel Core i5-14490F, 10 physical / 16 logical cores  
**BenchmarkDotNet:** 0.15.8

## Method

The suite compares the pre-migration LINQ shape with the explicit-loop replacement for:

- `FirstOrDefault`, `Any`, `All`, and predicate `Count`
- `Where(...).ToArray()`, `ToList()`, and `Cast<T>().ToList()`
- `Concat`, repeated/deep `Concat`, and `SelectMany(...).Distinct().ToList()`

Each operation was tested with array, `List<T>`, and iterator sources at 0, 1, 16, 256, and
4096 elements. The full run executed 300 benchmarks with 3 warm-up and 8 measurement iterations.
The disputed array/List cases were rerun with disassembly output.

Commands:

```powershell
dotnet run --project Tools/Prowl.Runtime.Benchmarks/Prowl.Runtime.Benchmarks.csproj `
  -c Release -- --filter "*SearchAndPredicateBenchmarks*" `
  "*MaterializationBenchmarks*" "*CompositionBenchmarks*" `
  --artifacts docs/benchmarks/2026-07-23-linq-migration/raw

dotnet run --project Tools/Prowl.Runtime.Benchmarks/Prowl.Runtime.Benchmarks.csproj `
  -c Release -- --filter "*DisputedLinqBenchmarks*" `
  --artifacts docs/benchmarks/2026-07-23-linq-migration/raw/disassembly

dotnet run --project Tools/Prowl.Runtime.Benchmarks/Prowl.Runtime.Benchmarks.csproj `
  -c Release -- --allocation-probe `
  docs/benchmarks/2026-07-23-linq-migration/raw/allocation-probe.csv

python Tools/Prowl.Runtime.Benchmarks/analyze_results.py `
  docs/benchmarks/2026-07-23-linq-migration/raw/results/BenchmarkRun-joined-2026-07-23-16-03-45-report-full.json `
  docs/benchmarks/2026-07-23-linq-migration/raw/allocation-probe.csv
```

## Findings

- `ToList()` on arrays and `List<T>` was consistently the strongest LINQ case. At 16–4096
  elements it was approximately 1.9–4.0 times faster than an indexed `Add` loop and allocated
  exactly the same bytes. The disassembly run reproduced the result.
- The focused disassembly shows the throughput/code-size tradeoff: LINQ `FirstOrDefault` emitted
  625–658 B versus 67–135 B for the loop, while LINQ `ToList` emitted 1,735–3,198 B versus
  175–219 B. The larger LINQ code is accepted only for the measured source/operator shapes.
- Non-capturing `FirstOrDefault` and `Any` predicates on arrays and lists were approximately
  1.3–2.0 times faster than the indexed loop at representative non-trivial sizes, with zero
  per-call allocation.
- At representative non-trivial sizes (16–4096), non-capturing `All` and predicate `Count` were
  consistently faster on `List<T>`. Their array results depended on size: LINQ won at 256/4096,
  while the loop could win at 16. Array forms therefore are not globally allowlisted for these
  two operators.
- Iterator-source predicate terminals were generally close to the loop (usually within about
  15%). They receive no automatic exception because the concrete specialization is lost and the
  loop remains clearer about enumeration and allocation.
- `Cast<T>().ToList()` loops were approximately 1.5–2.1 times faster and avoided 80–16,664 bytes
  of LINQ/iterator overhead depending on source and size.
- Explicit two-source concatenation was approximately 3.7–11 times faster for array/List sources.
  Repeated `Concat` was approximately 3.2–16 times slower than nested loops and allocated 696
  bytes per operation.
- The explicit `SelectMany` + deduplication loop was approximately 1.2–2 times faster at small and
  medium sizes and saved about 416 bytes. At 4096 items the throughput converged, but LINQ retained
  the allocation disadvantage.
- `Where(...).ToArray()` was mixed: the loop won at small/medium sizes while LINQ won for large
  array/List sources. The original Runtime sites also used captures, iterator sources, or chained
  materialization, so no broad exception is justified.

BenchmarkDotNet's allocated-byte column intermittently reported zero for methods that clearly
triggered Gen0 collections under .NET 10 Server GC. The committed allocation probe therefore uses
`GC.GetAllocatedBytesForCurrentThread` around warmed repeated calls. Its results are the allocation
source of truth for this report.

## PR0001 allowlist

PR0001 now permits only these measured source/operator shapes:

1. `ToList()` outside `[HotPath]` when the source is statically an array or `List<T>`.
2. `Any(static ...)` and `FirstOrDefault(static ...)` on arrays or `List<T>`.
3. `All(static ...)` and predicate `Count(static ...)` on `List<T>`.

Capturing lambdas, delegate variables, method groups, `IEnumerable<T>`-typed sources, query
expressions, `Where`, `Cast`, `Concat`, and multi-operator materialization remain warnings.
Explicit `static` is required so a future edit cannot silently introduce a closure allocation.

One migrated Runtime call site qualifies for restoration: `Scene.Count` uses predicate `Count`
over a concrete `List<GameObject>` and now uses an explicit `static` predicate. Other migrated
sites keep their loop replacements because their source shape, capturing behavior, operator chain,
or hot-path requirements differ from the allowlisted cases.

## Artifacts

- `raw/results/*-report-full.json`: complete timing and GC measurements
- `raw/results/*-report-github.md`: full human-readable result table
- `raw/allocation-probe.csv`: deterministic bytes-per-operation comparison
- `raw/disassembly/results/*-asm.md`: disputed-case assembly
- `raw/disassembly/results/*-disassembly-report.html`: combined disassembly report
- `Tools/Prowl.Runtime.Benchmarks/analyze_results.py`: reproducible timing/allocation roll-up
