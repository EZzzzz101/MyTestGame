using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 第一人称视角：仅本地 Owner 生效。Yaw / Pitch 全部作用在相机支架（_viewPivot，渲染层），
    /// 每帧应用——视角是本地即时表现，不等 Tick（等 Tick 会有可感知顿挫）。
    /// 权威 Yaw 通过 <see cref="ViewYaw"/> 暴露，由 PlayerPredictedMotor 在 Tick 内采样进 MoveInput 上行，
    /// 保证预测 / 回放用的是与视角一致的同一份输入。
    /// 铁律：本组件绝不写逻辑根（Rigidbody 物体）的 Transform——渲染层直接写刚体物体会
    /// 覆盖待生效的 Rigidbody.MovePosition（Unity 官方明令禁止），表现为"转动视角时无法移动"。
    /// 执行侧：仅本地 Owner；远端玩家实例的相机组件被禁用（OnStartClient）。
    /// </summary>
    public sealed class PlayerCamera : NetworkBehaviour
    {
        [SerializeField] private Camera _camera;
        [Tooltip("相机支架（挂在 Graphic 平滑层之下）：Yaw + Pitch 都作用在这里，Euler(pitch, yaw) 的 ZXY 顺序即标准 FPS 相机。")]
        [SerializeField] private Transform _viewPivot;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private float _sensitivity = PhysicsTuning.PlayerLookSensitivity;
        [SerializeField] private float _pitchLimit = PhysicsTuning.PlayerPitchLimit;

        private float _yaw;
        private float _pitch;
        private bool _isOwner;

        /// <summary>当前视角 Yaw（度）。PlayerPredictedMotor 每 Tick 采样进 MoveInput，随 [Replicate] 上行。</summary>
        public float ViewYaw => _yaw;

        private void Awake()
        {
            if (_input == null)
                _input = GetComponent<PlayerInputReader>();
        }

        // [双端 | 一次性（OnStartClient）] 非本地 Owner 关闭相机，避免多相机竞争；
        // Owner 初始视角取出生朝向，并把自身胶囊视觉层从本相机剔除（第一人称不看自己）。
        public override void OnStartClient()
        {
            base.OnStartClient();

            _isOwner = Owner != null && Owner.IsLocalClient;
            if (_camera != null)
                _camera.enabled = _isOwner;

            if (!_isOwner)
                return;

            // 以出生朝向（逻辑根的世界 Yaw）为基准累积，避免接手瞬间视角跳变。
            if (_viewPivot != null && _viewPivot.parent != null)
                _yaw = _viewPivot.parent.rotation.eulerAngles.y;

            // 自身胶囊已由 PlayerPredictedMotor 挪到 LocalPlayerBody 层（仅本端实例），
            // 这里从相机剔除该层。Scene 视图不受 CullingMask 影响，胶囊照常可见。
            if (_camera != null)
                _camera.cullingMask &= ~(1 << PhysicsLayers.LocalPlayerBody);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // [热路径 | 仅 Owner 每帧] 只做 struct 运算与四元数赋值，无分配。
        private void Update()
        {
            if (!_isOwner || _input == null || _viewPivot == null)
                return;

            Vector2 look = _input.ReadLook();

            _yaw += look.x * _sensitivity;
            _pitch -= look.y * _sensitivity;
            if (_pitch > _pitchLimit)
                _pitch = _pitchLimit;
            else if (_pitch < -_pitchLimit)
                _pitch = -_pitchLimit;

            // Quaternion.Euler 为 ZXY 内在顺序：R = Ry(yaw) * Rx(pitch)，
            // 即"先世界 Yaw、后本地 Pitch"，与父pivot/子pivot 两级支架等价。
            _viewPivot.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }
}
