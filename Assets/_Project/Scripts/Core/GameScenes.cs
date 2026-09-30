namespace SphereRoom.Core
{
    /// <summary>
    /// 场景名常量。业务代码里禁止散落场景名字符串。
    /// </summary>
    public static class GameScenes
    {
        /// <summary>主菜单场景，NetworkManager 常驻于此。</summary>
        public const string Boot = "Boot";

        /// <summary>对局场景：房间 + 共享球。</summary>
        public const string Room = "Room";
    }
}
