using UnityEngine;

namespace SphereRoom.Ball
{
    /// <summary>
    /// 球的音频表现层（M5）：**声音源头就是足球本身**（音源挂在 Graphic 子物体，3D 空间声随球移动、按距离衰减）。
    /// 双播放路径（M5 声音预测设计，无双响）：
    /// - 踢球者本人：本地模拟里当场踢到球（M4 reconcile-only 预测），经 <see cref="BallImpactDispatcher.LocalKickPredicted"/>
    ///   立即出声（零延迟）；服务器广播回传的同一事件被 IsLocallyPredictedKick 跳过；
    /// - 其他人（含主机）：等服务器权威广播，统一时间线。
    /// 纯表现组件（AGENTS §5.9）：普通 MonoBehaviour，不碰网络与玩法状态。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallAudioView : MonoBehaviour
    {
        [Tooltip("同物体上的事件源（SharedBall 逻辑根）。")]
        [SerializeField] private BallImpactDispatcher _dispatcher;

        [Tooltip("Graphic 子物体上的 3D 音源。")]
        [SerializeField] private AudioSource _source;

        [Tooltip("踢球音效。")]
        [SerializeField] private AudioClip _kickClip;

        [Tooltip("音量映射：有效速度达到该值时音量封顶为 1，不足按比例衰减（保底 0.35）。")]
        [SerializeField] private float _maxKickSpeed = 8f;

        private void OnEnable()
        {
            if (_dispatcher != null)
            {
                _dispatcher.BallImpacted += OnBallImpacted;
                _dispatcher.LocalKickPredicted += OnKickPredicted;
            }
        }

        private void OnDisable()
        {
            if (_dispatcher != null)
            {
                _dispatcher.BallImpacted -= OnBallImpacted;
                _dispatcher.LocalKickPredicted -= OnKickPredicted;
            }
        }

        // 只做踢球音效：撞墙 / 撞柱 / 滚动声**不做**（2026-10-02 定案——球多起来声音太乱）。
        // [踢球者本机 | 事件驱动（本地预测）] 我踢到球：立即出声，不等服务器回传。
        private void OnKickPredicted(BallImpactData data)
        {
            if (data.KickerClientId >= 0)
                PlayKick(data.RelativeSpeed);
        }

        // [双端 | 事件驱动（服务器广播）] 其他人踢的球：按强度播放；我已预测播过的这一次跳过（避免双响）。
        private void OnBallImpacted(BallImpactData data)
        {
            if (_source == null || _kickClip == null)
                return;

            if (data.KickerClientId < 0)
                return;                                            // 撞墙 / 撞柱 / 球撞球：M8 再做。

            if (_dispatcher.IsLocallyPredictedKick(data))
                return;                                            // 本机已预测出声，跳过服务器回传的同一事件。

            PlayKick(data.RelativeSpeed);
        }

        private void PlayKick(float effectiveSpeed)
        {
            if (_source == null || _kickClip == null)
                return;

            float volume = Mathf.Clamp01(effectiveSpeed / _maxKickSpeed);
            _source.PlayOneShot(_kickClip, Mathf.Max(0.35f, volume));
        }
    }
}
