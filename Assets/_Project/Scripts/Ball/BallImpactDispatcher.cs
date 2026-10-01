using System;
using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Ball
{
    /// <summary>
    /// 球体碰撞事件源：服务器判定撞击强度后广播给所有观察者，双端各自在本地派发事件供表现层订阅。
    /// 执行侧：判定 [服务器]、派发 [双端]；本类不含任何音效 / 特效实现（AGENTS §5.9）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallImpactDispatcher : NetworkBehaviour
    {
        /// <summary>撞击事件：踢中 / 撞墙 / 撞柱都会触发，载荷见 <see cref="BallImpactData"/>。</summary>
        public event Action<BallImpactData> BallImpacted;

        [Tooltip("低于该相对速度的接触不算撞击（集中定义于 PhysicsTuning.MinImpactSpeed）。")]
        [SerializeField] private float _minRelativeSpeed = PhysicsTuning.MinImpactSpeed;

        // [热路径][服务器 | 物理回调] 撞击强度达到阈值才广播；本方法内不做分配、不写字符串、不查组件。
        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServerInitialized)
                return;

            float relativeSpeed = collision.relativeVelocity.magnitude;
            if (relativeSpeed < _minRelativeSpeed)
                return;

            // 接触点优先取实际接触；拿不到时退化为球心与向上法线（保证事件仍可用）。
            Vector3 point = transform.position;
            Vector3 normal = Vector3.up;
            if (collision.contactCount > 0)
            {
                ContactPoint contact = collision.GetContact(0);
                point = contact.point;
                normal = contact.normal;
            }

            RpcBallImpacted(point, normal, relativeSpeed, TimeManager.LocalTick);
        }

        // [服务器 | 事件驱动] 广播给所有观察者（含主机自己的客户端）；双端在本地派发同一份数据。
        [ObserversRpc]
        private void RpcBallImpacted(Vector3 point, Vector3 normal, float relativeSpeed, uint tick)
        {
            BallImpacted?.Invoke(new BallImpactData(NetworkObject.ObjectId, point, normal, relativeSpeed, tick));
        }
    }
}
