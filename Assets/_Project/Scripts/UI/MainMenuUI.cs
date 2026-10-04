using SphereRoom.Core;
using SphereRoom.Network;
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

        private void Awake()
        {
            if (_hostButton != null)
                _hostButton.onClick.AddListener(OnHostClicked);
            if (_joinButton != null)
                _joinButton.onClick.AddListener(OnJoinClicked);
            if (_leaveButton != null)
                _leaveButton.onClick.AddListener(OnLeaveClicked);
        }

        private void OnDestroy()
        {
            if (_hostButton != null)
                _hostButton.onClick.RemoveListener(OnHostClicked);
            if (_joinButton != null)
                _joinButton.onClick.RemoveListener(OnJoinClicked);
            if (_leaveButton != null)
                _leaveButton.onClick.RemoveListener(OnLeaveClicked);
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
