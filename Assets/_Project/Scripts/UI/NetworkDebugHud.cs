using System.Text;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.UI;

namespace SphereRoom.UI
{
    /// <summary>
    /// 右上角网络/FPS 小面板：Ping（RTT）、单向延迟、丢包率、FPS。
    /// 执行侧：仅本地表现层，只读 FishNet 统计与 Unity 帧时长，不写任何玩法状态（AGENTS §5.9）。
    /// 面板可在 Inspector 指派自己搭的 Text；留空则由代码自建一个右上角小面板，省去为看数据搭 UI。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkDebugHud : MonoBehaviour
    {
        /// <summary>刷新间隔（秒）：面板不逐帧重建字符串，避免每帧产生垃圾（CODING_STANDARDS §3.1 的脏更新思路）。</summary>
        private const float RefreshInterval = 0.25f;

        private const int PanelWidth = 190;
        private const int PanelHeight = 100;
        private const int FontSize = 14;

        [Tooltip("留空则自动取 InstanceFinder.NetworkManager。")]
        [SerializeField] private NetworkManager _networkManager;

        [Tooltip("留空则运行时自建右上角小面板。")]
        [SerializeField] private Text _statsText;

        private readonly StringBuilder _builder = new StringBuilder(128);
        private float _refreshTimer;
        private float _fps;

        private void Awake()
        {
            if (_statsText == null)
                _statsText = CreateAutoPanel();
        }

        // [热路径] 逐帧只做帧时长平滑与倒计时；字符串重建被 RefreshInterval 节流（默认 0.25s 一次）。
        private void Update()
        {
            float unscaledDelta = Time.unscaledDeltaTime;
            if (unscaledDelta > 0f)
                _fps = Mathf.Lerp(_fps, 1f / unscaledDelta, 0.1f);

            _refreshTimer -= unscaledDelta;
            if (_refreshTimer > 0f)
                return;

            _refreshTimer = RefreshInterval;
            RefreshText();
        }

        private void RefreshText()
        {
            NetworkManager manager = ResolveNetworkManager();

            _builder.Clear();

            if (manager == null || !manager.IsClientStarted)
            {
                _builder.Append("未联机");
            }
            else
            {
                TimeManager timeManager = manager.TimeManager;
                _builder.Append("Ping: ").Append(timeManager.RoundTripTime).Append(" ms");
                _builder.Append('\n').Append("单向: ").Append(timeManager.HalfRoundTripTime).Append(" ms");
                _builder.Append('\n').Append("丢包: ").Append(GetPacketLossPercent(manager).ToString("0.0")).Append(" %");
            }

            _builder.Append('\n').Append("FPS: ").Append(Mathf.RoundToInt(_fps));
            _statsText.text = _builder.ToString();
        }

        private NetworkManager ResolveNetworkManager()
        {
            return _networkManager != null ? _networkManager : InstanceFinder.NetworkManager;
        }

        /// <summary>
        /// 本地连接视角的丢包率（%）：客户端看 client 侧统计，纯服务器看 server 侧统计。
        /// </summary>
        private static float GetPacketLossPercent(NetworkManager manager)
        {
            Transport transport = manager.TransportManager.Transport;
            if (transport == null)
                return 0f;

            bool asServer = manager.IsServerStarted && !manager.IsClientStarted;
            return transport.GetPacketLoss(asServer);
        }

        /// <summary>
        /// 自建右上角小面板：Canvas(Screen Space Overlay) + 半透明底 + 文本。
        /// 挂在自身节点下，随宿主对象一起销毁（HUD 放在 Boot 场景的常驻对象上即可）。
        /// </summary>
        private Text CreateAutoPanel()
        {
            GameObject canvasObject = new GameObject("Network Debug HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-12f, -12f);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(panelObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 8f);
            textRect.offsetMax = new Vector2(-10f, -8f);

            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = FontSize;
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = "未联机";

            return text;
        }
    }
}
