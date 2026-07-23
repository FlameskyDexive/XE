# High-Performance Engine Roadmap (Prowl)

**Date**: 2026-07-23  
**Runtime**: .NET 10 + CoreCLR (already `net10.0`; Runtime `IsAotCompatible`)  
**Success criteria**: All goals, staged (route **A → B → C**, optional **D**)

---

## Goals

1. **Idle / empty-scene GPU** under control (editor + player).
2. **CoreCLR throughput**: hot-path zero-alloc discipline, analyzers, host GC/PGO flags.
3. **RHI path** (Vulkan / D3D12) after command-buffer boundary is clean.
4. Optional later: dual ECS / data-oriented gameplay without breaking GO/MB API surface.

Unity 4.7 is used as a **capability matrix** only — see `Books/17-unity47-capability-matrix.md`.

---

## Stage A — Idle GPU budget

### Root causes (empty project)

- Large **ShadowAtlas** (historically 4K/8K) cleared every frame even with no casters.
- Full **DefaultRenderPipeline** (prepass MRT, shadow pass scaffolding) for Scene + Game views.
- Paper/Origami UI cost (secondary; not the primary empty-scene GPU driver).

### Actions

| Item | Status |
|------|--------|
| ShadowAtlas on-demand (`NeedsShadowAtlas`, lazy init, `PreferredSize`) | Done (Runtime) |
| Editor `ShadowAtlas.PreferredSize = 2048` | Done |
| RenderStats: atlas clear / pass skipped counters | Done |
| GameView camera collect without LINQ | Done |
| Editor Preview pipeline / quality tier | Done (QualitySettings: Preview/Full, ShadowAtlasSize, ShadowsEnabled) |
| Skip render when Scene/Game panel invisible | Done (Origami DockSpace only calls OnGUI on the active tab per leaf — background-tab panels already do zero work) |
| Non-Play GameView throttle / dirty cache | Done (~15Hz edit-mode render + re-render on RT size change) |
| QualitySettings surface | Done |

### Acceptance (A)

- No shadow casters → **no** ShadowAtlas GPU clear. ✅ (`NeedsShadowAtlas` gate)
- Hidden viewport → **0** full pipeline runs. ✅ (Origami active-tab-only dispatch)
- Stats distinguish Preview vs Full and atlas activity. ✅ (`QualitySettings.Tier`, `ShadowAtlasClears`, `ShadowPassesSkipped`)

---

## Stage B — CoreCLR throughput

- Host flags via `Directory.Build.props` / runtimeconfig: Server GC, Concurrent GC, TieredPGO, RetainVM (as applicable to Editor/Samples hosts).
- **Prowl.Analyzers** (PR0001–PR0007): port of XEngine XP0001–7.
  - Stage 1: HotPath rules **Error**; PR0001 **Warning** broadly (tighten by directory later).
  - Stage 2: Runtime PR0001 **Error**; batch LINQ removal.
  - Stage 3: solution-wide PR0001 Error (optional TreatWarningsAsErrors).
- Mark `[HotPath]` on Game render loop, `DefaultRenderPipeline.Internal_Render`, command executor hot path, scene update tight loops.
- Jobs / main-thread-only rules optional (PR0008 later).

### Acceptance (B)

- Analyzer tests green; hot-path violations fail build.
- No new LINQ on annotated hot paths; measurable alloc reduction on update/render loops.

---

## Stage C — RHI

- Abstract GfxDevice-like boundary over existing CommandBuffer + executor.
- Vulkan and/or D3D12 backends; keep OpenGL as reference/fallback while porting.
- Do not start C until Stage A idle cost is acceptable and command recording is backend-agnostic.

### Acceptance (C)

- Same high-level render pipeline runs on at least one modern backend with feature parity for Default pipeline core passes.

---

## Stage D — Optional dual ECS

- Data-oriented world for mass simulation **alongside** GO/MB, not a forced rewrite.
- Out of scope until A–C prove out.

---

## Analyzers summary

| ID | Rule | Default (stage 1) |
|----|------|-------------------|
| PR0001 | Ban System.Linq | Warning (Error on hot dirs later) |
| PR0002 | HotPath no capturing lambda | Error |
| PR0003 | HotPath no class enumerator foreach | Error |
| PR0004 | HotPath no string + / interpolation | Error |
| PR0005 | HotPath no await | Error |
| PR0006 | HotPath no params alloc | Error |
| PR0007 | HotPath no `new` ref/array (`[Pool]` exempt) | Error |

Attributes: `Prowl.HotPathAttribute`, `Prowl.PoolAttribute` under `Prowl.Runtime/Attributes/`.

---

## Timeline (indicative)

```
Week 0–1   Analyzers + tests + Directory.Build.props + HotPath marks
Week 1–4   Stage A GPU remainder (Preview, viewport skip, throttle)
Week 2–3   Capability matrix v1 + P0 backlog (this doc + Book 17)
Month 2    Stage B throughput / Runtime PR0001 Error
Month 3+   Stage C RHI; analyzer solution-wide Error; matrix P1 modules
```

---

## Risks

| Risk | Mitigation |
|------|------------|
| PR0001 floods compile | Staged severity + directory scope |
| Scope creep via “sync Unity” | Lock to matrix; P2/P3 stay backlog |
| RHI too early | Gate on Stage A + clean CB boundary |

---

## Related

- Plan: `.zcode/plans/plan-sess_*` (high-performance + analyzers + matrix)
- Book: `Books/17-unity47-capability-matrix.md`
