using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using UnityEngine;

namespace SphereRoom.Network
{
    /// <summary>
    /// 客户端传输选择：在 Multipass 挂载的多个传输之间挑一个给本地客户端用，并下发连接地址。
    /// 为什么需要这一层：Multipass 要求客户端**显式**指定 ClientTransport，不指定就 LogError 并连不上
    /// （Multipass.cs:64-89 注释明确要求 production 手动设置）。而上层不该知道具体传输类型。
    /// **零类型依赖设计**：字段类型是 FishNet 的基类 <see cref="Transport"/>，
    /// 所以本程序集不需要引用 FishySteamworks 的程序集——Steam 包没装也能编译，
    /// Inspector 里那个槽位空着即可（运行时走 <see cref="IsAvailable"/> 的失败分支）。
    /// 执行侧：纯本地；只在连接发起时调用一次，不在 Tick 热路径上。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TransportSelector : MonoBehaviour
    {
        [SerializeField] private Multipass _multipass;
        [SerializeField] private Transport _lanTransport;
        [SerializeField] private Transport _steamTransport;

        /// <summary>
        /// [本地 | 事件驱动（发起连接前）] 选定传输并下发地址。
        /// 返回 false 表示目标传输不可用（未挂载 / Steam 未就绪），调用方应转成用户可见提示而不是继续连。
        /// </summary>
        public bool TrySelect(TransportKind kind, string address)
        {
            Transport target = kind == TransportKind.Steam ? _steamTransport : _lanTransport;
            if (_multipass == null || target == null)
                return false;

            // 基类虚方法：Tugboat 与 FishySteamworks 都重写了 SetClientAddress（Transport.cs:195）。
            target.SetClientAddress(address);
            _multipass.SetClientTransport(target);
            return true;
        }

        /// <summary>[本地] 目标传输是否已挂载（用于在 UI 上禁用按钮 / 给出提示）。</summary>
        public bool IsAvailable(TransportKind kind)
        {
            return kind == TransportKind.Steam ? _steamTransport != null : _lanTransport != null;
        }
    }
}
