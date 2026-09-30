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

        private void OnLeaveClicked()
        {
            _bootstrap.StopNetwork();
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
        }
    }
}
