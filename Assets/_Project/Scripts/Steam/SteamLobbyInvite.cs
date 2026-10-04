using SphereRoom.Network;
using Steamworks;
using UnityEngine;

namespace SphereRoom.Steam
{
    /// <summary>
    /// Steam 的**发现 / 邀请层**：只负责"怎么找到房间、怎么把 Host 的 SteamID 递给对端"，
    /// 不碰任何玩法状态，也不决定用哪个传输（那是 <see cref="TransportSelector"/> 的事）。
    /// Lobby 只当发现层用，游戏数据走 Host⇄Client 的 P2P（AGENTS.md §5.1 定案）。
    /// 执行侧：纯本地；回调由 SteamBootstrap 每帧的 SteamAPI.RunCallbacks() 驱动。
    /// </summary>
    /// <remarks>
    /// 邀请必须覆盖**四条接收路径**，漏一条就会出现"点了加入没反应"：
    /// ① 对方游戏已在运行：
    ///    a. Lobby 邀请 → <c>GameLobbyJoinRequested_t</c>；
    ///    b. 好友列表「加入游戏」（Rich Presence）→ <c>GameRichPresenceJoinRequested_t</c>；
    /// ② 对方游戏未运行 → Steam 拉起进程，参数走**命令行**：
    ///    a. Lobby 邀请 → <c>+connect_lobby &lt;64 位 LobbyID&gt;</c>（Steamworks 官方明文，见 ISteamMatchmaking/InviteUserToLobby）；
    ///    b. Rich Presence → <c>+connect &lt;connect string&gt;</c>。
    /// 注意 <c>+connect_lobby</c> 以 <c>+connect</c> 为前缀，解析时必须**先匹配长键**，否则会把 LobbyID 当成 SteamID。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SteamLobbyInvite : MonoBehaviour
    {
        private const string ConnectLobbyKey = "+connect_lobby";
        private const string ConnectKey = "+connect";
        private const int MaxLobbyMembers = 4;
        private const uint ChatRoomEnterSuccess = 1; // EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess

        [Tooltip("联机入口：收到邀请后调用它的 JoinViaSteam。")]
        [SerializeField] private NetworkBootstrap _bootstrap;

        private CallResult<LobbyCreated_t> _lobbyCreatedResult;
        private CallResult<LobbyEnter_t> _lobbyEnterResult;
        private Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
        private Callback<GameRichPresenceJoinRequested_t> _richPresenceJoinRequested;

        private CSteamID _lobbyId;
        private bool _lobbyOwned;
        private bool _lobbyJoined;

        /// <summary>本地 SteamID64（供 UI 显示，方便手动把房间号告诉好友）。</summary>
        public static ulong LocalSteamId => SteamBootstrap.LocalSteamId;

        // [本地 | 一次性（Start）] 注册 Steam 回调 + 处理"被 Steam 拉起"时带来的命令行参数。
        // 放在 Start 而不是 Awake：SteamBootstrap.Awake 要先完成 Init，这里才能注册回调。
        private void Start()
        {
            if (!SteamBootstrap.IsReady)
                return;

            _lobbyCreatedResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEnterResult = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
            _lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);
            _richPresenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichPresenceJoinRequested);

            // 路径②：Steam 用 steam://run/&lt;appid&gt;//&lt;connect_string&gt; 拉起本进程时，参数在这里。
            TryConsumeLaunchCommandLine();
        }

        private void OnDestroy()
        {
            if (!SteamBootstrap.IsReady)
                return;

            SteamFriends.ClearRichPresence();
            if (_lobbyOwned || _lobbyJoined)
                SteamMatchmaking.LeaveLobby(_lobbyId);
        }

        /// <summary>
        /// [仅 Host | 事件驱动（UI 按钮）] 建 Lobby 并打开 Steam Overlay 邀请对话框。
        /// 必须在**服务器已经起来之后**调用：好友接受邀请后要能立刻连上 Host。
        /// </summary>
        public void CreateLobbyAndInvite()
        {
            if (!SteamBootstrap.IsReady)
            {
                Debug.LogWarning("[Steam] Steam 未就绪，无法创建 Lobby。");
                return;
            }

            SteamAPICall_t call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaxLobbyMembers);
            _lobbyCreatedResult.Set(call);
        }

        // [仅 Host | Steam 回调] Lobby 建好了 → 写 Rich Presence 让好友列表出现「加入游戏」，再弹邀请界面。
        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                Debug.LogWarning($"[Steam] 创建 Lobby 失败：{result.m_eResult}");
                return;
            }

            _lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            _lobbyOwned = true;

            // 让好友列表的「加入游戏」可用：Steam 会把 connect 原样回传给对端（GameRichPresenceJoinRequested_t / +connect）。
            SteamFriends.SetRichPresence("status", "Sphere Room");
            SteamFriends.SetRichPresence("connect", LocalSteamId.ToString());

            SteamFriends.ActivateGameOverlayInviteDialog(_lobbyId);
            Debug.Log($"[Steam] Lobby 已创建（{_lobbyId.m_SteamID}），已打开邀请界面。");
        }

        // [本地 | Steam 回调] 路径②-a 结果：JoinLobby 完成 → 拿到房主 SteamID 再发起联机。
        private void OnLobbyEntered(LobbyEnter_t result, bool ioFailure)
        {
            if (ioFailure || result.m_EChatRoomEnterResponse != ChatRoomEnterSuccess)
            {
                Debug.LogWarning($"[Steam] 加入 Lobby 失败：response={result.m_EChatRoomEnterResponse}。");
                return;
            }

            _lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            _lobbyJoined = true;

            JoinHost(SteamMatchmaking.GetLobbyOwner(_lobbyId).m_SteamID);
        }

        // [本地 | Steam 回调] 路径①-a：对方在 Overlay 里接受了 Lobby 邀请（本进程已在运行）。
        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t data)
        {
            // Lobby 里存的是 LobbyID，真正要连的是房主。
            // 必须先 JoinLobby 才能拿到 owner（不加入时 GetLobbyOwner 可能返回无效 ID）。
            SteamAPICall_t call = SteamMatchmaking.JoinLobby(data.m_steamIDLobby);
            _lobbyEnterResult.Set(call);
        }

        // [本地 | Steam 回调] 路径①-b：好友列表里点了「加入游戏」（本进程已在运行）。
        private void OnRichPresenceJoinRequested(GameRichPresenceJoinRequested_t data)
        {
            if (!ulong.TryParse(data.m_rgchConnect, out ulong hostSteamId))
            {
                Debug.LogWarning("[Steam] Rich Presence 的 connect string 不是 SteamID，已忽略。");
                return;
            }

            JoinHost(hostSteamId);
        }

        // [本地 | 一次性（Start）] 解析被 Steam 拉起时的命令行：先 +connect_lobby，再 +connect。
        // 只在启动路径走一次，不在热路径上。
        private void TryConsumeLaunchCommandLine()
        {
            SteamApps.GetLaunchCommandLine(out string commandLine, 1024);
            if (string.IsNullOrEmpty(commandLine))
                return;

            // 长键优先：+connect_lobby 的前 8 个字符就是 +connect。
            ulong lobbyId = ReadNumericArgument(commandLine, ConnectLobbyKey);
            if (lobbyId != 0UL)
            {
                SteamAPICall_t call = SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
                _lobbyEnterResult.Set(call);
                return;
            }

            ulong hostSteamId = ReadNumericArgument(commandLine, ConnectKey);
            if (hostSteamId != 0UL)
                JoinHost(hostSteamId);
        }

        /// <summary>从命令行里取 key 之后的第一个十进制数字串；取不到返回 0。</summary>
        private static ulong ReadNumericArgument(string commandLine, string key)
        {
            int keyIndex = commandLine.IndexOf(key, System.StringComparison.Ordinal);
            if (keyIndex < 0)
                return 0UL;

            int start = -1;
            int end = -1;
            for (int i = keyIndex + key.Length; i < commandLine.Length; i++)
            {
                char c = commandLine[i];
                if (c < '0' || c > '9')
                {
                    if (start >= 0)
                    {
                        end = i;
                        break;
                    }

                    continue;
                }

                if (start < 0)
                    start = i;
            }

            if (start < 0)
                return 0UL;
            if (end < 0)
                end = commandLine.Length;

            return ulong.TryParse(commandLine.Substring(start, end - start), out ulong value) ? value : 0UL;
        }

        // [本地 | 事件驱动] 统一出口：把"要连的 Host SteamID"交给联机入口，由它选 Steam 传输并发起连接。
        private void JoinHost(ulong hostSteamId)
        {
            if (hostSteamId == 0UL)
            {
                Debug.LogWarning("[Steam] 拿不到 Host 的 SteamID，已忽略本次邀请。");
                return;
            }

            if (_bootstrap == null)
            {
                Debug.LogError("[Steam] 未接线 NetworkBootstrap，无法加入。");
                return;
            }

            Debug.Log($"[Steam] 接受邀请，连接 Host SteamID = {hostSteamId}");
            _bootstrap.JoinViaSteam(hostSteamId);
        }
    }
}
