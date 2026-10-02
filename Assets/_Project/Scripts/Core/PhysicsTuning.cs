namespace SphereRoom.Core
{
    /// <summary>
    /// 物理与手感调参集中处。禁止在 Inspector 或业务代码里散落魔法数。
    /// </summary>
    public static class PhysicsTuning
    {
        // ---- 时间：TickRate 与 fixedDeltaTime 必须一一对应，任何人不得单独修改 ----

        /// <summary>网络 Tick 频率（Hz），与 Time.fixedDeltaTime 严格对齐。</summary>
        public const int TickRate = 50;

        /// <summary>物理步长，1 Tick = 1 物理步。</summary>
        public const float FixedDeltaTime = 0.02f;

        // ---- 玩家 ----

        /// <summary>水平移动速度（米/秒）。</summary>
        public const float PlayerMoveSpeed = 6f;

        /// <summary>视角灵敏度（度/像素）。</summary>
        public const float PlayerLookSensitivity = 0.12f;

        /// <summary>俯仰角上下限（度）。</summary>
        public const float PlayerPitchLimit = 85f;

        /// <summary>胶囊碰撞体高度。</summary>
        public const float PlayerHeight = 1.8f;

        /// <summary>胶囊碰撞体半径。</summary>
        public const float PlayerRadius = 0.4f;

        /// <summary>相机相对脚底的高度（眼高）。</summary>
        public const float PlayerEyeHeight = 1.6f;

        /// <summary>出生点相对地面的高度。</summary>
        public const float PlayerSpawnHeight = 1.2f;

        /// <summary>下落速度上限（避免高处掉落穿透地面），米/秒。</summary>
        public const float MaxFallSpeed = -20f;

        // ---- 共享球与房间（M2 起使用）----

        /// <summary>球与墙面/柱子的弹性（0.65：弹回观感与同步稳定的折中值）。</summary>
        public const float Bounciness = 0.65f;

        /// <summary>球与墙面/柱子的摩擦。</summary>
        public const float Friction = 0.4f;

        /// <summary>球半径。</summary>
        public const float BallRadius = 0.6f;

        /// <summary>球生成高度。</summary>
        public const float BallSpawnHeight = 2f;

        /// <summary>球的质量（比玩家重一些，推起来有惯性但不会被一碰就飞）。</summary>
        public const float BallMass = 2f;

        /// <summary>球的线性阻尼（0.05：滚得久但不停不下来）。</summary>
        public const float BallDrag = 0.05f;

        /// <summary>球的角阻尼。</summary>
        public const float BallAngularDrag = 0.05f;

        /// <summary>低于该相对速度的接触不算撞击（避免球静置贴墙时反复触发事件），米/秒。</summary>
        public const float MinImpactSpeed = 1.5f;

        /// <summary>场景内球数量上限，超限销毁最旧的（M7 定案：4 个）。</summary>
        public const int MaxBallCount = 4;

        /// <summary>定时生成间隔（Tick 数）：15 秒 @ 当前 TickRate。</summary>
        public const int SpawnIntervalTicks = 15 * TickRate;

        /// <summary>
        /// 定时生成的随机 XZ 半幅（米）：房间半宽 10、柱子在 ±5，取 ±4 可同时避开柱子和墙
        /// （球半径 0.6，落点与柱子中心至少差 1 米，不会卡在柱子里）。
        /// </summary>
        public const float BallSpawnRandomRange = 4f;

        // ---- 玩家移动解算（PHYSICS_DESIGN.md §2.8）----

        /// <summary>解算位移时的贴墙滑行迭代次数（固定值 = 确定性，不要改成动态次数）。</summary>
        public const int MoveSlideIterations = 3;

        /// <summary>小于该长度的剩余位移直接丢弃（米）。</summary>
        public const float MoveEpsilon = 0.001f;

        /// <summary>贴墙留缝，避免下次 Cast 起点落在碰撞面上返回 distance = 0（米）。</summary>
        public const float MoveSkinWidth = 0.01f;

        /// <summary>胶囊两个球心距原点的高度（= 高度/2 − 半径 = 0.9 − 0.4）。</summary>
        public const float CapsuleHalfSegment = 0.5f;

        /// <summary>胶囊中心到脚底的距离（= 高度/2）。</summary>
        public const float CapsuleHalfHeight = 0.9f;

        /// <summary>地面探测起点高度（米）。</summary>
        public const float GroundProbeStartY = 2f;

        /// <summary>地面探测球半径相对玩家半径的比例。</summary>
        public const float GroundProbeRadiusRatio = 0.9f;

        /// <summary>地面探测最大距离（米）。</summary>
        public const float GroundProbeDistance = 4f;

        /// <summary>玩家位移解算查询的层掩码（仅静态世界层）。</summary>
        public const int WorldLayerMask = 1 << PhysicsLayers.World;

        // ---- 推球冲量（M4 视手感启用）----

        /// <summary>玩家沿接触法线的速度低于该值时不产生推球冲量（米/秒）。</summary>
        public const float MinPlayerPushSpeed = 1f;

        /// <summary>玩家速度到球冲量的转换系数。</summary>
        public const float PlayerPushFactor = 1.2f;
    }
}
