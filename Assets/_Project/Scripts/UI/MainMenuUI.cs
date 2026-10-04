using SphereRoom.Core;
using SphereRoom.Network;
using SphereRoom.Steam;
using UnityEngine;
using UnityEngine.UI;

namespace SphereRoom.UI
{
    /// <summary>
    /// 主菜单：创建房间 / 加入 / 断开 + 状态文本。
    /// 文案只在状态变化时写入，不在每帧路径上拼接字符串。
    /// </summary>
    public sealed class MainMenuUI : MonoBehaviour
    {
        [SerializeField] private NetworkBootstrap _bootstrap;
        [SerializeField] private GameObject _menuPanel;
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _leaveButton;
        [SerializeField] private Text _statusText;

        [Tooltip("M9：Steam 邀请按钮（可选）。未接线时整个 Steam 入口静默隐藏，不影响 LAN。")]
        [SerializeField] private Button _steamInviteButton;

        [Tooltip("M9：Steam 邀请服务。为空时隐藏邀请按钮。")]
        [SerializeField] private SteamLobbyInvite _steamInvite;

        private void Awake()
        {
            if (_hostButton != null)
                _hostButton.onClick.AddListener(OnHostClicked);
            if (_joinButton != null)
                _joinButton.onClick.AddListener(OnJoinClicked);
            if (_leaveButton != null)
                _leaveButton.onClick.AddListener(OnLeaveClicked);
            if (_steamInviteButton != null)
                _steamInviteButton.onClick.AddListener(OnSteamInviteClicked);
        }

        private void OnDestroy()
        {
            if (_hostButton != null)
                _hostButton.onClick.RemoveListener(OnHostClicked);
            if (_joinButton != null)
                _joinButton.onClick.RemoveListener(OnJoinClicked);
            if (_leaveButton != null)
                _leaveButton.onClick.RemoveListener(OnLeaveClicked);
            if (_steamInviteButton != null)
                _steamInviteButton.onClick.RemoveListener(OnSteamInviteClicked);
        }

        private void OnEnable()
        {
            if (_bootstrap != null)
                _bootstrap.StatusChanged += OnStatusChanged;
        }

        private void OnDisable()
        {
            if (_bootstrap != null)
                _bootstrap.StatusChanged -= OnStatusChanged;
        }

        private void OnHostClicked()
        {
            _bootstrap.StartHost();
        }

        private void OnJoinClicked()
        {
            _bootstrap.JoinGame();
        }

        // [仅本地 | 事件驱动（点击「邀请 Steam 好友」）] 建 Lobby 并弹出 Steam Overlay 邀请界面。
        // 必须在房间已经建好之后点：好友接受邀请后会立刻连本机的 Host。
        private void OnSteamInviteClicked()
        {
            if (_steamInvite != null)
                _steamInvite.CreateLobbyAndInvite();
        }

        // [仅本地 | 事件驱动（点击「退出游戏」）] 客户端与房主走同一条路，语义不同：
        // - 客户端：断开连接并卸载 Room → 回到大厅；
        // - 房主：StopConnection 会停掉服务器 = 房间解散，Room 里其余客户端收到掉线后由 RoomClosedUI 弹「房间已解散」。
        // 必须走 ReturnToBoot 而不是 StopNetwork：Room 是叠加加载的全局场景，只断连接不会卸载，
        // 残留的场景会一直渲染在主菜单背后。
        private void OnLeaveClicked()
        {
            _bootstrap.ReturnToBoot();
        }

        private void OnStatusChanged(NetworkMode mode, string text)
        {
            if (_statusText != null)
                _statusText.text = text;

            bool offline = mode == NetworkMode.Offline;
            if (_menuPanel != null)
                _menuPanel.SetActive(offline);
            if (_leaveButton != null)
                _leaveButton.gameObject.SetActive(!offline);

            // Steam 邀请只在「已联机 + Steam 真的可用」时出现：Steam 没跑时不给玩家一个点了没反应的按钮。
            if (_steamInviteButton != null)
                _steamInviteButton.gameObject.SetActive(!offline && _steamInvite != null && SteamBootstrap.IsReady);

            // 焦点跟随联机状态：进对局锁鼠标开始操作，回大厅把鼠标还给 UI。
            // 顺序有讲究：先开闸再切焦点，否则 SetMode(Gameplay) 会被 GameplayAllowed 挡掉。
            InputFocus focus = InputFocus.Instance;
            if (focus == null)
                return;

            focus.GameplayAllowed = !offline;
            focus.SetMode(offline ? InputFocusMode.Menu : InputFocusMode.Gameplay);
        }
    }
}
