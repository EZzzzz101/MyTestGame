using SphereRoom.Ball;
using UnityEngine;

namespace SphereRoom.UI
{
    /// <summary>
    /// M8 Tapped 提示（挂在 Toast Canvas 上，显示节点 = 场景里放好的 Tapped 物体）：
    /// **碰到就显示、分开就失活**——没有任何计时 / 淡入淡出，纯 SetActive 开关（2026-10-02 定案）。
    /// 之前那版"撞击后 _visibleSeconds 秒隐藏"被砍掉：接触是持续状态，用倒计时猜结束时间会与真实接触错位
    /// （贴着球走、被顶在墙角时要么提前消失、要么一直挂着）；由服务器在"接触结束"时明确下发更准。
    /// 状态来源：TappedDispatcher 的两个事件——Tapped（接触开始）/ Released（接触结束/球被销毁）。
    /// </summary>
    public sealed class TappedIndicator : MonoBehaviour
    {
        [Tooltip("被撞派发器（Room 场景 GameManager 上的 TappedDispatcher）。")]
        [SerializeField] private TappedDispatcher _dispatcher;

        [Tooltip("Tapped 显示节点（Toast Canvas 下的 Tapped 物体）。")]
        [SerializeField] private GameObject _tappedObject;

        private void Awake()
        {
            SetVisible(false);
        }

        private void OnEnable()
        {
            if (_dispatcher != null)
            {
                _dispatcher.Tapped += OnTapped;
                _dispatcher.Released += OnReleased;
            }
        }

        private void OnDisable()
        {
            if (_dispatcher != null)
            {
                _dispatcher.Tapped -= OnTapped;
                _dispatcher.Released -= OnReleased;
            }
        }

        // [本机 | 事件驱动] 球碰到自己：显示。
        private void OnTapped()
        {
            SetVisible(true);
        }

        // [本机 | 事件驱动] 球与自己分开（或那个球被销毁）：失活。
        private void OnReleased()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_tappedObject != null)
                _tappedObject.SetActive(visible);
        }
    }
}
