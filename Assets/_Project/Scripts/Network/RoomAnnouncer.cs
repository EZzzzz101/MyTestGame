using System;
using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace SphereRoom.Network
{
    /// <summary>
    /// 房间玩家进出播报（M5）：服务器监听远程客户端的连接状态，把「谁进来了 / 谁走了」广播给所有客户端；
    /// 表现层（RoomToastUI）订阅 <see cref="PlayerPresenceChanged"/> 显示提示文字与音效。
    /// 执行侧：监听 [服务器]、广播 [双端]；挂在 Room 场景 GameManager 上（全局场景 NetworkObject，中途加入的客户端也会拿到本组件）。
    /// 主机自己的进出不播报：主机启动时没有任何远程观察者在线（本地客户端也尚未连接），广播无人可收；
    /// 主机退出走 M6 的 RoomClosedUI（连接断开提示），不经过本组件。
    /// 玩家编号与 PlayerIdentity 一致：取 ClientId，0 号为 Host，胶囊颜色同源。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomAnnouncer : NetworkBehaviour
    {
        /// <summary>玩家进出事件：参数为玩家编号（与 PlayerIdentity / 胶囊配色同源）与是否加入。</summary>
        public event Action<int, bool> PlayerPresenceChanged;

        // [服务器 | 一次性] 订阅远程连接状态（FishNet 4 的事件挂在 ServerManager 上，两参签名）。
        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        // [服务器 | 一次性] 反注册（服务器停止时连接参数可能已在置空途中，判空兜底）。
        public override void OnStopServer()
        {
            if (ServerManager != null)
                ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            base.OnStopServer();
        }

        // [服务器 | 事件驱动] 远程客户端连上 / 断开 → 广播编号与进出标记。
        // 用 args.ConnectionId 而非 connection.ClientId：断开路径上连接对象可能已失效，事件参数里的 Id 更稳。
        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionId < 0)
                return;

            RpcAnnounce(args.ConnectionId, args.ConnectionState == RemoteConnectionState.Started);
        }

        // [双端 | 事件驱动] 各客户端本地派发，UI 层订阅（含刚加入的玩家自己——「你进入了房间」）。
        [ObserversRpc]
        private void RpcAnnounce(int clientId, bool joined)
        {
            PlayerPresenceChanged?.Invoke(clientId, joined);
        }
    }
}
