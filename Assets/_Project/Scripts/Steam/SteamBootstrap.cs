using Steamworks;
using UnityEngine;

namespace SphereRoom.Steam
{
    /// <summary>
    /// Steam 客户端生命周期：Init / 每帧 RunCallbacks / Shutdown。
    /// 为什么必须自己写：Steamworks.NET 只提供 API 封装，Steam 需要有人调用
    /// <c>SteamAPI.Init()</c> 并每帧驱动 <c>SteamAPI.RunCallbacks()</c>，否则
    /// FishySteamworks 里的 <c>SteamNetworkingUtils.InitRelayNetworkAccess()</c>
    /// 与 <c>SteamUser.GetSteamID()</c> 全部失败（它在 Initialize 里 try/catch 掉了异常，
    /// 表现为"能进房间但连不上"，很难查）。
    /// 执行侧：纯本地；不参与网络、不进 Tick（Steam 回调是每帧驱动的）。
    /// 前提：项目根（Editor）与 Build 目录都要有 steam_appid.txt = 480，且 Steam 客户端正在运行。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SteamBootstrap : MonoBehaviour
    {
        private static SteamBootstrap _instance;

        /// <summary>场景内唯一实例；未挂载时为 null（此时 Steam 能力整体不可用）。</summary>
        public static SteamBootstrap Instance => _instance;

        /// <summary>Steam 是否初始化成功。未就绪时一切 Steam 路径都必须走失败分支，不能抛异常打断主流程。</summary>
        public static bool IsReady => _instance != null && _instance._initialized;

        /// <summary>本地用户的 SteamID64；未就绪时为 0。</summary>
        public static ulong LocalSteamId => IsReady ? SteamUser.GetSteamID().m_SteamID : 0UL;

        private bool _initialized;

        // [本地 | 一次性（Awake）] 初始化 Steam。失败只记日志：没有 Steam 也要能玩 LAN。
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                enabled = false;
                return;
            }

            _instance = this;

            if (!SteamAPI.IsSteamRunning())
            {
                Debug.LogWarning("[Steam] 未检测到运行中的 Steam 客户端，Steam 联机不可用（LAN 不受影响）。");
                return;
            }

            _initialized = SteamAPI.Init();
            if (!_initialized)
            {
                Debug.LogWarning("[Steam] SteamAPI.Init() 失败：检查项目根/Build 目录是否有 steam_appid.txt（内容 480）。");
                return;
            }

            Debug.Log($"[Steam] 已初始化，本地 SteamID = {LocalSteamId}");
        }

        // [本地 | 每帧] 驱动 Steam 回调（Lobby 邀请、P2P 状态都靠它投递）。
        private void Update()
        {
            if (_initialized)
                SteamAPI.RunCallbacks();
        }

        private void OnDestroy()
        {
            if (_initialized)
            {
                SteamAPI.Shutdown();
                _initialized = false;
            }

            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationQuit()
        {
            if (_initialized)
            {
                SteamAPI.Shutdown();
                _initialized = false;
            }
        }
    }
}
