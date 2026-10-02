using System;
using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Ball
{
    /// <summary>
    /// 服务器侧球体生成器：开局生成固定数量的球，并对外暴露 <see cref="SpawnBall"/> 供后续节点复用。
    /// 执行侧：仅服务器；客户端不生成网络对象（AGENTS §5.1）。本类不含上限 / 定时策略（M7 以新组件接入）。
    /// 对象池（2026-10-02）：M7 的定时生成 + 上限淘汰让球一直在上下场，改用 FishNet 自带池复用实例，
    /// 生成走 NetworkManager.GetPooledInstantiated（池空才 Instantiate），回收走 Despawn(nob, DespawnType.Pool)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallSpawner : NetworkBehaviour
    {
        /// <summary>每生成一个球（服务器侧）触发一次；M7 的定时生成 / 上限淘汰订阅它维护自己的队列。</summary>
        public event Action<NetworkObject> BallSpawned;

        /// <summary>每个球被销毁前（服务器侧）触发一次；订阅者（M8 TappedDispatcher）用它反订阅球的事件。
        /// 在真正 Despawn 之前触发，保证订阅者拿到的是仍然有效的对象。</summary>
        public event Action<NetworkObject> BallDespawned;

        [Tooltip("球预制体（需由 FishNet 生成器登记到 DefaultPrefabObjects）。")]
        [SerializeField] private NetworkObject _ballPrefab;

        [Tooltip("生成点；为空时用本对象位置。")]
        [SerializeField] private Transform[] _spawnPoints;

        [Tooltip("开局生成的球数量；M7 的定时生成不经过这里。")]
        [SerializeField] private int _initialBallCount = 1;

        [Tooltip("用 FishNet 对象池复用球：Despawn 时回池而不是 Destroy，下次生成优先从池里取。\n"
                 + "省掉反复 Instantiate / Destroy 的开销与 GC；网络的 Spawn / Despawn 报文不变。")]
        [SerializeField] private bool _usePooling = true;

        [Tooltip("开局预热的球数量（提前 Instantiate 好塞进池里，避免第一次淘汰补球时的实例化卡顿）。\n"
                 + "默认取 PhysicsTuning.MaxBallCount——池里常备够用即可，预热太多只是白占内存。")]
        [SerializeField] private int _prewarmCount = PhysicsTuning.MaxBallCount;

        private int _nextSpawnPoint;
        private int _initialSpawned;
        private bool _isSubscribedToTick;

        // [服务器 | 一次性（OnStartServer）] 订阅 Tick 后再生成：避免在对象自身初始化过程中 Spawn。
        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_ballPrefab == null)
                return;

            // 预热：先 Instantiate 出一批失活的球放进池里（M7 每 15 秒补一个、超 4 个淘汰最旧的，
            // 场上球数在 1~4 之间反复横跳，等于一直在 Instantiate/Destroy——池化就是为这个场景准备的）。
            if (_usePooling && _prewarmCount > 0)
                NetworkManager.CacheObjects(_ballPrefab, _prewarmCount, asServer: true);

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

            return SpawnBallAt(position, rotation);
        }

        /// <summary>[服务器] 在指定位置生成一个球（M7 定时随机位置用），返回生成的球。</summary>
        public NetworkObject SpawnBallAt(Vector3 position)
        {
            return SpawnBallAt(position, Quaternion.identity);
        }

        /// <summary>
        /// [服务器] 在指定位置 / 朝向生成一个球：优先从 FishNet 对象池取，池空了才 Instantiate。
        /// 注意：池取出来的是**已实例化但未 Spawn** 的对象（DefaultObjectPool.RetrieveObject 只做 Instantiate +
        /// 摆位置 + SetActive(true)），网络侧的 Spawn 仍要自己调——池只省实例化，不省同步。
        /// </summary>
        public NetworkObject SpawnBallAt(Vector3 position, Quaternion rotation)
        {
            if (!IsServerInitialized || _ballPrefab == null)
                return null;

            NetworkObject ball = _usePooling
                ? NetworkManager.GetPooledInstantiated(_ballPrefab, position, rotation, asServer: true)
                : Instantiate(_ballPrefab, position, rotation);
            if (ball == null)
                return null;

            ResetForReuse(ball);
            ServerManager.Spawn(ball);

            BallSpawned?.Invoke(ball);
            return ball;
        }

        /// <summary>
        /// [服务器] 回收 / 销毁一个球（M7 上限淘汰用）。先发事件再 Despawn，让订阅者能反订阅。
        /// 走 DespawnType.Pool 时 FishNet 会把它塞回池（客户端收到带 Pool 标记的 Despawn 报文同样回本地池）。
        /// </summary>
        public void DespawnBall(NetworkObject ball)
        {
            if (!IsServerInitialized || ball == null || !ball.IsSpawned)
                return;

            BallDespawned?.Invoke(ball);
            ServerManager.Despawn(ball, _usePooling ? DespawnType.Pool : DespawnType.Destroy);
        }

        /// <summary>
        /// [服务器 | 复用前] 清掉上一次生命留下的刚体状态。
        /// FishNet 的池只重置 NetworkObject 自身状态（ResetState），**不会碰 Rigidbody**：
        /// 不清速度的话，一个高速飞行的球被淘汰后回池，下次生成会带着旧速度从生成点飞出去。
        /// WakeUp 是补另一个坑：失活前已经睡着的刚体重新激活不会自己醒，不清它会悬在空中不下落。
        /// </summary>
        private static void ResetForReuse(NetworkObject ball)
        {
            Rigidbody body = ball.GetComponent<Rigidbody>();
            if (body == null)
                return;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
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
