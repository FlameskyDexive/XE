---
name: rendering-gpu-performance
description: Guides Prowl rendering and GPU performance work. Use when changing render pipelines, passes, command buffers, render targets, shadows, viewports, batching, RenderStats, graphics backends, or RHI boundaries.
---

# Rendering and GPU Performance

Optimize observable GPU and render-thread work, not only CPU syntax.

## Resource and pass discipline

- Create and clear heavyweight GPU resources only when needed. Keep shadow atlases and
  similar targets lazy and size them through quality settings.
- Skip passes, clears, submissions, and full viewport rendering when no visible work
  exists. Hidden or inactive views should perform no full pipeline run.
- Reuse render-frame collections and temporary objects. Avoid per-camera, per-light,
  per-pass, per-command, and per-draw allocations.
- Batch compatible work while preserving ordering, state transitions, and visibility.
- Cache stable pass/material/shader lookups with explicit invalidation.
- Do not retain frame-owned resources beyond their valid lifetime.

## Architecture

- Keep command recording backend-agnostic. Do not leak OpenGL-only assumptions across the
  CommandBuffer/executor boundary needed by Vulkan/D3D12 backends.
- Separate resource lifetime, command recording, and execution responsibilities.
- Preserve current rendering behavior and fallback paths while introducing skip gates or
  caching.
- Apply `../hot-path-performance/SKILL.md` to render-thread and per-frame CPU code.

## Observability and verification

- Add or update `RenderStats` counters for new optimization gates so performed and skipped
  work are distinguishable.
- Verify empty scenes, no-caster scenes, hidden viewports, Preview/Full quality tiers, and
  active content separately.
- Measure GPU passes/clears/submissions and CPU frame allocations before and after.
- Check resource resize, disposal, device/context loss, scene changes, and editor/game-view
  transitions for stale caches or leaked resources.
