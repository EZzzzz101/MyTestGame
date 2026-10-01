using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 玩家移动（M1 非预测版）：Owner 用**动态刚体 + 直接设置速度**移动（不做惯性，见 DEVELOPMENT_PLAN §4.5），
    /// 重力负责落地，NetworkTransform 把位姿同步给其他端。
    /// 不用 CharacterController 的原因（实测结论）：① CC 不参与 PhysX 接触 → 推不动球；
    /// ② CC 会沿球面爬升，玩家会站到球上不下来。动态刚体则同时满足挡墙、推球、落地。
    /// 执行侧：仅本地 Owner 驱动；远端对象只接受同步。
    /// 迁移：M3 通过**新增**预测组件替换本组件（骨架见包内 Demos/Prediction/Rigidbody），
    /// 本类属已验收核心类，按 CODING_STANDARDS §8.1 不再修改。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerInputReader))]
    public sealed class PlayerMotor : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private float _moveSpeed = PhysicsTuning.PlayerMoveSpeed;
        [SerializeField] private float _maxFallSpeed = PhysicsTuning.MaxFallSpeed;

        private Rigidbody _rigidbody;
        private bool _isOwner;
        private float _yaw;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            if (_input == null)
                _input = GetComponent<PlayerInputReader>();
        }

        // [双端 | 一次性（OnStartClient）] 判定所有权并只在本地 Owner 上开启输入。
        public override void OnStartClient()
        {
            base.OnStartClient();

            // FishNet 分析器要求：所有权判定放在 OnStartClient/OnStartServer，且用 Owner.IsLocalClient。
            _isOwner = Owner != null && Owner.IsLocalClient;

            if (_input == null)
                return;

            // 只有本地 Owner 采集输入；其余端只接受 NetworkTransform 同步。
            if (_isOwner)
                _input.EnableInput();
            else
                _input.DisableInput();

            if (_isOwner)
                _input.CancelPressed += OnCancelPressed;
        }

        // [双端 | 一次性（OnStopClient）] 反注册，避免事件悬空。
        public override void OnStopClient()
        {
            base.OnStopClient();

            if (_input != null && _isOwner)
                _input.CancelPressed -= OnCancelPressed;
        }

        /// <summary>[热路径] 累加 Yaw（由 PlayerCamera 在 Update 采样后调用，属本地表现，不进网络结构体）。</summary>
        public void ApplyYawDelta(float degrees)
        {
            _yaw += degrees;
        }

        // [热路径][物理步 = Tick] 只做 struct 运算：转向直接写 Transform（旋转已冻结，物理不会翻倒），
        // 移动直接设置速度（不做惯性）。垂直分量必须保留刚体自己（重力）的结果，
        // 否则写速度会把重力抵消，玩家又不会落地。
        private void FixedUpdate()
        {
            if (!_isOwner || _input == null)
                return;

            Quaternion rotation = Quaternion.Euler(0f, _yaw, 0f);
            transform.rotation = rotation;

            Vector2 move = _input.ReadMove();
            // 方向由 Yaw 直接算出，不读取 transform（Transform 的旋转要下一帧才反映到 forward）。
            Vector3 direction = rotation * new Vector3(move.x, 0f, move.y);

            float vertical = _rigidbody.linearVelocity.y;
            if (vertical < _maxFallSpeed)
                vertical = _maxFallSpeed;

            Vector3 velocity = direction * _moveSpeed;
            velocity.y = vertical;
            _rigidbody.linearVelocity = velocity;
        }

        // [仅 Owner | 事件驱动（ESC）] 本地表现：切换光标锁定。
        private void OnCancelPressed()
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
        }
    }
}
