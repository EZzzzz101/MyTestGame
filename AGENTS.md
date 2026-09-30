# AGENTS.md — Sphere Room (Unity 6 Multiplayer Demo)

> 本文档供 AI 编码代理（Codex / Claude Code / Copilot 等）阅读。执行任何代码改动前请完整阅读本文档。
> 人类可读的完整方案见 `DEVELOPMENT_PLAN.md`；代码规范见 `CODING_STANDARDS.md`（**强制遵守**）。

## 1. 项目使命

实现一道 Unity 技术笔试题：**1-4 人 Host 模式联机 Demo（球体房间）**。核心考察点：

1. 共享物理球体：双人**同一网络 Tick 内同时撞击**同一球体时不得严重不同步或错位；
2. 主机权威：共享状态以 Host 为唯一数据源；
3. 网络方案选择与状态同步方式；
4. 代码规范性。

**不看重**（不要在这些上面花任何时间）：美术/模型/贴图/UI 打磨/视觉特效。一律使用 Cube、内置 Sphere、uGUI 原型样式。

## 2. 硬约束（不可违反）

| 约束 | 值 |
|------|----|
| Unity 版本 | **6000.3.14f1**（评审方用此版本打开，不得使用其他版本特性） |
| 渲染管线 | URP（一旦选定不得中途切换） |
| 网络框架 | **FishNet 4**（不使用 Mirror / NGO / Photon / 自研 sockets） |
| Transport | LAN：**Tugboat（UDP 系）**；Steam：**SteamworksSockets**（App ID 480） |
| 拓扑 | Host-Client（一人 Host，其他 Join），无 Dedicated Server、无 Host Migration |
| TickRate | 50 Hz；`Time.fixedDeltaTime = 0.02`（1 Tick = 1 物理步，完全对齐） |
| 目标平台 | Windows Standalone x64 |
| 代码规范 | `CODING_STANDARDS.md` 全部条款，尤其热路径禁令（禁字符串拼接/装箱/LINQ/GC 分配） |
| Git | 保留完整提交历史，**严禁 squash**；每个功能一个提交，提交信息用 Conventional Commits |

## 3. 环境与依赖

manifest.json 需包含（git URL 安装，详见 DEVELOPMENT_PLAN.md 第 7 节）：

- `FishNet`：`https://github.com/FirstGearGames/FishNet.git?path=/Assets/FishNet#4.7.3`（锁 tag，评审方可复现）
- `SteamworksSockets`（仅 M9 Steam 里程碑引入）：`https://github.com/FirstGearGames/SteamworksSockets.git?path=/Assets/SteamworksSockets`
- `ParrelSync`（编辑器多开测试）：`https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync#1.5.3`（**原 JoinGame 地址仓库已不存在，官方仓库为 VeriorPies**）

Steam 路径需要：项目根与 Build 目录各放 `steam_appid.txt`，内容为 `480`。

**API 参考优先级**：FishNet 官方文档（fish-networking.gitbook.io）> 包内 `Demos/`（尤其 `Demos/Prediction/Rigidbody`，骨架场景 `Rigidbody Prediction Demo.unity`）> 本文档。**预测代码以官方示例为骨架改造，不得凭记忆编写 API 调用**；若 API 与本文档描述有出入，以包内示例为准并在提交信息中注明。

## 4. 目录结构（强制）

```
Assets/
├── _Project/                  # 所有自制资源都在此目录下，不污染 Assets 根
│   ├── Scripts/
│   │   ├── Core/              # Bootstrap、场景加载
│   │   ├── Network/           # 网络管理、Spawn 管理
│   │   ├── Player/            # PlayerInputReader / PlayerMotor / PlayerCamera / TappedUI
│   │   ├── Ball/              # BallPrediction / BallSpawner / TappedDispatcher
│   │   └── UI/                # 菜单、HUD
│   ├── Prefabs/               # Player / SharedBall / GameManager
│   ├── Scenes/                # Boot.unity / Room.unity
│   └── Materials/
├── FishNet/                   # 第三方（不动）
└── Plugins/                   # 第三方（不动）
```

- 每个一级子目录（Core/Network/Player/Ball/UI）各建 **asmdef**：`SphereRoom.Core`、`SphereRoom.Player` 等，引用 FishNet 的 asmdef；
- 命名空间与 asmdef 名一致：`SphereRoom.Player` 等；
- 不使用 `Assets/Scripts` 平铺结构；不在第三方目录内新增文件。

## 5. 架构规则（必须遵守）

### 5.1 联机流程与权威模型
- **联机流程为"直接加入式"（无大厅服务器、无匹配系统、无专用游戏服务器）**：Host 在自己进程内启动 Server+Client（listen server）→ 加载 Room 开始游戏 → 其他客户端任意时刻加入（LAN 输 IP 直连 / Steam 好友邀请后 P2P）。Steam Lobby 仅作发现/邀请层（存 Host SteamID），游戏数据全部走 Host⇄Client P2P。**禁止**实现独立大厅服务、房间转交、Host Migration。
- **一切共享状态由 Host 计算**：球的物理、生成/销毁、碰撞判定（Tapped）、计时器；
- 客户端职责仅三项：采集输入上传（Replicate）、本地预测、接受和解（Reconcile）；
- **客户端代码禁止**：直接修改 NetworkObject 的共享状态、自行 Instantiate/Despawn 网络对象、自行判定碰撞结果并广播。

### 5.2 时间与物理
- 网络逻辑一律以 `TimeManager` Tick 驱动（订阅 `TimeManager.OnTick`/`OnPostTick`），**禁止用 `Update`/`Time.time`/协程做任何网络相关计时**；
- 物理由 FishNet `PhysicsSimulator` 手动模拟（预测要求），Physics 设置 `Simulation Mode = Script`；
- 定时生成用 Tick 计数：`interval = 15 * TimeManager.TickRate`。

### 5.3 玩家（预测对象）
- Rigidbody + CapsuleCollider + `PredictionRigidbody`；
- 输入结构体：`MoveInput { Vector2 Move; float Yaw; }`（Pitch 只作用本地相机，不同步）；
- 移动在 `[Replicate]` 方法内做，`[Reconcile]` 内回滚重放；
- 远程玩家朝向用低频 SyncVar 同步 yaw，仅用于视觉。

### 5.4 共享球（核心难点）
- Rigidbody + SphereCollider + `PredictionRigidbody` + `BallPrediction(NetworkBehaviour)`；
- 球是**非玩家共享刚体**：无本地输入，仅 `[Reconcile]` 同步 Rigidbody 状态，客户端本地模拟 + 服务器修正（对应官方示例中的"rigidbodies without client input"模式）；
- 推球 = 玩家胶囊体的物理碰撞，**禁止**实现成按键施力/射线（题目考察的就是碰撞同步）；
- **Prefab 层级纪律**：逻辑根（NetworkObject/Rigidbody/PredictionRigidbody/网络脚本）+ `Graphic` 子物体（MeshRenderer）。Renderer 只准挂 Graphic 子物体——同时服务预测回滚的图形平滑与后期换皮。**换美术资源只许改 Graphic 子物体的 Mesh/Material，禁止动逻辑根组件**；
- 原型素材：玩家 = Capsule 原型 + 服务器分配玩家色（`playerIndex` SyncVar → 4 色板），球 = Sphere 原型，房间 = Cube。不引入任何外部美术资源。

### 5.5 生成与销毁
- 仅 Host 端 `Instantiate` + `ServerManager.Spawn()`；销毁用 `Despawn()`（会自动同步）；
- 球上限 8 个，超限 Despawn 最旧的（README 说明该设计决策）。

### 5.6 Tapped 提示
- Host 端 `OnCollisionEnter`（球 × 玩家）→ TargetRpc 到被碰玩家的连接 → 该客户端显示 "Tapped" 2 秒；
- 判定只在服务器发生；其他客户端不显示。

### 5.7 主机退出
- 客户端订阅 `ClientManager.OnClientDisconnectState`：弹窗"主机已断开连接"→ 返回 Boot 场景、重置 NetworkManager；
- 不实现 Host Migration（README 说明取舍）。

### 5.8 Steam（最后做，必做）
- `TransportMultiplexer` 同时挂 Tugboat + SteamworksSockets（一个 Build 同时支持 LAN 与 Steam）；
- Lobby：Host 创建 → Steam Overlay 邀请 → 对方接受后以 Lobby 成员 SteamID 建立 P2P 连接；
- `steam_appid.txt = 480`。

## 6. 任务分解（按顺序执行，每项含验收标准）

> 完成一项勾一项并提交一次 git commit。不要跳序，不要一次做完再提交。

- [ ] **T1 环境初始化**：Unity 6000.3.14f1 URP 项目、安装 FishNet/ParrelSync、建目录与 asmdef、Git init。
  验收：项目编译 0 error；ParrelSync 可克隆实例。
- [ ] **T2 联机骨架**：Boot/Room 场景、NetworkManager（Tugboat）、Host/Join 菜单、玩家 Spawn（非预测版移动即可）。
  验收：双开 Editor，A Host B Join，互见对方移动。
- [ ] **T3 房间与球（非预测）**：房间（墙+地+3 柱）、SharedBall 生成、物理材质弹回。
  验收：推动球撞柱真实弹回，另一端可见（允许拉扯，下一步修复）。
- [ ] **T4 预测与和解（核心）**：玩家改 PredictionRigidbody 移动；球接 reconcile-only 预测；图形平滑。
  验收：**双人对冲同 Tick 撞球 ×10 次，无瞬移、无严重错位**（T3 用例）。这是全项目最关键验收点。
- [ ] **T5 中途加入**：验证滚动中的球对新客户端位置/速度正确。
- [ ] **T6 主机退出**：客户端提示 + 返回菜单 + 可重连。
- [ ] **T7 定时生成**：Host Tick 计数每 15s 随机生成，上限 8。
- [ ] **T8 Tapped**：服务器判定 + TargetRpc + UI。
- [ ] **T9 Steam（P2-1 必做）**：SteamworksSockets + Multiplexer + Lobby 邀请。
  前提：T4 物理核心验收通过后再做（LAN-first：游戏逻辑与 Steam 层解耦，接入时游戏代码零改动）。
  验收：两台设备经 Steam Overlay 联机成功。同机限制：单 Steam 账号无法自连 P2P，Steam 联调需两设备/两账号；日常开发回路用 ParrelSync + Tugboat LAN。
- [ ] **T10 提交打包**：Windows Build、README（选型理由/运行步骤/Steam 测试步骤/已知限制）、按 CODING_STANDARDS 自查、Git 历史检查（无 squash）。

## 7. 关键禁令（违反即返工，完整版见 CODING_STANDARDS.md）

- ❌ `Update`/`FixedUpdate`/`OnTick`/网络回调内：字符串拼接（`+`、`$""`、`string.Format`、`.ToString()`）、装箱拆箱、LINQ、闭包/lambda 捕获、`new` 引用类型、`GetComponent`/`Find*`/`Camera.main`、`Debug.Log`；
- ❌ 用 `Time.time`/协程做网络计时；
- ❌ 客户端直接改共享状态、客户端 Spawn 网络对象；
- ❌ 把渲染 Mesh 直接挂在被回滚的逻辑刚体上；
- ❌ **修改 PROGRESS.md 已勾选节点的核心类来实现新功能**——走新增类/组件/事件扩展（开闭原则，CODING_STANDARDS §8.1）；
- ❌ 网络方法缺「执行侧 + 触发时机」注释、热路径方法缺 `[热路径]` 标注；提交死代码注释 / 无主 TODO；
- ❌ 新增 static 单例（NetworkManager 除外）、跨 asmdef 反向引用、UI 直接写玩法状态；
- ❌ 一次性 git squash 提交（评审要看开发过程）。

命名与注释细节见 CODING_STANDARDS §6/§7（布尔 `Is/Has/Can` 前缀、方法动词开头、网络数据结构 `Input/Data/State` 后缀、类头中文 `<summary>` 等）。

## 8. 完成定义（DoD）

1. `DEVELOPMENT_PLAN.md` 第 8 节测试矩阵 T1-T9 全通过（T8 Steam 已定级必做）；
2. 代码通过 CODING_STANDARDS 审查清单；
3. Windows Build 可运行，`Build/` 目录就位；
4. README ≤ 1 页，含网络选型理由；
5. Git 历史保留每个任务节点。

## 9. 给代理的执行提示

- 修改前先读对应模块现有代码；生成新脚本时同步更新 asmdef 引用；
- 所有 FishNet API 调用先对照包内 `Demos/`（预测部分见 `Demos/Prediction/Rigidbody`）确认签名，不确定就打开包源码读，不要猜；
- 物理调参（弹性/摩擦/平滑）集中放在一个 `PhysicsTuning` 常量类，便于迭代；
- 遇到预测不同步问题，优先检查：TickRate 与 fixedDeltaTime 是否对齐、reconcile 频率、图形平滑设置，而不是先怀疑网络。
- **节点收尾走检查点流程**：见 `.codex/skills/sphere-room-checkpoint/SKILL.md` —— 对照节点完成定义验收 → 按 `CODING_STANDARDS.md` §10 自查 → 勾选 `PROGRESS.md` 并追加完工提交记录 → 独立提交（不 squash）。Agent 判断某个节点完工时，应主动提醒用户执行该检查点。
