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
  pipeline states keyed by the exact shared `GraphicsPipelineKey`; unsupported blend state
  is rejected explicitly until the next parity slice lands.
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
- D3D12 constant-buffer root binding: explicit `CommandBuffer.SetBuffer` now resolves
  reflected `b` register names and binds each retained uniform buffer through the matching
  root CBV parameter before non-indexed, indexed, and instanced draw submission.
- D3D12 shader-visible descriptor allocation foundation: device-owned CBV/SRV/UAV and
  sampler heaps now expose stable monotonic CPU/GPU slots with serialized allocation and
  explicit capacity failures, ready for native SRV/sampler creation and table binding.
- D3D12 2D texture descriptors: non-depth texture allocation now creates a native SRV
  and default sampler in stable shader-visible slots; reallocating texture storage rewrites
  those descriptors without consuming additional heap capacity.
- D3D12 texture descriptor-table binding: command-local named textures now bind the
  cached shader-visible SRV/sampler heaps and reflected root descriptor tables before all
  draw variants, with matching `tN`/`sN` slots sharing the texture-owned sampler.
- D3D12 initial 2D texture upload: tightly packed non-depth pixels now stage through an
  aligned upload buffer, copy into default-heap texture storage, and transition to
  pixel-shader resource state; GPU readback verifies exact row-padded byte preservation.
- Vulkan initial 2D texture upload: tightly packed non-depth pixels now stage through a
  host-visible transfer buffer, copy into optimal image storage, and transition to
  shader-read layout; an independent fenced transfer submission enables byte-exact readback.
- D3D12 sampler-state updates: texture wrap and filter commands now update retained state
  and write a fresh shader-visible sampler descriptor, preserving descriptor immutability
  for in-flight draws before subsequent textured submissions bind the new slot.
- Vulkan sampler-state updates: texture wrap and filter commands now replace the native
  sampler, dirty subsequent descriptor writes, and retire superseded samplers only after
  the owning submission fence completes; color images also declare transfer-source usage
  required by the byte-exact readback path.
- Vulkan multiple texture/sampler sets: reflected sparse texture and sampler slots are
  updated together in one descriptor set, with texture-owned sampler state preserved for
  each matching `tN`/`sN` pair and exercised by a native two-texture draw.
- Vulkan 3D textures: `AllocateTexture3D` now creates a native 3D image/view, uploads the
  full volume through the fenced staging path, transitions it to shader-read layout, and
  binds it through the existing reflected texture/sampler descriptor path.
- Vulkan cubemaps: six base-level face uploads now populate a cube-compatible six-layer
  image with per-face layout transitions; sampling becomes available only after every face
  is shader-read ready through the native cube view and reflected descriptor path.
- D3D12 3D textures: `AllocateTexture3D` now creates native volume storage and a Texture3D
  SRV, uploads aligned rows across every depth slice, transitions to pixel-shader resource
  state, and binds the volume through reflected texture/sampler descriptor tables.
- D3D12 cubemaps: six base-level faces now upload into a six-element texture array with a
  native TextureCube SRV; each face uses its array subresource and sampling is rejected
  until the complete cube is available to the reflected descriptor-table path.
- Vulkan custom framebuffer MVP: one mip-0 2D color attachment now creates a native render
  pass/framebuffer pair, selects the attachment format for compatible PSO creation, draws
  at the framebuffer extent, and returns the texture to shader-read layout after the pass.
- D3D12 custom framebuffer MVP: one mip-0 2D color attachment now receives a stable native
  RTV, contributes its DXGI format to exact PSO cache identity, supports clear/draw target
  binding, and returns the texture to pixel-shader resource state after command translation.
- Vulkan cubemap mip levels: the base face size now allocates the complete native mip chain,
  each face/mip uploads and transitions independently, contiguous complete levels expand the
  sampler LOD range, and exact face/mip readback validates native subresource addressing.
- D3D12 cubemap mip levels: the base face size now allocates all mip subresources across the
  six-element array, uploads use exact `mip + face * mipCount` addressing, the TextureCube SRV
  exposes the chain, and row-padded readback verifies a selected face/mip before LOD sampling.
- Vulkan cubemap mip generation: `GenerateMipmap` now performs per-face native image blits
  through transfer-source/destination layouts for every level, restores shader-read layouts,
  marks the generated chain available, and expands the replacement sampler's maximum LOD.
- D3D12 cubemap mip generation: a cached fixed root signature/PSO downsamples each face on the
  GPU through source-mip Texture2DArray SRVs and target-mip RTVs, transitions only the written
  subresource, and exposes the completed chain through the existing TextureCube descriptor.
- Vulkan framebuffer subresources: a single-color custom framebuffer can now own a dedicated
  2D image view for a selected cubemap face and mip, validates the exact mip extent, renders
  through the existing attachment-format PSO path, and destroys the owned view with the FBO.
- D3D12 framebuffer subresources: a selected cubemap face and mip now receive an exact
  Texture2DArray RTV, validate the mip extent, transition only the target subresource for
  rendering, and return it to pixel-shader resource state before exact readback.
- Vulkan MRT framebuffers: custom render passes now support one through eight color
  attachments, pipeline caches include the complete ordered format layout, blend state
  emits one write mask per target, and dual `SV_Target` output is byte-exact on readback.
- D3D12 MRT framebuffers: custom targets now own one through eight RTV descriptors, PSO
  cache identity includes the complete ordered DXGI format layout, draw/clear binds every
  target, and resource barriers restore each attachment before byte-exact readback.
- Vulkan depth framebuffers: a color target can now pair with one native depth attachment,
  custom clears reset depth in-pass, PSO identity includes the depth format, and configured
  compare/write state rejects a farther draw after a nearer triangle updates the buffer.
- D3D12 depth framebuffers: a dedicated DSV descriptor heap backs color-plus-depth custom
  targets, clears reset the native depth resource, PSO identity includes the DSV format,
  and configured compare/write state rejects a farther draw after a nearer triangle.
- Vulkan stencil framebuffers: D24S8 targets now clear stencil in-pass, PSOs map compare
  functions, read/write masks, references, and pass/fail operations, and conditional draws
  prove matching stencil values pass while non-matching references are rejected.
- D3D12 stencil framebuffers: D24S8 targets clear depth/stencil through the DSV, PSOs map
  compare functions, masks, and operations while draw-time OM stencil references remain
  dynamic, and conditional draws prove matching values pass while mismatches are rejected.
- Vulkan blend state: graphics PSOs map the engine source/destination factors and equations
  onto every color attachment, retain blend state in exact pipeline cache identity, and an
  alpha-composite draw produces byte-exact color/alpha readback.
- D3D12 blend state: graphics PSOs map engine factors and equations through the render-target
  blend descriptor, retain blend state in exact pipeline cache identity, and an alpha-composite
  draw validates RGBA readback with only the permitted one-LSB UNORM rounding difference.
- Vulkan color framebuffer blits: independent custom read/draw framebuffers now resolve their
  exact color subresources, transition through transfer layouts, support scaled nearest/linear
  `vkCmdBlitImage`, and restore shader-read layouts after byte-validated GPU transfer.
- D3D12 color framebuffer blits: independent custom read/draw framebuffers now feed a cached
  fullscreen-triangle PSO with exact source UV rectangles and point/linear samplers, while the
  destination transitions through render-target state and returns to shader-readable state.
- Vulkan depth framebuffer blits: matching depth attachments now transition through transfer
  source/destination layouts for nearest `vkCmdBlitImage`, restore depth-attachment layouts,
  and copied near depth rejects a later farther draw in the destination framebuffer.
- D3D12 depth framebuffer blits: matching equal-size depth attachments transition from
  depth-write into copy source/destination states for full-subresource copies, return to
  depth-write state, and copied near depth rejects a later farther destination draw.
- Vulkan/D3D12 MRT prepass execution: Standard's HLSL prepass now presents one shared,
  stage-consistent material constant-buffer layout, while GPU contracts validate the real
  Color4b normals + Short4 motion/material + depth target mix, depth copy into scene color,
  and rejection of a later farther opaque draw on both modern backends.
- Modern shader constant-buffer ABI: DXC source reflection now rejects two resource names
  assigned to one logical slot, critical default shader layouts are collision-checked, and
  Standard forward/shadow plus Unlit forward passes use stage-consistent material blocks
  that compile to DXIL and, when the installed DXC supports it, SPIR-V.
- Vulkan/D3D12 global constants: command translation now seeds the `GlobalUniforms` binding
  from the pipeline-owned dynamic buffer before decoding pass commands, matching OpenGL's
  automatic per-draw behavior; shaders that declare only `b0` render the uploaded tint on
  both modern backends without an explicit `SetBuffer` command.
- Vulkan/D3D12 object constants: command translation now merges `_ObjectID` from
  `SetInstanceProperties` with the later object/world/previous matrix commands emitted by
  `DefaultRenderPipeline`, snapshots the `ObjectUniforms : b1` block per draw into aligned
  64 KiB submission arenas, and retires those arenas only after the owning fence completes.
  Two-draw GPU contracts confirm that both modern backends preserve distinct matrix and ID
  values for adjacent pixels rather than exposing the final command-buffer state to every draw.
- Vulkan/D3D12 Unlit material constants: `SetMaterialProperties` now captures both the
  material override snapshot and its shader, fills `_Tiling`, `_Offset`, and `_MainColor`
  from live shader defaults when not overridden, and writes a per-draw `UnlitMaterial : b2`
  slice into the same aligned submission arena used by object constants. Two-draw GPU
  contracts validate default values on the first draw and independent material overrides on
  the second draw for both modern backends.
- Vulkan/D3D12 Unlit material textures: the same material command now resolves `_MainTex`
  from the material override snapshot first and the live shader default second, then feeds the
  existing backend texture/sampler descriptor path. Two-draw GPU contracts sample distinct
  shader-default and material-override 1x1 textures on both modern backends.
- Vulkan/D3D12 Standard material constants: one shared allocation-free packer now merges live
  shader defaults with material overrides for the 64-byte forward `StandardMaterial : b2`
  layout and the 48-byte prepass/shadow cutout layouts. Eight-pixel GPU contracts exercise all
  forward scalar fields plus independent default and override snapshots for `StandardMaterial`,
  `PrepassMaterial`, and `ShadowMaterial` on both modern backends.
- Vulkan/D3D12 Standard material textures: `SetMaterialProperties` now resolves `_MainTex`,
  `_NormalTex`, `_SurfaceTex`, and `_EmissionTex` in one allocation-free shader-default scan,
  applies material overrides first, and clears all four managed slots on material changes so
  descriptors cannot leak from a previous draw. GPU contracts sample all four default and
  override textures into RGBA readback on both modern backends.
- Vulkan/D3D12 Gradient skybox constants: `GradientPS : b2` now receives a 48-byte per-draw
  snapshot containing `_TopColor`, `_BottomColor`, and `_Exponent`, merging live shader defaults
  with the scene-driven material overrides used by `DefaultRenderPipeline.RenderSkybox`. GPU
  contracts validate independent default and override pixels on both modern backends.
- Vulkan/D3D12 Procedural skybox constants: `SkyVS : b2` now receives an explicitly padded
  32-byte snapshot for `Resolution`, `fogDensity`, and `_SunDir`, matching HLSL's rule that the
  trailing `float3` begins in a new 16-byte register. Vertex-stage GPU contracts validate live
  defaults and the sun-direction override emitted by `DefaultRenderPipeline` on both backends.
- Vulkan/D3D12 Tonemapper material binding: `TonemapperPS : b2` now receives a 16-byte
  per-blit snapshot for `Contrast` and `Saturation`, while the existing managed `_MainTex`
  descriptor path supplies the current scene-color source. Combined GPU contracts validate
  independent constant and texture defaults/overrides on both modern backends.
- Vulkan/D3D12 Grid material and global depth binding: `GridPS : b2` now receives the full
  48-byte color/scale/line/falloff/distance snapshot, and modern command translators execute
  ordered `SetGlobalTexture` / `ClearGlobalTexture` operations instead of skipping them. Four-pixel
  GPU contracts validate every Grid scalar plus mid-command-buffer `_CameraDepthTexture` changes
  on both backends.
- Vulkan/D3D12 UI vertex projection binding: direct `SetMatrix("projection", ...)` commands now
  update a dedicated 64-byte `UIVS : b0` snapshot rather than falling through the unsupported
  uniform path. Two-draw vertex-stage GPU contracts validate that adjacent Paper UI draws retain
  independent projection matrices on both modern backends.
- Vulkan/D3D12 UI fragment state binding: direct float, int, float2, float4, and matrix commands
  now update an explicitly padded 320-byte `UIPS : b1` snapshot covering scissor, brush, DPI,
  viewport, and backdrop state. Ten-pixel GPU contracts validate both complete state sets across
  HLSL register boundaries together with `texture0`, `fontTexture`, and `backdropTexture` sampling
  on both modern backends.
- Vulkan/D3D12 UI backdrop blur material binding: `BlurDownPS : b0` and `BlurUpPS : b0`
  now receive explicitly padded 16-byte `_Offset` snapshots from live shader defaults or
  per-blit material overrides, combined with the existing per-draw `_MainTex` descriptor path.
  Four-pixel GPU contracts validate both blur passes and both material states on each backend.
- Vulkan/D3D12 UI backdrop capture execution: color `BlitFramebuffer` commands now accept a null
  read framebuffer and capture the current swapchain or headless default target into a custom
  framebuffer. D3D12 exposes reusable default-target SRVs and transitions them around its existing
  filtered blit pipeline; Vulkan enables transfer-source usage and performs explicit image-layout
  transitions. Dual-backend GPU readback validates the command sequence used by `PaperRenderer`.
- Vulkan/D3D12 UI backdrop blur end-to-end contract: one ordered command buffer now captures the
  default target, executes `BlurDownPS` and `BlurUpPS` with independent `_MainTex` material
  snapshots, and samples the resulting `backdropTexture` in the final UI stage. Channel-encoded
  GPU readback on both backends proves the complete Paper backdrop chain preserves state ordering.
- Vulkan/D3D12 FXAA image-effect parity: the default FXAA shader now carries an HLSL implementation
  of the existing FXAA 3.11 path alongside GLSL. `FXAAPS : b0` receives an explicitly padded
  32-byte resolution/threshold/subpixel snapshot, while `_MainTex` continues through the ordered
  material descriptor path. DXIL/SPIR-V compilation and four-pixel GPU contracts validate defaults,
  overrides, register boundaries, and texture snapshots on both modern backends.
- Vulkan/D3D12 Bloom image-effect parity: all four Bloom passes now retain GLSL while adding HLSL
  implementations for threshold, downsample, upsample, and composite execution. Explicitly padded
  16-byte `BloomThresholdPS : b0` and `BloomCompositePS : b0` snapshots combine with ordered
  `_MainTex` and `_BloomTex` descriptors. DXIL/SPIR-V compilation and six-pixel GPU contracts validate
  defaults, overrides, constant-register boundaries, texture slots, and per-draw snapshot ordering.
- Vulkan/D3D12 MotionBlur image-effect parity: the motion-blur pass now adds HLSL for the existing
  jittered, depth-aware sampling path. An explicitly padded 32-byte `MotionBlurPS : b2` snapshot
  carries resolution, intensity, sample count, and radius; `_MainTex` and `_MotionVectorsTex` use
  ordered material descriptors while depth is set and cleared explicitly in the effect command
  buffer. DXIL/SPIR-V compilation and four-pixel GPU contracts validate constants and texture order.
- Vulkan/D3D12 AutoExposure image-effect parity: all four exposure passes now retain GLSL while
  adding self-contained HLSL implementations for luminance extraction, downsampling, temporal
  adaptation, and exposure application. `AutoExposureAdaptPS : b2` and `AutoExposureApplyPS : b0`
  each receive explicitly padded 16-byte snapshots, while `_MainTex` and `_AdaptedTex` descriptors
  preserve per-draw texture ordering. DXIL/SPIR-V compilation and ten-pixel GPU contracts validate
  pass selection, defaults, overrides, constant layouts, and texture snapshots on both backends.
- Vulkan/D3D12 TAA image-effect parity: the temporal resolve pass now adds an HLSL equivalent of
  the Catmull-Rom history sample, motion-vector reprojection, YCoCg variance clipping, adaptive
  blending, and optional sharpening. A 32-byte `TAAResolvePS : b2` snapshot combines with ordered
  `_MainTex`, `_HistoryTex`, and `_MotionVectorsTex` material descriptors; depth is explicitly set
  and cleared in the effect command buffer. DXIL/SPIR-V compilation and four-pixel GPU contracts
  validate constants, material textures, and global depth state on both modern backends.
- Vulkan/D3D12 GTAO Calculate pass parity: the first GTAO stage now adds an HLSL port of the
  horizon-based occlusion calculation, including view-space reconstruction, horizon sampling,
  slice/sample quality controls, and blue-noise jitter. A 32-byte `GTAOCalculatePS : b2` snapshot
  combines with command-local depth/normal globals and the material `_Noise` descriptor. DXIL/SPIR-V
  compilation and four-pixel GPU contracts validate constants, resource slots, and snapshot order.
- Vulkan/D3D12 GTAO Blur pass parity: the separable spatial stage now adds an HLSL depth-aware
  bilateral blur matching the GLSL five-tap kernel. A padded 16-byte `GTAOBlurPS : b2` snapshot
  carries direction and radius while `_MainTex` combines with command-local depth. DXIL/SPIR-V
  compilation and four-pixel GPU contracts validate constants, descriptors, and snapshot order.
- Vulkan/D3D12 GTAO Composite pass parity: the final AO modulation stage now adds an HLSL equivalent
  of the existing scene-color multiplication. `_MainTex` and `_AOTex` are resolved from shader
  defaults or material overrides with ordered descriptors. DXIL/SPIR-V compilation and four-pixel
  GPU contracts validate default and override texture snapshots on both modern backends.
- Vulkan/D3D12 GTAO Temporal pass parity: the optional accumulation stage now adds HLSL motion
  reprojection, 3x3 current-frame neighborhood clamping, disocclusion rejection, and response-based
  history blending. A padded 16-byte `GTAOTemporalPS : b2` snapshot combines with ordered current AO,
  previous AO, and motion-vector descriptors; four-pixel GPU contracts validate both material states.
- Vulkan/D3D12 StandardTransparent pass parity: the default transparent material now supplies the
  StandardMaterial `b2` ABI and ordered `_MainTex`, `_NormalTex`, `_SurfaceTex`, and `_EmissionTex`
  descriptors to the modern HLSL path. DXIL/SPIR-V compilation plus alpha-blended GPU readback
  validate material-alpha packing, blend state, culling, and global/object constant bindings.
- Vulkan/D3D12 CubemapSkybox pass parity: the material skybox path now has HLSL face selection matching
  the GLSL orientation rules. A padded 32-byte `CubemapSkyboxPS : b2` snapshot combines tint and
  exposure with ordered right, left, top, bottom, front, and back texture descriptors; six-pixel GPU
  contracts validate defaults and overrides on both modern backends.
- Vulkan/D3D12 Gizmos pass parity: editor gizmo meshes now have an HLSL path carrying vertex colors
  through the global view-projection transform. Ordered `_CameraDepthTexture` snapshots drive the same
  50% RGB and 30% alpha occlusion dimming as GLSL, with visible/occluded GPU readback on both backends.
- Vulkan/D3D12 GizmoIcon pass parity: billboard icons now share a padded 32-byte `GizmoIconMaterial : b2`
  ABI across vertex and fragment stages. HLSL matches camera-facing center/scale placement, multiplies
  `_MainTex` by `_IconColor`, and applies ordered `_CameraDepthTexture` occlusion descriptors; four-pixel
  default/override GPU contracts validate both modern backends.
- Vulkan/D3D12 DefaultUI pass parity: GameCanvas UI now has an HLSL path with a padded 128-byte
  `DefaultUIMaterial : b2` layout covering tiling/offset/tint plus rounded-rect clip uniforms
  (`_ClipToLocal`, `_ClipRect`, `_ClipRadius`, `_ClipSoftness`, `_ClipEnable`). Ordered `_MainTex`
  descriptors combine with live defaults/overrides; six-pixel GPU contracts validate front constants,
  clip register packing, and tinted texture sampling on both modern backends.
- Vulkan/D3D12 DefaultText pass parity: overlay SDF text reuses the same `DefaultUIMaterial : b2`
  packing path while HLSL reconstructs coverage from the atlas distance field and applies RectMask
  clip. Four-pixel GPU contracts validate shared constant packing plus interior/exterior SDF coverage
  on both modern backends.
- Vulkan/D3D12 DefaultTextMesh pass parity: world-space SDF text reuses `UnlitMaterial : b2` packing
  with HLSL distance-field coverage (no RectMask). Four-pixel GPU contracts validate tiling/tint
  defaults/overrides and interior/exterior atlas sampling on both modern backends.
- Vulkan/D3D12 Sprite pass parity: transparent sprites reuse `UnlitMaterial : b2` with HLSL
  gamma-to-linear conversion and fog matching the GLSL path. Four-pixel GPU contracts validate
  constant packing and tinted `_MainTex` sampling on both modern backends.
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

1. Complete Standard/remaining modern material constant layouts and texture sets so the
   DefaultRenderPipeline can drive Standard passes instead of explicit test resources only.
2. Complete shadows, image effects, and UI parity.
3. Add an image comparison harness for DefaultRenderPipeline across backends.
4. Remove transitional public `Graphics.GL` / Silk types from common wrappers.
