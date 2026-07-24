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
  - Stage 2: Runtime LINQ migration. **Done.**
  - Stage 3: complete before/after BenchmarkDotNet gate. **Done (300 cases + disputed-case disassembly).**
  - Stage 4: benchmark-backed PR0001 operator/source allowlist. **Done (initial allowlist).**
- `[HotPath]` coverage: Game simulation/render frame, `DefaultRenderPipeline.Internal_Render`,
  command executor, and scene callback loops. **Done.**
- Render-frame allocation cleanup: reuse image-effect lists/contexts, renderable/light collection
  buffers, cull masks, grid renderable, and render-target format arrays. **Done (steady-state).**
- Jobs / main-thread-only rules optional (PR0008 later).

### LINQ benchmark gate

PR0001 is a migration aid, not an assumption that every LINQ expression is slower. Modern .NET may specialize
operators for arrays, lists, spans, and other common sources. Performance also depends on source type, operator
chain, delegate capture, result materialization, data size, tiered compilation, and PGO.

After the Runtime migration is complete:

1. Add a BenchmarkDotNet suite covering every replaced LINQ usage category, using both the pre-change LINQ
   implementation and the explicit-loop replacement.
2. Run the full suite on .NET 10 Release builds after warm-up, with TieredPGO and the intended host GC settings.
3. Test representative source types and sizes (empty, 1, small, medium, large), including arrays, `List<T>`,
   `IEnumerable<T>`, iterator sources, and materializing operators where applicable.
4. Record throughput, allocated bytes/op, Gen0/Gen1 collections, and code-size/disassembly evidence for disputed
   cases. Run enough repetitions to report confidence intervals and detect regressions rather than comparing a
   single run.
5. Keep or restore a LINQ form when it has no material allocation regression and its throughput is statistically
   equivalent to or better than the loop baseline. Hot paths may use a stricter, scenario-specific threshold.
6. Convert PR0001 from a blanket ban into allowlisted diagnostics (by operator/source shape or explicit
   suppression with benchmark evidence). Do not enable solution-wide PR0001 Error until this gate is complete.

Benchmark artifacts (source, environment metadata, raw results, and summary) must be committed so future .NET
runtime upgrades can rerun the same comparison.

#### Benchmark results (2026-07-23)

Full before/after suite executed (.NET 10.0.10, TieredPGO, Server GC, 300 benchmarks). See
**`docs/benchmarks/2026-07-23-linq-migration/README.md`** for the methodology, decisions, raw
results, allocation probe, and disassembly.

- **Allow:** non-hot `ToList()` on statically typed arrays/`List<T>`; static-lambda `Any` and
  `FirstOrDefault` on arrays/`List<T>`; static-lambda `All` and predicate `Count` on `List<T>`.
- **Keep flagging:** capturing predicates, iterator/`IEnumerable<T>` sources, query expressions,
  `Where`, `Cast`, `Concat`, and multi-operator materialization.
- **Runtime restoration:** `Scene.Count` qualifies (`List<GameObject>.Count(static ...)`) and is
  retained as LINQ; other migrated sites keep loops because they do not match the measured shape.

PR0001 remains **Warning** and now evaluates operator/source shape instead of treating every LINQ
usage as inherently slow.

### Acceptance (B)

- Analyzer tests green; hot-path violations fail build.
- No new LINQ on annotated hot paths; measurable alloc reduction on update/render loops.
- Full pre/post LINQ benchmark report is reproducible; retained LINQ exceptions are backed by measurements.

---

## Stage C — RHI

- Abstract GfxDevice-like boundary over existing CommandBuffer + executor. **Done (C0).**
- Vulkan and D3D12 backends; keep OpenGL as reference/fallback while porting. **In progress (C1).**
- Shared HLSL source compiled by DXC to SPIR-V/DXIL for modern backends; GLSL retained for OpenGL. **Done for critical defaults.**
- Do not start C until Stage A idle cost is acceptable and command recording is backend-agnostic. **Gate met.**

### Status (2026-07-23)

| Slice | Status |
|-------|--------|
| `IGraphicsDevice` / factory / Null / capabilities / `GpuHandle` | Done — `Prowl.Runtime/Graphics/RHI/` |
| OpenGL behind `OpenGLGraphicsDevice` (FIFO render thread preserved) | Done — `Graphics/Backends/OpenGL/` |
| Vulkan device + command translator (clear/viewport/resources; draws incomplete) | Done skeleton — `Graphics/Backends/Vulkan/` |
| D3D12 device + command translator (clear/viewport/resources; draws incomplete) | Done skeleton — `Graphics/Backends/D3D12/` |
| HLSLPROGRAM parser + DXC compiler + dual-source critical shaders | Done — Blit/Unlit/Invalid/Standard/Grid/UI/skyboxes/Tonemapper |
| Backend-neutral `ShaderVariant` recording in render/UI command paths | Done — C1a; SPIR-V/DXIL reaches modern translators |
| Modern vertex-input + raster state recording | Done — C1b; VAO records ready for PSO creation |
| Exact cross-backend graphics PSO cache key | Done — C1c; collision-safe full-state identity |
| Cross-stage HLSL descriptor reflection | Done — C1d; explicit `b/t/s` slots merged and validated |
| Collision-free physical descriptor binding plan | Done — C1e; Vulkan DXC shifts match runtime layout |
| Vulkan descriptor-set + pipeline-layout cache | Done — C1f; native layouts cached per shader variant |
| D3D12 root-signature cache | Done — C1g; native signatures cached per shader variant |
| Vulkan SPIR-V shader-module cache | Done — C1h; native stage modules cached per variant |
| D3D12 shader-generated fullscreen PSO cache | Done — C1i; exact-key native PSOs cached and GPU-smoked |
| D3D12 non-instanced vertex-input PSO cache | Done — C1j; engine formats/semantics map to native input layouts |
| D3D12 instanced vertex-input PSO cache | Done — C1k; dual input slots and native instance step rates |
| Vulkan shader-generated fullscreen PSO cache | Done — C1l; exact state + attachment-format identity, GPU-smoked |
| Vulkan non-instanced vertex-input PSO cache | Done — C1m; retained formats map to native bindings/attributes |
| Vulkan instanced vertex-input PSO cache | Done — C1n; dual bindings with validated core divisor 1 |
| D3D12 non-indexed draw execution | Done — C1o; PSO/root/vertex/topology binding and native DrawInstanced |
| D3D12 indexed draw execution | Done — C1p; 16/32-bit index views and native DrawIndexedInstanced |
| D3D12 instanced indexed draw execution | Done — C1q; dual vertex slots and recorded instance count |
| Vulkan non-indexed draw execution | Done — C1r; render pass/pipeline/vertex binding and native `vkCmdDraw` |
| Vulkan indexed draw execution | Done — C1s; 16/32-bit index binding and native `vkCmdDrawIndexed` |
| Vulkan instanced indexed draw execution | Done — C1t; dual vertex bindings and recorded instance count |
| Vulkan descriptor-set allocation | Done — C1u; device pool allocates distinct freeable sets per cached shader layout |
| Vulkan single-UBO descriptor update/binding | Done — C1v; reflected name update, draw binding, and fence retirement |
| Vulkan multi-UBO descriptor update/binding | Done — C1w; reflected buffer table and batched descriptor writes |
| Vulkan 2D texture/sampler descriptor binding | Done — C1x; sample-ready layout, native sampler, and reflected `t`/`s` writes |
| D3D12 constant-buffer root binding | Done — C1y; reflected names bind retained uniform buffers through root CBVs |
| D3D12 shader-visible descriptor slot allocation | Done — C1z; stable monotonic SRV/sampler CPU/GPU handles with capacity validation |
| D3D12 2D texture SRV/sampler creation | Done — C1aa; native descriptors use stable slots and survive storage reallocation |
| D3D12 texture descriptor-table binding | Done — C1ab; cached heaps and reflected SRV/sampler root tables bind before draws |
| D3D12 initial 2D texture upload | Done — C1ac; aligned staging copy, shader-resource transition, and byte-exact GPU readback |
| Vulkan initial 2D texture upload | Done — C1ad; fenced staging copy, shader-read transition, and byte-exact GPU readback |
| D3D12 texture sampler-state updates | Done — C1ae; copy-on-write sampler slots preserve in-flight descriptor immutability |
| Vulkan texture sampler-state updates | Done — C1af; replacement samplers and descriptor sets retire behind submission fences |
| Vulkan multiple texture/sampler descriptor sets | Done — C1ag; sparse matching `tN`/`sN` pairs update and draw together |
| Vulkan 3D texture allocation/upload/binding | Done — C1ah; native volume image/view, fenced upload, and sampled draw |
| Vulkan base-level cubemap allocation/upload/binding | Done — C1ai; six-layer cube image, per-face transitions, and sampled draw |
| D3D12 3D texture allocation/upload/binding | Done — C1aj; native volume SRV, aligned slice upload, and sampled draw |
| D3D12 base-level cubemap allocation/upload/binding | Done — C1ak; six-array resource, TextureCube SRV, and sampled draw |
| Vulkan single-color custom framebuffer | Done — C1al; native render pass/framebuffer, attachment-format PSO, and shader-read final layout |
| D3D12 single-color custom framebuffer | Done — C1am; native RTV allocation, attachment-format PSO, and shader-resource state restoration |
| Vulkan cubemap mip-level allocation/upload/readback/binding | Done — C1an; full mip-chain storage, per-face/mip transitions, exact readback, and LOD sampling |
| D3D12 cubemap mip-level allocation/upload/readback/binding | Done — C1ao; full mip-chain array storage, exact subresource copies/readback, and LOD sampling |
| Vulkan cubemap mip generation | Done — C1ap; per-face GPU blits, exact generated-mip readback, and expanded sampler LOD |
| D3D12 cubemap mip generation | Done — C1aq; cached GPU downsample PSO, per-face mip RTVs, exact readback, and LOD sampling |
| Vulkan cubemap face/mip custom framebuffer | Done — C1ar; owned subresource image view, exact extent validation, draw, and readback |
| D3D12 cubemap face/mip custom framebuffer | Done — C1as; exact Texture2DArray RTV subresource, state transitions, draw, and readback |
| Vulkan MRT custom framebuffer | Done — C1at; 1–8 attachment render passes, full format-layout PSO keys, dual-target draw, and readback |
| D3D12 MRT custom framebuffer | Done — C1au; 1–8 RTV arrays, full format-layout PSO keys, per-target barriers, draw, and readback |
| Vulkan depth custom framebuffer | Done — C1av; native depth attachment layout, clear, PSO compare/write state, occlusion draw, and readback |
| D3D12 depth custom framebuffer | Done — C1aw; dedicated DSV heap, clear, PSO compare/write state, occlusion draw, and readback |
| Vulkan stencil custom framebuffer | Done — C1ax; D24S8 clear, compare masks/reference, stencil operations, conditional draw, and readback |
| D3D12 stencil custom framebuffer | Done — C1ay; D24S8 clear, compare masks/reference, stencil operations, conditional draw, and readback |
| Vulkan blend-state parity | Done — C1az; PSO factor/equation mapping across color attachments and alpha-composite readback |
| D3D12 blend-state parity | Done — C1ba; PSO factor/equation mapping across render targets and alpha-composite readback |
| Vulkan color framebuffer blit | Done — C1bb; independent read/draw targets, scaled nearest/linear transfer, subresource barriers, and readback |
| D3D12 color framebuffer blit | Done — C1bc; independent read/draw targets, cached fullscreen PSO, point/linear sampling, and readback |
| Vulkan depth framebuffer blit | Done — C1bd; nearest depth transfer, depth-layout barriers, and copied-depth occlusion validation |
| D3D12 depth framebuffer blit | Done — C1be; full-subresource copy-state transitions and copied-depth occlusion validation |
| Vulkan/D3D12 MRT prepass execution contract | Done — C1bf; shared Standard prepass material layout, mixed Color4b/Short4/depth targets, depth reuse, and occlusion validation |
| Modern shader constant-buffer ABI validation | Done — C1bg; duplicate-slot rejection plus shared Standard/Unlit material layouts and DXIL/SPIR-V compile gates |
| Vulkan/D3D12 automatic global constant binding | Done — C1bh; each command translation inherits the current GlobalUniforms buffer with GPU color-readback validation |
| Vulkan/D3D12 automatic object constant packing | Done — C1bi; per-draw ObjectUniforms snapshots use aligned submission arenas with fence retirement and two-draw GPU readback validation |
| Vulkan/D3D12 Unlit material constant packing | Done — C1bj; SetMaterialProperties merges live shader defaults with material overrides into per-draw UnlitMaterial b2 snapshots |
| Vulkan/D3D12 Unlit material texture binding | Done — C1bk; SetMaterialProperties resolves shader-default or material-override _MainTex resources into per-draw texture/sampler descriptors |
| Vulkan/D3D12 Standard material constant packing | Done — C1bl; forward, prepass, and shadow b2 layouts merge live shader defaults with material overrides and pass GPU readback validation |
| Vulkan/D3D12 Standard material texture binding | Done — C1bm; forward _MainTex, _NormalTex, _SurfaceTex, and _EmissionTex bindings resolve defaults/overrides without stale per-draw descriptors |
| Vulkan/D3D12 Gradient skybox constant packing | Done — C1bn; GradientPS b2 merges live top/bottom color and exponent defaults with per-scene material overrides |
| Vulkan/D3D12 Procedural skybox constant packing | Done — C1bo; 32-byte SkyVS b2 preserves HLSL register padding and supplies resolution, fog density, and sun direction snapshots |
| Vulkan/D3D12 Tonemapper material binding | Done — C1bp; TonemapperPS b2 contrast/saturation snapshots combine with per-blit _MainTex descriptors in GPU validation |
| Vulkan/D3D12 Grid material and global depth binding | Done — C1bq; GridPS b2 plus ordered Set/ClearGlobalTexture commands drive scene-view depth-aware grid draws |
| Vulkan/D3D12 UI vertex projection binding | Done — C1br; direct projection matrix commands snapshot the UIVS b0 block independently for each Paper UI draw |
| Vulkan/D3D12 UI fragment state binding | Done — C1bs; full 320-byte UIPS b1 direct-uniform snapshots and three Paper UI texture slots pass GPU validation |
| Vulkan/D3D12 UI backdrop blur material binding | Done — C1bt; BlurDownPS/BlurUpPS b0 offset snapshots combine with per-blit _MainTex descriptors |
| Vulkan/D3D12 UI backdrop capture execution | Done — C1bu; null read-framebuffer color blits capture the current default target into temporary UI blur textures |
| Vulkan/D3D12 UI backdrop blur end-to-end contract | Done — C1bv; one command buffer validates capture, BlurDown, BlurUp, material snapshots, and backdrop sampling |
| Vulkan/D3D12 FXAA image-effect parity | Done — C1bw; full FXAA 3.11 HLSL source, 32-byte FXAAPS constants, and per-blit _MainTex descriptors pass GPU validation |
| Vulkan/D3D12 Bloom image-effect parity | Done — C1bx; four HLSL passes, threshold/composite constants, and ordered _MainTex/_BloomTex snapshots pass GPU validation |
| Vulkan/D3D12 MotionBlur image-effect parity | Done — C1by; HLSL motion/depth sampling, 32-byte MotionBlurPS constants, and ordered material/global textures pass GPU validation |
| Vulkan/D3D12 AutoExposure image-effect parity | Done — C1bz; four HLSL exposure passes, Adapt/Apply constant snapshots, and ordered _MainTex/_AdaptedTex descriptors pass GPU validation |
| Vulkan/D3D12 TAA image-effect parity | Done — C1ca; HLSL temporal resolve, 32-byte TAAResolvePS constants, and ordered history/motion/depth texture snapshots pass GPU validation |
| Vulkan/D3D12 GTAO Calculate pass parity | Done — C1cb; HLSL horizon-based AO calculation, 32-byte GTAOCalculatePS constants, and ordered depth/normal/noise snapshots pass GPU validation |
| Vulkan/D3D12 GTAO Blur pass parity | Done — C1cc; HLSL depth-aware bilateral blur, 16-byte GTAOBlurPS constants, and ordered main/depth snapshots pass GPU validation |
| Vulkan/D3D12 GTAO Composite pass parity | Done — C1cd; HLSL AO modulation, explicit `_MainTex`/`_AOTex` descriptors, and default/override GPU readback pass validation |
| Vulkan/D3D12 StandardTransparent pass parity | Done — C1ce; HLSL StandardMaterial b2 ABI, ordered material descriptors, alpha blending, culling, and GPU readback validation |
| Vulkan/D3D12 GTAO Temporal pass parity | Done — C1cf; HLSL motion reprojection, neighborhood-clamped history, 16-byte constants, and ordered three-texture GPU validation |
| Vulkan/D3D12 CubemapSkybox pass parity | Done — C1cg; HLSL six-face selection, 32-byte CubemapSkyboxPS constants, and ordered six-texture GPU validation |
| Vulkan/D3D12 Gizmos pass parity | Done — C1ch; HLSL vertex-color rendering and global depth-driven occlusion dimming pass GPU validation |
| Host selection (`--graphics=` / `PROWL_GRAPHICS_BACKEND`) + editor footer | Done |
| DefaultRenderPipeline full parity on Vulkan/D3D12 | **Not yet** — modern shader-property binding, shadows, image effects, and UI parity remain |

Host notes:

- Windowed **Auto** resolves to **OpenGL** so the Silk GL context matches the device.
- Explicit `--graphics=vulkan` or `--graphics=d3d12` selects a modern backend (Factory Auto still tries D3D12→Vulkan→OpenGL when Backend=Auto is forced on the factory).
- Headless uses Null / no device; CPU tests do not require a GPU.

Evidence / tests: `Prowl.Runtime.Test` filters `Rhi`, `ShaderCompiler`, `HeadlessGraphics`.

### Acceptance (C)

- Same high-level render pipeline runs on at least one modern backend with feature parity for Default pipeline core passes. **Pending** (devices compile and clear/present; pipeline draws not yet feature-complete).
- Common command/resource code has no new Silk OpenGL types outside `Backends/OpenGL`. **Mostly done** (transitional `Graphics.GL` remains for legacy wrappers).
- OpenGL remains a functional fallback. **Done.**

See also: `docs/benchmarks/` (Stage B), `skills/rendering-gpu-performance/SKILL.md`.

---

## Stage D — Optional dual ECS

- Data-oriented world for mass simulation **alongside** GO/MB, not a forced rewrite.
- Out of scope until A–C prove out.

---

## Analyzers summary

| ID | Rule | Default (stage 1) |
|----|------|-------------------|
| PR0001 | Review System.Linq; benchmark-backed operator/source allowlist | Warning (Error on validated hot dirs later) |
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
Month 2    Stage B throughput / Runtime LINQ migration + full before/after benchmark gate
Month 3+   Stage C RHI (C0 device seam Done; C1 Vulkan/D3D12 parity in progress)
```

---

## Risks

| Risk | Mitigation |
|------|------------|
| PR0001 rejects efficient LINQ or causes unnecessary rewrites | Benchmark gate + measured allowlist + staged severity |
| Scope creep via “sync Unity” | Lock to matrix; P2/P3 stay backlog |
| RHI too early | Gate on Stage A + clean CB boundary |

---

## Related

- Plan: `.zcode/plans/plan-sess_*` (high-performance + analyzers + matrix)
- Book: `Books/17-unity47-capability-matrix.md`
