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

---

### [ ] M4 球 reconcile + 图形平滑（P0-2 核心后半，★评审核心）

**目标**：双人同一 Tick 对冲撞球，双端轨迹一致、无瞬移、无严重错位。

工作项：
- `SharedBall` 接 `PredictionRigidbody` + `BallPrediction(NetworkBehaviour)`，采用官方示例的「rigidbodies without client input」模式：客户端本地模拟 + 服务器状态 reconcile。
- 球的 Reconcile 只同步 Rigidbody 状态（位置/速度/角速度），无误判、无过度回滚。
- 图形体（Graphic 子物体）由平滑层驱动，回滚不造成画面瞬移。
- 调参集中在 `PhysicsTuning`，对齐检查顺序：TickRate/fixedDeltaTime → reconcile 频率 → 图形平滑。

**完成定义（T3 用例）**：两人从对侧同时冲撞同一球 **×10 次**，双端落点与速度一致，无瞬移、无严重错位。
**这是全项目最关键的验收点，不合格不得进入 M9。**

---

### [ ] M5 中途加入（P1-1）

**目标**：球滚动中，新客户端加入后位置与速度正确。

工作项：确认 reconcile 流在 Late Join 时携带完整 Rigidbody 状态；新客户端不做一次性瞬移修正。
**完成定义（T4）**：球滚动中 C 加入，C 端球的位置与速度方向正确，无瞬移。

---

### [ ] M6 主机退出（P1-2）

**目标**：Host 消失时客户端有明确反馈且可恢复。

工作项：订阅 `ClientManager.OnClientDisconnectState` → 弹窗「主机已断开连接」→ 返回 Boot → Reset NetworkManager → 可重新 Host/Join。
**完成定义（T5）**：Host 关进程，客户端弹提示、不崩、能返回菜单并重连。
**取舍**：不做 Host Migration，README 写明理由。

---

### [ ] M7 定时生成（P1-3）

**目标**：Host 每 15s 随机位置生成一球，数量受控。

工作项：`BallSpawner` 用 Tick 计数（`interval = 15 * TimeManager.TickRate`，禁 Update/协程）；服务器 `System.Random`；XZ 随机、Y=2；上限 8 球，超限 Despawn 最旧。
**完成定义（T6）**：挂机 45s，3 个球按 15s 间隔出现；第 9 个球出现时最旧的消失。

---

### [ ] M8 Tapped 提示（P1-4）

**目标**：被球撞到的玩家自己看到 "Tapped"，别人看不到。

工作项：Host 端球×玩家 `OnCollisionEnter` 判定 → `TargetRpc` 到该连接 → 该客户端 UI 显示 2s 后淡出。
工作项（补充）：音频表现层 `BallAudioView` 订阅 M2 预留的 `BallImpacted` 播放踢中音效；滚动声由同步速度本地驱动（不订阅网络事件，M4 起速度才准确）。音源资产到 M8 再定。
**完成定义（T7）**：A 撞球时只有 A 显示 Tapped，B/C 无提示。

---

### [ ] M9 Steam 联机（P2-1，必做，最后做）

**目标**：一个 Build 同时支持 LAN 与 Steam，好友可经 Steam 邀请入房。

工作项：装 SteamworksSockets → `TransportMultiplexer` 挂 Tugboat + Steamworks → Host 建 Lobby → Overlay 邀请 → 从 Lobby 取 Host SteamID 发起 P2P；`steam_appid.txt = 480`（项目根 + Build 目录）。
**完成定义（T8）**：两台设备经 Steam Overlay 邀请联机成功。
**前提/限制**：M4 已通过；单 Steam 账号无法自连 P2P，需**两个 Steam 账号 + 两台设备**（账号已具备，第二台设备待确认）。日常开发回路仍用 ParrelSync + LAN，Steam 只在 M9 集中联调。

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
