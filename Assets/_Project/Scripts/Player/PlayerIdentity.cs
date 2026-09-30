using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 服务器分配的玩家编号（SyncVar）与配色。编号取自所属连接，全房间唯一。
    /// 执行侧：编号由服务器写入，客户端只读并通过 OnChange 更新表现。
    /// </summary>
    public sealed class PlayerIdentity : NetworkBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("挂 Graphic 子物体的 MeshRenderer（逻辑根与渲染严格分离）。")]
        [SerializeField] private Renderer _graphic;

        // FishNet 4.7 起 [SyncVar] 特性已废弃，改用 SyncVar<T>（以包内 API 为准）。
        private readonly SyncVar<int> _playerIndex = new SyncVar<int>(-1);

        private MaterialPropertyBlock _colorBlock;

        /// <summary>玩家编号：0 为 Host，其余为加入顺序。</summary>
        public int PlayerIndex => _playerIndex.Value;

        // [双端 | 一次性（Awake）] 分配前置：MaterialPropertyBlock 只创建一次，网络回调内不做 new。
        private void Awake()
        {
            // 分配前置：参考类型只在一次性回调里创建，网络回调内不做 new。
            _colorBlock = new MaterialPropertyBlock();
        }

        // [服务器 | 一次性（OnStartServer）] 用连接号作为玩家编号（0 为 Host）。
        public override void OnStartServer()
        {
            base.OnStartServer();

            int index = Owner != null && Owner.IsValid ? Owner.ClientId : 0;
            _playerIndex.Value = index;
        }

        // [双端 | 一次性（OnStartNetwork）] 订阅同步回调，并用当前值初始化一次颜色。
        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            _playerIndex.OnChange += OnPlayerIndexChanged;
            ApplyColor(_playerIndex.Value);
        }

        // [双端 | 一次性（OnStopNetwork）] 反注册同步回调。
        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _playerIndex.OnChange -= OnPlayerIndexChanged;
        }

        // [双端 | 事件驱动（SyncVar 变更）] 编号变化时刷新表现。
        private void OnPlayerIndexChanged(int prev, int next, bool asServer)
        {
            ApplyColor(next);
        }

        private void ApplyColor(int playerIndex)
        {
            if (_graphic == null || _colorBlock == null)
                return;

            _graphic.GetPropertyBlock(_colorBlock);
            _colorBlock.SetColor(BaseColorId, PlayerPalette.GetColor(playerIndex));
            _graphic.SetPropertyBlock(_colorBlock);
        }
    }
}
