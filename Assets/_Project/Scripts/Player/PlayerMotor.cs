using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 玩家移动（M1 非预测版）：Owner 用 CharacterController 移动与转向，NetworkTransform 把位姿同步给其他端。
    /// 为什么用 CC 而不是刚体：kinematic 刚体不与静态碰撞体产生接触（会穿墙），动态刚体又要逐个纠结质量/约束/摩擦；
    /// CC 自带与静态几何的扫掠碰撞，运动学状态最干净。推动球体靠同物体上的运动学 Rigidbody + CapsuleCollider。
    /// 执行侧：仅本地 Owner 驱动；远端对象只接受同步。
    /// 迁移：M3 通过**新增**预测组件替换本组件（骨架见包内 Demos/Prediction/CharacterController），
    /// 本类属已验收核心类，按 CODING_STANDARDS §8.1 不再修改。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInputReader))]
    public sealed class PlayerMotor : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private float _moveSpeed = PhysicsTuning.PlayerMoveSpeed;
        [SerializeField] private float _maxFallSpeed = PhysicsTuning.MaxFallSpeed;

        private CharacterController _controller;
        private Rigidbody _rigidbody;
        private bool _isOwner;
        private float _yaw;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            // 运动学刚体只用于与球体产生 PhysX 接触；缺失时只是推不动球，不影响移动。
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

        // [热路径] Owner 逐帧移动：CC 不是物理刚体、没有插值，放 FixedUpdate 只剩 50Hz 台阶感，逐帧 Move 才平滑。
        // TODO(M3): 换预测组件后删除本类；届时移动回到 Tick 驱动的 [Replicate] + 预测平滑。
        private void Update()
        {
            if (!_isOwner || _input == null)
                return;

            Quaternion rotation = Quaternion.Euler(0f, _yaw, 0f);
            // CC 不管理旋转：直接写 Transform。此时位移不再经过刚体，不存在"两个域互相覆盖"的问题。
            transform.rotation = rotation;

            // CC 不做重力：自己累积垂直速度（包内 CC 示例同法），否则玩家会停在出生高度不落地。
            _verticalVelocity += Physics.gravity.y * Time.deltaTime;
            if (_verticalVelocity < _maxFallSpeed)
                _verticalVelocity = _maxFallSpeed;

            Vector2 move = _input.ReadMove();
            // 方向由 Yaw 直接算出，不读取 transform（Transform 的旋转要下一帧才反映到 forward）。
            Vector3 direction = rotation * new Vector3(move.x, 0f, move.y);

            Vector3 motion = direction * (_moveSpeed * Time.deltaTime);
            motion.y = _verticalVelocity * Time.deltaTime;
            _controller.Move(motion);

            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = PhysicsTuning.GroundedStickSpeed;

            // CC 的位移只写 Transform，PhysX 看不到扫掠；再驱动一次运动学刚体，
            // 球（动态）才能被真实推动（kinematic × dynamic 才产生接触）。目标点取 CC 解算后的位置，
            // 因此撞墙被挡住的那部分位移不会把球一起顶穿墙。
            if (_rigidbody != null)
                _rigidbody.MovePosition(transform.position);
        }

        // [仅 Owner | 事件驱动（ESC）] 本地表现：切换光标锁定。
        private void OnCancelPressed()
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
        }
    }
}
