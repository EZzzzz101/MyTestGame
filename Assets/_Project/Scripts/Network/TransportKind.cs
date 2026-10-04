namespace SphereRoom.Network
{
    /// <summary>
    /// 客户端连接时使用的传输方式。选择发生在**运行时**（经 <see cref="TransportSelector"/> 切换 Multipass 的
    /// ClientTransport），不是编译期——编译宏只用来隔离 Steamworks.NET 的 API 调用，两者不要混为一谈。
    /// 服务器侧不区分：Multipass 在 <c>GlobalServerActions</c> 为 true 时同时监听所有传输。
    /// </summary>
    public enum TransportKind : byte
    {
        /// <summary>LAN：Tugboat（UDP），地址是 IPv4 字符串。</summary>
        Lan = 0,

        /// <summary>Steam：FishySteamworks（P2P over Steam relay），地址是 Host 的 SteamID64。</summary>
        Steam = 1,
    }
}
