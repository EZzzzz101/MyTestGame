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

        /// <summary>场景内球数量上限，超限销毁最旧的。</summary>
        public const int MaxBallCount = 8;

        /// <summary>定时生成间隔（Tick 数）：15 秒 @ 当前 TickRate。</summary>
        public const int SpawnIntervalTicks = 15 * TickRate;
    }
}
