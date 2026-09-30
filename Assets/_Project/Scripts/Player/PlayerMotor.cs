using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// M1 非预测版移动：Owner 自己移动，NetworkTransform 把位姿同步给其他端。
    /// M3 会用 [Replicate] / [Reconcile] + PredictionRigidbody 重写本类（届时本类的 Update 分支整体删除）。
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

        public override void OnStartClient()
        {
            base.OnStartClient();

            // FishNet 分析器要求：所有权判定放在 OnStartClient/OnStartServer，且用 Owner.IsLocalClient。
            _isOwner = Owner != null && Owner.IsLocalClient;

            if (_input == null)
                return;

            // 只有本地 Owner 采集输入；其余端只接受 NetworkTransform 同步。
            _input.SetInputEnabled(_isOwner);
            if (_isOwner)
                _input.CancelPressed += OnCancelPressed;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (_input != null && _isOwner)
                _input.CancelPressed -= OnCancelPressed;
        }

        /// <summary>累加 Yaw（由 PlayerCamera 在 Update 采样后调用，属本地表现，不进网络结构体）。</summary>
        public void ApplyYawDelta(float degrees)
        {
            _yaw += degrees;
        }

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

        private void OnCancelPressed()
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
        }
    }
}
