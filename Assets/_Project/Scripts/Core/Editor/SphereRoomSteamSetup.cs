using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using SphereRoom.Network;
using SphereRoom.Steam;
using SphereRoom.UI;
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SphereRoom.Core.Editor
{
    /// <summary>
    /// M9 接线工具：把 Boot 场景的传输层从「单挂 Tugboat」升级为「Multipass 挂 Tugboat + FishySteamworks」，
    /// 并挂上 Steam 生命周期与邀请组件。只做资产接线，不含任何运行时逻辑。
    /// 幂等：重复执行复用已有组件，不会挂出第二份。
    /// 沿用 SphereRoomSetup 的做法（SerializedObject 写私有序列化字段），避免手工接线漂移。
    /// </summary>
    /// <remarks>
    /// 为什么 FishySteamworks 要用反射拿类型：它放在 Assets/Plugins 下、没有 asmdef，
    /// 因此编译进 Assembly-CSharp；本编辑器程序集不引用 Assembly-CSharp，靠类型全名解析。
    /// 这样做的好处是 Steam 包没装时本工具只是提示缺失，不会让编辑器程序集编译失败。
    /// </remarks>
    public static class SphereRoomSteamSetup
    {
        private const string BootScenePath = "Assets/_Project/Scenes/Boot.unity";
        private const string FishySteamworksTypeName = "FishySteamworks.FishySteamworks";

        [MenuItem("SphereRoom/Setup/M9 Steam 接线（Multipass + FishySteamworks + 邀请）", priority = 11)]
        public static void ApplySteamWiring()
        {
            Type fishyType = FindFishySteamworksType();
            if (fishyType == null)
            {
                Debug.LogError($"[SphereRoom] 找不到类型 {FishySteamworksTypeName}。" +
                               "请确认已把 FishySteamworks 放进 Assets/Plugins/（含 FishySteamworks.cs）。");
                return;
            }

            // 工具会切场景：执行前提醒用户保存。
            Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);

            NetworkManager networkManager = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
            NetworkBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<NetworkBootstrap>();
            if (networkManager == null || bootstrap == null)
            {
                Debug.LogError("[SphereRoom] Boot 场景缺少 NetworkManager / NetworkBootstrap，请先执行场景重建。");
                return;
            }

            GameObject target = networkManager.gameObject;

            Tugboat tugboat = target.GetComponent<Tugboat>();
            if (tugboat == null)
            {
                Debug.LogError("[SphereRoom] NetworkManager 上没有 Tugboat，无法接线 Multipass。");
                return;
            }

            Component fishy = EnsureFishySteamworks(target, fishyType);

            Multipass multipass = EnsureMultipass(target, tugboat, fishy);

            TransportManager transportManager = target.GetComponent<TransportManager>();
            if (transportManager == null)
            {
                Debug.LogError("[SphereRoom] NetworkManager 上没有 TransportManager。");
                return;
            }

            transportManager.Transport = multipass;
            EditorUtility.SetDirty(transportManager);

            TransportSelector selector = EnsureTransportSelector(target, multipass, tugboat, fishy);
            SetObjectReference(bootstrap, "_transportSelector", selector);

            SteamBootstrap steamBootstrap = target.GetComponent<SteamBootstrap>();
            if (steamBootstrap == null)
                steamBootstrap = target.gameObject.AddComponent<SteamBootstrap>();

            SteamLobbyInvite invite = target.GetComponent<SteamLobbyInvite>();
            if (invite == null)
                invite = target.gameObject.AddComponent<SteamLobbyInvite>();
            SetObjectReference(invite, "_bootstrap", bootstrap);

            // UI 入口：给主菜单加「邀请 Steam 好友」按钮（放在「退出游戏」下方，同为对局中可见）。
            MainMenuUI menu = UnityEngine.Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            if (menu == null)
            {
                Debug.LogWarning("[SphereRoom] 没找到 MainMenuUI，已跳过 Steam 邀请按钮接线（传输接线仍然完成）。");
            }
            else
            {
                Button steamButton = EnsureSteamInviteButton(menu.transform);
                SetObjectReference(menu, "_steamInviteButton", steamButton);
                SetObjectReference(menu, "_steamInvite", invite);
                steamButton.gameObject.SetActive(false);
            }

            EditorSceneManager.SaveScene(scene);
            Debug.Log("[SphereRoom] M9 Steam 接线完成：Multipass(Tugboat + FishySteamworks) + SteamBootstrap + SteamLobbyInvite。" +
                      "记得给 Boot 菜单加一个「Steam 邀请」按钮调用 SteamLobbyInvite.CreateLobbyAndInvite()。");
        }

        /// <summary>在所有已加载程序集里按全名找 FishySteamworks 类型（它编译进 Assembly-CSharp）。</summary>
        private static Type FindFishySteamworksType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type t = assemblies[i].GetType(FishySteamworksTypeName);
                if (t != null)
                    return t;
            }

            return null;
        }

        // 复用已有组件；没有才加，并把 P2P 打开（FishySteamworks README 要求 P2P 走 Steam relay）。
        private static Component EnsureFishySteamworks(GameObject target, Type fishyType)
        {
            Component existing = target.GetComponent(fishyType);
            if (existing != null)
            {
                SetBool(existing, "_peerToPeer", true);
                return existing;
            }

            Component added = target.AddComponent(fishyType);
            SetBool(added, "_peerToPeer", true);
            return added;
        }

        // Multipass 的 transports 顺序即 index 顺序：[0]=LAN(Tugboat)、[1]=Steam。
        private static Multipass EnsureMultipass(GameObject target, Transport lan, Component steam)
        {
            Multipass multipass = target.GetComponent<Multipass>();
            if (multipass == null)
                multipass = target.AddComponent<Multipass>();

            SerializedObject so = new SerializedObject(multipass);
            SerializedProperty transports = so.FindProperty("_transports");
            transports.arraySize = 2;
            transports.GetArrayElementAtIndex(0).objectReferenceValue = lan;
            transports.GetArrayElementAtIndex(1).objectReferenceValue = steam;
            so.ApplyModifiedPropertiesWithoutUndo();

            return multipass;
        }

        private static TransportSelector EnsureTransportSelector(GameObject target, Multipass multipass, Transport lan, Component steam)
        {
            TransportSelector selector = target.GetComponent<TransportSelector>();
            if (selector == null)
                selector = target.AddComponent<TransportSelector>();

            SetObjectReference(selector, "_multipass", multipass);
            SetObjectReference(selector, "_lanTransport", lan);
            SetObjectReference(selector, "_steamTransport", steam);
            return selector;
        }

        /// <summary>复用或创建「邀请 Steam 好友」按钮：左上角，「退出游戏」(24,-24,高48) 正下方。</summary>
        private static Button EnsureSteamInviteButton(Transform parent)
        {
            Transform existing = parent.Find("Steam Invite Button");
            if (existing != null)
            {
                Button existingButton = existing.GetComponent<Button>();
                if (existingButton != null)
                    return existingButton;
            }

            GameObject go = new GameObject("Steam Invite Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -80f);
            rt.sizeDelta = new Vector2(180f, 48f);

            Image image = go.GetComponent<Image>();
            image.color = new Color(0.13f, 0.52f, 0.36f, 1f);
            // 按钮自带 onClick 由 MainMenuUI 在 Awake 里订阅，这里不挂持久监听。

            GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            RectTransform labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            Text label = labelGo.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = "邀请 Steam 好友";
            label.fontSize = 20;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;

            return go.GetComponent<Button>();
        }

        /// <summary>写私有 / 公有序列化字段（对象引用）。沿用 SphereRoomSetup 的做法。</summary>
        private static void SetObjectReference(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            if (target == null)
                return;

            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[SphereRoom] {target.GetType().Name} 上找不到字段 {fieldName}，已跳过。");
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(UnityEngine.Object target, string fieldName, bool value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[SphereRoom] {target.GetType().Name} 上找不到字段 {fieldName}，已跳过。");
                return;
            }

            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
