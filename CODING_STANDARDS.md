# Sphere Room — 代码规范（CODING STANDARDS）

> 本规范为**强制标准**，适用于本项目全部 C# 代码。CI/代码审查/Agent 生成代码均以此为准。
> 核心目标：**热路径 0 GC 分配、网络状态一致性、可读可查的过程历史**（对应笔试评分项"整个项目代码的规范性"）。

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
3. 预测对象（玩家、球）必须挂 `PredictionRigidbody`；逻辑体与图形体分离，Renderer 挂图形子对象；
4. 物理材质参数（弹性/摩擦）集中在 `PhysicsTuning` 常量类，禁止 Inspector 内散落魔法数；
5. 碰撞响应逻辑（Tapped 判定）只在服务器 `OnCollisionEnter` 中处理。

## 6. 结构与命名规范

1. **asmdef**：`Assets/_Project/Scripts/*` 按模块划分（Core/Network/Player/Ball/UI），命名空间 `SphereRoom.<Module>`；
2. **私有字段** `_camelCase`；公共属性/方法 `PascalCase`；常量 `PascalCase` 或 `UPPER_SNAKE`（全项目统一一种）；接口 `I` 前缀；
3. **一个类一个文件**，文件名 = 类名；单文件 ≤ 400 行，超出即拆分；
4. `using` 置于 namespace 外（.editorconfig 统一）；删除未使用 using；
5. 网络 Prefab 命名：`Player`、`SharedBall`、`GameManager`；场景：`Boot`、`Room`；
6. 禁止中文注释进入代码（Git 提交信息用中文可以，代码注释统一中文/英文选一并全项目统一——本项目选**中文注释**）；
7. 序列化字段必须带 `[SerializeField] private`，禁止 public 字段。

## 7. Git 提交规范

1. **保留过程**：每个功能/修复独立提交，**严禁 squash / rebase 压缩历史**（评审明确要看提交记录）；
2. Conventional Commits：`feat(player): 预测移动` / `fix(ball): reconcile 抖动` / `chore(build): 打包配置`；
3. 不提交：`Library/`、`Temp/`、`Logs/`、`Build/` 中间产物（.gitignore 用 Unity 官方模板；**最终交付时 Build/ 目录需要随仓库或单独提供，以题目要求为准**）；
4. 提交前编译 0 error 0 warning（新增代码不得引入 warning）。

## 8. 审查清单（Checklist）

每次提交前自查 / 评审方按此打分：

- [ ] 热路径（Update/FixedUpdate/OnTick/RPC/物理回调）内无：字符串拼接、装箱拆箱、LINQ、闭包、`new` 引用类型、`GetComponent`/`Find*`、`Debug.Log`、`params`、接口 foreach；
- [ ] 物理查询使用 NonAlloc 重载；
- [ ] 网络计时全部基于 Tick；
- [ ] 共享状态只由服务器写入；
- [ ] 输入在服务器侧被验证/clamp；
- [ ] 对象池覆盖高频生成/销毁路径；
- [ ] `PhysicsTuning` 无散落魔法数；
- [ ] TickRate/fixedDeltaTime 未被单独改动；
- [ ] 新类有 asmdef 归属与命名空间；
- [ ] 无编译 warning；
- [ ] 提交信息符合规范且未被压缩。
