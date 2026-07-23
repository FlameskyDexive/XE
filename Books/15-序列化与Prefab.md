# 15 · 序列化与 Prefab

---

## 15.1 序列化栈

| 层 | 技术 |
|----|------|
| 库 | **[Prowl.Echo](https://github.com/ProwlEngine/Prowl.Echo)** |
| 图表示 | `EchoObject` 树；`$id` / `$type` 图引用 |
| API | `Serializer.Serialize` / `Deserialize` / `DeserializeInto` |
| 自定义 | `ISerializable`、`ISerializationCallbackReceiver` |
| 上下文 | `SerializationContext`（`Defer`）、`DependencySerializationContext` |

### 字段纳入规则

- 公共字段/属性，或带 `[SerializeField]`
- 跳过 `[SerializeIgnore]` / `[NonSerialized]`
- 具体规则由 Echo + `RuntimeUtils` 共同解释

---

## 15.2 EngineObject 头

所有 `EngineObject` 共享：

- `Name`
- `AssetPath`
- `AssetID`

当 `AssetID != Guid.Empty` 时，引用处通常 **只存 Guid**，由 `AssetDatabase` 解析，而不是深拷贝资源字节。

---

## 15.3 GameObject 自定义序列化

**写：**

- 头、Identifier、启用标志、Tag/Layer、HideFlags
- Transform
- Components 列表
- Children 列表
- 可选 Prefab 块（`PrefabAssetId` 非空时）

**读：**

1. 反序列化时先分配 **临时** Guid
2. **Scene** 在 `OnAfterDeserialize` 写回稳定 Guid
3. 父节点 **不** 存在子节点上；由父反序列化 Children 时重建
4. **组件先于子物体**（与编码顺序一致，保证 `$id` 完整）
5. 类型缺失 → `MissingMonobehaviour` 持有原始 `EchoObject`；延迟 **back-patch** 可恢复困在缺失组件内的对象引用
6. 空 Transform / 空 Components 安全降级（桩 `$id`）

---

## 15.4 Scene 序列化

**`OnBeforeSerialize`：**

- 拍平 `AllObjects` → `serializeObj`
- 并行捕获 `_goIdentifiers`、打包的 `_compIdentifiers` + `_compIdOffsets`

**`OnAfterDeserialize`：**

- 将 Guid 写回 GO/组件
- 对每个根调用 `Add(obj)`（重建注册与生命周期）

这把「图重建」与「身份恢复」拆成两阶段，避免反序列化中途触发不完整的场景逻辑。

---

## 15.5 AssetRef 与依赖

```csharp
// 序列化 AssetRef<T>
始终写 AssetID
if AssetID 非空 → 纯引用 + DependencySerializationContext.Add(guid)
else if 有运行时实例 → 内联 Instance 子树
```

用途：

- 场景/Prefab 存盘体积小
- 构建时 `AssetCollector` 能完整收集依赖
- 运行时与编辑器共用同一引用语义

---

## 15.6 身份模型对照

| ID | 生命周期 | 用途 |
|----|----------|------|
| `InstanceID` | 进程内 | 调试、短期引用 |
| `Identifier` (GO/MB) | 跨存档 | Undo、Selection、热重载、Prefab 路径 |
| `AssetID` | 内容库主键 | 资源引用 |

---

## 15.7 Prefab 系统

### 运行时资源 — PrefabAsset

**文件:** `Resources/PrefabAsset.cs`

```csharp
public class PrefabAsset : EngineObject
{
    public EchoObject GameObjectData;
    public GameObject? Instantiate(); // Deserialize + StampPrefabId
}
```

`StampPrefabId`：

- 写入 `PrefabAssetId`
- 记录组件数/子物体数
- **跳过** 已有不同 Prefab id 的嵌套实例边界

### 实例数据（在 GameObject 上）

| 字段 | 说明 |
|------|------|
| `PrefabAssetId` | 来源 Prefab |
| `PrefabOverrides` | `List<PropertyOverride>` |
| `PrefabComponentCount` / `PrefabChildCount` | 结构差异检测 |

### PropertyOverride

**文件:** `PropertyOverride.cs`

```csharp
public class PropertyOverride
{
    public string Path;       // "{componentGuid}.{fieldPath}" 或 "$.{goField}"
    public EchoObject Value;
}
```

路径用 **组件 Guid** 而非类型名索引，重排组件后仍可匹配。

---

## 15.8 编辑器 Prefab 操作

**文件:** `Prowl.Editor/Utils/PrefabUtility.cs`

| 操作 | 行为 |
|------|------|
| `CreatePrefab` | 序列化干净 GO 树为 `.prefab`，写 meta Guid，源物体盖章为实例 |
| `InstantiatePrefab(Guid)` | `AssetDatabase.Get` → `PrefabAsset.Instantiate()` |
| `BreakPrefabInstance` | 清除追踪（可 Undo） |
| `ApplyOverrides` / `ApplySingleOverride` | 写回资产并刷新实例 |
| `RevertOverrides` / `RevertSingleOverride` | 从源重实例化，再应用剩余 Override |
| `RefreshAllInstances` | 场景内匹配 Prefab id 的根全部刷新 |

嵌套 Prefab：每个 GO 自有 `PrefabAssetId` 作为边界；Stamp 不穿越不同 id。

---

## 15.9 其他 Echo 使用场景

| 场景 | 用法 |
|------|------|
| Play Mode | 编辑场景整图克隆 |
| Undo | Property 前后快照 |
| 热重载 | `~autosave.scene` 往返 |
| Importer Settings | `DefaultSettings()` Echo 树 |
| 项目/构建设置 | 持久化配置 |
| Missing 脚本 | 原始子树保底 |

---

## 15.10 设计模式小结

- **自定义图序列化**（ISerializable）
- **占位 + 延迟修复**（MissingMonobehaviour / back-patch）
- **身份捕获与恢复分离**（Deserialize vs Scene 回调）
- **引用 vs 内联双路径**（AssetID / 运行时资源）
- **原型 + 差分**（Prefab 模板 + PropertyOverride）

---

## 15.11 最佳实践

1. 需要稳定引用的组件字段用 `Identifier` 或场景查找，勿持久化 `InstanceID`
2. 资源字段用 `AssetRef<T>`
3. 不要序列化可从其他状态重建的缓存（标 `[SerializeIgnore]`）
4. 修改脚本字段名等于断数据；重要资产先备份
5. Prefab Override 路径依赖组件 Guid——避免在实例上随意「替换组件」而不走官方 API
6. 大型场景注意 Echo 图大小；合理拆分子场景/Prefab

---

## 15.12 相关文件

| 路径 | 角色 |
|------|------|
| `EngineObject.cs` | 头字段 |
| `GameObject/GameObject.cs` | GO 序列化 |
| `GameObject/MonoBehaviour.cs` | 回调接口 |
| `Resources/Scene.cs` | 场景身份恢复 |
| `Resources/PrefabAsset.cs` | Prefab 资源 |
| `PropertyOverride.cs` | Override 记录 |
| `Components/MissingMonobehaviour.cs` | 缺失脚本 |
| `Prowl.Editor/Utils/PrefabUtility.cs` | 编辑器操作 |
| `Prowl.Editor/Core/Undo.cs` | Echo 快照撤销 |
