using UnityEditor;
using UnityEngine;

namespace SphereRoom.Core.Editor
{
    /// <summary>
    /// 接线工具的统一前置守卫：**Play 模式下禁止改场景资产**。
    /// 执行侧：编辑器；被各 SphereRoom/Setup/… 菜单项在动场景之前调用。
    /// </summary>
    /// <remarks>
    /// 为什么必须拦这一下（2026-10-04 实测事故，别删这条注释）：
    /// 1. FishNet 的 <c>NetworkManager</c> 上 <c>_dontDestroyOnLoad: 1</c>，Play 开始后 GameObject 会被
    ///    移进 DontDestroyOnLoad 场景。此时 <c>EditorSceneManager.SaveScene(Boot)</c> 写的是
    ///    **不包含它的** Boot 场景 → NetworkManager 连同它身上十来个组件被整个从场景文件里抹掉
    ///    （TimeManager / TransportManager / Tugboat / Multipass / FishySteamworks / PlayerSpawner /
    ///    NetworkBootstrap / TransportSelector / SteamBootstrap / SteamLobbyInvite / NetworkDebugHud）。
    ///    工具日志还显示"接线完成"，但场景已经废了，而且不重新打开场景根本看不出来。
    /// 2. Play 模式下对场景的其它修改本来就随退出播放一起丢弃，工具"跑完了"其实什么都没留下，
    ///    白白制造一次误导。
    ///
    /// 补充：ParrelSync 克隆体的 Assets 是指向本体的链接，所以在克隆体里误触菜单
    /// **直接改坏的就是主工程的场景**——这就是为什么必须在本体与克隆体两侧都拦住。
    /// </remarks>
    public static class EditorWiringGuard
    {
        /// <summary>
        /// [编辑器 | 手动执行菜单时] 确认当前不在 Play 模式。返回 false 时调用方必须立刻 return，
        /// 一行场景都不要动。
        /// </summary>
        public static bool CanModifyScene(string toolName)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                return true;

            Debug.LogError($"[SphereRoom] 「{toolName}」不能在 Play 模式下执行，已中止（场景未做任何改动）。\n" +
                           "原因：Play 模式下带 DontDestroyOnLoad 的 NetworkManager 不在 Boot 场景里，" +
                           "此时保存场景会把它从场景文件中永久抹掉；\n" +
                           "另外 Play 模式下的场景改动退出播放后本来就会全部丢弃。\n" +
                           "请先退出 Play 模式（克隆体和本体都退），再执行本菜单。");
            return false;
        }
    }
}
