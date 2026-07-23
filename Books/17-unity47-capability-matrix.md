# 17 · Unity 4.7.1 能力矩阵（非 1:1 移植）

> **目标**: 以 Unity 4.7.1f1 的 **模块职责与编辑器/运行时契约** 为对照，记录 Prowl 现状、缺口与优先级。  
> **明确不做**: 把 Unity C++ 源文件逐行翻译进 Prowl。API 表面继续 Unity 亲和（GO/MB），内部可用 SoA/Jobs/RHI。

**参考树**

- Runtime: `Unity4.7.1f1/Runtime/{Core,BaseClasses,Graphics,GfxDevice,Camera,Animation,Audio,Input,Physics2D,Profiler,Serialize,Shaders,Terrain,NavMesh,IMGUI,...}`
- Editor: `Unity4.7.1f1/Editor/Src/{AssetPipeline,BuildPipeline,Commands,Application,...}`

**状态列**: `有` = 可用；`部分` = 有骨架或子集；`无` = 未实现 / 刻意不跟。

---

## 17.1 Runtime

| Unity 模块 | Prowl 对应 | 状态 | 优先级 | 备注 |
|------------|------------|------|--------|------|
| Core / Application 帧循环 | `Application`, `Game`, `Time` | 部分 | **P0** | 有主循环；缺 QualitySettings / 完整 Player 设置契约 |
| BaseClasses (GO/Component) | `GameObject`, `Component`, `MonoBehaviour` | 有 | — | 继续亲和；热路径用分析器约束 |
| Graphics + Camera | `Camera`, `DefaultRenderPipeline`, `CommandBuffer` | 部分 | **P0** | 完整管线可用；空场景成本高 → 阶段 A 止血 |
| GfxDevice | Silk.NET OpenGL + 渲染线程 | 部分 | **P0** | 阶段 C：RHI（VK/D3D12）对标 GfxDevice |
| Shaders / Materials | `Shader`, `Material`, 自定义 shading 语言 | 有 | P1 | 与 4.7 管线模型不同，能力对齐即可 |
| Serialize | Echo + `ISerializable` | 部分 | P1 | Prefab/Override 行为对照表见 backlog |
| Animation / mecanim | AnimationClip / 蒙皮 | 部分 | P2 | 功能差距，非先搬 C++ |
| Audio | `Audio` 子系统 | 部分 | P2 | 按产品需求扩 |
| Input | `InputManagement` | 部分 | P1 | 设备/动作图能力对照 |
| Physics (3D) | Jitter 集成 | 部分 | P1 | |
| Physics2D | — | 无 | P2 | 有 2D 需求再开 |
| Profiler | `RenderStats` + 零散计时 | 部分 | **P0** | 对齐 Unity Profiler 语义：CPU 区段 + 可选 GPU timer |
| Terrain | `TerrainData` + 编辑器 | 部分 | P2 | |
| NavMesh | — | 无 | P2 | 可参考 XEngine/DotRecast，不强制抄 Unity |
| IMGUI (Runtime) | 非目标 | 无 | P3 | 运行时 UI 走 Paper/Runtime UI，不移植旧 IMGUI |
| ClusterRenderer / Network / Video | — | 无 | P3 | 超出实用对齐范围可忽略 |
| Threads / Jobs | 渲染线程 + 有限并行 | 部分 | P1 | 阶段 B：Jobs + 主线程规则（可选 PR0008） |

---

## 17.2 Editor

| Unity 模块 | Prowl 对应 | 状态 | 优先级 | 备注 |
|------------|------------|------|--------|------|
| Application / 编辑器循环 | `EditorApplication` | 部分 | **P0** | Scene+Game 双完整渲染 → Preview 管线 / 脏刷新 |
| AssetPipeline | Editor 资产库 / Importers | 部分 | P1 | Import/Refresh/依赖图缺口列表化 |
| BuildPipeline | `DesktopBuildPipeline` 等 | 部分 | P1 | 多平台 / PlayerSettings 矩阵 |
| Commands / Undo | 编辑器命令体系 | 部分 | P1 | |
| Scene / Game View | `SceneViewPanel`, `GameViewPanel` | 部分 | **P0** | 不可见跳过、非 Play 降频、Quality tier |
| Inspector / CustomEditors | CustomEditors + Paper UI | 有 | — | **不** 移植 Unity C++ IMGUI |
| Prefab 工作流 | Echo Prefab | 部分 | P1 | Override/Apply 差异表 |
| Profiler 窗口 | Stats overlay / RenderStats | 部分 | **P0** | 区分 Preview vs Full、Atlas clear 等 |
| AssetServer / 旧协作 | — | 无 | P3 | 不跟 |

---

## 17.3 P0 可执行 backlog（当前里程碑）

1. **空场景 GPU**: ShadowAtlas 按需（已落地骨架）；Editor Preview 管线；不可见视口 0 次完整管线；非 Play GameView 降频/缓存。
2. **QualitySettings**: 阴影图集尺寸、阴影距离、Preview vs Full 档位（编辑器默认 `ShadowAtlas.PreferredSize = 2048` 已设）。
3. **Profiler / RenderStats**: AtlasCleared、ShadowPassesSkipped、PipelineMode 可观测；后续 CPU 区段。
4. **GfxDevice 抽象准备**: 阶段 C RHI 前先收拢 GL 调用边界（CommandBuffer / executor）。
5. **分析器驱动热路径**: PR0001–7；热路径去 LINQ/分配（`GameViewPanel` 相机收集已去 LINQ）。

---

## 17.4 原则

- **同步的是能力与契约**，不是文件树。
- P2/P3 不进当前里程碑，除非产品明确需求。
- 与路线图阶段挂钩：A = 空场景/编辑器成本；B = 吞吐与 Jobs；C = RHI。

---

## 17.5 修订记录

| 日期 | 说明 |
|------|------|
| 2026-07-23 | v1：矩阵骨架 + P0 backlog |
