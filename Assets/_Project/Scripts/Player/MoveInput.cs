using FishNet.Object.Prediction;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 玩家上行输入（每 Tick 一份）：<c>Move</c> 为 0-1 摇杆域，<c>Yaw</c> 为绝对朝向（度）。
    /// 执行侧：Owner 采集并上行，服务器与 Owner 用同一份数据重演（PHYSICS_DESIGN.md §2.4）。
    /// </summary>
    public struct MoveInput : IReplicateData
    {
        public MoveInput(Vector2 move, float yaw)
        {
            Move = move;
            Yaw = yaw;
            _tick = 0;
        }

        /// <summary>水平输入方向（0-1 摇杆域）。</summary>
        public Vector2 Move;

        /// <summary>绝对朝向（度）；Pitch 只作用本地相机，不进本结构体。</summary>
        public float Yaw;

        /// <summary>Tick 由 FishNet 在运行时写入，不需要手动赋值。</summary>
        private uint _tick;

        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }
}
