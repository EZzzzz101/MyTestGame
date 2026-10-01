using FishNet.Connection;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 玩家预测移动：Kinematic 刚体 + 自 sweep 解算位移（PHYSICS_DESIGN.md §2），不使用 PredictionRigidbody
    /// （那是给 Dynamic 刚体写速度用的，Kinematic 会忽略速度）。
    /// 执行侧：Owner 采集输入并预测；服务器在 Tick 内执行同一方法体并下发和解。
    /// 骨架：位置驱动的 Replicate/Reconcile（结构参考包内 Demos/Prediction/CharacterController，
    /// 但位移用 Physics.CapsuleCast 自解算 + MovePosition，不用 CharacterController）。
    /// </summary>
    public sealed class PlayerPredictedMotor : TickNetworkBehaviour
    {
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private float _moveSpeed = PhysicsTuning.PlayerMoveSpeed;
        [SerializeField] private float _lookSensitivity = PhysicsTuning.PlayerLookSensitivity;

        private Rigidbody _rigidbody;
        private bool _isOwner;
        private float _yaw;
        private float _pendingYaw;
        private float _lastGroundY;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            if (_input == null)
                _input = GetComponent<PlayerInputReader>();

            _lastGroundY = transform.position.y;
            // TickNetworkBehaviour 需要显式声明要接收哪些 Tick 回调。
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        // [双端 | 一次性（OnOwnershipClient）] 判定所有权并只让本地 Owner 采信输入。
        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);

            _isOwner = Owner != null && Owner.IsLocalClient;
            if (_isOwner)
            {
                // 以当前朝向为基准继续累积，避免接手瞬间视角跳变。
                _yaw = transform.eulerAngles.y;
                _pendingYaw = _yaw;
            }

            if (_input == null)
                return;

            if (_isOwner)
                _input.EnableInput();
            else
                _input.DisableInput();
        }

        // [仅 Owner | 每帧] 视角采样：Look 是本地即时表现（不等 Tick），只把增量累加进 pendingYaw，
        // 由下一 Tick 打包进 MoveInput 上行，保证回滚重放用的是同一份输入。
        private void Update()
        {
            if (!_isOwner || _input == null)
                return;

            Vector2 look = _input.ReadLook();
            _pendingYaw += look.x * _lookSensitivity;
        }

        // [双端 | Tick 驱动] 收集输入并执行 Replicate（回滚重放时同一方法体会被再次调用）。
        protected override void TimeManager_OnTick()
        {
            // 直接调用本项目自己的 [Replicate] 方法（官方示例同法：属性标注决定 FishNet 的回滚/重放处理）。
            Move(BuildMoveData());
        }

        // [双端 | Tick 驱动] 物理步之后构建和解：服务器下发给 Owner，客户端自建一份作为丢包兜底。
        protected override void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        /// <summary>[仅 Owner] 打包本 Tick 的输入；非 Owner（如服务器上的 AI 玩家）返回 default。</summary>
        private MoveInput BuildMoveData()
        {
            if (!_isOwner || _input == null)
                return default;

            return new MoveInput(_input.ReadMove(), _pendingYaw);
        }

        /// <summary>[双端] 构建和解数据：客户端也建一份，丢包时可临时兜底（官方示例同法）。</summary>
        public override void CreateReconcile()
        {
            // 同样直接调用本项目自己的 [Reconcile] 方法（官方示例同法）。
            Reconcile(new PlayerReconcileState(transform.position, _yaw));
        }

        // [双端 | Tick 驱动] 位移解算：输入 → 期望位移 → CapsuleCast 解算 → MovePosition。
        // 用 MovePosition 而不是直接写 position：Kinematic 用 MovePosition 时 PhysX 仍会对被挤入的
        // Dynamic 球做 depenetration，这是我们"推球"的物理基础（PHYSICS_DESIGN.md §2.6）。
        [Replicate]
        private void Move(MoveInput md, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            float delta = (float)TimeManager.TickDelta;

            _yaw = md.Yaw;
            Quaternion rotation = Quaternion.Euler(0f, _yaw, 0f);

            Vector3 direction = rotation * new Vector3(md.Move.x, 0f, md.Move.y);
            Vector3 desired = direction * (_moveSpeed * delta);

            Vector3 next = ResolveMove(_rigidbody.position, desired);
            next.y = ResolveGroundY(next.x, next.z);

            _rigidbody.MovePosition(next);
            _rigidbody.MoveRotation(rotation);
        }

        // [双端 | Tick 驱动] 和解：Kinematic 直接写位置/朝向（没有速度需要恢复，载荷比 Dynamic 方案更小）。
        [Reconcile]
        private void Reconcile(PlayerReconcileState rd, Channel channel = Channel.Unreliable)
        {
            _rigidbody.position = rd.Position;
            _yaw = rd.Yaw;
            _rigidbody.rotation = Quaternion.Euler(0f, rd.Yaw, 0f);
        }

        /// <summary>
        /// [热路径][双端 | 每 Tick] 用 CapsuleCast 把期望位移解算成不会穿墙的位移。
        /// 只查 World 层：球与其他玩家不阻断玩家移动（PHYSICS_DESIGN.md §2.2）。
        /// </summary>
        private Vector3 ResolveMove(Vector3 position, Vector3 desiredDelta)
        {
            Vector3 remaining = desiredDelta;

            for (int i = 0; i < PhysicsTuning.MoveSlideIterations; i++)
            {
                float dist = remaining.magnitude;
                if (dist <= PhysicsTuning.MoveEpsilon)
                    break;

                Vector3 dir = remaining / dist;
                Vector3 p1 = position + Vector3.up * PhysicsTuning.CapsuleHalfSegment;
                Vector3 p2 = position - Vector3.up * PhysicsTuning.CapsuleHalfSegment;

                // 单次 CapsuleCast 自身无堆分配，不需要 NonAlloc 版本。
                if (Physics.CapsuleCast(p1, p2, PhysicsTuning.PlayerRadius, dir, out RaycastHit hit,
                        dist, PhysicsTuning.WorldLayerMask, QueryTriggerInteraction.Ignore))
                {
                    float safe = Mathf.Max(0f, hit.distance - PhysicsTuning.MoveSkinWidth);
                    position += dir * safe;

                    // 剩余位移投影到墙面 → 贴墙滑行，而不是被粘住。
                    Vector3 leftOver = dir * (dist - safe);
                    remaining = Vector3.ProjectOnPlane(leftOver, hit.normal);
                }
                else
                {
                    position += dir * dist;
                    break;
                }
            }

            return position;
        }

        /// <summary>
        /// [热路径][双端 | 每 Tick] 向下探测地面并把脚底贴合地面高度（Kinematic 没有重力，必须自己管）。
        /// 探不到地面时沿用上一 Tick 的结果，避免瞬间掉到 0（PHYSICS_DESIGN.md §2.3）。
        /// </summary>
        private float ResolveGroundY(float currentX, float currentZ)
        {
            Vector3 origin = new Vector3(currentX, PhysicsTuning.GroundProbeStartY, currentZ);
            if (Physics.SphereCast(origin, PhysicsTuning.PlayerRadius * PhysicsTuning.GroundProbeRadiusRatio,
                    Vector3.down, out RaycastHit hit, PhysicsTuning.GroundProbeDistance,
                    PhysicsTuning.WorldLayerMask, QueryTriggerInteraction.Ignore))
            {
                _lastGroundY = hit.point.y;
                return _lastGroundY + PhysicsTuning.CapsuleHalfHeight;
            }

            return _lastGroundY + PhysicsTuning.CapsuleHalfHeight;
        }
    }
}
