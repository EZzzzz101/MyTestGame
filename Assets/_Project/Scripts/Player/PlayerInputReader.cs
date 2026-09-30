using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SphereRoom.Player
{
    /// <summary>
    /// 本地玩家输入采集。
    /// 设计定案：不使用 PlayerInput 组件的 Send Messages 模式（事件回调 + 字符串消息与热路径纪律冲突）；
    /// Move 供 Tick 内采样（M3 起进入 [Replicate]），Look / ESC 在 Update 采样不入 Tick。
    /// Action 引用在 Awake 里解析一次并缓存，运行期不再做任何查找。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        private const string PlayerMapName = "Player";
        private const string MoveActionName = "Move";
        private const string LookActionName = "Look";
        private const string UiMapName = "UI";
        private const string CancelActionName = "Cancel";

        [Tooltip("指向 Assets/_Project/Input/SphereRoom.inputactions。")]
        [SerializeField] private InputActionAsset _actions;

        private InputActionMap _playerActionMap;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _cancelAction;

        /// <summary>ESC 按下（用于释放光标 / 返回菜单）。</summary>
        public event Action CancelPressed;

        /// <summary>玩家操作是否已启用（仅本地 Owner 为 true）。</summary>
        public bool InputEnabled { get; private set; }

        private void Awake()
        {
            if (_actions == null)
            {
                enabled = false;
                return;
            }

            _playerActionMap = _actions.FindActionMap(PlayerMapName, true);
            _moveAction = _playerActionMap.FindAction(MoveActionName, true);
            _lookAction = _playerActionMap.FindAction(LookActionName, true);
            _cancelAction = _actions.FindActionMap(UiMapName, true).FindAction(CancelActionName, true);

            // 地图只启用一次：同一进程里可能有多个玩家对象（本地玩家 + 远端玩家），
            // 若各自 Enable/Disable 同一份资产的 ActionMap 会互相踩。是否采信输入改由 InputEnabled 标志决定。
            _playerActionMap.Enable();
            SetInputEnabled(false);
        }

        private void OnEnable()
        {
            if (_cancelAction != null)
                _cancelAction.performed += OnCancelPerformed;
        }

        private void OnDisable()
        {
            if (_cancelAction != null)
                _cancelAction.performed -= OnCancelPerformed;
        }

        /// <summary>
        /// 标记本对象是否采信输入。只控制读取开关，不切换 ActionMap 的启停（避免与同进程其他玩家对象冲突）。
        /// </summary>
        public void SetInputEnabled(bool value)
        {
            InputEnabled = value;
        }

        /// <summary>采样移动输入（0-1 摇杆域）。Tick 内调用。</summary>
        public Vector2 ReadMove()
        {
            if (!InputEnabled || _moveAction == null)
                return Vector2.zero;

            return _moveAction.ReadValue<Vector2>();
        }

        /// <summary>采样视角输入（鼠标像素增量）。Update 内调用。</summary>
        public Vector2 ReadLook()
        {
            if (!InputEnabled || _lookAction == null)
                return Vector2.zero;

            return _lookAction.ReadValue<Vector2>();
        }

        private void OnCancelPerformed(InputAction.CallbackContext context)
        {
            CancelPressed?.Invoke();
        }
    }
}
