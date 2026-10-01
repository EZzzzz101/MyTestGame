using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 玩家移动（M1 非预测版）：Owner 用动态刚体在物理域移动与转向（关重力、冻结旋转），
    /// NetworkTransform 把位姿同步给其他端。
    /// 执行侧：仅本地 Owner 驱动；远端对象只接受同步。
    /// 迁移：M3 通过**新增**预测组件（[Replicate]/[Reconcile] + PredictionRigidbody）替换本组件，
    /// 本类属 M1 已验收核心类，按 CODING_STANDARDS §8.1 不再修改。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerInputReader))]
    public sealed class PlayerMotor : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private float _moveSpeed = PhysicsTuning.PlayerMoveSpeed;

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

        // [热路径] 转向与位移都在物理域完成（MoveRotation/MovePosition）：
        // 动态刚体才能被墙/柱挡住，也才能与球产生真实接触（kinematic 与静态、kinematic 之间都不产生接触）。
        private void FixedUpdate()
        {
            if (!_isOwner || _input == null)
                return;

            // 转向与位移都放在物理域里做（MoveRotation / MovePosition），
            // 避免在 Update 直接改写 Transform 与运动学刚体互相覆盖。
            Quaternion rotation = Quaternion.Euler(0f, _yaw, 0f);
            _rigidbody.MoveRotation(rotation);

            Vector2 move = _input.ReadMove();
            if (move.sqrMagnitude <= 0f)
                return;

            // 方向由 Yaw 直接算出，不读取 transform（刚体位姿要到下一个物理步才刷新）。
            Vector3 direction = rotation * new Vector3(move.x, 0f, move.y);
            // 用集中常量而不是 Time.fixedDeltaTime：网络/物理步长只允许 PhysicsTuning 定义。
            Vector3 delta = direction * (_moveSpeed * PhysicsTuning.FixedDeltaTime);
            _rigidbody.MovePosition(_rigidbody.position + delta);
        }

        // [仅 Owner | 事件驱动（ESC）] 本地表现：切换光标锁定。
        private void OnCancelPressed()
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
        }
    }
}
