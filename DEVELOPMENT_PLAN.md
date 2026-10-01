# 球体房间（Sphere Room）— Unity 6 联机 Demo 工程方案

> 版本 v1.1 ｜ Unity **6000.3.14f1** ｜ FishNet 4 ｜ Host-Client（主机权威） ｜ Windows x64
> 本文档为工程实施方案，需求分级见第 1 节，全部必要功能与加分项（含 Steam 超级加分项）均有对应实现方案。

---

## 1. 需求清单

| 级别 | 功能 | 验收标准 |
|------|------|----------|
| **P0-1** | 多人联机（1-4 客户端） | Host 创建房间，其他客户端加入，主机权威，状态以 Host 为唯一数据源 |
| **P0-2** | 共享物理球体 | 玩家可推动；撞障碍真实弹回；**双人同 Tick 同时撞击不出现严重不同步/错位** |
| **P0-3** | 第一人称视角 | FPS 移动 + 鼠标视角 |
| **P1-1** | 中途加入同步 | 中途加入的玩家正确同步球体当前**位置与速度** |
| **P1-2** | 主机退出处理 | 主机断开时客户端有合理表现或提示，不崩溃 |
| **P1-3** | 定时生成 | Host 控制，每 15s 随机位置生成一个球 |
| **P1-4** | 碰触提示 | 玩家触球时 UI 显示 "Tapped" |
| **P2-1（已定级为必做）** | Steam 联机 | Steam Transport/Relay（App ID 480），好友/房间邀请，README 写明测试步骤 |

不投入项：美术/贴图/UI 打磨/特效（不评分）。素材一律 Cube + 内置 Sphere + uGUI 原型。

---

## 2. 技术选型（结论）

| 项 | 选择 | 一句话理由 |
|----|------|-----------|
| 网络框架 | **FishNet 4** | 唯一内置"预测→和解→回滚重演→图形平滑"完整管线且支持非玩家共享刚体的主流框架，直接命中 P0-2 评分核心；Mirror/NGO 无内置预测，双人争球必现拉扯回跳 |
| LAN 传输 | **Tugboat**（UDP + 可靠通道） | 物理同步是高频小包、最新状态优先，UDP 免队头阻塞；Tugboat 自带可靠层覆盖事件消息，不裸 UDP、不自造轮子 |
| Steam 传输 | **SteamworksSockets**（ISteamNetworkingSockets，UDP/SDR） | FishNet 官方配套，P2P + NAT 穿透全由 Steam 解决 |
| 拓扑 | **Host-Client**（listen server） | 题目指定主机权威；无专用服务器、无 Host Migration（FishNet 不支持；P1-2 用"提示+返回菜单"满足） |
| 渲染管线 | **URP** | Unity 6 默认模板，选定后不切换 |
| TickRate | **50 Hz**，`fixedDeltaTime = 0.02s` | 1 Tick = 1 物理步，完全对齐，回滚重演语义最干净 |
| API Level | .NET Standard 2.1（默认） | — |

---

## 3. 联机流程（直接加入式 / 原神式）

```
客户端A(菜单) ──"创建房间"──▶ 启动 Server+Client(同进程) ──▶ 加载 Room，开始游戏
                                                        │ 监听中（Tugboat UDP 7770 / Steam P2P）
客户端B/C/D(菜单) ──"加入"──────────────────────────────┘
     │  入口1(LAN)：输入 Host IP 直连
     └─ 入口2(Steam)：Steam 好友邀请 → Overlay 接受 → 从 Lobby 取 Host SteamID → P2P 连接
                        ↓
        任意时刻可加入（含对局进行中）：场景同步 → Spawn 玩家 → reconcile 同步球状态
                        ↓
        Host 退出 → 全房间结束：客户端弹窗"主机已断开" → 返回 Boot 菜单
```

- **无自建大厅服务器、无匹配系统、无专用游戏服务器**。"组队"语义由 Steam Lobby 承担（仅作发现/邀请层，存 Host 的 SteamID；游戏数据全部走 Host⇄Client P2P）；
- **Steam 接入采用分层策略（LAN-first）**：M1-M3 全部跑 Tugboat LAN（ParrelSync 双开 Editor，分钟级迭代）；连接入口收敛在 Boot 菜单与 TransportMultiplexer，M4 一次性接 Steam（Lobby + P2P），游戏逻辑零改动。原因：物理同步是评分核心需要最快迭代回路；Steam P2P 联调需两台设备/两个 Steam 账号（单账号单机无法自连 P2P），回路慢，放最后；
- 若需要"大厅组队 → 统一开局"的流程体验：等价实现 = Steam Lobby 当大厅（好友组队、Host 点开局），开局动作只是 Host 启动 listen server + 队友从 Lobby 取 Host 地址发起连接。自建大厅服务器的全部职责（房间列表、组队状态、Host 地址分发）Steam Lobby 均免费覆盖，且自建大厅不解决游戏数据的 NAT 穿透，本项目不做；
- LAN 路径无 Steam 依赖，直连 IP 即入；
- 中途加入是流程的一等公民（P1-1），不是特殊分支。

---

## 4. 系统架构设计

### 4.1 场景结构

```
Scenes/
├── Boot.unity        # NetworkManager(DontDestroyOnLoad)、主菜单 UI（Host/Join、IP、Steam）
└── Room.unity        # 房间（地板+墙+障碍柱）、GameManager、玩家生成点
```

- 菜单 Host → `ServerManager.StartConnection() + ClientManager.StartConnection()` → 加载 Room → 玩家 Spawn；
- 菜单 Join → Client 连接 → 场景由 Host 加载同步（FishNet 场景管理）。

### 4.2 Prefab 与网络对象清单

| Prefab | 组件 | 网络语义 |
|--------|------|----------|
| `Player` | `NetworkObject`、`Rigidbody`+`CapsuleCollider`、`PredictionRigidbody`、`PlayerInputReader`、`PlayerMotor`、`PlayerCamera`(仅 Owner)、`PlayerTappedUI` | **预测对象**：客户端预测，服务器和解 |
| `SharedBall` | `NetworkObject`、`Rigidbody`+`SphereCollider`、`PredictionRigidbody`、`BallPrediction`、`BallImpactDispatcher`（碰撞事件源） | **共享刚体**：服务器权威模拟，客户端预测 + reconcile 修正 |
| `GameManager` | `NetworkObject`、`BallSpawner`、`TappedDispatcher` | Host 单例，服务器逻辑：定时生成、Tapped 判定 |

**Prefab 层级纪律（逻辑根 + Graphic 子物体）**：所有网络 Prefab 结构固定为——

```
Player (逻辑根: NetworkObject / Rigidbody / PredictionRigidbody / 各网络脚本)
└── Graphic (图形子物体: Capsule 原型 MeshRenderer / 换皮只动这里)
SharedBall (逻辑根: 同上)
└── Graphic (Sphere 原型 MeshRenderer)
```

- 图形子物体挂 Renderer，逻辑根只挂网络/物理组件——**这层分离同时服务于预测回滚（图形平滑）与后期换资源**；
- **原型素材即最终形态默认**：玩家 = 内置 Capsule 原型 + 服务器分配玩家色（`playerIndex` SyncVar → 4 色板），球 = 内置 Sphere 原型 + 彩色材质，房间 = Cube 拼地板/墙/柱。零外部资源依赖，评审方开箱即跑；
- **后期加美术资源（可选，不评分，永不优先）**：只替换 Graphic 子物体的 Mesh/Material/Texture（球换足球同理），逻辑根与全部代码零改动。因层级纪律已保证，此项不设里程碑、随时可做。两条硬要求：Graphic 缩放从 `PhysicsTuning.BallRadius` 推导（换 Mesh 不改物理尺寸）；图形平滑同时覆盖位置与旋转（否则有纹路的足球滚动会看着"打滑"）；
- **表现层扩展点（音频等）**：离散的碰撞/踢中事件由服务器判定后 `ObserversRpc` 广播，表现层订阅；连续的滚动声由同步速度在本地驱动、不占网络事件。契约与落地节点见 `AGENTS.md` §5.9。

### 4.3 状态同步策略总表

| 状态 | 同步方式 | 说明 |
|------|----------|------|
| 玩家移动 | Replicate（输入）+ Reconcile（状态） | 输入每 Tick 上行；服务器重演；回传和解 |
| 球体物理 | Reconcile-only（Rigidbody 状态） | 服务器模拟，客户端预测 + 回滚修正，`PredictionRigidbody` 图形平滑 |
| 远程玩家朝向 | 低频 SyncVar（yaw） | 视觉用；第一人称相机不同步 |
| 球的生成/销毁 | 服务器 Instantiate + Spawn / Despawn | 可靠通道，事件语义 |
| Tapped 提示 | TargetRpc（服务器→被碰玩家） | 服务器权威碰撞判定 |
| 球体碰撞/踢中事件 | ObserversRpc（服务器判定后广播） | 表现层（音效/特效）订阅；滚动声不占网络，由同步速度本地驱动 |
| 计时器（15s） | 不同步 | 纯服务器内部 Tick 计数 |
| 房间人数 | 服务器 SyncVar | 菜单/HUD 显示用 |

### 4.4 核心难点：共享物理球的双人同 Tick 撞击

1. 所有玩家输入带 Tick 编号上行到 Host；
2. Host 在 Tick N 收齐（或超时补齐）所有在线玩家输入后，按确定顺序重演物理（同 Tick 同输入集合 → 服务器结果唯一）；
3. Host 回传 Tick N 和解状态（每个玩家 + 每个球的 Rigidbody 位置/速度/角速度）；
4. 客户端偏差超阈值 → 回滚到服务器状态 → 顺序重放本地输入到当前 Tick；
5. 逻辑体与图形体分离：图形由 `PredictionRigidbody` 平滑器驱动，回滚不造成画面瞬移；
6. 效果：双人同 Tick 撞球，双端轨迹一致（服务器唯一真相），预测错误在一两个 Tick 内无感修正。

**实现参照**：FishNet 官方 `Demos/Prediction/Rigidbody`（场景 `Rigidbody Prediction Demo.unity`）——以该示例为骨架改造，不自己发明。

### 4.5 输入与移动模型

- 输入结构（每 Tick 采集）：`MoveInput { Vector2 Move; float Yaw; }`（Pitch 只作用本地相机，不同步）；
- 玩家 Rigidbody + `PredictionRigidbody`，直接设置 velocity，不做惯性；
- 推球 = 玩家胶囊体物理碰撞的自然结果，**不做射线/按键推力**。

---

## 5. 功能实现方案

### P0-1 多人联机
- `Boot` 场景 `NetworkManager`：Tugboat（UDP，端口 7770）；
- Host = Server+Client 双启动；Join = 输 IP 直连（Steam 路径见 P2-1）；
- 玩家 Spawn：`PlayerSpawnManager`（Host 端，按连接 ID 生成于出生点）。

### P0-2 共享物理球
- 见 4.4；物理材质 `Bounciness 0.55~0.7 / Friction 0.4`；
- 墙体/障碍静态 Collider，3~4 根柱子保证弹回可验证。
- 换皮预留：球换足球只改 `Graphic` 子物体的 Mesh/Material/Texture；缩放由 `PhysicsTuning.BallRadius` 推导，平滑需覆盖旋转；
- 音频预留：踢中/撞击走 `BallImpactDispatcher`（服务器判定 + `ObserversRpc`）事件，音效在 M8 作为订阅者接入；滚动声由同步速度本地驱动。

### P0-3 第一人称
- `PlayerCamera` 仅 Owner 启用；Yaw 作用玩家本体、Pitch 作用相机 pivot；
- `Cursor.lockState = Locked`，ESC 释放。

### P1-1 中途加入同步
- reconcile 流自动携带球 Rigidbody 状态；验证用例：球滚动中 C 加入，无瞬移、速度方向正确。

### P1-2 主机退出处理
- 客户端订阅 `ClientManager.OnClientDisconnectState`：弹窗 → 返回 Boot → Reset NetworkManager；
- README 说明不做 Host Migration 的取舍。

### P1-3 定时生成
- Host 端 `BallSpawner`：Tick 计数（禁 Update 计时），每 `15 × TickRate` Tick 生成一球，服务器 `System.Random`，XZ 随机、Y=2m；
- 上限 8 球，超限 Despawn 最旧（README 说明，防无限增长）。

### P1-4 碰触提示
- Host 端 `OnCollisionEnter`（球×玩家）→ `TargetRpc` → 被碰玩家 UI 显示 "Tapped"（2s 淡出）；判定只在服务器。

### P2-1 Steam 联机
- `TransportMultiplexer`：Tugboat + SteamworksSockets 并存（一个 Build 同时支持 LAN 与 Steam）；
- App ID **480**：项目根与 Build 目录各放 `steam_appid.txt`（内容 `480`）；
- 邀请闭环：Host 创建 Steam Lobby → Overlay 好友邀请 → 接受后以 Lobby 内 SteamID 发起 P2P 连接（Lobby 仅作发现层，见第 3 节）；
- README 测试步骤（必写）：
  1. 两台设备/两个 Steam 账号，均登录 Steam 客户端；
  2. Build 目录含 `steam_appid.txt`；
  3. Host → Overlay 邀请 → 对方接受 → 入房；
  4. 同机限制说明：Steam P2P 需两个不同 SteamID，单机双开不可行时用 ParrelSync 测 LAN，Steam 路径两台设备验证。

---

## 6. 里程碑（总预算 3-6h，超出按优先级裁剪）

| 里程碑 | 内容 | 预算 | 出口标准 |
|--------|------|------|----------|
| **M0 环境** | 6000.3.14f1 + URP 模板 + FishNet + ParrelSync；目录/asmdef；Git | 0.5h | ParrelSync 双开，空场景 Host/Join 成功 |
| **M1 联机骨架** | Boot/Room 场景、NetworkManager、玩家 Spawn、FPS 移动（非预测版） | 1h | 双客户端互见对方移动 |
| **M2 物理球（核心）** | SharedBall 预测 + reconcile；玩家预测移动；弹回 | 1.5h | **双人同 Tick 对撞无严重错位** |
| **M3 加分项四连** | 中途加入 / 主机退出 / 定时生成 / Tapped | 1h | 各验收点通过 |
| **M4 Steam（P2-1）** | SteamworksSockets + Multiplexer + Lobby 邀请闭环（LAN-first 分层接入，见第 3 节） | 1h | 两台设备经 Steam 联机成功 |
| **M5 打磨提交** | Windows Build、README、规范自查、Git 历史检查 | 0.5h | 按题目要求可提交 |

> 裁剪顺序（仅作超时应急，不做计划）：球上限优化 → UI/美术（不评分）；**M0-M4 绝不裁**（Steam 已定级必做）。美术资源替换不占里程碑（见 4.2 Prefab 层级纪律）。

---

## 7. 依赖包清单

Unity 版本：**6000.3.14f1**。

| 包 | 用途 | 安装 |
|----|------|------|
| **FishNet 4** | 网络框架（预测/和解/RPC/Spawn） | git URL：`https://github.com/FirstGearGames/FishNet.git?path=/Assets/FishNet#4.7.3`（锁 tag 保证评审可复现） |
| **SteamworksSockets** | Steam transport（M4 引入，必做） | git URL：`https://github.com/FirstGearGames/SteamworksSockets.git?path=/Assets/SteamworksSockets`（URL 变动以 FishNet 官方文档 Transport 章为准） |
| **FishySteamworks** | Steam transport 备选 | `https://github.com/FirstGearGames/FishySteamworks.git?path=/Assets/FishySteamworks` |
| **ParrelSync** | 同机多开 Editor 测试 | `https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync#1.5.3`（原 JoinGame 仓库已不存在） |
| URP | 渲染管线 | Unity 6 模板自带 |

- `steam_appid.txt`（一行 `480`）：项目根（Editor）+ Build 输出目录。

---

## 8. 测试矩阵

| # | 用例 | 步骤 | 通过标准 |
|---|------|------|----------|
| T1 | 基础联机 | ParrelSync 双开 Host/Join | 互见对方，1-4 人均测 |
| T2 | 推球 | A 推球撞柱 | 真实弹回，B 端轨迹一致 |
| T3 | **双人同 Tick 对撞** | 两对侧同时冲撞同一球 ×10 | 双端落点/速度一致，无瞬移无严重错位 |
| T4 | 中途加入 | 球滚动中 C 加入 | C 端位置与速度正确 |
| T5 | 主机退出 | Host 关进程 | 客户端弹提示可返回，不崩溃 |
| T6 | 定时生成 | 挂机 45s | 3 球按 15s 间隔随机出现 |
| T7 | Tapped | 玩家撞球 | 该玩家显示 Tapped，他人不显示 |
| T8 | Steam | 两设备 Overlay 邀请 | 全流程 Steam transport 可用 |
| T9 | 稳定性 | 4 客户端 + 8 球 5 分钟 | 帧率稳定，无内存增长，断线正常 |

性能红线：热路径遵守 `CODING_STANDARDS.md`；Build 后 Profiler 抽查稳态 0 GC/帧。

---

## 9. 提交物清单

1. GitHub 仓库，完整提交历史（**严禁 squash**，粒度按里程碑）；
2. `Build/` Windows x64 包（Steam 路径含 `steam_appid.txt`）；
3. `README.md`（≤1 页）：运行步骤、**网络选型理由**（FishNet + 预测和解回滚 + UDP 系 transport）、双人同 Tick 撞球同步机制简述、Steam 测试步骤、已知限制；
4. 邮件：GitHub 链接 → `purling_fs@163.com`，标题 **【姓名--技术测试】**。

---

## 10. 风险与对策

| 风险 | 概率 | 对策 |
|------|------|------|
| FishNet 预测 API 调通超预算 | 中 | M0 先跑通官方 `Demos/Prediction/Rigidbody`，以其为骨架 |
| 双人撞球偶发偏差 | 中 | 平滑参数调优；TickRate/物理步严格对齐；README 说明以服务器和解为准 |
| Steam transport URL/版本变动 | 低 | 以官方文档为准；备选 FishySteamworks；P2-1 已定级必做，只替换实现方式、不裁剪功能 |
| 时间不足 | — | 按第 6 节裁剪顺序，M0-M4 不裁 |

---

## 附：文档索引

- `DEVELOPMENT_PLAN.md` — 本文档（工程方案）
- `AGENTS.md` — Codex/AI Agent 工程约定与任务分解（T1-T10）
- `CODING_STANDARDS.md` — 代码规范（热路径禁令、审查清单）
