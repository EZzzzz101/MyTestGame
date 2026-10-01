using FishNet.Object.Prediction;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 玩家和解状态：Kinematic 玩家没有速度需要恢复，只带位置与朝向——载荷比 Dynamic 方案更小、
    /// 回滚更干净（PHYSICS_DESIGN.md §2.4）。
    /// 执行侧：服务器构建并下发，Owner 用它纠正预测；客户端也自建一份作为丢包兜底。
    /// </summary>
    public struct PlayerReconcileState : IReconcileData
    {
        public PlayerReconcileState(Vector3 position, float yaw)
        {
            Position = position;
            Yaw = yaw;
            _tick = 0;
        }

        /// <summary>权威位置。</summary>
        public Vector3 Position;

        /// <summary>权威朝向（度）。</summary>
        public float Yaw;

        /// <summary>Tick 由 FishNet 在运行时写入。</summary>
        private uint _tick;

        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }
}
