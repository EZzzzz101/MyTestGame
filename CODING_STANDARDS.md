# Sphere Room — 代码规范（CODING STANDARDS）

> 本规范为**强制标准**，适用于本项目全部 C# 代码。CI/代码审查/Agent 生成代码均以此为准。
> 核心目标：**热路径 0 GC 分配、网络状态一致性、面向维护的结构纪律、可读可查的过程历史**（对应笔试评分项"整个项目代码的规范性"）。

---

## 1. 术语：热路径（Hot Path）

以下代码视为热路径，执行第 3 节全部禁令：

- `Update()`、`LateUpdate()`、`FixedUpdate()`
- 网络 Tick 回调：`TimeManager.OnTick` / `OnPostTick`、`[Replicate]` / `[Reconcile]` 方法体
- 任何 RPC 方法体（ServerRpc/ObserversRpc/TargetRpc）、`OnSpawn`/`OnDespawn`、SyncVar 回调
- `OnCollision*` / `OnTrigger*` 物理回调
- `OnGUI()`（每帧多次调用）

---

## 2. 总原则

1. **分配前置**：所有引用类型分配发生在 `Awake`/`OnEnable`/`OnStartNetwork` 等一次性回调；
2. **状态显式**：可变状态收拢到字段，不依赖局部链式临时对象；
3. **服务器权威**：任何共享状态的写操作只能发生在服务器侧（Host）逻辑；
4. **可读性优先于炫技**：原型项目，直白的 if/for 优于任何抽象。

---

## 3. 热路径禁令清单（逐条含替代方案）

### 3.1 禁止：字符串拼接与字符串生成
```csharp
// ❌ 禁止（热路径内）
string s = "Score: " + score;
string s2 = $"Tick {tick}";
string s3 = string.Format("{0}", v);
string s4 = someInt.ToString();
Debug.Log($"vel={rb.velocity}");
```
**替代**：
- UI 文本只在**值变化时**更新（脏标记），拼接发生在事件驱动的一次性路径；
- 调试用字符串统一走 `[System.Diagnostics.Conditional("UNITY_EDITOR")]` 或 `DEBUG_LOG` 开关封装的 Logger，Build 中编译剔除；
- 确需高频拼接（如计分板）使用复用字段 `StringBuilder`，仅 `if (sb.Length != 0) sb.Clear()`。

### 3.2 禁止：装箱 / 拆箱（Boxing / Unboxing）
```csharp
// ❌ 装箱
object boxed = someStruct;            // 值类型 → object
dict[enumKey]                          // 非枚举优化的旧式 Hashtable
listOfObjects.Add(123);                // ArrayList / List<object> 存值类型
interfaceRef.Call(myStruct);           // 调用未加 where T : struct 泛型约束的接口
event Action<object>(123);             // object 参数事件传值类型
// ❌ 拆箱
int v = (int)objRef;
```
**替代**：
- 集合一律泛型：`List<int>`、`Dictionary<int, BallState>`；
- 泛型方法加约束：`void Send<T>(T data) where T : struct, IReplicateData`；
- 事件用强类型委托 `Action<int>` 而非 `Action<object>`；
- 禁用 `ArrayList`/`Hashtable`（遗留类型，本项目出现即审查不通过）。

### 3.3 禁止：LINQ（热路径全禁）
```csharp
// ❌
var active = balls.Where(b => b.IsActive).OrderBy(...).First();
```
**替代**：手写 for 循环 + 缓存 `Count`。LINQ 仅允许在编辑器工具/一次性初始化代码使用并注释说明。

### 3.4 禁止：闭包与 Lambda 捕获
```csharp
// ❌ 捕获局部变量 → 编译器生成闭包类 + 堆分配
someEvent += () => Process(localVar);
list.ForEach(x => Accumulate(x));
```
**替代**：改为普通成员方法（静态委托缓存 `static readonly Action` 字段）。

### 3.5 禁止：`new` 引用类型分配
```csharp
// ❌
void Update() { var state = new BallState(); ... }
void OnTick() { var input = new MoveInput(); }   // struct 装箱风险低但走池化风格更佳
```
**替代**：struct 输入声明为类字段复用；引用类型状态用**对象池**（`Queue<T>` + 预分配）。

### 3.6 禁止：反射式查找 API（热路径）
```csharp
// ❌ 每帧查找
void Update() { GetComponent<Rigidbody>().velocity = v; }
void Update() { var cam = Camera.main; }
void Update() { GameObject.Find("Ball"); }
```
**替代**：`Awake`/`OnStartNetwork` 中缓存到 `private Rigidbody _rb;` 字段。`Camera.main` 用 `Camera.main` 缓存字段或 Cinemachine 替代（Unity 6 中已缓存，但仍要求字段化统一管理）。

### 3.7 禁止：`Debug.Log` 直调（热路径）
**替代**：封装 `Log` 静态类，`[Conditional("UNITY_EDITOR")]` + 常量开关；Build 打包剥离。禁止热路径日志。

### 3.8 禁止：迭代器与 `params`
- `yield return` 方法（协程/`IEnumerable`）不得在热路径调用；
- `params object[] args` 形参不得在热路径调用（隐式数组分配 + 装箱）。

### 3.9 禁止：每帧 `Instantiate`/`Destroy`
**替代**：球与 UI 提示走对象池；网络对象生命周期仍由服务器 Spawn/Despawn 语义驱动，池仅作表示层复用。

### 3.10 禁止：产生分配的物理查询
```csharp
// ❌
Physics.OverlapSphere(...)              // 返回数组
Physics.RaycastAll(...)
// ✅
Physics.OverlapSphereNonAlloc(pos, r, _buffer)
Physics.RaycastNonAlloc(...)
```

### 3.11 禁止：foreach 对接口枚举
- `foreach` 遍历 `List<T>`/`Dictionary`（struct enumerator）允许；
- 遍历 `IEnumerable`/`IEnumerable<T>` 类型变量禁止（ GetEnumerator() 接口调用产生分配）。

---

## 4. 网络代码规范

1. **权威纪律**：共享状态（球物理、生成、碰撞判定、计时）只在 Host 侧写；客户端仅 Replicate 输入 + Reconcile 接受；
2. **计时一律 Tick**：网络逻辑用 `TimeManager.Tick`，禁止 `Time.time`/`Time.deltaTime`/协程做同步相关计时；
3. **RPC 最小化**：优先 SyncVar/自动同步，其次 TargetRpc，最后 ObserversRpc；禁止用 RPC 轮询状态；
4. **带宽纪律**：
   - 只同步需要的状态（远程玩家只同步 yaw，不同步 pitch/相机）；
   - 输入结构体按位压缩意识：`Move` 用 `Vector2` 足够，不塞冗余字段；
   - 禁止每 Tick 同步字符串；
5. **输入验证**：服务器侧对输入做合法性 clamp（速度上限、位置越界回正），客户端数据不可信；
6. **对象生命周期**：只有服务器可以 Spawn/Despawn 网络对象；客户端生成请求必须走 ServerRpc；
7. **断线路径**：所有网络事件（连接/断线）必须有 UI 反馈，禁止静默失败。

## 5. 物理规范

1. `Time.fixedDeltaTime = 0.02` 且 `TimeManager.TickRate = 50`，**任何人不得单独修改其一**（Tick-物理对齐是预测正确性的前提）；
2. 物理模拟走 `PhysicsSimulator`（Script 模式），禁止其他脚本调用 `Physics.Simulate()`；
3. **球**（预测对象）必须挂 `PredictionRigidbody`；**玩家是 Kinematic，禁止挂 `PredictionRigidbody`**（该组件靠写入 velocity 驱动，Kinematic 会忽略 velocity），玩家用位置驱动 + 自 sweep 解算（见 `PHYSICS_DESIGN.md`）；逻辑体与图形体分离，Renderer 挂图形子对象；
4. **玩家的水平位移必须经 `Physics.CapsuleCast` 自 sweep 解算后才可写入**（迭代次数固定、掩码只含 `World` 层）；**禁止**把玩家位置直接交给 PhysX 求解（Kinematic 与静态体在默认 `Contact Pairs Mode` 下不产生接触，会穿墙）；**禁止**修改 Physics 的 `Contact Pairs Mode`；
5. **物理查询的分配规则**：`Physics.CapsuleCast` / `SphereCast` / `Raycast`（单结果版本，带 `out RaycastHit`）**本身无堆分配，直接用，不要换成 `*NonAlloc`**（`CapsuleCastNonAlloc` 是给"多结果"场景的，本项目的移动解算只需要单个结果）；需要多结果时才用 `Overlap*NonAlloc` + 预分配缓冲区；
6. 物理材质参数（弹性/摩擦）与解算常量（`MoveSlideIterations` / `MoveSkinWidth` / 层掩码 等）集中在 `PhysicsTuning` 常量类，禁止 Inspector 内散落魔法数或组件内裸数字；
7. 碰撞响应逻辑（Tapped 判定）只在服务器 `OnCollisionEnter` 中处理。

## 6. 命名规范（全项目统一，Agent 生成代码逐条对照）

### 6.1 C# 标识符总表

| 元素 | 规则 | 正例 | 反例 |
|------|------|------|------|
| 类 / 结构体 / 枚举 | `PascalCase` 名词，按职责命名，**禁滥用 Manager** | `BallSpawner`、`TappedDispatcher` | `BallManager`（职责不明）、`Util` |
| 接口 | `I` + `PascalCase` | `IBallStateProvider` | — |
| 私有字段（含 `[SerializeField]`） | `_camelCase` | `_rb`、`_moveInput` | `m_rb`、`rb`（裸驼峰） |
| 公共属性 | `PascalCase`；**布尔必须 `Is`/`Has`/`Can` 前缀** | `IsHostReady`、`CanSpawn` | `ready`、`flag` |
| 常量 / `static readonly` | `PascalCase`，语义完整 | `MaxBallCount`、`SpawnIntervalTicks` | `NUM`、`_max` |
| 方法 | `PascalCase` **动词开头** | `SpawnBall`、`ApplyMovement`、`ClampInput` | `Ball()`、`DealWith()` |
| 事件 | `PascalCase` 名词短语；处理器 `On` + 事件名 | `event Action<BallState> BallSpawned` / `OnBallSpawned` | `Notify`、`Fire1` |
| 网络数据结构 | 名词 + 用途后缀：`Input`（上行输入）/ `Data`（复写数据）/ `State`（和解状态） | `MoveInput`、`BallReconcileState` | `MoveDataInfo` |
| 泛型参数 | `T` 或 `T` + 含义 | `TState` | `<type>` |

### 6.2 命名禁区

- **禁模糊词**：`temp`、`data2`、`info`、`handle`、`misc`，以及只靠数字区分的 `ball1`/`ball2`；
- **禁拼音与中文标识符**（注释用中文，标识符一律英文）；
- **缩写白名单**：仅允许 `UI`、`ID`、`RPC`、`RTT`、`GC`；短名（`_rb`、`_cam`）**仅限私有缓存字段**；
- **不用名字后缀表达网络语义**：执行侧由 FishNet attribute 与注释标注表达（见 7.2），不搞 `XxxOnServer` 这类命名。

### 6.3 Unity 资产命名

| 资产类型 | 规则 | 正例 |
|----------|------|------|
| Prefab | `PascalCase` 名词，无前缀无编号 | `Player`、`SharedBall` |
| 场景 | `PascalCase` | `Boot`、`Room` |
| 材质 | `Mat_` 前缀 + 用途 | `Mat_Player0`~`3`、`Mat_Ball` |
| 输入资产 | 已定案 | `Assets/_Project/Input/SphereRoom.inputactions` |
| 脚本资产（`.cs`） | 文件名 = 类名，一个文件一个类 | — |

### 6.4 asmdef 结构

1. `Assets/_Project/Scripts/*` 按模块划分（Core/Network/Player/Ball/UI），命名空间 `SphereRoom.<Module>`；
2. **依赖必须单向**：`Core ← Network ← Player/Ball ← UI`（UI 可引用 Player/Ball，反向禁止）；
3. 一个类一个文件，单文件 ≤ 400 行，超出即拆分；
4. `using` 置于 namespace 外（.editorconfig 统一），提交前删除未使用 using；
5. 序列化字段必须 `[SerializeField] private`，禁止 public 字段（public 走属性）。

---

## 7. 注释规范（面向维护，Agent 同样强制）

### 7.1 总则

1. **语言**：注释统一**中文**；标识符一律英文；
2. **写 why 不写 what**：解释"为什么这么做 / 有什么约束 / 踩过什么坑"，不复述代码在做什么；
3. **同步性**：改代码必须同步改注释。**过期注释比没有注释更糟**（它是错误的文档），发现即删即改；
4. **删除优于注释**：不用的代码直接删（Git 保存历史），**禁止注释掉代码后提交**。

### 7.2 必须写注释的位置

**① 类头**：一句话职责 + 网络语义。模板：

```csharp
/// <summary>
/// 球体预测同步：客户端本地模拟 + 服务器 reconcile 修正（非玩家共享刚体，无本地输入）。
/// 执行侧：双端；[Reconcile] 数据由服务器下发。
/// </summary>
```

**② public 方法**：XML `<summary>` 中文说明；参数含义不平凡时补 `<param>`。

**③ 网络相关方法**（RPC / `[Replicate]` / `[Reconcile]` / Spawn-Despawn 逻辑）：首行**必须**标注两件事——
- **执行侧**：`[服务器]` / `[客户端]` / `[仅 Owner]` / `[双端]`；
- **触发时机**：`Tick 驱动` / `事件驱动` / `一次性（OnStartNetwork 等）`。

```csharp
// [服务器 | Tick 驱动] 每 15s 随机位置生成一球，上限 8 个（见 PhysicsTuning.MaxBallCount）。
[ObserversRpc]
private void RpcBallSpawned(...)
```

**④ 热路径方法**：首行标注 `[热路径]`，表示第 3 节全部禁令适用——这是给后续维护者（含 Agent）的警示牌。

**⑤ 调参与魔法值**：写出处或依据。如 `// 0.6：弹回观感与同步稳定的折中值，集中定义于 PhysicsTuning.Bounciness`。

**⑥ 临时方案**：必须带替换节点号：

```csharp
// TODO(M4): 临时方案——NetworkTransform 近似同步；M4 替换为 reconcile-only 预测。
```

### 7.3 注释禁区

- ❌ 复述代码的废话注释（`// 设置速度` `velocity = v;`）；
- ❌ 注释掉的死代码入库；
- ❌ 与代码不同步的过期注释；
- ❌ 无主 TODO / FIXME（必须带节点号或问题编号，`// TODO:` 裸写不允许）；
- ❌ 用注释做变更记录（"xx 修改于 xx 日"——那是 Git 提交信息的职责）。

---

## 8. 面向维护的开发原则（可读性与可维护性）

> 本章目标是：**任何节点完成后，后续节点（含 M5-M8 加分项）不需要"读懂并回改"已验收的代码就能接入**。Agent 实现新需求前先通读本章。

### 8.1 开闭原则（本章核心，重点遵守）

**对扩展开放，对修改关闭**：新功能通过**新增类 / 新增组件 / 订阅已有事件**实现，而不是在已验收的核心类里加分支。

- **落地判定标准**：实现一个新需求时，修改的文件数应 ≤ 2，且**不得修改 PROGRESS.md 已勾选节点的核心类**（如 `BallPrediction`、`PlayerMotor`）。需要往里加 `if` 分支 → 违反开闭，改为：挂新组件、或核心类本来就暴露的事件/数据扩展；
- **M4 完成时必须留好 M5-M8 的扩展点**：核心逻辑类对外**只暴露事件与只读状态**（`event Action<...> BallSpawned`、`public BallState Current`），加分项模块一律写成"订阅者 + 新增类"，不回改核心；
- **反向约束（防过度设计 / YAGNI）**：禁止为想象中的需求建抽象层、接口、继承体系。只对**排期上确定会扩展**的点做抽象（本项目 = M5-M8 加分项、后期换皮），其余直接写死。原型项目的过度抽象和Copy-Paste一样是维护性负债。

### 8.2 单一职责

- 一个类只做一件事，**网络同步 / 玩法判定 / 表现与 UI 三层分离**（项目内范例：`BallPrediction` 只管同步，`TappedDispatcher` 只管判定，`PlayerTappedUI` 只管显示）；
- 违反信号：类名出现 `And`、泛化的 `Manager`/`Processor`；单文件 > 400 行；方法 > 50 行。出现即重构拆分。

### 8.3 组合优于继承

- MonoBehaviour 行为用**挂组件组合**表达，继承深度 ≤ 1；
- 禁止"为了复写一个方法"建基类——拆成两个组件。

### 8.4 显式依赖与解耦

- 依赖必须显式：`[SerializeField]` 或方法参数传入；**全项目唯一允许的静态单例是 NetworkManager**（FishNet 生态），其他类禁止 `static Instance`；
- **跨模块用事件解耦**：核心逻辑发布事件（`BallSpawned`、`HostDisconnected`），表现层订阅；**UI 层禁止直接写玩法状态**，反向只读；
- asmdef 依赖方向见 6.4，反向引用编译期就会被拦截，不允许为绕过而把类挪到 Core。

### 8.5 DRY 与性能的取舍

- 同一段逻辑出现**第 3 次**必须抽取；
- **例外**：热路径为了 0 GC 分配，允许保留手写展开的重复代码（如多处 for 循环代替 LINQ），必须注释说明"为避免分配未抽取"——性能优先于 DRY，且让维护者知道这是有意为之。

### 8.6 可读性基础

- 方法 ≤ 50 行（不含注释）；嵌套 ≤ 3 层，超出用 **early return** 或提取方法；
- **禁止魔法数**，一律命名常量（集中 `PhysicsTuning`）；
- **公共 API 禁止 bool 参数**（调用处不可读）：拆成两个方法或用枚举；
- 一行一个声明、一行一个语句；三元运算符只用于简单赋值；
- 长布尔表达式提取为命名良好的局部变量或方法（`bool shouldRespawn = IsBelowLimit && HasFreeSlot;`）。

---

## 9. Git 提交规范

1. **保留过程**：每个功能/修复独立提交，**严禁 squash / rebase 压缩历史**（评审明确要看提交记录）；
2. Conventional Commits：`feat(player): 预测移动` / `fix(ball): reconcile 抖动` / `chore(build): 打包配置`；
3. 不提交：`Library/`、`Temp/`、`Logs/`、`Build/` 中间产物（.gitignore 用 Unity 官方模板；**最终交付时 Build/ 目录需要随仓库或单独提供，以题目要求为准**）；
4. 提交前编译 0 error 0 warning（新增代码不得引入 warning）。

## 10. 审查清单（Checklist）

每次提交前自查 / 评审方按此打分：

**性能与网络**
- [ ] 热路径（Update/FixedUpdate/OnTick/RPC/物理回调）内无：字符串拼接、装箱拆箱、LINQ、闭包、`new` 引用类型、`GetComponent`/`Find*`、`Debug.Log`、`params`、接口 foreach；
- [ ] 多结果物理查询用 `Overlap*NonAlloc` + 预分配缓冲区；单结果查询用 `CapsuleCast`/`SphereCast`（零分配，勿套 NonAlloc）；
- [ ] 网络计时全部基于 Tick；
- [ ] 共享状态只由服务器写入；输入在服务器侧被验证/clamp；
- [ ] 对象池覆盖高频生成/销毁路径；TickRate/fixedDeltaTime 未被单独改动。

**命名与注释**
- [ ] 命名符合 6.1 总表：布尔 `Is`/`Has`/`Can`、方法动词开头、网络数据结构后缀（Input/Data/State）；无模糊词/拼音/数字后缀/Manager 滥用；
- [ ] 新增 public 类/方法有中文 `<summary>`；网络方法标注执行侧 + 触发时机；热路径方法有 `[热路径]` 标注；
- [ ] 临时方案 TODO 带节点号；无死代码注释、无过期注释、无无主 TODO。

**结构与维护性**
- [ ] 开闭原则：新需求通过新增类/组件/事件接入，未修改已验收节点核心类（如必须修改，提交信息中说明理由）；
- [ ] 依赖单向（6.4），无新增 static 单例（NetworkManager 除外）；UI 未直接写玩法状态；
- [ ] 方法 ≤ 50 行、嵌套 ≤ 3 层、无魔法数、公共 API 无 bool 参数；
- [ ] 新类有 asmdef 归属与命名空间；单文件 ≤ 400 行。

**提交质量**
- [ ] `PhysicsTuning` 无散落魔法数；
- [ ] 无编译 warning；
- [ ] 提交信息符合规范且未被压缩。
