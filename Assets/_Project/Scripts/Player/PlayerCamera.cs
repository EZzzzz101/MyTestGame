using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 第一人称相机：仅本地 Owner 启用渲染与俯仰控制。
    /// Yaw 作用在玩家本体（随移动方向、随 NetworkTransform 同步），Pitch 只作用相机支点，不同步。
    /// </summary>
    public sealed class PlayerCamera : NetworkBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _pitchPivot;
        [SerializeField] private PlayerInputReader _input;
        [Tooltip("Yaw 交由 PlayerMotor 在物理域应用，避免与运动学刚体互相覆盖。")]
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private float _sensitivity = PhysicsTuning.PlayerLookSensitivity;
        [SerializeField] private float _pitchLimit = PhysicsTuning.PlayerPitchLimit;

        private float _pitch;
        private bool _isOwner;

        private void Awake()
        {
            if (_input == null)
                _input = GetComponent<PlayerInputReader>();
            if (_motor == null)
                _motor = GetComponent<PlayerMotor>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            _isOwner = Owner != null && Owner.IsLocalClient;
            if (_camera != null)
                _camera.enabled = _isOwner;

            if (_isOwner)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Update()
        {
            if (!_isOwner || _input == null || _pitchPivot == null)
                return;

            Vector2 look = _input.ReadLook();

            // Yaw：交给 PlayerMotor 在 FixedUpdate 里用 MoveRotation 应用（物理域，避免抖动与覆盖）。
            if (_motor != null)
                _motor.ApplyYawDelta(look.x * _sensitivity);

            // Pitch：夹在上下限内，避免翻头。
            _pitch -= look.y * _sensitivity;
            if (_pitch > _pitchLimit)
                _pitch = _pitchLimit;
            else if (_pitch < -_pitchLimit)
                _pitch = -_pitchLimit;

            _pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
