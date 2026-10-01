namespace SphereRoom.Core
{
    /// <summary>
    /// 物理层索引集中处，与 ProjectSettings/TagManager.asset 的层名、Physics 碰撞矩阵一一对应
    /// （见 PHYSICS_DESIGN.md §2.7）。
    /// 层名：World(6) 静态几何 / Player(7) 玩家 / Ball(8) 共享球 / LocalPlayerBody(9) 本地玩家自己的胶囊视觉。
    /// </summary>
    public static class PhysicsLayers
    {
        /// <summary>静态世界几何（地板 / 墙 / 柱）。玩家位移解算只查这一层。</summary>
        public const int World = 6;

        /// <summary>玩家胶囊。</summary>
        public const int Player = 7;

        /// <summary>共享球。</summary>
        public const int Ball = 8;

        /// <summary>
        /// 本地玩家自己的胶囊视觉层（仅渲染用途，无碰撞体）：
        /// Owner 在运行时把自己的 Graphic 挪到这一层，本地相机从 CullingMask 剔除它（第一人称不看自己）；
        /// Scene 视图不受 CullingMask 影响，照常可见（便于调试）；
        /// 其他客户端上同一玩家的 Graphic 仍在 Default 层，照常渲染。
        /// </summary>
        public const int LocalPlayerBody = 9;
    }
}
