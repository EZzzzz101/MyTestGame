using System;
using System.Collections.Generic;
using FishNet.Object;
using SphereRoom.Core;
using UnityEngine;

namespace SphereRoom.Ball
{
    /// <summary>
    /// M7 定时生成 + 数量上限：每 SpawnIntervalTicks（15s @ TickRate 50）在房间内随机 XZ 生成一个球，
    /// 场上球数超过 MaxBallCount 时销毁最旧的（FIFO）。
    /// 以新组件接入（AGENTS §8.1）：BallSpawner 只负责"生一个球"，定时与淘汰策略归本组件。
    /// 定时器用 Tick 计数而不是 Update / 协程——与物理同频、确定性好（PROGRESS M7 工作项）。
    /// 执行侧：仅服务器（OnStartServer / OnStopServer 管理订阅）；随机只在服务器跑，客户端不生成网络对象。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallSpawnScheduler : NetworkBehaviour
    {
        [Tooltip("球的生成器（同物体上的 BallSpawner）。")]
        [SerializeField] private BallSpawner _spawner;

        [Tooltip("生成间隔（Tick 数），默认 PhysicsTuning.SpawnIntervalTicks（15 秒）。")]
        [SerializeField] private int _intervalTicks = PhysicsTuning.SpawnIntervalTicks;

        [Tooltip("场上球数量上限，超限销毁最旧的（默认 PhysicsTuning.MaxBallCount）。")]
        [SerializeField] private int _maxBallCount = PhysicsTuning.MaxBallCount;

        [Tooltip("随机 XZ 半幅（米），默认 PhysicsTuning.BallSpawnRandomRange（避开柱子与墙）。")]
        [SerializeField] private float _spawnRange = PhysicsTuning.BallSpawnRandomRange;

        // 按生成顺序排列的球列表（队首最旧）。用 List 而不是 Queue：淘汰/外部销毁时要能移除中间项。
        private readonly List<NetworkObject> _balls = new();

        private System.Random _random;

        // [服务器 | 一次性（OnStartServer）] 订阅生成器与 Tick。
        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_spawner == null)
                _spawner = GetComponent<BallSpawner>();
            if (_spawner == null)
                return;

            // 开局球由 BallSpawner 自己生成，经同一事件入队，保证淘汰时算得上它们。
            _spawner.BallSpawned += OnBallSpawned;
            _spawner.BallDespawned += OnBallDespawned;

            _random = new System.Random();
            TimeManager.OnTick += OnTick;
        }

        // [服务器 | 一次性（OnStopServer）] 反订阅，避免悬空回调。
        public override void OnStopServer()
        {
            base.OnStopServer();

            if (_spawner != null)
            {
                _spawner.BallSpawned -= OnBallSpawned;
                _spawner.BallDespawned -= OnBallDespawned;
            }

            TimeManager.OnTick -= OnTick;
            _balls.Clear();
        }

        // [服务器 | Tick 驱动] 到点生成；开局球（Tick 0）不在这里生成，避免与 BallSpawner 重复。
        private void OnTick()
        {
            if (_spawner == null || _intervalTicks <= 0)
                return;
            if (TimeManager.LocalTick == 0 || TimeManager.LocalTick % _intervalTicks != 0)
                return;

            SpawnRandomBall();
        }

        // [服务器 | Tick 驱动] 随机 XZ（Y 取 BallSpawnHeight）生成一个球，随后按上限淘汰最旧的。
        private void SpawnRandomBall()
        {
            float x = RandomRangeValue();
            float z = RandomRangeValue();
            Vector3 position = new Vector3(x, PhysicsTuning.BallSpawnHeight, z);

            _spawner.SpawnBallAt(position);
            TrimToMax();
        }

        // [热路径][服务器 | Tick 驱动] 超过上限就销毁最旧的（列表队首）。
        private void TrimToMax()
        {
            // 先清掉已被外部销毁 / 已失效的引用（如场景卸载）。
            for (int i = _balls.Count - 1; i >= 0; i--)
            {
                if (_balls[i] == null || !_balls[i].IsSpawned)
                    _balls.RemoveAt(i);
            }

            while (_balls.Count > _maxBallCount)
            {
                NetworkObject oldest = _balls[0];
                _balls.RemoveAt(0);
                // 触发 BallDespawned：订阅者（M8）先反订阅，再真正 Despawn。
                _spawner.DespawnBall(oldest);
            }
        }

        private float RandomRangeValue()
        {
            return ((float)_random.NextDouble() * 2f - 1f) * _spawnRange;
        }

        private void OnBallSpawned(NetworkObject ball)
        {
            if (ball != null)
                _balls.Add(ball);
        }

        private void OnBallDespawned(NetworkObject ball)
        {
            if (ball != null)
                _balls.Remove(ball);
        }
    }
}
