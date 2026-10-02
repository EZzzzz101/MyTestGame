using SphereRoom.Network;
using UnityEngine;
using UnityEngine.UI;

namespace SphereRoom.UI
{
    /// <summary>
    /// 房间玩家进出提示（M5）：顶部居中一行文字，出现后保持 <see cref="_holdSeconds"/> 再渐隐 <see cref="_fadeSeconds"/>。
    /// 不遮挡交互（需求定案）：Canvas 不挂 GraphicRaycaster，且 Text.raycastTarget 在 Awake 强制关闭（双保险），
    /// 永远不会吞掉点击 / UI 射线。
    /// 文案在事件回调里拼接（玩家进出是事件频率，不是热路径，不违反 §7 字符串禁令）。
    /// 同一时间只显示最新一条（1-4 人房间足够，不做堆叠队列）。
    /// </summary>
    public sealed class RoomToastUI : MonoBehaviour
    {
        [SerializeField] private RoomAnnouncer _announcer;
        [SerializeField] private Text _toastText;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _presenceClip;

        [Tooltip("文字完全显示的保持时长（秒）。")]
        [SerializeField] private float _holdSeconds = 2f;

        [Tooltip("渐隐时长（秒）。")]
        [SerializeField] private float _fadeSeconds = 0.8f;

        // 剩余总时长（hold + fade），≤0 表示当前没有提示。
        private float _remaining;

        private void Awake()
        {
            if (_toastText != null)
                _toastText.raycastTarget = false;
            if (_canvasGroup != null)
                _canvasGroup.alpha = 0f;
        }

        private void OnEnable()
        {
            if (_announcer != null)
                _announcer.PlayerPresenceChanged += OnPlayerPresenceChanged;
        }

        private void OnDisable()
        {
            if (_announcer != null)
                _announcer.PlayerPresenceChanged -= OnPlayerPresenceChanged;
        }

        // [双端 | 事件驱动] 玩家进出：更新文案、播音效、重置计时。
        private void OnPlayerPresenceChanged(int playerId, bool joined)
        {
            if (_toastText != null)
                _toastText.text = $"{playerId} 号玩家{(joined ? "进入" : "离开")}房间";

            if (_audioSource != null && _presenceClip != null)
                _audioSource.PlayOneShot(_presenceClip);

            _remaining = _holdSeconds + _fadeSeconds;
            if (_canvasGroup != null)
                _canvasGroup.alpha = 1f;
        }

        // [表现层 | 每帧] 渐隐计时（纯 UI 表现，不在网络 / Tick 路径上）。
        private void Update()
        {
            if (_remaining <= 0f)
                return;

            _remaining -= Time.unscaledDeltaTime;
            if (_canvasGroup != null)
                _canvasGroup.alpha = Mathf.Clamp01(_remaining / _fadeSeconds);
        }
    }
}
