using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace SphereRoom.Player
{
    /// <summary>
    /// 服务器分配的玩家编号（SyncVar）与配色。编号取自所属连接，全房间唯一。
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

        private void Awake()
        {
            // 分配前置：参考类型只在一次性回调里创建，网络回调内不做 new。
            _colorBlock = new MaterialPropertyBlock();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            int index = Owner != null && Owner.IsValid ? Owner.ClientId : 0;
            _playerIndex.Value = index;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            _playerIndex.OnChange += OnPlayerIndexChanged;
            ApplyColor(_playerIndex.Value, false);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _playerIndex.OnChange -= OnPlayerIndexChanged;
        }

        private void OnPlayerIndexChanged(int prev, int next, bool asServer)
        {
            ApplyColor(next, asServer);
        }

        private void ApplyColor(int playerIndex, bool asServer)
        {
            if (_graphic == null || _colorBlock == null)
                return;

            _graphic.GetPropertyBlock(_colorBlock);
            _colorBlock.SetColor(BaseColorId, PlayerPalette.GetColor(playerIndex));
            _graphic.SetPropertyBlock(_colorBlock);
        }
    }
}
