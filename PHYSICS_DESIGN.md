# PHYSICS_DESIGN.md — 物理与碰撞设计（玩家 / 球 / 静态几何）

> 本文件是**物理选型的唯一权威**。与 `AGENTS.md §5.3`、`DEVELOPMENT_PLAN.md §4` 冲突时以本文件为准（§7 列出了需要同步修订的位置）。
> 适用节点：M2 收尾 → M3（玩家预测）→ M4（球 reconcile）。

---

## 0. 速览（一页结论）

| 对象 | 刚体类型 | 驱动方式 | 与谁发生物理接触 |
|------|---------|---------|----------------|
| **球** `SharedBall` | **Dynamic**（不变） | `PredictionRigidbody` + 速度/力 | World、Ball、**Player**（要回弹、要被推） |
| **玩家** `Player` | **Kinematic**（改动） | **自己 CapsuleCast 解算出位移** → `MovePosition` | **仅 Ball**（默认 contact pairs 允许）；与 World **无物理接触**（靠自 sweep） |
| **房间** 地板/墙/柱 | 无刚体（静态 Collider） | — | Ball（回弹）；对 Player 只作 sweep 查询目标 |

**三条一句话决策：**

1. **玩家改 Kinematic**：只有 Kinematic 能真正保证"不受球推动、不受惯性带走"。Dynamic + 每帧覆写速度做不到（物理步内动量已交换，且回滚重放时球的误差会污染玩家位置——直接踩 M4 的评分点）。
2. **穿墙不靠 PhysX，靠自 sweep**：Unity 默认 `Default Contact Pairs` **不产生 kinematic-static 接触对**，所以 Kinematic 玩家在物理上永远不会被墙挡住。位移必须由 `Physics.CapsuleCast` 自己解算后再写入位置。
3. **玩家移动的检测层不含球**：位置解算只查 `World` 层 → 玩家移动路径不被球阻断；而 `Player↔Ball` 的物理接触仍然开启 → 球照样被推开、被弹开。两件事互不冲突。

---

## 1. 问题定义

### 1.1 现状（M2 收尾）
`Player.prefab` 目前是 **Dynamic**（`m_IsKinematic: 0`，mass 1，gravity 开，冻结旋转，`CollisionDetection = Discrete`），靠 `PlayerMotor.FixedUpdate` 每物理步**直接覆写 `linearVelocity`** 来移动（保留 y 给重力）。

### 1.2 两个互相拉扯的诉求
- **诉求 A**：球必须是物理的 —— 要回弹、要被推、要被两个人抢。 → **Dynamic，无争议**。
- **诉求 B**：玩家要像一堵"会走的墙" —— 球撞上来玩家纹丝不动、不被惯性带走。 → **Kinematic 的语义**。

### 1.3 为什么"改 Kinematic 后会穿墙"
不是 Unity 的 bug，是三条事实叠加的必然结果：

| 事实 | 后果 |
|------|------|
| Unity Physics 的 `Contact Pairs Mode` 默认为 `Default Contact Pairs`，**明确排除 kinematic-static 接触对** | Kinematic 玩家与静态墙**永远不产生接触** —— 既不被挡，也不会被 depenetrate 推回来 |
| Kinematic 刚体的 `collisionDetectionMode` 只有 `Discrete` / `ContinuousSpeculative` 可选 | `Discrete`（默认）下移动是纯瞬移，一帧位移超过墙厚即穿透 |
| 官方文档对 `ContinuousSpeculative` 的原文警告：*"in some cases, high speed objects may still tunneling through other geometries"* | 即使开 CCD 也不能作为唯一防线 |

**关键认知**：`MovePosition` / `ContinuousSpeculative` 都救不了这个问题 —— 因为它们都指望 PhysX 去挡玩家，而 PhysX 在默认配置下**根本不与静态体做 kinematic 接触**。

> 所以 `AGENTS.md §5.3` 里"kinematic 与静态碰撞体之间不产生接触，玩家会穿墙"这句**是对的**，但它只在"把碰撞交给 PhysX"这个前提下成立。**换掉这个前提，结论就翻转**：玩家位移自己算，PhysX 完全不参与玩家与静态世界的碰撞 —— 它就没机会穿墙，因为每一段位移都是我们验证过才写进去的。

---

## 2. 定案：玩家 = Kinematic + 自 sweep 解算

### 2.1 `Player.prefab` 刚体配置（逐项，照改）

| 属性 | 值 | 理由 |
|------|-----|------|
| `m_IsKinematic` | **1** | 不受任何力/冲量影响 → 球撞不动它 |
| `m_UseGravity` | **0** | Kinematic 本就不吃重力，显式关掉避免误解；落地由 §2.3 自己处理 |
| `m_Constraints` | **56**（Freeze Rotation X/Y/Z，保持现值） | 不翻倒 |
| `m_CollisionDetection` | **`ContinuousSpeculative`(=3)** | Kinematic 唯一可用的 CCD，作为§2.2 自 sweep 之外的**兜底**（防与其他 Dynamic 的高速接触穿透） |
| `m_Interpolate` | **0（None）** | 逻辑体不做插值；从 M3 起图形平滑由 reconcile + 平滑器负责（现在开 `Interpolate` 是因为 M1 走 NetworkTransform，M3 要关掉） |
| `m_Mass` | 1（无意义） | Kinematic 视作无限质量，值被忽略 |

`CapsuleCollider` 不变：`Radius = 0.4`、`Height = 1.8`、center 在原点。

> **不要修改** `Project Settings > Physics > Contact Pairs Mode`（保持 `Default Contact Pairs`）。
> 改它会让 kinematic-static 产生接触对，把"墙的阻挡"重新交回 PhysX —— 既引入性能开销，又把**确定性**（预测回滚的生命线）交给物理引擎的接触求解器。墙的阻挡必须由我们的 sweep 独断。

### 2.2 水平移动解算（核心算法）

在 `[Replicate]` 内执行，输入是期望位移，输出是**已验证可用的位移**：

```csharp
/// <summary>
/// [热路径][双端 | 每 Tick] 用 CapsuleCast 把期望位移解算成不会穿墙的位移。
/// 只对 World 层做查询：球与其他玩家不阻断玩家移动（诉求 B 的一半）。
/// </summary>
private Vector3 ResolveMove(Vector3 position, Vector3 desiredDelta)
{
    Vector3 remaining = desiredDelta;

    for (int i = 0; i < PhysicsTuning.MoveSlideIterations; i++)
    {
        float dist = remaining.magnitude;
        if (dist <= PhysicsTuning.MoveEpsilon)
            break;

        Vector3 dir = remaining / dist;
        Vector3 p1 = position + Vector3.up * PhysicsTuning.CapsuleHalfSegment;
        Vector3 p2 = position - Vector3.up * PhysicsTuning.CapsuleHalfSegment;

        // CapsuleCast 单次查询自身无堆分配，无需 NonAlloc 版本。
        if (Physics.CapsuleCast(p1, p2, PhysicsTuning.PlayerRadius, dir, out RaycastHit hit,
                dist, PhysicsTuning.WorldLayerMask, QueryTriggerInteraction.Ignore))
        {
            float safe = Mathf.Max(0f, hit.distance - PhysicsTuning.MoveSkinWidth);
            position += dir * safe;

            // 剩余位移投影到墙面 → 贴墙滑行，而不是被粘住。
            Vector3 leftOver = dir * (dist - safe);
            remaining = Vector3.ProjectOnPlane(leftOver, hit.normal);
        }
        else
        {
            position += dir * dist;
            break;
        }
    }

    return position;
}
```

要求与坑：

- **迭代次数固定 3 次**（`MoveSlideIterations`）—— 固定次数 = 确定性；不固定会让两端结果分叉。角落处剩余位移丢弃，下个 Tick 继续。
- **`MoveSkinWidth`**（建议 `0.01f`）：不贴面留缝，否则下次 Cast 起点落在碰撞体表面会产生 `distance = 0` 的死锁。
- **起点不能与碰撞体重叠**。出生点必须保证不插在墙里（`PlayerSpawnHeight` 已按此设计）。若重叠，PhysX 会返回 `distance = 0`，玩家被永久卡住 —— 这是本方案唯一的"硬卡死"风险，§6 有用例专门覆盖。
- **只用 `TimeManager.TickDelta`（固定 0.02）**：单 Tick 最大位移 = `PlayerMoveSpeed × 0.02`。当前 6 m/s → **0.12 m**。这是 §2.5 墙厚要求的输入值。

### 2.3 垂直方向（Kinematic 没有重力，必须自己管）

玩家在平地房间，垂直方向保持最简实现：

```csharp
/// <summary>[热路径][双端 | 每 Tick] 向下探测地面并把脚底贴合地面高度。</summary>
private float ResolveGroundY(float currentX, float currentZ)
{
    Vector3 origin = new(currentX, PhysicsTuning.GroundProbeStartY, currentZ);
    if (Physics.SphereCast(origin, PhysicsTuning.PlayerRadius * PhysicsTuning.GroundProbeRadiusRatio,
            Vector3.down, out RaycastHit hit, PhysicsTuning.GroundProbeDistance,
            PhysicsTuning.WorldLayerMask, QueryTriggerInteraction.Ignore))
    {
        return hit.point.y + PhysicsTuning.CapsuleHalfHeight;
    }

    return _lastGroundY;   // 探不到地面就沿用上一 Tick（不要瞬间掉到 0）
}
```

- 房间是平地时该值恒定，成本可忽略；
- 不做真实重力/跳跃（本 Demo 不需要），未来要加再在本函数内扩展 —— 属于 `PlayerPredictedMotor` 内部实现，不破坏对外接口。

### 2.4 `[Replicate]` / `[Reconcile]` 骨架

玩家是 Kinematic，**不使用 `PredictionRigidbody`**（那是给 Dynamic 刚体用的：靠写入 velocity 再 `Simulate()` 驱动，Kinematic 会忽略 velocity）。玩家用**位置直接驱动**：

```csharp
[Replicate]
private void Move(MoveInput input, ReplicateState state = ReplicateState.Invalid,
                  Channel channel = Channel.Unreliable)
{
    // 服务器与 Owner 均在 Tick 内执行；回滚重放时同一方法体被再次调用。
    float delta = (float)TimeManager.TickDelta;

    _yaw = input.Yaw;                                  // 朝向由输入直接决定（无惯性）
    Quaternion rotation = Quaternion.Euler(0f, _yaw, 0f);

    Vector3 direction = rotation * new Vector3(input.Move.x, 0f, input.Move.y);
    Vector3 desired = direction * PhysicsTuning.PlayerMoveSpeed * delta;

    Vector3 next = ResolveMove(_rigidbody.position, desired);
    next.y = ResolveGroundY(next.x, next.z);

    // MovePosition（而非直接写 position）：Kinematic 用 MovePosition 时 PhysX 仍会
    // 对被挤入的 Dynamic 球执行 depenetration，这是我们"推球"的物理基础（§2.6）。
    _rigidbody.MovePosition(next);
}

[Reconcile]
private void Reconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
{
    // Kinematic 直接写 position 即可（没有速度需要恢复 → 数据量比 Dynamic 方案更小）。
    _rigidbody.position = rd.Position;
    _yaw = rd.Yaw;
    _rigidbody.rotation = Quaternion.Euler(0f, rd.Yaw, 0f);
}
```

`ReconcileData` 极简：

```csharp
public struct ReconcileData : IReconcileData
{
    public Vector3 Position;
    public float Yaw;
    private uint _tick;
    public void Dispose() { }
    public uint GetTick() => _tick;
    public void SetTick(uint value) => _tick = value;
}
```

> 对比 Dynamic 方案（需同步位置 + 线速度 + 角速度）——Kinematic 的 reconcile 载荷更小、无"速度恢复"环节，回滚更干净。这是改 Kinematic 的**额外收益**。

### 2.5 墙的几何要求（配合自 sweep）

- 单 Tick 最大位移 = `PlayerMoveSpeed × 0.02 = 0.12 m`；
- **墙/柱/地板的碰撞体厚度 ≥ 0.3 m**（≈ 2.5 倍单 Tick 位移）。用 Cube 拼，不要用薄 Quad 或单面 MeshCollider；
- 所有静态几何**必须放在 `World` 层**，否则 sweep 查不到 —— 这是最容易漏的配置点。

### 2.6 推球：物理接触 + 可选冲量

**分工**（改 Kinematic 后推球依然成立，机制从"动量交换"变成"挤出 + 冲量"）：

| 情形 | 发生的层 | 结果 |
|------|---------|------|
| 玩家移动挤入球 | PhysX：kinematic-dynamic 接触对**默认存在** | 球被 depenetrate 推开（位置修正，**不带速度**） |
| 球滚过来撞静止玩家 | 同上 | 球被弹开，玩家不动（Kinematic 无限质量） ✓ 诉求 B 达成 |
| 想要"撞飞"的爽感 | 服务器显式补冲量 | 见下 |

**服务器补冲量（M3 先不加，M4 调参阶段按手感决定）**：在 `BallPrediction` 的 `[Replicate]` 内（球是权威对象），服务器分支下用 `Physics.OverlapCapsuleNonAlloc` 查 `Ball` 层 → 命中球则：

```csharp
// [热路径][仅服务器 | 每 Tick] 玩家撞球时补一份冲量，让球"飞出去"而不是被"挤出去"。
Vector3 playerVelocity = /* 玩家上 Tick 位移 / TickDelta，缓存而来，不 GetComponent */;
Vector3 dir = (ball.position - player.position).normalized;
float speedAlong = Vector3.Dot(playerVelocity, dir);
if (speedAlong > PhysicsTuning.MinPlayerPushSpeed)
    ball.AddForce(dir * speedAlong * PhysicsTuning.PlayerPushFactor, ForceMode.Impulse);
```

- **只在服务器执行**，客户端球靠 reconcile 修正（避免"客户端自算冲量 → 与权威分叉"）；
- 冲量必须**幂等可重放**：条件只依赖被同步的状态（球位置、玩家位置），不依赖帧率或随机数。

### 2.7 层的划分与碰撞矩阵

项目当前只有 Unity 默认层（`Default/TransparentFX/Ignore Raycast/Water/UI`），需新增三层：

| 层名 | 用途 | 谁在上面 |
|------|------|---------|
| `World` | 静态几何 | 地板、墙、柱（标为 Static） |
| `Player` | 玩家胶囊 | `Player.prefab` 逻辑根 |
| `Ball` | 共享球 | `SharedBall.prefab` 逻辑根 |

`Project Settings > Physics > Layer Collision Matrix`：

| | World | Player | Ball |
|---|---|---|---|
| **World** | — | ✅（保持开，无害；实际由 sweep 处理） | ✅ 保持开（球要回弹） |
| **Player** | ✅ | ⬜ 先关（见下） | ✅ **必须开**（推球/弹球） |
| **Ball** | ✅ | ✅ | ✅ 保持开（球撞球） |

- **`Player ↔ Ball` 必须开启** —— 这是推球和"球撞玩家弹开"的物理基础，关掉就全没了。
- **`Player ↔ Player` 先关**：Kinematic-Kinematic 在默认 contact pairs 下本来也不产生接触，开着无意义；玩家互相穿过在 1–4 人 demo 里可接受。想加"玩家互相推开"是独立增强（需要在移动解算的 mask 里加 `Player` 层，但远程玩家位置在客户端是插值的 → 会引入预测误差，**M4 通过之前不要做**）。

**sweep 用的层掩码与碰撞矩阵是两回事，不要混：**

- 移动解算 `WorldLayerMask` = **仅 `World`** → 玩家移动路径不被球/其他玩家阻断（诉求 B 的另一半）；
- 地面探测 `WorldLayerMask` = 仅 `World`；
- 碰撞矩阵管的是"物理引擎要不要让它们接触"，与上述查询掩码互不影响。

### 2.8 新增的 `PhysicsTuning` 常量

```csharp
// ---- 玩家移动解算（M3）----

/// <summary>解算位移时的贴墙滑行迭代次数（固定值 = 确定性，不要改成动态次数）。</summary>
public const int MoveSlideIterations = 3;

/// <summary>小于该长度的剩余位移直接丢弃（米）。</summary>
public const float MoveEpsilon = 0.001f;

/// <summary>贴墙留缝，避免下次 Cast 起点落在碰撞面上返回 distance = 0（米）。</summary>
public const float MoveSkinWidth = 0.01f;

/// <summary>胶囊两个球心距原点的高度（= Height/2 - Radius = 0.9 - 0.4）。</summary>
public const float CapsuleHalfSegment = 0.5f;

/// <summary>胶囊中心到脚底的距离（= Height/2）。</summary>
public const float CapsuleHalfHeight = 0.9f;

/// <summary>地面探测起点高度（米）。</summary>
public const float GroundProbeStartY = 2f;

/// <summary>地面探测的球半径相对玩家半径的比例。</summary>
public const float GroundProbeRadiusRatio = 0.9f;

/// <summary>地面探测最大距离（米）。</summary>
public const float GroundProbeDistance = 4f;

/// <summary>玩家移动解算查询的层掩码（仅静态世界层）。</summary>
public const int WorldLayerMask = 1 << WorldLayer;   // WorldLayer 见 PhysicsLayers 常量

// ---- 推球冲量（M4 视手感启用）----

/// <summary>玩家沿接触法线的速度低于该值时不产生推球冲量（米/秒）。</summary>
public const float MinPlayerPushSpeed = 1f;

/// <summary>玩家速度到球冲量的转换系数。</summary>
public const float PlayerPushFactor = 1.2f;
```

---

## 3. 被否决的备选（防跑偏，别回头试）

### 3.1 `CharacterController` —— 否决
FishNet 官方 `Demos/Prediction/CharacterController/CharacterControllerPrediction.cs` 的原文注释：

> *"Character controllers are a bit problematic with colliders. If you were to pass `Vector3.zero` into the move then there's a chance other colliders will clip through the characterController. **When this is combined with reconciles, it's practically guaranteed this will happen.**"*

即：CC + 预测回滚 **必然穿模**，官方为此专门打了 `enabled = false → 改位置 → enabled = true` 的补丁。叠加 CC 不参与 PhysX 接触（推不动球）、会沿球面爬升 → **排除**。

### 3.2 Dynamic + 每 Tick 覆写速度（现状）—— 被替换
能跑，且推球自然（真实动量交换）。但在 M3/M4 语境下有两个硬伤：
1. 物理步内动量交换**已经发生**，玩家在那一 Tick 会被球推走一点，之后才被覆写 —— "不受球影响"是打折的；
2. **（决定性）** 回滚重放时，球的预测/权威位置存在误差 → 通过动量交换污染**玩家的预测位置** → 玩家与球互相污染。这正是 M4 要解决的"双人同 Tick 撞球"，把玩家绑进球的误差链会直接拉低评分点的表现。
3. 垂直/旋转方向的惯性残留需要额外压制代码。

### 3.3 玩家全 Trigger + 全自研碰撞 —— 否决
最可控，但要自己实现与球、与墙、与其他玩家的一切交互，工作量与风险都不匹配笔试题预算，且放弃 PhysX 在球侧的价值。

### 3.4 关闭 `Player ↔ Ball` 物理接触，球侧全自研 —— 否决
玩家会直接穿过球（因为没有任何阻挡），在没有实现球侧完整碰撞前表现为穿模。且球撞玩家需要球侧自写，等于重做 §3.3。

---

## 4. 改动清单（Codex 按此执行）

| # | 文件 / 资产 | 动作 |
|---|------------|------|
| 1 | `ProjectSettings/TagManager.asset` | 新增层 `World`(6) / `Player`(7) / `Ball`(8) |
| 2 | `ProjectSettings/PhysicsSettings.asset` | Layer Collision Matrix：**开 `Player↔Ball`**；关 `Player↔Player`。**不动** `Contact Pairs Mode` |
| 3 | `Assets/_Project/Prefabs/Player.prefab` | `m_IsKinematic: 1`、`m_UseGravity: 0`、`m_CollisionDetection: 3`、`m_Interpolate: 0`；层设为 `Player`；`Graphic` 子物体层设为 `Default`（渲染无关） |
| 4 | `Assets/_Project/Prefabs/SharedBall.prefab` | 层设为 `Ball`；保持 Dynamic 不变 |
| 5 | `Assets/_Project/Scenes/Room.unity` | 地板/墙/柱 → 层设 `World` + 勾 Static；每块碰撞体厚度 ≥ 0.3 m |
| 6 | `Assets/_Project/Scripts/Core/PhysicsTuning.cs` | 按 §2.8 追加常量 |
| 7 | `Assets/_Project/Scripts/Player/PlayerPredictedMotor.cs` | **新增**（M3）；实现 §2.2/§2.3/§2.4；M1 的 `PlayerMotor` 按 §8.1 冻结不回改，只在预制体上替换组件 |
| 8 | `Assets/_Project/Scripts/Ball/BallPrediction.cs` | M4 接入 §2.6 的服务器推球冲量（M3 先不写） |

> 所有新增常量走 `PhysicsTuning`，**不许**在组件里留裸数字（CODING_STANDARDS §8.6）。

---

## 5. 与既有文档的同步点（本次一并修改）

- `AGENTS.md §5.3` 玩家条目：把"动态 Rigidbody + 直接设速度"改写为"**Kinematic + 自 sweep 解算**"，并修正"也不用 kinematic 刚体…会穿墙"这句 —— 保留事实描述，补上解法（自 sweep 后不成立）；
- `AGENTS.md §5.4` 共享球：补一句"玩家 Kinematic，球侧不需要为推球做特殊处理，PhysX 的 kinematic-dynamic 接触对负责挤出"；
- `DEVELOPMENT_PLAN.md §4` 玩家对象表 + §4.5 物理同步说明：同步改写；
- `PROGRESS.md`：M2 的"推球"验收说明补充**机制变更**（M2 时是 Dynamic 动量交换，M3 改 Kinematic 后需**重新验收**）；M3 增加工作项与验收标准。

---

## 6. 验收用例（M3 必须全过）

| # | 用例 | 通过标准 |
|---|------|---------|
| P1 | 贴墙八方向全速猛冲各 3 秒 | 不穿墙、不卡进墙里 |
| P2 | 持续推墙角（两墙夹角） | 被挡住并沿墙滑行，不穿出 |
| P3 | 高速球撞静止玩家（≥ 10 m/s） | **玩家位置零变化**，球被弹开 |
| P4 | 玩家全速撞静止球 | 球被推走；玩家**不被减速、不被推回** |
| P5 | 玩家把球挤到墙角继续推进 | 球**不穿墙**、不飞出房间 |
| P6 | 出生点复核 | 玩家出生瞬间不与任何静态碰撞体重叠（防 §2.2 的 `distance = 0` 死卡） |
| P7 | 双开双端 | 双方看到的玩家位置一致；远程玩家无明显抖动 |
| P8 | 双人同 Tick 对冲撞球（M4 用例 T3） | 双端球轨迹一致，无瞬移、无严重错位 |

**回归提醒**：P3/P4 是"诉求 B"的直接验收，M4 完成图形平滑后必须**再跑一次**（平滑器可能掩盖真实位移）。

---

## 7. 风险与回退

| 风险 | 概率 | 处理 |
|------|------|------|
| Kinematic 用 `MovePosition` 时 PhysX **不**对球做 depenetration（版本行为差异） | 中 | P4 用例专门验证。若推不动：改为在球的 `[Replicate]` 里用 §2.6 的显式冲量承担全部推球职责，玩家侧零改动 |
| 角落迭代 3 次仍残留位移导致微穿 | 低 | 提高迭代次数（保持固定值）或降低 `MoveSkinWidth`；不要改成"直到无碰撞"的动态循环 |
| 玩家出生重叠静态体 → 永久卡死 | 低 | 出生点用 `Physics.CheckCapsule` 校验（P6），异常时抬高出生的 y |
| 预测回滚下 sweep 结果两端分叉 | 低 | 检查：迭代次数是否固定、`TickDelta` 是否被改动、`World` 层几何是否两端一致（静态资产本身就一致） |
| 与球的高速接触在 Kinematic 上穿透 | 低 | `ContinuousSpeculative` 已开（兜底）；必要时对球启用 `ContinuousDynamic` |

**回退方案**：若 Kinematic 路线在 M3 验收受挫，回退到 §3.2（Dynamic + 每 Tick 覆写速度）——只需还原 `Player.prefab` 的四个刚体字段，`PlayerPredictedMotor` 换成写 velocity 的版本，其余（层、sweep、验收用例）全部保留可复用。

---

## 8. 附：为什么"诉求 B"在 Dynamic 下注定打折

一句话给出可复述的判据：

> **Kinematic 是"无限质量、不受力"的物理语义；Dynamic + 覆写速度是"每帧假装没有惯性"。**
> 前者是物理引擎保证的**恒等式**，后者是每 Tick 修补的**近似**。
> 而在预测回滚里，任何"近似"都会在重放时被放大 —— 所以关键对象的状态语义要用引擎保证的恒等式，不要用补丁。
