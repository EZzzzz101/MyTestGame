using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

namespace SphereRoom.Ball
{
    /// <summary>
    /// 共享球的 reconcile-only 预测（M4，PHYSICS_DESIGN.md §2.6 / DEVELOPMENT_PLAN.md §4.4）。
    /// 球是非玩家共享刚体：没有本地输入，服务器是唯一控制者。
    /// 工作方式：双端每 Tick 由 TimeManager 步进物理（PhysicsMode.TimeManager）本地模拟球的刚体；
    /// 服务器每 Tick 末构建 reconcile（Rigidbody 完整状态），经状态转发（NetworkObject._enableStateForwarding）
    /// 发给所有观察者；客户端收到后写回状态，与本地模拟的偏差由 Graphic 平滑层（PredictionSmoother）抹平。
    /// 效果：非主机玩家推球当场有物理反应（本地模拟），服务器 reconcile 只做静默修正。
    /// 执行侧：双端；[Reconcile] 由服务器发送、客户端应用（客户端自建的副本仅作丢包兜底）。
    /// 骨架：包内 Demos/Prediction/Rigidbody（PredictionRigidbody 进 ReconcileData 的写法）。
    /// 注意（2026-10-01 ILPP 报错修复）：FishNet Codegen 硬性要求预测方法成对——只有 [Reconcile] 会编译报错
    /// "must contain both a [Replicate] and [Reconcile] method"。因此保留一个空 [Replicate]：
    /// 输入结构体为空载荷，方法体留空（物理由 TimeManager 统一步进，无脚本施力）。
    /// 它同时驱动 FishNet 的 replicate 队列/历史推进，使回滚重放与 reconcile 的 Tick 对齐机制正常工作。
    /// </summary>
    public sealed class BallPrediction : TickNetworkBehaviour
    {
        /// <summary>
        /// [网络数据] 空输入：球没有本地输入（服务器是唯一控制者），载荷仅剩 Tick 计数。
        /// </summary>
        public struct NoInput : IReplicateData
        {
            private uint _tick;

            public void Dispose() { }

            public uint GetTick() => _tick;

            public void SetTick(uint value) => _tick = value;
        }

        /// <summary>
        /// [网络数据] 球的完整刚体状态（位置/旋转/速度/角速度，由 PredictionRigidbody 自定义序列化）。
        /// </summary>
        public struct BallReconcileData : IReconcileData
        {
            /// <summary>球的 PredictionRigidbody（序列化时携带完整 Rigidbody 状态）。</summary>
            public PredictionRigidbody Ball;

            private uint _tick;

            public BallReconcileData(PredictionRigidbody ball)
            {
                Ball = ball;
                _tick = 0;
            }

            // PredictionRigidbody 配合预测使用时自动走对象池，无需手动 Dispose（官方示例同法）。
            public void Dispose() { }

            public uint GetTick() => _tick;

            public void SetTick(uint value) => _tick = value;
        }

        private PredictionRigidbody _ball = new();

        private void Awake()
        {
            _ball.Initialize(GetComponent<Rigidbody>());
        }

        // [双端 | 一次性] 声明回调：Tick 内推进空的 replicate 队列/历史，PostTick 构建和解。
        public override void OnStartNetwork()
        {
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        // [双端 | Tick 驱动] 推进 replicate（空输入，见 Move 的说明）。
        protected override void TimeManager_OnTick()
        {
            Move(default);
        }

        // [双端 | Tick 驱动（PostTick）] 物理步之后构建和解。
        // 服务器经状态转发发给所有观察者；客户端也自建一份作为丢包兜底
        // （FishNet 只在收到服务器 reconcile 包时才真正和解，本地副本不会覆盖服务器状态）。
        protected override void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        /// <summary>[双端] 构建和解数据并调用 [Reconcile] 方法（官方示例同法）。</summary>
        public override void CreateReconcile()
        {
            Reconcile(new BallReconcileData(_ball));
        }

        // [双端 | Tick 驱动] 空 Replicate：满足 FishNet Codegen 的预测方法成对要求（见类头注释）。
        // 服务器执行并转发空输入给观察者，客户端用它推进本地预测副本与重放历史——
        // 方法体本身无事可做（被动刚体，物理由 TimeManager 步进），但这个调用链不可省。
        [Replicate]
        private void Move(NoInput md, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
        }

        // [双端 | 事件（收到服务器和解包）] 写回球的刚体状态（含清空本地待施力队列）；
        // 与本地模拟的偏差由 Graphic 子物体的平滑器掩盖，避免画面瞬移。
        [Reconcile]
        private void Reconcile(BallReconcileData rd, Channel channel = Channel.Unreliable)
        {
            _ball.Reconcile(rd.Ball);
        }
    }
}
