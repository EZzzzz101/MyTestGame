using System;
using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Ball
{
    /// <summary>
    /// 球体碰撞事件源：服务器判定撞击强度后广播给所有观察者，双端各自在本地派发事件供表现层订阅。
    /// 执行侧：判定 [服务器] + 客户端预测 [仅本地玩家踢球]；本类不含任何音效 / 特效实现（AGENTS §5.9）。
    /// M5 声音预测设计：M4 起踢球者的客户端本地就在真实模拟球（reconcile-only 预测），
    /// 若等服务器广播回传（½RTT）才出声，视觉（即时）与听觉（延迟）会打架——
    /// 因此本地玩家的踢球走 <see cref="LocalKickPredicted"/>（零延迟预测出声），
    /// 服务器广播回传的同一事件由 <see cref="IsLocallyPredictedKick"/> 跳过，不会双响；
    /// 其他人踢的球不抢跑（本地抢跑会与服务器时间线叠声），等广播统一；
    /// 主机没有预测路径（服务器即本机，广播零延迟到达），广播到达即播放。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallImpactDispatcher : NetworkBehaviour
    {
        /// <summary>撞击事件（服务器权威判定后广播）：踢中 / 撞墙 / 撞柱都会触发，载荷见 <see cref="BallImpactData"/>。</summary>
        public event Action<BallImpactData> BallImpacted;

        /// <summary>本地玩家的踢球预测事件（仅踢球者本机触发，零延迟）：表现层用它立即出声。</summary>
        public event Action<BallImpactData> LocalKickPredicted;

        /// <summary>接触开始事件（仅服务器派发，**不节流**）：球碰到某个玩家，载荷的 KickerClientId 即该玩家。
        /// 为什么不直接复用 <see cref="BallImpacted"/>：撞击广播为了压音效鬼畜做了 0.2s 节流，
        /// 被吞掉的那次撞击就不会点亮"碰到"提示——接触是持续状态，必须每次都准。</summary>
        public event Action<BallImpactData> BallContacted;

        /// <summary>接触结束事件（仅服务器派发）：球与某个玩家分开了。载荷的 KickerClientId 即该玩家；
        /// 非玩家接触（墙 / 柱 / 球撞球）不会派发。与 <see cref="BallContacted"/> 成对，M8 的 Tapped 提示用它收掉显示。</summary>
        public event Action<BallImpactData> BallSeparated;

        [Tooltip("低于该有效速度的接触不算撞击（集中定义于 PhysicsTuning.MinImpactSpeed）。")]
        [SerializeField] private float _minRelativeSpeed = PhysicsTuning.MinImpactSpeed;

        [Tooltip("同一球两次撞击事件的最小间隔（秒）。挤压（球被玩家顶在墙角）、贴墙/贴胶囊滑行、reconcile 修正回重叠后重撞，"
                 + "都会让 OnCollisionEnter 在短时间内反复触发——不节流会变成音效鬼畜。")]
        [SerializeField] private float _minEventInterval = 0.2f;

        private Rigidbody _rigidbody;

        // 上次放行事件的时间（Time.time）。服务器分支与客户端预测分支在各自进程内互斥，一个字段即可。
        private float _lastEventTime = -999f;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// [双端 | 每次 Spawn（含对象池复用）] 清掉上一次生命的节流时间戳。
        /// 球走对象池后同一个实例会反复上下场，不清的话新球开局第一次撞击可能被旧时间戳吞掉。
        /// </summary>
        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _lastEventTime = -999f;
        }

        /// <summary>
        /// 该次服务器广播的撞击是否已由本机预测播过（= 踢球者本人的客户端）——用于跳过回传、避免双响。
        /// 主机例外：主机没有走预测路径（服务器即本机），广播到达即播放。
        /// </summary>
        public bool IsLocallyPredictedKick(BallImpactData data)
        {
            return data.KickerClientId >= 0
                && !IsServerInitialized
                && LocalConnection != null
                && data.KickerClientId == LocalConnection.ClientId;
        }

        // [热路径][双端 | 物理回调] 服务器：阈值 + 节流判定后广播给所有观察者；
        // 客户端：只有本地玩家踢的球走预测事件（其他人踢的不抢跑）。
        private void OnCollisionEnter(Collision collision)
        {
            if (IsServerInitialized)
            {
                if (TryBuildImpact(collision, out int kicker, out Vector3 point, out Vector3 normal, out float speed))
                {
                    // 接触状态先落地（不节流）：表现层的"碰到就显示"不能因为音效节流被吞掉。
                    // 挤压时 Enter 会高频重入，但订阅方按 (球, 玩家) 去重，重复派发没有副作用。
                    if (kicker >= 0)
                        BallContacted?.Invoke(new BallImpactData(NetworkObject.ObjectId, point, normal, speed, TimeManager.LocalTick, kicker));

                    // 撞击广播走节流：同一球短时间内反复接触（挤压 / 滑行 / reconcile 重撞）会变音效鬼畜。
                    if (PassesEventThrottle())
                        RpcBallImpacted(point, normal, speed, TimeManager.LocalTick, kicker);
                }
            }
            else if (IsClientInitialized)
            {
                if (TryBuildImpact(collision, out int kicker, out Vector3 point, out Vector3 normal, out float speed)
                    && kicker >= 0 && LocalConnection != null && kicker == LocalConnection.ClientId
                    && PassesEventThrottle())
                {
                    LocalKickPredicted?.Invoke(new BallImpactData(NetworkObject.ObjectId, point, normal, speed, TimeManager.LocalTick, kicker));
                }
            }
        }

        /// <summary>
        /// [服务器 | 物理回调] 接触结束（球与玩家分开）：不判速度阈值、也不节流——
        /// 它不是"踢了一次"，只是接触状态从有到无，一次接触只会来一次。
        /// 客户端不派发：接触状态由服务器权威判定，UI 只需要服务器那一句。
        /// </summary>
        private void OnCollisionExit(Collision collision)
        {
            if (!IsServerInitialized)
                return;

            int kicker = ResolveKicker(collision);
            if (kicker < 0)
                return;

            BallSeparated?.Invoke(new BallImpactData(NetworkObject.ObjectId, transform.position, Vector3.up, 0f, TimeManager.LocalTick, kicker));
        }

        /// <summary>
        /// [双端 | 物理回调（事件频率）] 撞击事件节流：距上次放行不足 <see cref="_minEventInterval"/> 的一律吞掉。
        /// 物理层的"接触开始"不等于"踢了一次"——挤压 / 滑行 / reconcile 回滚重撞都会高频重触发 OnCollisionEnter
        /// （2026-10-01 实测鬼畜音根因），在事件源统一节流，双端都不受益连发。
        /// </summary>
        private bool PassesEventThrottle()
        {
            if (Time.time - _lastEventTime < _minEventInterval)
                return false;

            _lastEventTime = Time.time;
            return true;
        }

        /// <summary>[双端 | 物理回调（事件频率）] 统一的撞击判定：有效速度过阈值才算撞击，并解析接触点与踢球者。</summary>
        private bool TryBuildImpact(Collision collision, out int kickerClientId, out Vector3 point, out Vector3 normal, out float effectiveSpeed)
        {
            kickerClientId = -1;
            point = transform.position;
            normal = Vector3.up;
            effectiveSpeed = 0f;

            // 有效速度取两者最大（M5 定案）：
            // - relativeVelocity：双方刚体速度差（Dynamic 对 Dynamic 的真实判据，撞墙反弹用它）；
            // - impulse / 球质量：解算冲量换算出的速度变化。M3 起玩家是 Kinematic（velocity 恒为 0），
            //   推静止球时 relativeVelocity ≈ 0 会被阈值滤掉，但 depenetration 冲量真实存在——
            //   不补这条判据，"推球"永远低于阈值、踢球音效不会触发。
            effectiveSpeed = Mathf.Max(
                collision.relativeVelocity.magnitude,
                collision.impulse.magnitude / _rigidbody.mass);
            if (effectiveSpeed < _minRelativeSpeed)
                return false;

            // 接触点优先取实际接触；拿不到时退化为球心与向上法线（保证事件仍可用）。
            if (collision.contactCount > 0)
            {
                ContactPoint contact = collision.GetContact(0);
                point = contact.point;
                normal = contact.normal;
            }

            kickerClientId = ResolveKicker(collision);
            return true;
        }

        /// <summary>
        /// [服务器 | 物理回调（事件频率）] 解析踢球者：对方的刚体挂着 NetworkObject 且有 Owner 才算玩家踢的。
        /// 不用层判定——玩家逻辑根当前在 Default 层而非 Player 层（ PHYSICS_DESIGN §2.7 的层规划未落到预制体根上）；
        /// 静态墙 / 柱没有 attachedRigidbody，天然被排除；球撞球时对方 NetworkObject 无 Owner，同样返回 -1。
        /// 物理回调以事件频率触发（阈值之上才进来，非每 Tick），此处允许一次 GetComponent。
        /// </summary>
        private int ResolveKicker(Collision collision)
        {
            Rigidbody other = collision.collider != null ? collision.collider.attachedRigidbody : null;
            if (other == null)
                return -1;

            NetworkObject nob = other.GetComponent<NetworkObject>();
            if (nob == null || nob.Owner == null || !nob.Owner.IsValid)
                return -1;

            return nob.Owner.ClientId;
        }

        // [服务器 | 事件驱动] 广播给所有观察者（含主机自己的客户端）；双端在本地派发同一份数据。
        [ObserversRpc]
        private void RpcBallImpacted(Vector3 point, Vector3 normal, float relativeSpeed, uint tick, int kickerClientId)
        {
            BallImpacted?.Invoke(new BallImpactData(NetworkObject.ObjectId, point, normal, relativeSpeed, tick, kickerClientId));
        }
    }
}
