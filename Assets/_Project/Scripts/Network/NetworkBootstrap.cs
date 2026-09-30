using System;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Network
{
    /// <summary>
    /// 联机入口：包装 FishNet 的连接启动/停止，以及 Boot → Room 的场景流。
    /// 只负责连接与场景切换，不承载任何玩法状态（玩法状态一律由服务器权威侧持有）。
    /// 执行侧：Host/Client 两个入口都在本地 UI 侧调用，网络事件回调由 FishNet 触发。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        [Tooltip("M1 期间用 Inspector 配置直连地址；IP 输入框在后续补齐。")]
        [SerializeField] private string _address = "127.0.0.1";

        [SerializeField] private ushort _port = 7770;

        [Tooltip("主机启动后自动加载 Room 场景（全局场景，后加入的客户端也会加载）。")]
        [SerializeField] private bool _loadRoomSceneOnHostStart = true;

        private NetworkMode _mode = NetworkMode.Offline;
        private bool _roomLoadRequested;

        /// <summary>当前网络角色。</summary>
        public NetworkMode Mode => _mode;

        /// <summary>直连地址（LAN）。</summary>
        public string Address
        {
            get => _address;
            set => _address = value;
        }

        /// <summary>网络状态变化：模式 + 面向 UI 的文案。只在事件驱动路径触发。</summary>
        public event Action<NetworkMode, string> StatusChanged;

        private void Awake()
        {
            if (_networkManager == null)
                _networkManager = GetComponent<NetworkManager>();
        }

        private void Start()
        {
            if (_networkManager == null)
            {
                ReportStatus(NetworkMode.Offline, "缺少 NetworkManager");
                return;
            }

            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;

            ReportStatus(NetworkMode.Offline, "未联机");
        }

        private void OnDestroy()
        {
            if (_networkManager == null)
                return;

            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        }

        /// <summary>创建房间：同进程启动 Server + Client（listen server），随后加载 Room。</summary>
        public void StartHost()
        {
            if (_networkManager == null || _mode != NetworkMode.Offline)
                return;

            _mode = NetworkMode.Host;
            ReportStatus(_mode, "正在启动主机…");

            // listen server：本机同时是 Server 与 Client，端口由 Inspector 配置。
            _networkManager.ServerManager.StartConnection(_port);
            _networkManager.ClientManager.StartConnection(_address, _port);
        }

        /// <summary>加入房间：用 Inspector 里配置的地址直连。</summary>
        public void JoinGame()
        {
            JoinGame(_address);
        }

        /// <summary>加入房间：指定地址直连（LAN）。</summary>
        public void JoinGame(string address)
        {
            if (_networkManager == null || _mode != NetworkMode.Offline)
                return;

            _address = address;
            _mode = NetworkMode.Client;
            ReportStatus(_mode, "正在连接主机…");

            _networkManager.ClientManager.StartConnection(address, _port);
        }

        /// <summary>断开并回到未联机状态。</summary>
        public void StopNetwork()
        {
            if (_networkManager == null)
                return;

            if (_networkManager.ClientManager.Started)
                _networkManager.ClientManager.StopConnection();
            if (_networkManager.ServerManager.Started)
                _networkManager.ServerManager.StopConnection(true);

            _roomLoadRequested = false;
            _mode = NetworkMode.Offline;
            ReportStatus(_mode, "未联机");
        }

        // [服务器 | 事件驱动（服务器连接状态变化）] 主机就绪后加载对局场景。
        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started)
                return;
            if (_mode != NetworkMode.Host)
                return;

            ReportStatus(_mode, "主机已启动，等待加入");
            LoadRoomScene();
        }

        // [客户端 | 事件驱动（客户端连接状态变化）] 维护本地联机状态与 UI 文案。
        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    ReportStatus(_mode, _mode == NetworkMode.Host ? "主机已就绪" : "已连接主机");
                    break;

                case LocalConnectionState.Stopped:
                    if (_mode == NetworkMode.Offline)
                        return;

                    _mode = NetworkMode.Offline;
                    _roomLoadRequested = false;
                    ReportStatus(_mode, "已断开连接");
                    break;
            }
        }

        /// <summary>
        /// 加载对局场景。用全局场景（而非单连接场景），后加入的客户端会自动加载同一场景，
        /// M5「中途加入」依赖这一行为。
        /// </summary>
        // [服务器 | 一次性] 只在主机启动后触发一次。
        private void LoadRoomScene()
        {
            if (!_loadRoomSceneOnHostStart || _roomLoadRequested)
                return;

            _roomLoadRequested = true;

            SceneLoadData sceneLoadData = new SceneLoadData(GameScenes.Room);
            // 叠加加载：Boot 常驻（NetworkManager 与菜单都在其中），不做替换。
            sceneLoadData.ReplaceScenes = ReplaceOption.None;
            sceneLoadData.PreferredActiveScene = new PreferredScene(new SceneLookupData(GameScenes.Room));

            _networkManager.SceneManager.LoadGlobalScenes(sceneLoadData);
        }

        private void ReportStatus(NetworkMode mode, string text)
        {
            StatusChanged?.Invoke(mode, text);
        }
    }
}
