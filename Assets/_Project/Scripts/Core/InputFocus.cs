using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SphereRoom.Core
{
    /// <summary>
    /// 本地输入焦点控制器：对局焦点（鼠标锁定、跟随视角）与菜单焦点（鼠标可见、可点 UI）的切换入口。
    /// 为什么要单独一层：光标是全局状态，而"能否转视角"必须和它严格一致。之前 ESC 只解锁了光标
    /// （Unity 编辑器自身的解锁行为），视角采样没停，于是表现为"鼠标出来了但视角还在动"；
    /// 同时退出按钮点了没反应，是因为没有任何代码把光标真正交还给 UI。
    /// 本组件统一裁决：焦点一变，光标状态与视角采样同时跟着变。
    /// 执行侧：纯本地（不入网络、不入 Tick）：输入焦点是表现层概念，与网络角色无关。
    /// 触发时机：Update 轮询。键盘/鼠标按下是离散事件，塞进 Tick 会引入最多一个 Tick 的延迟且无意义。
    /// 热路径纪律：本方法内只做设备状态读取，无字符串拼接、无装箱、无 LINQ、无分配。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputFocus : MonoBehaviour
    {
        private static InputFocus _instance;

        /// <summary>场景内的唯一实例；未放置本组件时为 null（此时 <see cref="IsGameplay"/> 恒为 true，行为退化为改动前）。</summary>
        public static InputFocus Instance => _instance;

        /// <summary>
        /// 当前是否处于对局焦点。<see cref="PlayerInputReader"/> 在 Tick 内用它决定是否采样移动 / 视角输入。
        /// 没有实例时返回 true：保证忘记挂组件也不会把玩家动不了。
        /// </summary>
        public static bool IsGameplay => _instance == null || _instance._mode == InputFocusMode.Gameplay;

        /// <summary>焦点变化时触发（携带新焦点）。UI / 表现层只订阅，不反向改焦点。</summary>
        public event Action<InputFocusMode> FocusChanged;

        /// <summary>当前焦点。</summary>
        public InputFocusMode Mode => _mode;

        /// <summary>
        /// 是否允许进入对局焦点（只有联机中才应为 true）。由 UI 层在联机状态变化时设置。
        /// 没有这道闸会出现"在大厅里点了一下画面就把光标锁死，菜单按钮全都点不到"。
        /// </summary>
        public bool GameplayAllowed { get; set; }

        private InputFocusMode _mode = InputFocusMode.Menu;

        // [本地 | 一次性（Awake）] 注册单例并落地初始光标状态（大厅 = 菜单焦点）。
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                enabled = false;
                return;
            }

            _instance = this;
            ApplyCursor(_mode);
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        // [本地 | 每帧（Update）] 轮询 ESC 与左键；不进网络、不进 Tick。
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                // 单向：ESC 只负责"把鼠标还给玩家"，回到对局靠点画面，避免 ESC 反复横跳。
                SetMode(InputFocusMode.Menu);
                return;
            }

            if (_mode != InputFocusMode.Menu || !GameplayAllowed)
                return;

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            // 点在 UI 上（例如左上角的「退出游戏」）不算"点回画面"，否则点了按钮还会顺手把光标锁回去。
            if (IsPointerOverUi())
                return;

            SetMode(InputFocusMode.Gameplay);
        }

        /// <summary>[本地 | 事件驱动] 切换焦点：同步光标状态并广播。同值不重复触发。</summary>
        public void SetMode(InputFocusMode mode)
        {
            if (mode == InputFocusMode.Gameplay && !GameplayAllowed)
                return;

            if (_mode == mode)
                return;

            _mode = mode;
            ApplyCursor(mode);
            FocusChanged?.Invoke(mode);
        }

        // [本地 | 事件驱动] 焦点 → 光标：对局锁死并隐藏，菜单完全释放。
        private static void ApplyCursor(InputFocusMode mode)
        {
            bool gameplay = mode == InputFocusMode.Gameplay;
            Cursor.lockState = gameplay ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !gameplay;
        }

        // [本地 | 每帧] EventSystem.current 无分配；只在菜单焦点下调用，不在对局热路径上。
        private static bool IsPointerOverUi()
        {
            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }
    }
}
