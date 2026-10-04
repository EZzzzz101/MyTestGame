using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SphereRoom.Network
{
    /// <summary>
    /// 本机局域网 IPv4 探测：创建房间后把 IP 直接显示给房主，省去让他另开 cmd 跑 ipconfig。
    /// 只认「已启用 + 非回环 + 有 IPv4 单播地址」的网卡；优先取**有 IPv4 默认网关**的那块
    /// （真实内网），避开 Hyper-V / WSL / 虚拟网卡这类没有网关的接口，并跳过 169.254.*（没拿到 DHCP 的自动专用地址）。
    /// 结果进程内只探测一次并缓存：换 Wi-Fi 这类网络环境变化要重进游戏才会刷新，本 Demo 可接受。
    /// 执行侧：纯本地；只在 UI 首次显示 LAN 提示时调用，不在任何热路径上。
    /// </summary>
    public static class LanIpProbe
    {
        /// <summary>探测结果缓存；null = 尚未探测。</summary>
        private static string _cached;

        /// <summary>本机首选局域网 IPv4；一台可用网卡都没有时退回 127.0.0.1（此时也只有本机能连）。</summary>
        public static string PreferredAddress => _cached ?? (_cached = Probe());

        // [仅本地 | 一次性（UI 首次取值）] 枚举网卡找内网地址。枚举有开销，靠缓存保证只发生一次。
        private static string Probe()
        {
            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            string fallback = null;

            for (int i = 0; i < adapters.Length; i++)
            {
                NetworkInterface adapter = adapters[i];
                if (adapter.OperationalStatus != OperationalStatus.Up)
                    continue;
                // 回环必然连不到别的机器，直接跳过。
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                IPInterfaceProperties properties = adapter.GetIPProperties();
                if (properties == null)
                    continue;

                IPAddress candidate = FindUnicastIpv4(properties);
                if (candidate == null)
                    continue;

                string address = candidate.ToString();
                if (HasIpv4Gateway(properties))
                    return address;

                // 没网关的（虚拟网卡等）先记着：全部枚举完都没有「有网关」的再用它。
                if (fallback == null)
                    fallback = address;
            }

            return fallback ?? "127.0.0.1";
        }

        // [仅本地] 该网卡是否存在 IPv4 默认网关。有网关 ≈ 真实联网的物理网卡，优先级最高。
        private static bool HasIpv4Gateway(IPInterfaceProperties properties)
        {
            GatewayIPAddressInformationCollection gateways = properties.GatewayAddresses;
            if (gateways == null)
                return false;

            for (int i = 0; i < gateways.Count; i++)
            {
                GatewayIPAddressInformation information = gateways[i];
                IPAddress gateway = information != null ? information.Address : null;
                if (gateway != null && gateway.AddressFamily == AddressFamily.InterNetwork)
                    return true;
            }

            return false;
        }

        // [仅本地] 取该网卡上第一个可用 IPv4 单播地址；跳过回环与 169.254.*（链路本地自动地址 = 没拿到 DHCP）。
        private static IPAddress FindUnicastIpv4(IPInterfaceProperties properties)
        {
            UnicastIPAddressInformationCollection addresses = properties.UnicastAddresses;
            if (addresses == null)
                return null;

            for (int i = 0; i < addresses.Count; i++)
            {
                UnicastIPAddressInformation information = addresses[i];
                if (information == null)
                    continue;

                IPAddress address = information.Address;
                if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(address))
                    continue;

                byte[] bytes = address.GetAddressBytes();
                if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254)
                    continue;

                return address;
            }

            return null;
        }
    }
}
