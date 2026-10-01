using System;
using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Ball
{
    /// <summary>
    /// 服务器侧球体生成器：开局生成固定数量的球，并对外暴露 <see cref="SpawnBall"/> 供后续节点复用。
    /// 执行侧：仅服务器；客户端不生成网络对象（AGENTS §5.1）。本类不含上限 / 定时策略（M7 以新组件接入）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallSpawner : NetworkBehaviour
    {
        /// <summary>每生成一个球（服务器侧）触发一次；M7 的定时生成 / 上限淘汰订阅它维护自己的队列。</summary>
        public event Action<NetworkObject> BallSpawned;

        [Tooltip("球预制体（需由 FishNet 生成器登记到 DefaultPrefabObjects）。")]
        [SerializeField] private NetworkObject _ballPrefab;

        [Tooltip("生成点；为空时用本对象位置。")]
        [SerializeField] private Transform[] _spawnPoints;

        [Tooltip("开局生成的球数量；M7 的定时生成不经过这里。")]
        [SerializeField] private int _initialBallCount = 1;

        private int _nextSpawnPoint;
        private int _initialSpawned;
        private bool _isSubscribedToTick;

        // [服务器 | 一次性（OnStartServer）] 订阅 Tick 后再生成：避免在对象自身初始化过程中 Spawn。
        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_ballPrefab == null)
                return;

            TimeManager.OnTick += OnTick;
            _isSubscribedToTick = true;
        }

        // [服务器 | 一次性（OnStopServer）] 反订阅，避免悬空回调。
        public override void OnStopServer()
        {
            base.OnStopServer();

            if (!_isSubscribedToTick)
                return;

            TimeManager.OnTick -= OnTick;
            _isSubscribedToTick = false;
        }

        /// <summary>
        /// [服务器] 生成一个球并返回它；非服务器或未配置预制体时返回 null。
        /// 每个球按生成点顺序轮转放置。
        /// </summary>
        public NetworkObject SpawnBall()
        {
            if (!IsServerInitialized || _ballPrefab == null)
                return null;

            Transform spawnPoint = NextSpawnPoint();
            Vector3 position = spawnPoint == null
                ? transform.position + Vector3.up * PhysicsTuning.BallSpawnHeight
                : spawnPoint.position;
            Quaternion rotation = spawnPoint == null ? Quaternion.identity : spawnPoint.rotation;

            NetworkObject ball = Instantiate(_ballPrefab, position, rotation);
            ServerManager.Spawn(ball);

            BallSpawned?.Invoke(ball);
            return ball;
        }

        // [服务器 | Tick 驱动] 首个 Tick 补上开局球；达到目标数量后本方法不再做事。
        private void OnTick()
        {
            if (_initialSpawned >= _initialBallCount)
                return;

            _initialSpawned++;
            SpawnBall();
        }

        private Transform NextSpawnPoint()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
                return null;

            Transform point = _spawnPoints[_nextSpawnPoint];
            _nextSpawnPoint = (_nextSpawnPoint + 1) % _spawnPoints.Length;
            return point;
        }
    }
}
