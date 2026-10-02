using SphereRoom.Network;
using UnityEngine;
using UnityEngine.UI;

namespace SphereRoom.UI
{
    /// <summary>
    /// 房间解散面板（M6）：主机意外断开时弹出「房间已解散」，点击「返回大厅」卸载 Room 场景回到 Boot 主菜单。
    /// 与 M9 Steam 无关的恢复路径：换传输层（Tugboat → SteamworksSockets）后 OnClientConnectionState 的
    /// 断开语义不变，本面板原样复用——做它不是白工。
    /// 自己主动点「退出游戏」不触发本面板（NetworkBootstrap 用 _stopRequestedLocally 区分主动 / 意外断开）。
    /// </summary>
    public sealed class RoomClosedUI : MonoBehaviour
    {
        [SerializeField] private NetworkBootstrap _bootstrap;
        [SerializeField] private GameObject _panel;
        [SerializeField] private Button _returnButton;

        [Tooltip("主菜单面板：房间解散期间隐藏，返回后再显示（避免两个居中面板叠在一起）。")]
        [SerializeField] private GameObject _menuPanel;

        private void Awake()
        {
            if (_returnButton != null)
                _returnButton.onClick.AddListener(OnReturnClicked);
            if (_panel != null)
                _panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_returnButton != null)
                _returnButton.onClick.RemoveListener(OnReturnClicked);
        }

        private void OnEnable()
        {
            if (_bootstrap != null)
                _bootstrap.HostConnectionLost += OnHostConnectionLost;
        }

        private void OnDisable()
        {
            if (_bootstrap != null)
                _bootstrap.HostConnectionLost -= OnHostConnectionLost;
        }

        // [客户端 | 事件驱动] 主机断开：显示解散面板，藏起主菜单（MainMenuUI 收到 Offline 状态会把菜单弹出来，
        // 本回调在其后触发，最终呈现 = 只见解散面板）。
        private void OnHostConnectionLost()
        {
            if (_panel != null)
                _panel.SetActive(true);
            if (_menuPanel != null)
                _menuPanel.SetActive(false);
        }

        // [客户端 | 事件驱动（按钮）] 返回 Boot：先恢复面板可见性，再断开 + 卸载 Room 场景。
        private void OnReturnClicked()
        {
            if (_panel != null)
                _panel.SetActive(false);
            if (_menuPanel != null)
                _menuPanel.SetActive(true);

            if (_bootstrap != null)
                _bootstrap.ReturnToBoot();
        }
    }
}
