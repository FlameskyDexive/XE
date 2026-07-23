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
- Vulkan native shader-layout cache: each `ShaderVariant` creates one descriptor-set layout
  and pipeline layout from the physical binding plan; layouts are reused and device-owned.
- D3D12 native shader-layout cache: each `ShaderVariant` creates one root signature with
  CBV root descriptors plus SRV/sampler descriptor tables; signatures are device-owned.
- Vulkan native shader-module cache: validated SPIR-V vertex/fragment modules are created
  once per `ShaderVariant`, reused by future PSOs, and destroyed with the device.
- D3D12 fullscreen PSO cache: shader-generated fullscreen draws create and reuse native
  pipeline states keyed by the exact shared `GraphicsPipelineKey`; unsupported vertex,
  depth/stencil, and blend state is rejected explicitly until later parity slices land.
- D3D12 vertex-input PSO cache: retained engine `VertexFormat` records now map standard
  mesh semantics and formats into native input layouts; non-instanced vertex PSOs share
  the same exact cache, while instanced layouts remain an explicit follow-up.
- D3D12 instanced input layouts: base vertices use slot 0 and instance data uses slot 1
  with the engine divisor as the native instance step rate; custom locations 8-13 map to
  `TEXCOORD8`-`TEXCOORD13` for the retained instance matrix/color/custom-data contract.
- Vulkan fullscreen graphics pipeline cache: SPIR-V modules and pipeline layouts now feed
  native shader-generated fullscreen PSOs, keyed by the exact shared pipeline state plus
  color-attachment format for render-pass compatibility.
- Vulkan vertex-input graphics pipeline cache: retained non-instanced `VertexFormat`
  records now map to native binding/attribute descriptions, preserving semantic locations,
  formats, offsets, and strides in the same attachment-compatible pipeline cache.
- Vulkan instanced input layouts: base vertices use binding 0 and instance data binding 1;
  the core Vulkan path validates divisor 1 and maps locations 8-13 for the retained
  instance matrix/color/custom-data stream.
- D3D12 non-indexed draw execution: `CommandBuffer.DrawArrays` now resolves the exact
  cached PSO, binds the root signature and retained vertex buffer, sets topology, and
  submits a native draw; headless devices use a device-owned 1x1 RTV for GPU smoke tests.
- D3D12 indexed draw execution: retained 16/32-bit index buffers now bind through native
  index views and `CommandBuffer.DrawIndexed` submits `DrawIndexedInstanced` with the
  recorded start index and base vertex while preserving index width in the PSO key.
- D3D12 instanced indexed draw execution: the retained base and instance buffers bind as
  vertex slots 0/1 and `CommandBuffer.DrawIndexedInstanced` now submits the recorded
  instance count with the cached dual-stream PSO.
- Vulkan non-indexed draw execution: `CommandBuffer.DrawArrays` now begins the active
  render pass, resolves the attachment-compatible cached pipeline, binds the retained
  vertex buffer, and submits native `vkCmdDraw`; headless devices own a 1x1 color target.
- Vulkan indexed draw execution: retained 16/32-bit index buffers bind with the matching
  native index type and `CommandBuffer.DrawIndexed` submits `vkCmdDrawIndexed` with the
  recorded start index and base vertex while preserving index width in the pipeline key.
- Vulkan instanced indexed draw execution: retained base and instance buffers bind as
  vertex bindings 0/1 and `CommandBuffer.DrawIndexedInstanced` submits the recorded
  instance count through native `vkCmdDrawIndexed` with the cached dual-stream pipeline.
- Vulkan descriptor-set allocation foundation: a device-owned descriptor pool now
  allocates distinct freeable sets from cached shader layouts, establishing safe identity
  for later per-draw resource updates without mutating a shader-global descriptor set.
- Vulkan uniform-buffer descriptor binding: explicit `CommandBuffer.SetBuffer` resolves
  the reflected buffer name, updates an independent descriptor set, binds it at draw time,
  and retires the set with the submission fence; the first slice supports one UBO layout.
- Vulkan multi-UBO descriptor binding: command-local named buffer state now resolves every
  reflected `b` register, batches native descriptor writes into one independent set, and
  reuses that set until shader or buffer state changes within the command buffer.
- Vulkan 2D texture/sampler descriptor binding: allocated sampled images now receive a
  device sampler and transition to shader-read layout; reflected matching `t`/`s` slots
  update sampled-image and sampler descriptors and bind them with the draw submission.
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

1. Complete multiple texture/sampler sets, uploads, and non-2D resource binding on Vulkan.
2. Complete descriptor allocation/update/binding on D3D12.
3. Complete custom framebuffer, depth/stencil, and blend-state parity.
4. Cubemap faces, mip generation, blit, MRT prepass, shadows, image effects, UI.
5. Image comparison harness for DefaultRenderPipeline across backends.
6. Remove transitional public `Graphics.GL` / Silk types from common wrappers.
