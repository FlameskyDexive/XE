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

## Restoration audit (`9037a12a` .. HEAD)

Scope: every Stage A/B commit from `9037a12a` (PR0001 introduction) through
`87919e46` (benchmark suite + allowlist), including the LINQ removals that landed
inside the latter checkpoint commit (GameObject, Scene, RuntimeUtils, Camera,
RenderPipeline, etc.).

Commits covered:

| Commit | Title |
|--------|-------|
| `9037a12a` | perf(stage-a): idle-scene GPU budget + Prowl.Analyzers (PR0001-7) |
| `d5c2225a` | perf(analyzers): mark per-frame RenderStats methods [HotPath] |
| `36fc87ff` | docs: mark Stage A acceptance criteria as verified |
| `2c9715a0` | perf(stage-b): remove LINQ from DefaultRenderPipeline render path |
| `cf58aff7` | perf(stage-b): remove LINQ from Rendering/ |
| `68b07b1d` | perf(stage-b): remove LINQ from 8 Runtime files (batch 2) |
| `12470b40` | perf(stage-b): remove LINQ from EmbeddedResources, SceneComponentRegistry, ClayBackedImporter (batch 3) |
| `9ce2d1a9` | perf(stage-b): remove LINQ from Debug gizmo mesh upload (batch 4) |
| `87919e46` | perf(stage-b): add LINQ migration benchmark suite, results, and PR0001 allowlist |

**26** removed `ToList` / `Any` / `All` / `Count` / `FirstOrDefault` call sites were
re-checked against the allowlist (source static type + capturing + HotPath). Result:

### Restored (2)

1. `Scene.Count` — predicate `Count` over a concrete `List<GameObject>`, restored as
   `_allObj.Count(static obj => !obj.IsDisposed)`.
2. `DefaultRenderPipeline.RenderSkybox` — `FirstOrDefault` over a concrete
   `List<IRenderableLight>` with a non-capturing predicate, restored as
   `lights.FirstOrDefault(static l => l is IRenderableLight rl && rl.GetLightType() == LightType.Directional)`.
   Benchmarks put this 1.3–2× ahead of the manual scan at representative sizes with zero
   per-call allocation.

### Kept as loops (24) — near-miss reasons

| Site | Why not restored |
|------|------------------|
| `GameObject.Find` / `FindGameObjectWithTag` `AllObjects.FirstOrDefault` | Source is `IEnumerable<GameObject>`; predicates capture args |
| `GameObject` `components.ToList()` / parameterless `FirstOrDefault()` | Source is `IReadOnlyCollection<MonoBehaviour>` |
| `GameObject` `_components.Count(x => …)` | `List<T>` but captures `componentType` |
| `GameObject` `types.All(…)` | Source is `Type[]`; `All` not allowlisted on arrays; also captures |
| `GameObject` / `Scene` `GetComponents<…>().ToList()` | Iterator `IEnumerable` source |
| `SceneComponentRegistry` `GetMethods().FirstOrDefault` | `MethodInfo[]` but captures `method` |
| `Input` `_actionMaps.FirstOrDefault` | `List<T>` but captures `mapName` |
| `InputActionMap.Enabled` `_actions.Values.Any` | Non-capturing, but source is `Dictionary.ValueCollection` |
| `LayerFilter` `Constraints.Any` | `ReadOnlyHashSet`; predicates capture opposite body |
| `RenderPipeline` `Keys.Where(…).ToList()` | Forbidden chain; iterator source |
| `EmbeddedResources` `resourceNames.FirstOrDefault` | `string[]` but predicates capture path |
| `PrefabAsset` `GetComponents().Count()` | Iterator + parameterless `Count` |
| `Scene.IsEmpty` `AllObjects.Any()` | Iterator + parameterless `Any` |
| `Scene.GetAllCameras` `SelectMany().Distinct().ToList()` | Forbidden multi-operator chain |
| `RuntimeUtils` `GetTypes().FirstOrDefault` | `Type[]` but captures `typeNameOnly` |
| `RuntimeUtils` reflection `FirstOrDefault` | `IEnumerable` sources + capture `name` |
| `ClayBackedImporter` `animations.Select(…).ToList()` | `ToList` receives the `Select` iterator, not the list |
| `GameViewPanel` camera `SelectMany().Where().OrderBy().ToList()` | Forbidden multi-operator chain |

`Where` / `Select` / `Cast` / `Concat` / `Distinct` / `OrderBy` / `ToArray` and all other
removed operators never qualify for the allowlist and correctly remain loops.

## Artifacts

- `raw/results/*-report-full.json`: complete timing and GC measurements
- `raw/results/*-report-github.md`: full human-readable result table
- `raw/allocation-probe.csv`: deterministic bytes-per-operation comparison
- `raw/disassembly/results/*-asm.md`: disputed-case assembly
- `raw/disassembly/results/*-disassembly-report.html`: combined disassembly report
- `Tools/Prowl.Runtime.Benchmarks/analyze_results.py`: reproducible timing/allocation roll-up
