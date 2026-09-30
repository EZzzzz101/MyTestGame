namespace SphereRoom.Network
{
    /// <summary>当前网络角色。</summary>
    public enum NetworkMode : byte
    {
        /// <summary>未联机。</summary>
        Offline = 0,

        /// <summary>自己是主机（Server + Client，同进程）。</summary>
        Host = 1,

        /// <summary>自己是客户端。</summary>
        Client = 2,
    }
}
