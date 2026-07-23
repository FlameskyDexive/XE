# Stage C RHI status (2026-07-23)

## What landed

- Backend-neutral contracts under `Prowl.Runtime/Graphics/RHI/`
  (`IGraphicsDevice`, factory, Null device, descriptors, backend selection).
- OpenGL extracted to `Graphics/Backends/OpenGL/OpenGLGraphicsDevice` with the existing
  FIFO render thread and `CommandExecutor`.
- Vulkan + Direct3D 12 device skeletons with format maps and command translators that
  handle clears, viewport/scissor, and buffer/texture lifecycle. Draw/PSO/descriptor
  binding paths are not yet feature-complete for `DefaultRenderPipeline`.
- Shader pipeline: `HLSLPROGRAM` parsing, `DxcShaderCompiler` (process-based DXC),
  dual-source critical default shaders, `ShaderPass` backend-aware variants.
- Backend-neutral shader command boundary: `CommandBuffer`, default/instanced render paths,
  and Paper UI now record `ShaderVariant`; OpenGL unwraps `GraphicsProgram`, while
  Vulkan/D3D12 retain SPIR-V/DXIL variants for PSO creation.
- Modern vertex-input state: Vulkan/D3D12 translators now retain raster state and create/
  dispose backend vertex-array records with vertex, index, and instance-buffer layouts.
- Exact graphics PSO cache identity: shader variants have stable IDs and the shared
  `GraphicsPipelineKey` covers shader, VAO, topology, index width, and full raster state.
- Cross-stage HLSL binding reflection: vertex + fragment resources are merged by explicit
  `b/t/s` registers, deduplicated, sorted, and checked for conflicting stage declarations.
- Collision-free descriptor binding plan: logical HLSL buffer/texture/sampler namespaces
  map to deterministic physical bindings; Vulkan DXC uses matching `-fvk-*-shift` values.
- Host wiring: CLI/env backend selection, Silk window API per backend, editor footer
  shows active device name.

## How to select a backend

```text
--graphics=opengl|vulkan|d3d12|null|auto
# or
PROWL_GRAPHICS_BACKEND=vulkan
```

Windowed Auto uses OpenGL so the window context matches the device. Pass an explicit
backend to exercise Vulkan/D3D12.

## Verification

```powershell
dotnet build Prowl.Runtime/Prowl.Runtime.csproj -c Debug
dotnet test Prowl.Runtime.Test/Prowl.Runtime.Test.csproj `
  --filter "FullyQualifiedName~Rhi|FullyQualifiedName~ShaderCompiler|FullyQualifiedName~HeadlessGraphics|FullyQualifiedName~GraphicsBackendSelection"
```

## Remaining toward full parity

1. Complete Vulkan/D3D12 PSO creation using the exact shared pipeline-cache key.
2. Create root signature / descriptor set layouts from the stable physical binding plan.
3. Complete name-based resource updates + descriptor binding + draw execution.
4. Cubemap faces, mip generation, blit, MRT prepass, shadows, image effects, UI.
5. Image comparison harness for DefaultRenderPipeline across backends.
6. Remove transitional public `Graphics.GL` / Silk types from common wrappers.
