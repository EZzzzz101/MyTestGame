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

        private CharacterController _controller;
        private bool _isOwner;
        private float _yaw;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
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

            Vector2 move = _input.ReadMove();
            if (move.sqrMagnitude <= 0f)
                return;

            // 方向由 Yaw 直接算出，不读取 transform（Transform 的旋转要下一帧才反映到 forward）。
            Vector3 direction = rotation * new Vector3(move.x, 0f, move.y);
            _controller.Move(direction * (_moveSpeed * Time.deltaTime));
        }

        // [仅 Owner | 事件驱动（ESC）] 本地表现：切换光标锁定。
        private void OnCancelPressed()
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
        }
    }
}
