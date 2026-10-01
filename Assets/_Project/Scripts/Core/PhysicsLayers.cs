namespace SphereRoom.Core
{
    /// <summary>
    /// 物理层索引集中处，与 ProjectSettings/TagManager.asset 的层名、Physics 碰撞矩阵一一对应
    /// （见 PHYSICS_DESIGN.md §2.7）。
    /// 层名：World(6) 静态几何 / Player(7) 玩家 / Ball(8) 共享球。
    /// </summary>
    public static class PhysicsLayers
    {
        /// <summary>静态世界几何（地板 / 墙 / 柱）。玩家位移解算只查这一层。</summary>
        public const int World = 6;

        /// <summary>玩家胶囊。</summary>
        public const int Player = 7;

        /// <summary>共享球。</summary>
        public const int Ball = 8;
    }
}
