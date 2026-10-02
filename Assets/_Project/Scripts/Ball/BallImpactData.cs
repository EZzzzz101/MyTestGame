using UnityEngine;

namespace SphereRoom.Ball
{
    /// <summary>
    /// 球体碰撞事件载荷：表现层（音效 / 特效 / UI）订阅用。
    /// 执行侧：服务器判定后广播，双端都会在本地派发同一份数据；本结构只读，不参与玩法逻辑（AGENTS §5.9）。
    /// </summary>
    public struct BallImpactData
    {
        /// <summary>发生碰撞的球（NetworkObject.ObjectId），用于区分场景里的多个球。</summary>
        public int BallObjectId;

        /// <summary>接触点（世界坐标）。</summary>
        public Vector3 Point;

        /// <summary>接触法线（世界坐标）。</summary>
        public Vector3 Normal;

        /// <summary>碰撞时的相对速度大小（米/秒），可作为音量 / 强度分级依据。</summary>
        public float RelativeSpeed;

        /// <summary>发生碰撞的网络 Tick。</summary>
        public uint Tick;

        /// <summary>踢球者编号（-1 = 非玩家接触：撞墙 / 撞柱 / 球撞球）。表现层用它区分踢球与撞击，并排除踢球者本人。</summary>
        public int KickerClientId;

        public BallImpactData(int ballObjectId, Vector3 point, Vector3 normal, float relativeSpeed, uint tick, int kickerClientId)
        {
            BallObjectId = ballObjectId;
            Point = point;
            Normal = normal;
            RelativeSpeed = relativeSpeed;
            Tick = tick;
            KickerClientId = kickerClientId;
        }
    }
}
