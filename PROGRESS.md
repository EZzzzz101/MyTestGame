# Sphere Room — 模块里程碑清单（PROGRESS）

> 用法：本文件是唯一的推进台账。**一个节点 = 一个大模块完工**，节点内按功能拆多个 commit，节点内全部验收项通过后再打勾并提交一次。
> 约束来源：`AGENTS.md`（工程约定）、`DEVELOPMENT_PLAN.md`（方案）、`CODING_STANDARDS.md`（代码规范，强制）。

---

## 0. 当前工程状态（2026-09-30 核对）

| 项 | 状态 |
|----|------|
| Unity | 6000.3.14f1 ✅ |
| 渲染管线 | URP 17.3.0（模板自带）✅ |
| 输入 | Input System 1.19.0，`activeInputHandler = Input System` ✅ |
| 网络框架 | FishNet 4.7.3（tag 锁定，包内声明 4.7.2）✅ 已装并解析 |
| 多开测试 | ParrelSync 1.5.3 ✅ 克隆可开可 Play（M0 验收通过） |
| 目录/asmdef | ✅ `Assets/_Project` + 6 个 asmdef（Core/Network/Player/Ball/UI + Core.Editor），GUID 引用 FishNet.Runtime / Unity.InputSystem / UnityEngine.UI |
| 场景 | ✅ `Boot`/`Room` 已生成并进 Build Settings（Boot 索引 0）；Room 目前仅地板，墙/柱/球归 M2 |
| 输入资产 | ✅ `_Project/Input/SphereRoom.inputactions`（模板资产迁入改名，GUID 不变，引用未失效） |
| 接线工具 | ✅ `SphereRoom > Setup > 一键重建`（Editor 工具，可重复执行，只做资产组装） |
| Git | ✅ 已初始化（`main`，首个提交 `cb7232e`） |

模板遗留清理：`Assets/Scenes/SampleScene.unity`、`Assets/Readme.asset` 已删；仅剩 `Assets/InputSystem_Actions.inputactions` 待 M1 迁入 `_Project/Input/` 并改名 `SphereRoom.inputactions`。

---

## 1. 模块框架与依赖顺序

```
M0 工程基线
└─ M1 联机骨架（NetworkManager + Boot/Room 场景流 + 玩家非预测移动）
   └─ M2 房间与球（几何 + 共享球生成，非预测）
      └─ M3 玩家预测（[Replicate] 输入 / [Reconcile] 和解）
         └─ M4 球 reconcile + 图形平滑   ← 评分核心，全项目最高风险
            ├─ M5 中途加入 ┐
            ├─ M6 主机退出 ├ 四个独立加分模块，可乱序
            ├─ M7 定时生成 │
            └─ M8 Tapped   ┘
               └─ M9 Steam 联机（必做，最后做）
                  └─ M10 交付打包
```

- **关键路径**：M0 → M1 → M2 → M3 → M4。这条链上任何一环拖延都会挤压后面所有节点。
- **M5–M8 的顺序自由**，但都必须排在 M4 之后（中途加入本质是 reconcile 流的附带产物）。
- **M9 严格遵守 LAN-first**：M0–M8 全部跑 Tugboat，M9 只加 Transport/Lobby 层，游戏逻辑代码零改动。

---

## 2. 横切约束（不单列节点，每个节点都必须满足）

1. **时间对齐**：`TimeManager.TickRate = 50` 且 `Time.fixedDeltaTime = 0.02`，1 Tick = 1 物理步，任何人不许单改其一；网络逻辑计时只用 Tick。
2. **服务器权威**：球物理、生成/销毁、碰撞判定、计时器只在 Host 侧写；客户端只做「采集输入 → 本地预测 → 接受和解」。
3. **热路径 0 分配**：`Update/FixedUpdate/OnTick/[Replicate]/[Reconcile]/RPC/OnCollision*` 内禁字符串拼接、装箱、LINQ、闭包、`new` 引用类型、`GetComponent`/`Find*`/`Camera.main`、`Debug.Log`。
4. **Prefab 层级纪律**：逻辑根（NetworkObject/Rigidbody/PredictionRigidbody/网络脚本）+ `Graphic` 子物体（Renderer）。渲染 Mesh 永不挂逻辑根。
5. **物理参数集中**：弹性/摩擦/平滑权重全部收在 `PhysicsTuning`，不在 Inspector 散落魔法数。
6. **每次节点完工**：编译 0 error 0 warning → 勾选本清单 → 独立 commit（Conventional Commits，中文描述），严禁 squash。
7. **物理选型**（`PHYSICS_DESIGN.md` 为唯一权威）：**玩家 = Kinematic + 自 sweep 解算**（不受球推动/不受惯性，穿墙由 `CapsuleCast` 解决，不依赖 PhysX）；**球 = Dynamic**（回弹、被推）；`Player↔Ball` 接触必须开启，玩家的 sweep 掩码只含 `World` 层。任何"改玩家刚体类型 / 关碰撞矩阵 / 改 Physics `Contact Pairs Mode`"的改动，必须**先更新 `PHYSICS_DESIGN.md`** 再动代码。

---

## 3. 里程碑清单

### [x] M0 工程基线

**目标**：工程可编译、可多开、目录与版本规范就位，之后所有节点都在这套基线上开工。

工作项：
- 装 FishNet 4（git URL，锁 tag `#4.7.3`）与 ParrelSync（`VeriorPies/ParrelSync`，锁 tag `#1.5.3`）到 `Packages/manifest.json`，确认包内 `Demos/Prediction/Rigidbody`（骨架场景 `Rigidbody Prediction Demo.unity`）存在（M3/M4 的参照骨架）。
- 多开工具**定案：ParrelSync 为主，MPPM（com.unity.multiplayer.center）留作备选不装**。原因：MPPM 的 Local Players 多个玩家共享同一进程的静态变量，FishNet 的静态单例/管理器结构在共享进程下有冲突风险；ParrelSync 独立克隆进程隔离彻底，是 FishNet 社区验证过的方案。若后续实测 ParrelSync 克隆有包还原问题，再评估切 MPPM。
- 建 `Assets/_Project/{Scripts/{Core,Network,Player,Ball,UI},Prefabs,Scenes,Materials}`。
- 每个 Scripts 子目录一个 asmdef：`SphereRoom.Core / .Network / .Player / .Ball / .UI`，命名空间同名，引用 FishNet asmdef。
- 建空场景 `Boot`、`Room`，加入 Build Settings 且 Boot 为索引 0。
- ProjectSettings 基线：Physics 交给 FishNet `PhysicsSimulator`（Script 模式）、`Time.fixedDeltaTime = 0.02`；NetworkManager 的 TickRate = 50（M1 落地）。
- 确认 Windows Standalone x64 为目标平台。

**完成定义（DoD）**：Editor 编译 0 error；ParrelSync 克隆实例能进 Play（入口是**顶级菜单** `ParrelSync > Clones Manager`，**不在 Window 菜单下**）；`git log` 有本条提交。
**风险**：FishNet 的包路径/asmdef 名需按包内实际内容确认，不凭记忆写。
**完工**：`bb015d9`→`14ff5e5`（2026-09-30/10-01）— 依赖安装、目录与 asmdef、Boot/Room 场景、Git 初始化全部就位；ParrelSync 克隆可打开并进 Play（2026-10-01 复核确认）。

---

### [x] M1 联机骨架（P0-1 / P0-3 前半）

**目标**：两台 Editor 实例能 Host/Join 相连并互见移动的玩家。

工作项：
- `Boot` 场景常驻 `NetworkManager`（DontDestroyOnLoad）+ Tugboat transport（UDP 7770）。
- uGUI 主菜单：`创建房间(Host)` / `加入(输 IP)` / 状态文本 / 断开返回。
- Host 流程：`ServerManager.StartConnection()` + `ClientManager.StartConnection()` → 加载 `Room`；Join 流程：`ClientManager` 连 IP，场景由 Host 同步。
- `PlayerSpawnManager`（仅 Host）：按连接在出生点 `Instantiate` + `ServerManager.Spawn()`。
- `Player` 预制体（逻辑根 + Graphic 子物体）：NetworkObject / Rigidbody + CapsuleCollider / PlayerMotor（**非预测版**，先跑通链路）/ PlayerCamera（仅 Owner）/ `PlayerInputReader`（见下）。
- 玩家色板：`playerIndex` SyncVar → 4 色。
- 输入方案（**定案**）：
  - 不使用 `PlayerInput` 组件（Send Messages/事件回调模式与 Tick 采样模型和热路径纪律冲突，且字符串消息有分配）；
  - 脚本命名 `PlayerInputReader`（避免与 `UnityEngine.InputSystem.PlayerInput` 撞名）；
  - 输入资产：**把模板 `Assets/InputSystem_Actions.inputactions` 移入 `Assets/_Project/Input/` 并改名 `SphereRoom.inputactions`**（当前无任何引用依赖模板资产，移动安全；遵守"自制资源不落 Assets 根"纪律），在其中加 Move/Look/ESC 等 Action；
  - 采样策略：**Move 在 `OnTick` 内 `ReadValue` 采样**（进 `MoveInput` 结构体随 Replicate 上行）；**Look（鼠标 delta）在 `Update` 采样并即时应用相机旋转**（视角是本地表现，不等 Tick，Pitch 不同步）；ESC 在 Update 采样控制光标/UI。生成 C# Class 或直接资产引用二选一，代码侧不 `Load` 字符串路径。
- 归置 FishNet 自动生成物：FishNet 生成器默认把 `DefaultPrefabObjects.asset` 丢在 **Assets 根**（路径可在 `Edit > Project Settings > FishNet` 修改）。建 NetworkManager 时把它移到 `_Project/Network/`，保持「自制资源不落 Assets 根」纪律。
- 场景清理：`Boot`/`Room` 目前是 URP 模板场景的副本（含 Main Camera / 平行光 / Global Volume）。`Room` 的 Main Camera 必须在玩家相机生效时禁用或移除，避免双相机竞争。
- 已知正常副作用：FishNet 安装时自动写入 `scriptingDefineSymbols: Standalone: FISHNET;FISHNET_V4`，属预期行为，不要手工删除。

**完成定义**：ParrelSync 双开，A Host / B Join，两端互见对方移动与朝向；断线不崩。
**注意**：本节点故意不上预测，先把「连接 + 场景 + Spawn + 输入」链路跑通，M3 再换成预测移动。
**完工**：`8d04a14`（2026-10-01）— 双开 Host/Join 互见移动已验收通过；收尾时按新增 §6/§7/§8 完成合规整改（命名、热路径与网络注释、材质改名）。M1 的 `PlayerMotor` 自此冻结，M3 以新增预测组件替换，不再回改本类（§8.1）。

---

### [~] M2 房间与球（P0-2 前半）（代码与接线工具已就位，待 Unity 内重建资产 + 运行验收）

**目标**：有可玩的房间和会弹回、会同步的共享球（允许被拉扯）。

工作项：
- `Room` 场景几何：Cube 拼地板 + 四面墙 + 3~4 根障碍柱；静态 Collider（**每块厚度 ≥ 0.3 m**，层 = `World`，勾 Static）；顶棚按需（防球飞出）。
- `PhysicsTuning` 常量类：Bounciness 0.55~0.7 / Friction 0.4 / 平滑参数；层索引常量（`World`/`Player`/`Ball`）（解算用常量在 M3 追加，见 `PHYSICS_DESIGN.md §2.8`）。
- `SharedBall` 预制体（逻辑根 + Graphic 子物体）+ 材质；Host 端开局生成 1 个。
- 球状态同步先走「非预测近似」（NetworkTransform 或状态 SyncVar），明确标注为临时方案。
- 玩家胶囊体与球的物理碰撞天然推球（**不做射线/按键施力**）。
  - ⚠️ **机制变更提示**：本节点验收时玩家是 **Dynamic**，推球 = 动态 × 动态的真实动量交换；M3 玩家改为 **Kinematic** 后，推球机制变为 `kinematic-dynamic` 接触对的 **depenetration（挤出）**，手感会变化 → **M3 必须重新验收推球用例（见 M3 的 P3/P4）**，不要把 M2 的推球结论直接沿用。
- 球的层级纪律（为将来换足球预留，AGENTS §5.4/§5.9）：逻辑根 + `Graphic` 子物体（Sphere 原型 + `Mat_Ball`），Graphic 缩放由 `PhysicsTuning.BallRadius` 推导；换皮只改 Mesh/Material/Texture。
- 新增 `BallImpactDispatcher`（服务器碰撞事件源）：`OnCollisionEnter` 判定 → `ObserversRpc` 广播 → 触发本地 `event Action<BallImpactData> BallImpacted`。M2 只保证「服务器判定 → 广播 → 本地事件」链路可用，音效订阅者（`BallAudioView`）在 M8 接。

**完成定义**：A 推球撞柱，两端都看到真实弹回、球不穿墙；允许拉扯与延迟（M4 修）。
**完成定义（补充）**：碰球事件已能被订阅（M8 之前可用一次性临时订阅验证链路，验证后删除）。

---

### [ ] M3 玩家预测（P0-2 核心前半）

**目标**：本地移动零输入延迟，服务器权威且能纠正作弊/偏差。

工作项：
- **物理选型落地（本节点第一件事，依据 `PHYSICS_DESIGN.md`）**：
  - `Player.prefab` 刚体改 **Kinematic**（`m_IsKinematic: 1` / `m_UseGravity: 0` / `m_CollisionDetection: ContinuousSpeculative` / `m_Interpolate: 0`），层设 `Player`；
  - `SharedBall.prefab` 层设 `Ball`；房间静态几何层设 `World` 并勾 Static，碰撞体厚度 ≥ 0.3 m；
  - 新增层 `World`/`Player`/`Ball` 与碰撞矩阵（**开 `Player↔Ball`**、关 `Player↔Player`；**不动** `Contact Pairs Mode`）；
  - `PhysicsTuning` 追加 `PHYSICS_DESIGN.md §2.8` 的解算常量。
- 物理改由 FishNet `PhysicsSimulator` 手动步进。
- 输入结构体 `MoveInput { Vector2 Move; float Yaw; }`（Pitch 只作用本地相机，不同步）。
- 移动逻辑写进 `[Replicate]` 方法（每 Tick 采集输入上行），`[Reconcile]` 内回滚重放。
- 移动位移用 `Physics.CapsuleCast` **自 sweep 解算**（sweep + 贴墙滑行、迭代次数固定 3；查询掩码只含 `World` 层），垂直方向用向下 `SphereCast` 贴地。**玩家不挂 `PredictionRigidbody`**（Dynamic 专用），`[Reconcile]` 直接写 `position`。
- 实施方式（§8.1）：**新增** `PlayerPredictedMotor` 组件并在预制体上替换 M1 的 `PlayerMotor`，不回改已验收的核心类。
- 骨架：球用包内 `Demos/Prediction/Rigidbody`；玩家侧**不用** `CharacterController`（官方原文：与 reconcile 结合会 "practically guaranteed" 穿模），按 `PHYSICS_DESIGN.md §2.4` 的骨架实现。
- 服务器侧输入验证：速度上限 clamp、位置越界回正。
- 远程玩家朝向用低频 SyncVar（yaw）仅作视觉。

**完成定义**：双开下本地移动无延迟感；远程玩家位置平滑无明显抖动；服务器能拒绝异常速度。
**完成定义（物理，逐条见 `PHYSICS_DESIGN.md §6`）**：
- P1 贴墙八方向全速猛冲 3s 不穿墙；
- P3 高速球撞静止玩家 → **玩家位置零变化**、球被弹开；
- P4 玩家全速撞球 → 球被推走，玩家不被减速/推回（M2 的推球结论必须在此重新验收）；
- P5 玩家把球挤到墙角 → 球不穿墙；
- P6 出生点无重叠（防 sweep 起点在碰撞体内导致永久卡死）。
**风险**：以官方 `Demos/Prediction/Rigidbody` 为骨架改造，逐行核对 API 签名；若 `MovePosition` 在 Kinematic 下不触发对球的 depenetration（P4 失败），按 `PHYSICS_DESIGN.md §2.6/§7` 改由球侧显式冲量承担推球。

**已诊断问题（2026-10-01，两轮运行期排查；修复已由 WorkBuddy 直接落盘，待 Unity 内验证）**：

第一轮（视角 50Hz 顿挫）——已落盘修复：
- 根因 1：`CameraPivot` 挂在**逻辑根**下，没骑在 `PredictionSmoother` 平滑层上 → 画面以 Tick 步进；
- 根因 2：Boot 场景 TimeManager `_physicsMode = Unity(0)`，物理步进与 Tick 相位不锁定。
- 修复：CameraPivot 移入 `Graphic`（localPosition (0, 0.7777778, 0)）；`_ownerSmoothedProperties` 255→1（仅 Position）；Rigidbody `Interpolate`→None；`_physicsMode`→1（TimeManager）。

第二轮（**转动视角时无法移动** + Scene 视图看不到自己胶囊）——已落盘修复：
- 现象：鼠标转动视角期间 WASD 完全不动，停止转视角后恢复移动；且本地胶囊在 Scene 视图不可见。
- 根因（转动锁死）：`PlayerPredictedMotor.Update()` 每帧直接写逻辑根的 `transform.rotation`。逻辑根是 Kinematic Rigidbody 物体，位移靠 Tick 内 `Rigidbody.MovePosition`（待下一次模拟步生效）；**渲染层直接写刚体物体的 Transform 会覆盖/取消待生效的 MovePosition**（Unity 物理团队官方口径："You should never modify a Transform that has a physics component on it, period. If you want to rotate, use MoveRotation."）。不转视角时写入的四元数值不变、被脏检查跳过，所以只有转动时触发——症状完全吻合。项目 `Physics.autoSyncTransforms = 0` 放大了该时序冲突。
- 根因（Scene 不可见）：上一轮用 `renderer.enabled = false` 隐藏本地胶囊，Scene 视图一并被隐藏——设计错误，隐藏应只作用于 Game 视图。
- 修复（架构定案：**视角归渲染层，逻辑根只在 Tick 内经 Rigidbody API 写入**）：
  1. ✅ `PlayerCamera.cs` 重写：Yaw+Pitch 全部作用在 `_viewPivot`（原 `_pitchPivot`，即 CameraPivot，Graphic 平滑层之下），每帧应用 `Quaternion.Euler(pitch, yaw, 0)`（ZXY 顺序 = 先世界 Yaw 后本地 Pitch，标准 FPS 相机）；暴露 `ViewYaw` 供 Tick 采样；`OnStartClient` 内 Owner 剔除 `LocalPlayerBody` 层；死引用 `_motor`（PlayerMotor）一并删除；
  2. ✅ `PlayerPredictedMotor.cs`：**删除 Update() 与 `_pendingYaw`**（渲染层写逻辑根的反模式源头）；`BuildMoveData` 的 Yaw 改采样 `_camera.ViewYaw`（Tick 边界采样，回放用历史值，确定性不变）；`_lookSensitivity` 字段删除（灵敏度归 PlayerCamera）；
  3. ✅ 胶囊隐藏改**层剔除**方案：新增层 `LocalPlayerBody(9)`（TagManager + `PhysicsLayers.LocalPlayerBody` 常量）；Owner 在 `OnOwnershipClient` 把自己的 Graphic 挪到该层，本地相机 CullingMask 剔除之——**Game 视图不可见、Scene 视图照常可见**、其他客户端上同一玩家仍照常渲染（layer 非同步属性，只影响本端实例）；
  4. ✅ `Player.prefab`：PlayerCamera 组件 `_pitchPivot`→`_viewPivot`（同 fileID）、删 `_motor`；Motor 组件补 `_camera`/`_graphicRenderer` 引用、删 `_lookSensitivity`；
  5. ✅ 推论落档：Owner 自己实例的逻辑根从此**完全不旋转**（视觉 Yaw 在相机支架上）；其他端看到的该玩家朝向 = 服务器侧 `MoveRotation` → reconcile Yaw → 观察端 `Reconcile()` 写入（`_spectatorSmoothedProperties=255` 含旋转，远端平滑）。
- **待验证（Unity 内，双开跑一遍）**：边转视角边移动正常；本机视角平滑；Scene 视图能看到自己的胶囊；远端玩家移动/转向平滑。验证通过后：
  - 删除 `_logMoveDiagnostics` 诊断代码（TODO 已注明）；
  - 重跑 M3 物理验收用例 P1/P3/P4（Kinematic 推球机制尚未重新验收）；
  - 本条整段删除。
- 备注：`ReadLook()` 现在只剩 PlayerCamera 一个每帧读者（Motor 不再读 Look），"每帧一个读者"已满足。

---

### [~] M4 球 reconcile + 图形平滑（P0-2 核心后半，★评审核心）

**目标**：双人同一 Tick 对冲撞球，双端轨迹一致、无瞬移、无严重错位。

**实现已落盘（2026-10-01 by WorkBuddy，待 Unity 内验证 + T3 用例验收后勾选）**：
- ✅ 新增 `Assets/_Project/Scripts/Ball/BallPrediction.cs`：reconcile-only 预测。`BallReconcileData : IReconcileData` 携带 `PredictionRigidbody`（完整刚体状态）；OnPostTick 构建 reconcile；服务器经状态转发发给所有观察者，客户端写回 + Graphic 平滑。骨架 = 包内 `Demos/Prediction/Rigidbody`。
- ⚠️ **2026-10-01 ILPP 报错修复**：初版"无 [Replicate]"写法被 FishNet Codegen 拒绝（`BallPrediction must contain both a [Replicate] and [Reconcile] method when using prediction`，见包内 `PredictionProcessor.cs` 的成对校验）。修正为**空 [Replicate]**：`NoInput : IReplicateData`（空载荷）+ 空方法体 `Move(NoInput, ...)`，OnTick 内 `Move(default)` 推进 replicate 队列/历史，使重放与 reconcile 的 Tick 对齐机制正常工作。物理仍由 TimeManager 统一步进，方法体无事可做但调用链不可省。
- ✅ `SharedBall.prefab`：`_enablePrediction: 1`、`_predictionType: 1`（Rigidbody）、`_graphicalObject` → Graphic 子物体（平滑层）、移除 NetworkTransform（与预测抢写 Transform，M2 过渡方案退役）、挂 BallPrediction（组件索引：BallImpactDispatcher=0，BallPrediction=1）。
- ✅ `_enableStateForwarding: 1`（原有配置，reconcile 发给所有观察者而非仅 Owner）。
- ⏳ 待验证（Unity 内，双开）：非主机推球是否当场弹开（M2 时代"1 RTT 延迟弹开"现象应消失）；双端球轨迹一致性；无瞬移。
- 🔧 调参备选（按需）：球 `m_CollisionDetection: 0 → 2`（ContinuousDynamic，PHYSICS_DESIGN §7 高速穿透备选）；`PhysicsTuning` 平滑参数。

**已确认现象（2026-10-01 双开实测，M2 临时方案的预期行为，M4 落地后自动消失，禁止在临时方案上修）**：
- 非主机玩家推球 → 球延迟约 1 个 RTT + 插值缓冲后才弹开；主机玩家推球即时弹开。
- 根因：`SharedBall` 当前 `_enablePrediction: 0` + NetworkTransform 快照同步（M2 过渡）。客户端上球的位置完全由服务器快照驱动：本地刚体即使被 Kinematic 玩家挤出一点点，也会立刻被快照拉回；服务器要等输入上行（½RTT）→ 本 Tick 模拟 → 球状态下发（½RTT）→ 插值缓冲，才表现为"弹开"。主机 = 服务器本身，输入零延迟、球本地模拟，所以即时。**这正是 M4 要消灭的东西**：reconcile-only 预测落地后，客户端的球是本地真实模拟的刚体，Kinematic 玩家推它当场就有物理反应（预测），服务器 reconcile 只做静默修正。

**完成定义（T3 用例）**：两人从对侧同时冲撞同一球 **×10 次**，双端落点与速度一致，无瞬移、无严重错位。
**这是全项目最关键的验收点，不合格不得进入 M9。**

**UI 布局定案（2026-10-01，随 M4 一并落盘）**：Boot 场景 Leave Button 从右上角移至**左上角**（anchor/pivot (0,1)，pos (24,-24)），文案改为「退出游戏」；**右上角留给 NetworkDebugHud** 调试面板（原有位置不变）。`SphereRoomSetup.cs` 接线工具已同步（防误重跑倒退）。

---

### [~] M5 中途加入（P1-1）

**目标**：球滚动中，新客户端加入后位置与速度正确。

工作项：确认 reconcile 流在 Late Join 时携带完整 Rigidbody 状态；新客户端不做一次性瞬移修正。
**完成定义（T4）**：球滚动中 C 加入，C 端球的位置与速度方向正确，无瞬移。

**M5 追加范围（2026-10-01 用户定案）——玩家进出提示 + 踢球音效，已落盘待验证**：
- ✅ 新增 `Network/RoomAnnouncer.cs`：服务器监听 `ServerManager.OnRemoteConnectionState`（两参签名，用 `args.ConnectionId`），经 `[ObserversRpc]` 广播「编号 + 进/出」；挂在 Room 场景 GameManager（全局场景 NetworkObject，中途加入者也能收到，含自己进入的提示）。主机自己的进出不播报（启动时无观察者；主机退出走 M6）。
- ✅ 新增 `UI/RoomToastUI.cs`：顶部居中一行「X 号玩家进入/离开房间」，保持 2s + 渐隐 0.8s（Update 计时，纯表现层）；**不遮挡射线**：Toast Canvas 不挂 GraphicRaycaster + Text.raycastTarget=false 双保险；播放进入退出音效（事件回调拼字符串，事件频率非热路径）。
- ✅ 踢球音效（原 M8 前置到 M5）：`BallImpactData` 新增 `KickerClientId`（-1=非玩家接触）；`BallImpactDispatcher` 服务器侧解析踢球者（`attachedRigidbody` 反查 NetworkObject→Owner，**不用层判定**——玩家逻辑根在 Default 层不在 Player 层；墙/柱无刚体天然排除，球撞球无 Owner 返回 -1）。新增 `Ball/BallAudioView.cs`（纯表现 MonoBehaviour）：**声音源头=足球**（音源挂 Graphic 子物体，3D 空间声随球移动衰减）。**双播放路径（声音预测，2026-10-01 二次定案）**：踢球者本人经 `LocalKickPredicted`（客户端 OnCollisionEnter 检测本地玩家踢球）零延迟立即出声——M4 起球在踢球者客户端本地真实模拟，等广播回传（½RTT）会视觉即时/听觉延迟打架；服务器广播回传的同一事件用 `IsLocallyPredictedKick` 跳过（防双响，主机例外：主机无预测路径，广播到达即播）；其他人踢的球不抢跑，等广播统一时间线。
- ⚠️ **踢球判定关键补丁**：M3 起玩家是 Kinematic（velocity 恒 0），推静止球时 `collision.relativeVelocity ≈ 0` 会被 MinImpactSpeed=1.5 滤掉。已补第二条判据：`effectiveSpeed = max(relativeVelocity, impulse / BallMass)`（depenetration 冲量换算速度变化）。**若实测推球仍无音效，调参备选：降低 MinImpactSpeed 或改用玩家当前移动速度做阈值**。
- ⚠️ **鬼畜连响修复（2026-10-01 双开实测）**：球被玩家顶在墙角 / 贴墙滑行 / 客户端踢向主机胶囊（Kinematic 等效无限质量，不干脆弹开）时，`OnCollisionEnter` 高频重触发（每 Tick 一次 depenetration 冲量都过阈值；reconcile 修正回重叠后本地又撞一次），音效变机枪。根因是物理事件频率，不是网络。**修复 = 事件源节流**：`BallImpactDispatcher._minEventInterval = 0.2s`（服务器广播与客户端预测分支共用 `PassesEventThrottle()`）。若还觉得密，调大该值；若嫌迟钝，调小。
- ✅ 接线：`SphereRoomSetup` 新增菜单项「M5+M6 增量接线」（增量不重建、可重复执行，见 M6 节）。Unity 侧先编译再执行。
- ⏳ 待验证：T4 用例 + 双开看进入/退出提示与音效、踢球音效（含"踢的人自己听不到"）。

---

### [~] M6 主机退出（P1-2）

**目标**：Host 消失时客户端有明确反馈且可恢复。

工作项：订阅 `ClientManager.OnClientDisconnectState` → 弹窗「主机已断开连接」→ 返回 Boot → Reset NetworkManager → 可重新 Host/Join。
**完成定义（T5）**：Host 关进程，客户端弹提示、不崩、能返回菜单并重连。
**取舍**：不做 Host Migration，README 写明理由。

**实现已落盘（2026-10-01 by WorkBuddy，待 Unity 内验证后勾选）**：
- ✅ 「要不要做」已定案：**做**。M9 Steam 只改「怎么找到房间、怎么连」（大厅发现 + P2P 传输），不改「主机掉线后客户端怎么办」——`OnClientConnectionState` 的断开语义在 Tugboat / SteamworksSockets 下一致，「返回大厅」恢复路径 M9 原样复用。
- ✅ `NetworkBootstrap`：新增 `_stopRequestedLocally`（StopNetwork 先立标记再停，区分主动退出 / 意外掉线）；`HostConnectionLost` 事件（仅客户端模式 + 非主动断开时触发，且在 StatusChanged 之后，保证最终只见解散面板）；`ReturnToBoot()` = StopNetwork + 卸载 Room 场景（全局叠加场景不会随断开自动卸载，isLoaded 判空兜底）。
- ✅ 新增 `UI/RoomClosedUI.cs`：「房间已解散 / 与主机的连接已断开」面板 + 「返回大厅」按钮（不用「返回组队」——当前 Boot 的 Host/Join 菜单就是组队界面）。显示期间隐藏主菜单（两个居中面板不叠加）；点击 = 恢复菜单 + ReturnToBoot。主动点「退出游戏」不触发本面板。
- ✅ 接线：`SphereRoomSetup` 菜单项「M5+M6 增量接线」在 Boot 场景 Menu Canvas 下建 Room Closed Panel（含 _menuPanel 私有字段经 SerializedObject 转交）。**执行前先保存当前场景**（工具会切场景）。
- ⏳ 待验证：T5 用例（Host 关进程 → 客户端弹面板不崩 → 返回大厅 → 重新 Host/Join）。已知边界：加入失败（地址不通）也会走同一面板，文案为「房间已解散」略有出入，测试项目可接受。

---

### [~] M7 定时生成（P1-3）

**目标**：Host 每 15s 随机位置生成一球，数量受控（**上限 4 个，2026-10-02 定案**）。

工作项：`BallSpawner` 用 Tick 计数（`interval = 15 * TimeManager.TickRate`，禁 Update/协程）；服务器 `System.Random`；XZ 随机、Y=2；上限 4 球，超限 Despawn 最旧。
**完成定义（T6，按 4 球上限改写）**：挂机 45s 场上共 4 个球（开局 1 + 15/30/45s 各 1），按 15s 间隔出现；**第 5 个球出现（60s）时最旧的消失**。

**实现已落盘（2026-10-02 by WorkBuddy，待 Unity 内验证）**：
- ✅ `PhysicsTuning.MaxBallCount` 8 → **4**；新增 `BallSpawnRandomRange = 4f`（XZ 半幅：房间半宽 10、柱子在 ±5，取 ±4 避开柱子与墙）。
- ✅ `BallSpawner` 增量扩展（不改既有行为）：新增 `SpawnBallAt(Vector3[, Quaternion])`（M7 的随机位置生成）与 `DespawnBall()`（**先发 `BallDespawned` 再真正 Despawn**，让订阅者能反订阅）+ 新事件 `BallDespawned`。
- ✅ 新增 `Ball/BallSpawnScheduler.cs`（挂 GameManager，AGENTS §8.1 新组件接入）：订阅 `TimeManager.OnTick`，`LocalTick % SpawnIntervalTicks == 0` 时随机 XZ + Y=2 生成；开局球（Tick 0）不重复生成；生成后 `TrimToMax()`——先清掉失效引用，再 while 超上限就销毁队首（最旧）。用 `List` 而非 `Queue`，因为要支持移除中间项。
- ✅ **球对象池（2026-10-02）**：M7 让球一直在上下场（15s 补一个、超 4 个淘汰最旧），等于持续 Instantiate/Destroy。改用 FishNet 自带池：`NetworkManager.GetPooledInstantiated(prefab, pos, rot, asServer)` 取（池空才 Instantiate），`ServerManager.Despawn(nob, DespawnType.Pool)` 回收；`OnStartServer` 里 `CacheObjects` 预热 `MaxBallCount` 个。
  - ⚠️ 池取出来的是**已实例化但未 Spawn** 的对象（`DefaultObjectPool.RetrieveObject` 只做 Instantiate + 摆位置 + `SetActive(true)`），`ServerManager.Spawn` 仍要自己调——**池只省实例化，不省网络同步**。
  - ⚠️ FishNet 回池只 `ResetState`（重置 NetworkObject 自身状态），**不碰 Rigidbody**：`BallSpawner.ResetForReuse` 必须清 `velocity`/`angularVelocity`，否则淘汰掉的高速球下次生成会带着旧速度飞出去；再 `WakeUp()`，否则失活前睡着的球复用后会悬在空中不下落。
  - ⚠️ 复用还带走了 `BallImpactDispatcher._lastEventTime`，已在 `OnStartNetwork`（每次 Spawn 都走，含池复用）里重置。
- ⏳ 待验证：T6（挂机 45s 数球、60s 看最旧消失）；T6b——第 5 个球出现时旧球**回池不消失实例**（Hierarchy 里失活而非销毁），且新球从生成点自由落体、不带旧速度。

---

### [~] M8 Tapped 提示（P1-4）

**目标**：被球撞到的玩家自己看到 "Tapped"，别人看不到。

工作项：Host 端球×玩家 `OnCollisionEnter` 判定 → `TargetRpc` 到该连接 → 该客户端 UI 显示。
工作项（补充，2026-10-01 更新）：踢球音效已随 M5 前置完成（BallAudioView + 足球音效 + KickerClientId，见 M5 节）。
**完成定义（T7）**：A 撞球时只有 A 显示 Tapped，B/C 无提示。

**实现已落盘（2026-10-02 by WorkBuddy，按用户定案调整）**：
- ✅ **碰到就显示、分开就失活（2026-10-02 二次改）**：砍掉原"撞击后 `_visibleSeconds = 1s` 隐藏"的倒计时——接触是持续状态，用计时器猜结束时间必然与真实接触错位（贴着球走会提前消失、被顶在墙角会一直挂着）。改为由服务器的**接触结束**事件明确收掉，UI 只剩 `SetActive(true/false)` 两个分支，无 Update、无计时。
- ✅ **单独组件（不与音效共用回调，清晰优先）**：`Ball/TappedDispatcher.cs`（挂 GameManager）+ `UI/TappedIndicator.cs`（挂 Toast Canvas，驱动 `Toast Canvas/Tapped` 节点）。
- ✅ 链路：服务器侧 `TappedDispatcher` 订阅 `BallSpawner.BallSpawned` → 给每个球挂 `BallContacted`（接触开始）/ `BallSeparated`（`OnCollisionExit`，接触结束）→ 维护「球×玩家」接触表 → `KickerClientId` 经 `ServerManager.Clients` 找连接 → `[TargetRpc] RpcSetTapped(bool)` 只发给被碰玩家 → 本机翻成 `Tapped` / `Released` 事件。
- ⚠️ **为什么接触状态不能复用 `BallImpacted`**：撞击广播带 0.2s 节流（压音效鬼畜），被吞掉的那次撞击就点不亮 Tapped。所以 `BallImpactDispatcher` 另开一对**不节流**的 `BallContacted` / `BallSeparated`，只走本地服务器侧、不广播。
- ⚠️ **为什么用"接触对"列表而不是计数器**：挤压时 `OnCollisionEnter/Exit` 会来回抖动，按 (球, 玩家) 去重不会把同一次接触算两次，也能容忍漏掉的 Enter。球被销毁 / 玩家掉线都会主动清表，避免 Tapped 永远亮着。
- ⚠️ **为什么不让 UI 直接订阅球**：球是运行时生成的，场景里的 Tapped UI 拿不到它的引用；经常驻场景对象 GameManager 中转，UI 才能在场景里稳定序列化订阅（同理 M5 的 RoomAnnouncer 也挂在 GameManager 上）。
- ⚠️ **滚动声 / 撞墙声不做**（2026-10-02 定案：球多起来声音太乱）。`BallAudioView` 只保留踢球音效。
- ⏳ 待验证：T7（A 撞球只有 A 的屏幕显示 Tapped；B/C 无提示）；T7b 新增——**球离开后立刻失活**（贴着球走时保持显示，走开即消失）。

---

### [~] M8b 输入焦点与「退出游戏」（2026-10-04 新增，local 状态已落盘待验证）

**目标**：ESC 把鼠标还给玩家（视角停住）、点画面回到对局；「退出游戏」客户端回大厅 / 房主解散房间。

**根因（排查结论）**：
- ⚠️ **ESC 从来没被处理过**：`CancelPressed` 的唯一订阅者是 `Player/PlayerMotor.cs`（M1 冻结类），而 Player 预制体上只有 `PlayerPredictedMotor` + `PlayerInputReader` + `PlayerIdentity` + `PlayerCamera`，**没有 PlayerMotor** → 那段代码是死代码。用户在编辑器里看到"鼠标出现"是 **Unity 编辑器自带的 ESC 解锁光标**，打包后不会出现，视角采样也没停 → "鼠标出来了但视角还在动"。
- ⚠️ 「退出游戏」按钮接线本身没问题：Leave Button 是 Menu Canvas 的直接子物体（**不是** Menu Panel 的子物体，不会被 `_menuPanel.SetActive(false)` 一起藏掉）、Canvas 有 GraphicRaycaster、Button `m_Interactable: 1`、EventSystem 用的是 `InputSystemUIInputModule`。真正的问题是**没有任何代码在 ESC 后把 `Cursor.lockState` 交还给 UI**（`PlayerCamera.OnStartClient` 锁上后再没人解锁），于是按钮"看得见点不动"。

**实现已落盘（待 Unity 内验证）**：
- ✅ 新增 `Core/InputFocusMode.cs`（`Menu` / `Gameplay` 两态）+ `Core/InputFocus.cs`（场景级单例，Boot 场景新增 `Input Focus` 根节点）：
  - ESC → `Menu`（单向：只交还鼠标，回对局靠点画面，避免反复横跳）；点画面（且**指针不在 UI 上**）→ `Gameplay`。
  - `GameplayAllowed` 闸门由 UI 层按联机状态设置，**没有它会在大厅里点一下画面就把光标锁死，菜单按钮全点不到**。
  - 焦点一变，光标状态（`Cursor.lockState` / `Cursor.visible`）与视角采样同时切换——两者必须同源，这是本节点存在的全部理由。
  - 用 InputSystem 设备 API 直读（`Keyboard.current` / `Mouse.current`），不用 InputAction：本组件必须在玩家生成之前就能响应，不能依赖 `PlayerInputReader` 的生命周期。
- ✅ `Player/PlayerInputReader.cs`（改）：`ReadMove` / `ReadLook` 在菜单焦点下返回零 → **视角不采样就不累积 yaw，回到对局时也不会跳视角**。
- ✅ `UI/MainMenuUI.cs`（改）：`OnLeaveClicked` 由 `StopNetwork()` 改为 **`ReturnToBoot()`**——Room 是叠加加载的全局场景，只断连接不卸载会残留在主菜单背后；房主走同一条路，`ServerManager.StopConnection` 即解散房间，Room 内其余客户端经 `HostConnectionLost` 弹「房间已解散」。状态变化时同步焦点（先开 `GameplayAllowed` 闸、再 `SetMode`，顺序反了会被闸门挡掉）。
- ✅ `Core/SphereRoom.Core.asmdef`（改）：补 `Unity.InputSystem` + `UnityEngine.UI`（`EventSystem.current.IsPointerOverGameObject()` 需要）两个引用。
- ⏳ 待验证（local / Tugboat，ParrelSync 双开）：① 进房后按 ESC → 鼠标出现、移动鼠标视角不动；② 点画面 → 鼠标隐藏、视角恢复且不跳变；③ 点「退出游戏」→ 客户端回大厅（Room 卸载）、房主端房间解散且对方弹「房间已解散」面板 → 「返回大厅」可用；④ 大厅里点空白处**不会**把光标锁死。
- 📌 遗留：`Player/PlayerMotor.cs`（M1 冻结类，含唯一的 `CancelPressed` 订阅）已是死代码，建议在 M10 自查时删除（连同预制体上可能残留的引用一起核对）。

---

### [~] M9 Steam 联机（P2-1，必做，最后做）—— 代码与包已就位，待 Editor 内跑一次接线工具 + 两设备联调

**目标**：一个 Build 同时支持 LAN 与 Steam，好友可经 Steam 邀请入房。

**⚠️ 两个文档错误已修正（2026-10-04）**：
1. `AGENTS.md §3/§5.8` 的 **`FirstGearGames/SteamworksSockets?path=/Assets/SteamworksSockets` 是 404，仓库根本不存在**，包名也是错的。FishNet 官方 Steam transport 的真名是 **`FishySteamworks`**（`com.firstgeargames.fishysteamworks`，v4.1.1，出处：FishNet 主仓库 README）。
2. **`TransportMultiplexer` 是错名**，包内真实类是 **`Multipass`**（`Runtime/Transporting/Transports/Multipass/Multipass.cs`，菜单 `FishNet/Transport/Multipass`）。

**传输方案定案（2026-10-04）：用 Multipass 并存，不用编译宏、也不替换 Tugboat。**
- 服务器侧 `GlobalServerActions = true`（默认）→ `StartConnection(server)` 遍历所有 transport 全部监听（Multipass.cs:826-851）→ 一局同时收 LAN 与 Steam 客户端。
- 客户端侧必须先 `SetClientTransport`（Multipass.cs:64-89、846-849），重载支持按**基类 `Transport` 引用**指定（:613）→ Network 层只持有基类引用，**不必引用 Steam 包程序集**，包没装也能编译。
- 连接 Id 由 Multipass 映射成 `MultipassId` 后交上层 → `ServerManager`/`ClientManager` 用法不变，**M0-M8 玩法代码零改动**（正好兑现「LAN-first 接入时游戏代码零改动」）。
- **决定性依据**：FishySteamworks 官方 README「Testing Two Builds Locally」原文——Steam 不允许两个 build 自连，**单机两开测试必须用默认 transport**。替换掉 Tugboat 就等于废掉 ParrelSync 回路，M2/M4 的双人同 Tick 撞球用例再也没法回归。

工作项：装 FishySteamworks（**unitypackage 方式**，非标准 UPM 布局）+ Steamworks.NET → `Multipass` 挂 Tugboat + FishySteamworks → Host 建 Lobby → Overlay 邀请 → 从 Lobby 取 Host SteamID 发起 P2P；`steam_appid.txt = 480`（项目根 + Build 目录）。
**完成定义（T8）**：两台设备经 Steam Overlay 邀请联机成功。
**前提/限制**：M4 已通过；单 Steam 账号无法自连 P2P，需**两个 Steam 账号 + 两台设备**（账号已具备，第二台设备待确认）。日常开发回路仍用 ParrelSync + LAN，Steam 只在 M9 集中联调。

**邀请的接收语义（2026-10-04 对齐，M9 实现时必须覆盖）**：
- 被邀请方**不需要先在大厅、也不需要先在游戏里**——但**必须有一条路能接住邀请**，二选一都要写：
  1. **对方游戏没在跑**：Steam 用 `steam://run/<appid>//<connect_string>` 拉起游戏，connect string 走**命令行**传给新进程 → 游戏启动时必须解析命令行（Steamworks.NET 的 `SteamApps.GetLaunchCommandLine()`，或直接 `System.Environment.GetCommandLineArgs()`）并据此直连。
  2. **对方游戏已经在跑**：Steam 不会新开进程，而是把邀请送给**现有进程**的回调（Lobby 邀请 = `GameLobbyJoinRequested_t { m_steamIDLobby, m_steamIDFriend }`；好友「加入游戏」= `GameRichPresenceJoinRequested_t`）→ 必须在进程内**注册并处理这些回调**，否则点了"加入"毫无反应（最常见的"邀请没反应"就是漏了这条）。
- ⚠️ FishySteamworks 的"地址"就是 **Host 的 SteamID64**（官方 README：填 transport 的 Client Address，或 `ClientManager.StartConnection(steamId64)`）。**具体 API 以包内实际类型为准，不要凭记忆写**。
- ⚠️ **编译宏的正确用途只有一个**：把 Steamworks.NET 的 Steam API 调用（Lobby 创建、邀请回调、`SteamAPI.Init`）包在 `#if !DISABLESTEAMWORKS` 里，保证没装 Steamworks.NET 的机器也能编译。**不要用宏去区分 LAN/Steam 两套传输**——那是运行时的 `SetClientTransport`。
- ⚠️ 两台机器都要登录 Steam 且当前账号能运行 AppID 480（否则 P2P 建不起来）。

**实现记录（2026-10-04）**：

包安装（都放 `Assets/Plugins/`，随 git 走，评审方可离线编译）：
- `Assets/Plugins/FishySteamworks/`（v4.1.1，来自 `FirstGearGames/FishySteamworks` 的 `FishNet/Plugins/FishySteamworks`；已删掉自带的 `SteamManager.unitypackage`）。**无 asmdef → 编译进 `Assembly-CSharp-firstpass`**（顶层 `Plugins` 是 Unity 的 firstpass 特殊目录）。
- `Assets/Plugins/Steamworks.NET/`（`com.rlabrecque.steamworks.net` 2025.165.0，自带 asmdef，GUID `68bd7fdb68ef2684e982e8a9825b18a5`，`autoReferenced=true` → firstpass 与 `SphereRoom.Steam` 都能直接用 `Steamworks` 命名空间）。
- 项目根新增 `steam_appid.txt`（内容 `480`）。**Build 目录也要放一份，M10 打包时补**（或手工拷）。

新增文件：
| 文件 | 职责 |
|------|------|
| `Scripts/Steam/SphereRoom.Steam.asmdef` | 独立程序集，隔离 Steam 依赖 |
| `Scripts/Steam/SteamBootstrap.cs` | `SteamAPI.Init()` / 每帧 `RunCallbacks()` / `Shutdown()`。**必须自己写**：FishySteamworks 内部 `InitializeRelayNetworkAccess()` 是 try/catch 静默失败，没有 Init 会表现为"能进房间但连不上" |
| `Scripts/Steam/SteamLobbyInvite.cs` | 纯发现层：建 Lobby → Overlay 邀请；接住 4 条接收路径 |
| `Scripts/Network/TransportKind.cs` | `Lan=0` / `Steam=1` |
| `Scripts/Network/TransportSelector.cs` | **零类型依赖**：字段只用 FishNet 基类 `Transport`，所以 Network 程序集不需要引用 Steam 包 |
| `Scripts/Core/Editor/SphereRoomSteamSetup.cs` | 菜单 `SphereRoom/Setup/M9 Steam 接线`（priority 11），幂等接线 |

修改：`NetworkBootstrap`（新增 `_transportSelector` / `JoinGame(addr,kind)` / `JoinViaSteam(ulong)` / `TrySelectTransport`；`StartHost` 仍选 LAN 传输——**主机进程内的本地客户端不能走 Steam**，Steam 禁止自连 P2P）、`MainMenuUI`（新增 `_steamInviteButton` / `_steamInvite`，仅在"已联机 且 `SteamBootstrap.IsReady`"时显形）、`SphereRoom.UI.asmdef` 与 `SphereRoom.Core.Editor.asmdef`（加 `SphereRoom.Steam` 引用）。

**邀请的 4 条接收路径（`SteamLobbyInvite` 全覆盖）**：
| 场景 | 入口 | 处理 |
|------|------|------|
| 对方在游戏中 · Lobby 邀请 | `GameLobbyJoinRequested_t` | `JoinLobby` → `LobbyEnter_t` → `GetLobbyOwner` → 连 Host |
| 对方在游戏中 · 好友列表「加入游戏」 | `GameRichPresenceJoinRequested_t` | parse connect → 连 Host |
| 对方未启动 · Lobby 邀请 | 命令行 `+connect_lobby <lobbyid>` | 同上（**长键必须先匹配**：`+connect_lobby` 的前 8 字符就是 `+connect`） |
| 对方未启动 · Rich Presence | 命令行 `+connect <steamid64>` | 直连 Host |

配套在 Lobby 建好后 `SetRichPresence("connect", 本地SteamID)`，否则好友列表里根本没有「加入游戏」这一项（第 2、4 条路径会一直是死的）。

**M9 运行步骤（给操作者）**：
1. 让 Editor 重编译一次（关掉 Play 模式 / `Ctrl+R`）。首次会编译 `FishySteamworks`（无 asmdef → `Assembly-CSharp-firstpass`）与 `Steamworks.NET`。Console 必须 0 error。
2. 菜单 **`SphereRoom/Setup/M9 Steam 接线（Multipass + FishySteamworks + 邀请）`** 点一次（幂等），它会自动：
   - NetworkManager 上加 `FishySteamworks`（`_peerToPeer = true`）；
   - 加 `Multipass`，`_transports = [Tugboat, FishySteamworks]`；
   - `TransportManager.Transport` 指向 `Multipass`；
   - 加 `TransportSelector` 并接到 `NetworkBootstrap._transportSelector`；
   - 加 `SteamBootstrap` / `SteamLobbyInvite`（后者接到 `_bootstrap`）；
   - 主菜单左上角「退出游戏」下方生成「邀请 Steam 好友」按钮并接到 `MainMenuUI`；
   - 保存 Boot 场景。
3. **先做 LAN 回归**（Tugboat 没被移除，ParrelSync 回路还在）：双开 → Host/Join → 跑 M2/M4 的双人同 Tick 撞球用例，确认 Multipass 接线没有破坏原有同步。
4. **再做 Steam 两设备联调**：两台设备各登录**不同** Steam 账号、Steam 客户端在线、账号有 AppID 480 运行权限；A 建房间 → 点「邀请 Steam 好友」→ Overlay 选 B → B 接受。
   - B 游戏没开 → Steam 拉起进程并带 `+connect_lobby <id>` → 自动进房；
   - B 游戏已开 → `GameLobbyJoinRequested_t` 回调 → 自动进房。
   - 日志看 `[Steam] 接受邀请，连接 Host SteamID = …`；连不上先查 `SteamAPI.Init()` 是否成功、两边 `steam_appid.txt` 是否存在。
5. **已知限制**：单设备两开**无法**测 Steam（Steam 禁止自连 P2P，FishySteamworks README 明示）→ 本机回路一律用 Tugboat。打包时 `Build/` 目录也要放 `steam_appid.txt`（M10 补）。

---

### [ ] M10 交付打包（DoD）

工作项：
- Windows x64 Build 输出到 `Build/`（含 `steam_appid.txt`）。
- `README.md`（≤1 页）：运行步骤、网络选型理由、双人同 Tick 撞球机制简述、Steam 测试步骤、已知限制。
- 按 `CODING_STANDARDS.md` 第 8 节清单逐条自查。
- `git log` 检查：每个节点有提交、无 squash。
- 提交物：仓库链接 + Build + README（邮件标题【姓名--技术测试】）。

---

## 4. 附录：与原始文档编号的对应

| 本清单 | DEVELOPMENT_PLAN 里程碑 | AGENTS 任务 | 测试矩阵 |
|--------|------------------------|-------------|----------|
| M0 | M0 环境 | T1 | — |
| M1 | M1 联机骨架 | T2 | T1 |
| M2 | M2 前半 | T3 | T2 |
| M3 + M4 | M2 后半 | T4 | **T3** |
| M5 | M3 | T5 | T4 |
| M6 | M3 | T6 | T5 |
| M7 | M3 | T7 | T6 |
| M8 | M3 | T8 | T7 |
| M9 | M4 | T9 | T8 |
| M10 | M5 | T10 | T9 稳定性 |

时间预算：M0 0.5h / M1 1h / M2 1h / M3+M4 1.5h / M5–M8 共 1h / M9 1h / M10 0.5h（合计约 6.5h）。
超时预案：优先压缩打磨类工作（UI/美术，不评分）；M5–M9 **均不裁**（Steam 已定级必做，只允许换实现方式）。
