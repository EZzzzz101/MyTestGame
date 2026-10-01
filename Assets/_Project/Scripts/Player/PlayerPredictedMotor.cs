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
    /// 层级与写入铁律（2026-10-01 "转动视角时无法移动"修复定案）：
    /// - CameraPivot 必须挂在 Graphic（NetworkObject.GraphicalObject 平滑层）之下，否则相机跟随逻辑根以 Tick 步进 → 50Hz 顿挫；
    /// - 逻辑根（Rigidbody 物体）的 Transform 只允许在 Tick 回调内经 Rigidbody API（MovePosition/MoveRotation）写入。
    ///   渲染层（Update）直接写刚体物体会覆盖待生效的 MovePosition（Unity 官方明令禁止）——
    ///   视角 Yaw 归 PlayerCamera（渲染层，相机支架），本组件只在 Tick 内经 ViewYaw 采样进输入。
    /// </summary>
    public sealed class PlayerPredictedMotor : TickNetworkBehaviour
    {
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private PlayerCamera _camera;
        [SerializeField] private float _moveSpeed = PhysicsTuning.PlayerMoveSpeed;
        [Tooltip("Graphic 子物体上的胶囊渲染器；本地 Owner 会把它的 GameObject 挪到 LocalPlayerBody 层（仅本相机剔除，Scene 视图仍可见）。")]
        [SerializeField] private Renderer _graphicRenderer;

        // TODO(M3): 临时诊断开关，定位"单向移动/移动鬼畜"后立即删除（含下面的 LogWarning）。
        [Header("临时诊断（定位完删除）")]
        [SerializeField] private bool _logMoveDiagnostics = true;

        private Rigidbody _rigidbody;
        private bool _isOwner;
        private float _yaw;
        private float _lastGroundY;
        private float _nextDiagnosticTime;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            if (_input == null)
                _input = GetComponent<PlayerInputReader>();
            if (_camera == null)
                _camera = GetComponent<PlayerCamera>();
            if (_graphicRenderer == null)
                _graphicRenderer = GetComponentInChildren<Renderer>();

            _lastGroundY = transform.position.y;
            // TickNetworkBehaviour 需要显式声明要接收哪些 Tick 回调。
            SetTickCallbacks(TickCallback.Tick | TickCallback.PostTick);
        }

        // [双端 | 一次性（OnOwnershipClient）] 判定所有权并只让本地 Owner 采信输入。
        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);

            _isOwner = Owner != null && Owner.IsLocalClient;

            // 本地 Owner 隐藏自己的胶囊（第一人称）：把 Graphic 挪到 LocalPlayerBody 层，
            // 由 PlayerCamera 从 CullingMask 剔除。不用 renderer.enabled = false——那会连 Scene 视图一起隐藏，
            // 调试时看不到自己。layer 非网络同步属性，只影响本端实例；其他客户端上同一玩家仍在 Default 层照常渲染。
            if (_isOwner && _graphicRenderer != null)
                _graphicRenderer.gameObject.layer = PhysicsLayers.LocalPlayerBody;

            if (_input == null)
                return;

            if (_isOwner)
                _input.EnableInput();
            else
                _input.DisableInput();
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

        /// <summary>[仅 Owner] 打包本 Tick 的输入；非 Owner（如服务器上的 AI 玩家）返回 default。
        /// Yaw 取自 PlayerCamera.ViewYaw（渲染层每帧累积的当前视角），Tick 边界采样一次——
        /// 与视觉一致，且回滚重放用的是这份历史值，确定性好。</summary>
        private MoveInput BuildMoveData()
        {
            if (!_isOwner || _input == null)
                return default;

            float yaw = _camera != null ? _camera.ViewYaw : _yaw;
            return new MoveInput(_input.ReadMove(), yaw);
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

            Vector3 from = _rigidbody.position;
            Vector3 next = ResolveMove(from, desired);
            next.y = ResolveGroundY(next.x, next.z);

            _rigidbody.MovePosition(next);
            // 朝向：Owner 的视觉 Yaw 由 PlayerCamera 在渲染层处理（本实例逻辑根不转，避免与 MovePosition 抢写）；
            // 服务器 / 观察者实例在这里按输入写入，供其他端经 reconcile 状态看到本玩家的朝向。
            if (!_isOwner)
                _rigidbody.MoveRotation(rotation);

            LogMoveDiagnostics(md, desired, from, next);
        }

        /// <summary>[仅诊断] 每秒打一行：输入向量、期望位移、解算前后位置。定位完删除本方法与调用。</summary>
        private void LogMoveDiagnostics(MoveInput md, Vector3 desired, Vector3 from, Vector3 next)
        {
            if (!_logMoveDiagnostics)
                return;

            float now = Time.unscaledTime;
            if (now < _nextDiagnosticTime)
                return;

            _nextDiagnosticTime = now + 1f;
            Vector3 applied = next - from;
            Debug.LogWarning($"[MoveDiag] tick={TimeManager.LocalTick} owner={_isOwner} move={md.Move} yaw={md.Yaw:0.0} " +
                             $"desired=({desired.x:0.000},{desired.y:0.000},{desired.z:0.000}) " +
                             $"from=({from.x:0.00},{from.y:0.00},{from.z:0.00}) " +
                             $"applied=({applied.x:0.000},{applied.y:0.000},{applied.z:0.000}) groundY={_lastGroundY:0.00}");
        }

        // [双端 | Tick 驱动] 和解：Kinematic 直接写位置/朝向（没有速度需要恢复，载荷比 Dynamic 方案更小）。
        [Reconcile]
        private void Reconcile(PlayerReconcileState rd, Channel channel = Channel.Unreliable)
        {
            _rigidbody.position = rd.Position;

            // Owner 的朝向完全由输入决定；用服务器回传的旧 yaw 覆盖会表现为视角来回抖动。
            if (_isOwner)
                return;

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
                // 注意原点约定：本预制体的 CapsuleCollider.center = (0, 0.9, 0)，**物体原点在脚底**，
                // 所以胶囊中心要先抬 CapsuleHalfHeight，再取上下两个球心（文档公式假设原点即胶囊中心）。
                Vector3 capsuleCenter = position + Vector3.up * PhysicsTuning.CapsuleHalfHeight;
                Vector3 p1 = capsuleCenter + Vector3.up * PhysicsTuning.CapsuleHalfSegment;
                Vector3 p2 = capsuleCenter - Vector3.up * PhysicsTuning.CapsuleHalfSegment;

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
                // 原点在脚底 → 脚底贴合地面就是原点取地面高度（不要再加 CapsuleHalfHeight）。
                _lastGroundY = hit.point.y;
            }

            return _lastGroundY;
        }
    }
}
