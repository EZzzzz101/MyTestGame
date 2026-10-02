using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Object;
using UnityEngine;

namespace SphereRoom.Ball
{
    /// <summary>
    /// M8 Tapped 派发：服务器把「你被球碰到 / 球离开你了」用 TargetRpc 只发给被碰的那个玩家（T7：A 撞球只有 A 显示）。
    /// 挂在 Room 场景 GameManager 上（与 BallSpawner 同物体）：服务器侧订阅每个球的"接触开始 / 结束"事件，
    /// 维护「谁正被球贴着」的接触表，按 KickerClientId 找到对应连接下发；
    /// 被碰玩家本机的 TappedIndicator（场景 UI）订阅本组件的 Tapped / Released。
    /// **为什么不让 UI 直接订阅球**：Tapped UI 是常驻场景对象，球是运行时生成的，
    /// 场景对象拿不到生成后才出现的东西的引用；经 GameManager 中转，UI 可以在场景里稳定序列化订阅。
    /// 执行侧：判定 [服务器]、下发 [被碰玩家本机]。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TappedDispatcher : NetworkBehaviour
    {
        /// <summary>本客户端被球碰到（接触开始）。</summary>
        public event Action Tapped;

        /// <summary>本客户端身上的球都分开了（接触结束，或碰到的球被销毁）。</summary>
        public event Action Released;

        [Tooltip("球的生成器（同物体上的 BallSpawner），用于订阅每个生成出来的球的接触开始 / 结束事件。")]
        [SerializeField] private BallSpawner _spawner;

        /// <summary>
        /// 当前存在的「球 × 玩家」接触对（服务器侧权威状态）。
        /// 用"对"而不是计数器：挤压时 OnCollisionEnter/Exit 可能来回抖动，按 (球, 玩家) 去重后
        /// 不会把同一次接触算成两次，也能容忍漏掉的 Enter（Exit 找不到就直接忽略）。
        /// 场上最多 MaxBallCount × 玩家数 项（4×4），线性扫描无所谓。
        /// </summary>
        private readonly List<Contact> _contacts = new();

        private struct Contact
        {
            public int BallId;
            public int ClientId;
        }

        // [服务器 | 一次性（OnStartServer）] 订阅球的生成 / 销毁，逐个接线撞击 / 分离事件。
        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_spawner == null)
                _spawner = GetComponent<BallSpawner>();
            if (_spawner == null)
                return;

            _spawner.BallSpawned += OnBallSpawned;
            _spawner.BallDespawned += OnBallDespawned;
        }

        // [服务器 | 一次性（OnStopServer）] 反订阅 + 清空接触状态（场景卸载时别把残留状态带进下一局）。
        public override void OnStopServer()
        {
            base.OnStopServer();

            if (_spawner != null)
            {
                _spawner.BallSpawned -= OnBallSpawned;
                _spawner.BallDespawned -= OnBallDespawned;
            }

            _contacts.Clear();
        }

        // [服务器 | 事件驱动] 新球生成 → 订阅它的撞击 / 分离事件（本物体上的球在 OnStartServer 前已生成的由 BallSpawned 覆盖）。
        private void OnBallSpawned(NetworkObject ball)
        {
            BallImpactDispatcher dispatcher = ball != null ? ball.GetComponent<BallImpactDispatcher>() : null;
            if (dispatcher == null)
                return;

            // 订阅"接触开始/结束"而不是"撞击广播"：撞击广播被 0.2s 节流过，拿它当接触状态会漏。
            dispatcher.BallContacted += OnBallContacted;
            dispatcher.BallSeparated += OnBallSeparated;
        }

        // [服务器 | 事件驱动] 球被销毁 → 反订阅，并清掉它残留的接触（否则 Tapped 会永远亮着）。
        // DespawnBall 先发本事件再真正 Despawn，此时 ObjectId 仍有效。
        private void OnBallDespawned(NetworkObject ball)
        {
            if (ball == null)
                return;

            BallImpactDispatcher dispatcher = ball.GetComponent<BallImpactDispatcher>();
            if (dispatcher != null)
            {
                dispatcher.BallContacted -= OnBallContacted;
                dispatcher.BallSeparated -= OnBallSeparated;
            }

            RemoveBallContacts(ball.ObjectId);
        }

        // [服务器 | 事件驱动] 球碰到玩家 → 记下接触对，再按"该玩家身上还有没有球"下发状态。
        private void OnBallContacted(BallImpactData data)
        {
            if (data.KickerClientId < 0)
                return;

            if (!HasContact(data.BallObjectId, data.KickerClientId))
            {
                _contacts.Add(new Contact { BallId = data.BallObjectId, ClientId = data.KickerClientId });
                SendState(data.KickerClientId);
            }
        }

        // [服务器 | 事件驱动] 球与玩家分开 → 移除接触对，再按"还有没有别的球贴着"下发状态。
        private void OnBallSeparated(BallImpactData data)
        {
            if (data.KickerClientId < 0)
                return;

            if (TryRemoveContact(data.BallObjectId, data.KickerClientId))
                SendState(data.KickerClientId);
        }

        /// <summary>
        /// [服务器 | 事件驱动] 把"你正被球贴着吗"的最新结果下发给该玩家。
        /// 只有全部接触都断开才发 false——两个球同时贴着时，走一个不该把另一个的提示一起收掉。
        /// </summary>
        private void SendState(int clientId)
        {
            if (!ServerManager.Clients.TryGetValue(clientId, out NetworkConnection connection) || !connection.IsValid)
            {
                // 玩家已经掉线：接触状态没有意义，顺手清掉，避免列表里留垃圾。
                RemoveClientContacts(clientId);
                return;
            }

            RpcSetTapped(connection, ClientHasAnyContact(clientId));
        }

        // [被碰玩家本机 | 事件驱动] 收到服务器状态，翻成本地事件给 UI。
        [TargetRpc]
        private void RpcSetTapped(NetworkConnection connection, bool tapped)
        {
            if (tapped)
                Tapped?.Invoke();
            else
                Released?.Invoke();
        }

        private bool HasContact(int ballId, int clientId)
        {
            for (int i = 0; i < _contacts.Count; i++)
            {
                if (_contacts[i].BallId == ballId && _contacts[i].ClientId == clientId)
                    return true;
            }

            return false;
        }

        private bool ClientHasAnyContact(int clientId)
        {
            for (int i = 0; i < _contacts.Count; i++)
            {
                if (_contacts[i].ClientId == clientId)
                    return true;
            }

            return false;
        }

        private bool TryRemoveContact(int ballId, int clientId)
        {
            for (int i = 0; i < _contacts.Count; i++)
            {
                if (_contacts[i].BallId == ballId && _contacts[i].ClientId == clientId)
                {
                    _contacts.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private void RemoveBallContacts(int ballId)
        {
            // 先摘完再统一重算：SendState 在玩家已掉线时会反过来清 _contacts，边遍历边改会错位。
            List<int> affected = new();
            for (int i = _contacts.Count - 1; i >= 0; i--)
            {
                if (_contacts[i].BallId != ballId)
                    continue;

                affected.Add(_contacts[i].ClientId);
                _contacts.RemoveAt(i);
            }

            // 球没了，它压着的那些玩家的提示要按"还剩什么"重新算。
            for (int i = 0; i < affected.Count; i++)
                SendState(affected[i]);
        }

        private void RemoveClientContacts(int clientId)
        {
            for (int i = _contacts.Count - 1; i >= 0; i--)
            {
                if (_contacts[i].ClientId == clientId)
                    _contacts.RemoveAt(i);
            }
        }
    }
}
