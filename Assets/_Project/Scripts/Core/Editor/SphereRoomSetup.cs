using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using SphereRoom.Ball;
using SphereRoom.Network;
using SphereRoom.Player;
using SphereRoom.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SphereRoom.Core.Editor
{
    /// <summary>
    /// M1 接线工具：一键生成 Player 预制体与 Boot / Room 场景。
    /// 幂等：每次执行都整体重建这几份资产，不做增量修改，避免手工接线漂移。
    /// 只做资产组装，不含任何运行时逻辑。
    /// </summary>
    public static class SphereRoomSetup
    {
        private const string InputActionsPath = "Assets/_Project/Input/SphereRoom.inputactions";
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player.prefab";
        private const string BallPrefabPath = "Assets/_Project/Prefabs/SharedBall.prefab";
        private const string PlayerMaterialPath = "Assets/_Project/Materials/Mat_PlayerGraphic.mat";
        private const string FloorMaterialPath = "Assets/_Project/Materials/Mat_Floor.mat";
        private const string WallMaterialPath = "Assets/_Project/Materials/Mat_Wall.mat";
        private const string PillarMaterialPath = "Assets/_Project/Materials/Mat_Pillar.mat";
        private const string BallMaterialPath = "Assets/_Project/Materials/Mat_Ball.mat";
        private const string BallPhysicsMaterialPath = "Assets/_Project/Materials/Phys_Ball.physicMaterial";
        private const string BootScenePath = "Assets/_Project/Scenes/Boot.unity";
        private const string RoomScenePath = "Assets/_Project/Scenes/Room.unity";
        private const string PresenceClipPath = "Assets/_Project/Audio/进入退出音效.mp3";
        private const string KickClipPath = "Assets/_Project/Audio/足球音效.mp3";

        private const ushort DefaultPort = 7770;

        // 房间原型尺寸（Cube 拼接；物理参数集中在 PhysicsTuning）
        private const float RoomSize = 20f;
        private const float WallHeight = 3f;
        private const float WallThickness = 0.6f;
        private const float CeilingThickness = 0.5f;
        private const float PillarSize = 0.8f;
        private const int BallSpawnPointCount = 4;

        /// <summary>一键重建全部接线资产：SharedBall / Player 预制体 + Boot/Room 场景。</summary>
        [MenuItem("SphereRoom/Setup/一键重建（预制体 + Boot/Room 场景）", priority = 0)]
        public static void RebuildAll()
        {
            NetworkObject ballPrefab = BuildSharedBallPrefab();
            NetworkObject playerPrefab = BuildPlayerPrefab();
            BuildRoomScene(ballPrefab);
            BuildBootScene(playerPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[SphereRoom] 接线完成：SharedBall / Player 预制体 + Boot/Room 场景已重建，FishNet 可生成预制体列表已刷新。");
        }

        /// <summary>只重建 Player 预制体（改了脚本或预制体结构时用）。</summary>
        [MenuItem("SphereRoom/Setup/仅重建 Player 预制体", priority = 20)]
        public static void RebuildPlayerOnly()
        {
            BuildPlayerPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SphereRoom] Player 预制体已重建。");
        }

        /// <summary>只重建 SharedBall 预制体（改了球脚本或球的层级时用）。</summary>
        [MenuItem("SphereRoom/Setup/仅重建 SharedBall 预制体", priority = 22)]
        public static void RebuildBallOnly()
        {
            BuildSharedBallPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SphereRoom] SharedBall 预制体已重建。");
        }

        /// <summary>只重建 Boot / Room 场景（预制体未变时用）。</summary>
        [MenuItem("SphereRoom/Setup/仅重建场景（Boot + Room）", priority = 21)]
        public static void RebuildScenesOnly()
        {
            GameObject playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            GameObject ballAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
            if (playerAsset == null || ballAsset == null)
            {
                Debug.LogError("[SphereRoom] 找不到 Player / SharedBall 预制体，请先执行对应的「仅重建 … 预制体」。");
                return;
            }

            BuildRoomScene(ballAsset.GetComponent<NetworkObject>());
            BuildBootScene(playerAsset.GetComponent<NetworkObject>());
            AssetDatabase.SaveAssets();
            Debug.Log("[SphereRoom] Boot / Room 场景已重建。");
        }

        /// <summary>
        /// M5+M6 增量接线：只新增对象与组件，不重建既有资产（与「一键重建」互不影响，可重复执行）。
        /// 内容：① Room 场景 GameManager 挂 RoomAnnouncer + 新建 Toast Canvas（无 GraphicRaycaster，永不遮挡射线）；
        /// ② Boot 场景 Menu Canvas 下新建 Room Closed Panel（房间解散提示）；
        /// ③ SharedBall 预制体 Graphic 挂 3D 音源 + 根挂 BallAudioView，绑两个音效。
        /// ⚠️ 执行前先保存当前打开的场景——本方法会切换活动场景（Single 模式打开 Boot / Room）。
        /// </summary>
        [MenuItem("SphereRoom/Setup/M5+M6 增量接线（进入退出提示 + 音效 + 房间解散）", priority = 10)]
        public static void ApplyM5M6Increment()
        {
            AudioClip presenceClip = AssetDatabase.LoadAssetAtPath<AudioClip>(PresenceClipPath);
            AudioClip kickClip = AssetDatabase.LoadAssetAtPath<AudioClip>(KickClipPath);
            if (presenceClip == null || kickClip == null)
            {
                Debug.LogError($"[SphereRoom] 找不到音效资产：{PresenceClipPath} / {KickClipPath}。");
                return;
            }

            // 先做完 Room 场景的两段（M5 提示 + M8 Tapped），再切 Boot，最后改预制体。
            ApplyRoomSceneM5(presenceClip);
            ApplyRoomSceneM8();
            ApplyBootSceneM6();
            ApplyBallPrefabM5(kickClip);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SphereRoom] M5+M6 增量接线完成：Room 广播与提示 UI、Boot 房间解散面板、球体音源已就位。");
        }

        /// <summary>
        /// M5 · Room 场景：GameManager 挂 RoomAnnouncer；Toast Canvas 及其子节点**只补不删**（非破坏式）。
        /// ⚠️ 早期版本是"整棵 Canvas 删了重建"，把手放在 Canvas 下的自定义节点（如 M8 的 Tapped）一起删掉了——
        /// 已改为：已有 Canvas 复用、子节点缺哪个建哪个、绝不删除用户摆放的节点（2026-10-02 修复）。
        /// </summary>
        private static void ApplyRoomSceneM5(AudioClip presenceClip)
        {
            Scene scene = EditorSceneManager.OpenScene(RoomScenePath, OpenSceneMode.Single);

            BallSpawner spawner = Object.FindFirstObjectByType<BallSpawner>();
            if (spawner == null)
            {
                Debug.LogError("[SphereRoom] Room 场景里找不到 GameManager（BallSpawner），请先执行场景重建。");
                return;
            }

            RoomAnnouncer announcer = spawner.GetComponent<RoomAnnouncer>();
            if (announcer == null)
                announcer = spawner.gameObject.AddComponent<RoomAnnouncer>();

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 复用已有 Toast Canvas（找不到才建）：不能删——下面可能有用户手放的节点。
            RoomToastUI toastUi = Object.FindFirstObjectByType<RoomToastUI>(FindObjectsInactive.Include);
            GameObject canvasObject;
            if (toastUi != null)
            {
                canvasObject = toastUi.gameObject;
            }
            else
            {
                canvasObject = new GameObject("Toast Canvas");
                Canvas canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 5;
                // 故意不挂 GraphicRaycaster：整块画布永不参与 UI 射线检测（需求：不遮挡射线）。
                toastUi = canvasObject.AddComponent<RoomToastUI>();
            }

            // Presence Toast：缺才建，已有则复用（保留用户的布局调整）。
            Transform presenceTransform = canvasObject.transform.Find("Presence Toast");
            GameObject toastObject;
            if (presenceTransform != null)
            {
                toastObject = presenceTransform.gameObject;
                if (toastObject.GetComponent<CanvasGroup>() == null)
                    toastObject.AddComponent<CanvasGroup>();
                if (toastObject.GetComponent<Text>() == null)
                    toastObject.AddComponent<Text>();
            }
            else
            {
                toastObject = new GameObject("Presence Toast", typeof(RectTransform), typeof(Text), typeof(CanvasGroup));
                toastObject.transform.SetParent(canvasObject.transform, false);
                RectTransform toastRect = toastObject.GetComponent<RectTransform>();
                toastRect.anchorMin = new Vector2(0.5f, 1f);
                toastRect.anchorMax = new Vector2(0.5f, 1f);
                toastRect.pivot = new Vector2(0.5f, 1f);
                toastRect.anchoredPosition = new Vector2(0f, -48f);
                toastRect.sizeDelta = new Vector2(640f, 44f);
            }

            Text toastText = toastObject.GetComponent<Text>();
            toastText.font = font;
            toastText.fontSize = 24;
            toastText.alignment = TextAnchor.MiddleCenter;
            toastText.color = Color.white;

            CanvasGroup group = toastObject.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            // Toast Audio：缺才建。
            Transform audioTransform = canvasObject.transform.Find("Toast Audio");
            GameObject audioObject;
            if (audioTransform != null)
            {
                audioObject = audioTransform.gameObject;
            }
            else
            {
                audioObject = new GameObject("Toast Audio");
                audioObject.transform.SetParent(canvasObject.transform, false);
            }

            AudioSource source = audioObject.GetComponent<AudioSource>();
            if (source == null)
                source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            SetObjectReference(toastUi, "_announcer", announcer);
            SetObjectReference(toastUi, "_toastText", toastText);
            SetObjectReference(toastUi, "_canvasGroup", group);
            SetObjectReference(toastUi, "_audioSource", source);
            SetObjectReference(toastUi, "_presenceClip", presenceClip);

            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// M7+M8 · Room 场景：GameManager 挂 BallSpawnScheduler（定时生成 + 4 球上限）与 TappedDispatcher（TargetRpc 派发）；
        /// Toast Canvas 挂 TappedIndicator 并接上你放好的 Tapped 节点。
        /// **非破坏式**：组件 / 节点存在就复用只刷接线，缺什么补什么——重复跑不会删掉用户在场景里摆好的东西。
        /// </summary>
        private static void ApplyRoomSceneM8()
        {
            BallSpawner spawner = Object.FindFirstObjectByType<BallSpawner>();
            RoomToastUI toastUi = Object.FindFirstObjectByType<RoomToastUI>(FindObjectsInactive.Include);
            if (spawner == null || toastUi == null)
            {
                Debug.LogError("[SphereRoom] Room 场景缺少 GameManager 或 Toast Canvas，请先跑 M5+M6 增量接线 / 场景重建。");
                return;
            }

            // 1) GameManager：定时生成调度器（默认参数来自 PhysicsTuning，无需接线）。
            BallSpawnScheduler scheduler = spawner.GetComponent<BallSpawnScheduler>();
            if (scheduler == null)
                scheduler = spawner.gameObject.AddComponent<BallSpawnScheduler>();
            SetObjectReference(scheduler, "_spawner", spawner);

            // 1b) 对象池：球被定时生成 + 超限淘汰反复上下场，开启复用并预热到上限数量。
            SetBool(spawner, "_usePooling", true);
            SetInt(spawner, "_prewarmCount", PhysicsTuning.MaxBallCount);

            // 2) GameManager：Tapped 派发器（服务器按 KickerClientId → TargetRpc）。
            TappedDispatcher tappedDispatcher = spawner.GetComponent<TappedDispatcher>();
            if (tappedDispatcher == null)
                tappedDispatcher = spawner.gameObject.AddComponent<TappedDispatcher>();
            SetObjectReference(tappedDispatcher, "_spawner", spawner);

            // 3) Toast Canvas：Tapped 提示。用户在 Canvas 下放好了 Tapped 就直接用（不碰它的布局）；
            // 没有（例如被旧版工具误删过）就按默认位置建一个。
            Transform tapped = toastUi.transform.Find("Tapped");
            if (tapped == null)
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                GameObject tappedObject = new GameObject("Tapped", typeof(RectTransform), typeof(Text));
                tappedObject.transform.SetParent(toastUi.transform, false);

                RectTransform tappedRect = tappedObject.GetComponent<RectTransform>();
                tappedRect.anchorMin = new Vector2(0.5f, 1f);
                tappedRect.anchorMax = new Vector2(0.5f, 1f);
                tappedRect.pivot = new Vector2(0.5f, 1f);
                tappedRect.anchoredPosition = new Vector2(0f, -110f);   // 提示文字下方一行
                tappedRect.sizeDelta = new Vector2(400f, 40f);

                Text tappedText = tappedObject.GetComponent<Text>();
                tappedText.text = "Tapped";
                tappedText.font = font;
                tappedText.fontSize = 28;
                tappedText.alignment = TextAnchor.MiddleCenter;
                tappedText.color = Color.yellow;
                tappedText.raycastTarget = false;

                tapped = tappedObject.transform;
                Debug.LogWarning("[SphereRoom] Toast Canvas 下没有 Tapped，已按默认位置新建一个（可自行拖动调整）。");
            }

            TappedIndicator indicator = toastUi.GetComponent<TappedIndicator>();
            if (indicator == null)
                indicator = toastUi.gameObject.AddComponent<TappedIndicator>();
            SetObjectReference(indicator, "_dispatcher", tappedDispatcher);
            SetObjectReference(indicator, "_tappedObject", tapped.gameObject);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }

        /// <summary>M6 · Boot 场景：Menu Canvas 下的 Room Closed Panel——非破坏式，已有则复用只刷接线。</summary>
        private static void ApplyBootSceneM6()
        {
            Scene scene = EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);

            MainMenuUI menuUi = Object.FindFirstObjectByType<MainMenuUI>(FindObjectsInactive.Include);
            NetworkBootstrap bootstrap = Object.FindFirstObjectByType<NetworkBootstrap>();
            if (menuUi == null || bootstrap == null)
            {
                Debug.LogError("[SphereRoom] Boot 场景缺少 Menu Canvas 或 NetworkManager，请先执行场景重建。");
                return;
            }

            // 主菜单面板是 MainMenuUI 的私有字段，经序列化对象读取再转交给 RoomClosedUI。
            SerializedObject menuSerialized = new SerializedObject(menuUi);
            Object menuPanel = menuSerialized.FindProperty("_menuPanel")?.objectReferenceValue;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 复用已有面板（不删——用户在里面摆的东西要保留）；没有才建。
            Button returnButton;
            Transform existingPanel = menuUi.transform.Find("Room Closed Panel");
            GameObject panel;
            if (existingPanel != null)
            {
                panel = existingPanel.gameObject;
                Transform existingButton = panel.transform.Find("Return Button");
                returnButton = existingButton != null
                    ? existingButton.GetComponent<Button>()
                    : CreateButton(panel.transform, "Return Button", "返回大厅", font, new Vector2(0f, -40f));
            }
            else
            {
                panel = CreatePanel(menuUi.transform, "Room Closed Panel", new Vector2(420f, 230f), Vector2.zero);
                CreateText(panel.transform, "Title", "房间已解散", font, 30, new Vector2(0f, 60f), new Vector2(380f, 46f));
                CreateText(panel.transform, "Reason", "与主机的连接已断开", font, 18, new Vector2(0f, 18f), new Vector2(380f, 30f));
                returnButton = CreateButton(panel.transform, "Return Button", "返回大厅", font, new Vector2(0f, -40f));
            }

            // 组件同样复用：重复跑工具不该挂出第二个 RoomClosedUI。
            RoomClosedUI closedUi = menuUi.GetComponent<RoomClosedUI>();
            if (closedUi == null)
                closedUi = menuUi.gameObject.AddComponent<RoomClosedUI>();
            SetObjectReference(closedUi, "_bootstrap", bootstrap);
            SetObjectReference(closedUi, "_panel", panel);
            SetObjectReference(closedUi, "_returnButton", returnButton);
            SetObjectReference(closedUi, "_menuPanel", menuPanel);

            // 默认隐藏（运行期由 RoomClosedUI.Awake 再兜底一次）。
            panel.SetActive(false);

            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>M5 · SharedBall 预制体：Graphic 挂 3D 音源，根挂 BallAudioView（普通 MonoBehaviour，不动 NetworkObject 行为列表）。</summary>
        private static void ApplyBallPrefabM5(AudioClip kickClip)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(BallPrefabPath);

            Transform graphic = root.transform.Find("Graphic");
            BallImpactDispatcher dispatcher = root.GetComponent<BallImpactDispatcher>();
            if (graphic == null || dispatcher == null)
            {
                Debug.LogError("[SphereRoom] SharedBall 预制体缺少 Graphic 子物体或 BallImpactDispatcher。");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }

            AudioSource source = graphic.GetComponent<AudioSource>();
            if (source == null)
                source = graphic.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 30f;

            BallAudioView audioView = root.GetComponent<BallAudioView>();
            if (audioView == null)
                audioView = root.AddComponent<BallAudioView>();

            SetObjectReference(audioView, "_dispatcher", dispatcher);
            SetObjectReference(audioView, "_source", source);
            SetObjectReference(audioView, "_kickClip", kickClip);

            PrefabUtility.SaveAsPrefabAsset(root, BallPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }

        #region Player 预制体

        /// <summary>
        /// Player 预制体：逻辑根（网络/物理/脚本）+ Graphic 子物体（渲染）+ CameraPivot（相机支点）。
        /// 层级纪律见 AGENTS.md §5.4：渲染 Mesh 永远不挂逻辑根。
        /// </summary>
        private static NetworkObject BuildPlayerPrefab()
        {
            Material graphicMaterial = GetOrCreateUrpMaterial(PlayerMaterialPath, new Color(0.85f, 0.85f, 0.85f));

            GameObject root = new GameObject("Player");

            // ---- 逻辑根 ----
            NetworkObject networkObject = root.AddComponent<NetworkObject>();

            Rigidbody rigidbody = root.AddComponent<Rigidbody>();
            // M1 非预测版：Owner 用 MovePosition 驱动，关掉重力避免落地穿模。
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            // 运动学刚体 + MovePosition/MoveRotation：开启插值让本地看到的移动平滑（否则只有 50Hz 的台阶感）。
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = PhysicsTuning.PlayerHeight;
            capsule.radius = PhysicsTuning.PlayerRadius;
            capsule.center = new Vector3(0f, PhysicsTuning.PlayerHeight * 0.5f, 0f);

            NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
            // 保持 Disabled：不自动改写 Rigidbody 设置，M1 由本工程自己控制运动学状态。
            SerializedObject transformSettings = new SerializedObject(networkTransform);
            transformSettings.FindProperty("_componentConfiguration").enumValueIndex = 0;
            // Owner 权威（clientAuthoritative 默认 true）：不让服务器再把位姿回发给自己，否则本地转动会被旧状态顶回去。
            transformSettings.FindProperty("_sendToOwner").boolValue = false;
            transformSettings.ApplyModifiedPropertiesWithoutUndo();

            PlayerInputReader inputReader = root.AddComponent<PlayerInputReader>();
            PlayerMotor motor = root.AddComponent<PlayerMotor>();
            PlayerIdentity identity = root.AddComponent<PlayerIdentity>();
            PlayerCamera playerCamera = root.AddComponent<PlayerCamera>();

            // ---- Graphic 子物体（只放渲染）----
            GameObject graphic = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            graphic.name = "Graphic";
            Object.DestroyImmediate(graphic.GetComponent<Collider>());
            graphic.transform.SetParent(root.transform, false);
            MeshRenderer graphicRenderer = graphic.GetComponent<MeshRenderer>();
            graphicRenderer.sharedMaterial = graphicMaterial;
            // 内置胶囊 Mesh 高 2、半径 0.5，按 PhysicsTuning 缩放贴合 CapsuleCollider。
            graphic.transform.localScale = new Vector3(
                PhysicsTuning.PlayerRadius / 0.5f,
                PhysicsTuning.PlayerHeight / 2f,
                PhysicsTuning.PlayerRadius / 0.5f);
            graphic.transform.localPosition = new Vector3(0f, PhysicsTuning.PlayerHeight * 0.5f, 0f);

            // ---- 相机支点 + 相机（仅 Owner 启用，见 PlayerCamera）----
            // ⚠️ 本方法构建的是 M1 基线预制体；Player 预制体已手工演进到 M3（PlayerPredictedMotor、
            //    CameraPivot 移入 Graphic 平滑层、NetworkTransform 移除）。重跑会用 M1 结构覆盖现有预制体，
            //    除非先把本方法同步到最新架构（2026-10-01 备注）。
            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(graphic.transform, false);
            // CameraPivot 必须挂 Graphic（NetworkObject.GraphicalObject 平滑层）之下，否则相机跟随逻辑根以 Tick 步进；
            // localPosition 需除以 Graphic 的 Y 缩放（眼高相对胶囊中心，再除胶囊半高缩放）。
            pivot.transform.localPosition = new Vector3(
                0f,
                (PhysicsTuning.PlayerEyeHeight - PhysicsTuning.PlayerHeight * 0.5f) / (PhysicsTuning.PlayerHeight * 0.5f),
                0f);

            GameObject cameraObject = new GameObject("PlayerCamera");
            cameraObject.transform.SetParent(pivot.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.depth = 0f;
            cameraObject.AddComponent<AudioListener>();

            // ---- 绑定私有序列化字段（避免运行时 GetComponent/Find）----
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            SetObjectReference(inputReader, "_actions", actions);
            SetObjectReference(motor, "_input", inputReader);
            SetObjectReference(identity, "_graphic", graphicRenderer);
            SetObjectReference(playerCamera, "_camera", camera);
            SetObjectReference(playerCamera, "_viewPivot", pivot.transform);
            SetObjectReference(playerCamera, "_input", inputReader);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);

            return prefab.GetComponent<NetworkObject>();
        }

        #endregion

        #region SharedBall 预制体

        /// <summary>
        /// SharedBall 预制体：逻辑根（网络 / 物理 / 事件源）+ Graphic 子物体（渲染，换足球只改这里）。
        /// 层级纪律见 AGENTS §5.4：Graphic 缩放由 PhysicsTuning.BallRadius 推导，换 Mesh 不改物理尺寸。
        /// </summary>
        private static NetworkObject BuildSharedBallPrefab()
        {
            Material ballMaterial = GetOrCreateUrpMaterial(BallMaterialPath, new Color(0.92f, 0.92f, 0.88f));
            PhysicsMaterial ballPhysicsMaterial = GetOrCreateBallPhysicsMaterial();

            GameObject root = new GameObject("SharedBall");

            root.AddComponent<NetworkObject>();

            Rigidbody rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = PhysicsTuning.BallMass;
            // Unity 6 起 drag/angularDrag 更名为 linearDamping/angularDamping（旧名已标记废弃）。
            rigidbody.linearDamping = PhysicsTuning.BallDrag;
            rigidbody.angularDamping = PhysicsTuning.BallAngularDrag;

            SphereCollider sphereCollider = root.AddComponent<SphereCollider>();
            sphereCollider.radius = PhysicsTuning.BallRadius;
            // 用 sharedMaterial：预制体上不实例化材质（material 会为每个实例复制一份）。
            sphereCollider.sharedMaterial = ballPhysicsMaterial;

            // M2 临时方案：服务器权威 + 由组件把客户端副本切成运动学（NetworkTransform.CanMakeKinematic）。
            // TODO(M4): 换成 reconcile-only 预测（BallPrediction + PredictionRigidbody），本组件移除。
            NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
            SerializedObject transformSettings = new SerializedObject(networkTransform);
            transformSettings.FindProperty("_componentConfiguration").enumValueIndex = 2;
            transformSettings.FindProperty("_clientAuthoritative").boolValue = false;
            transformSettings.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<BallImpactDispatcher>();

            GameObject graphic = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            graphic.name = "Graphic";
            Object.DestroyImmediate(graphic.GetComponent<Collider>());
            graphic.transform.SetParent(root.transform, false);
            graphic.GetComponent<MeshRenderer>().sharedMaterial = ballMaterial;
            // 内置球体 Mesh 半径 0.5：按物理半径缩放，保证视觉尺寸与碰撞体一致。
            float graphicScale = PhysicsTuning.BallRadius / 0.5f;
            graphic.transform.localScale = new Vector3(graphicScale, graphicScale, graphicScale);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BallPrefabPath);
            Object.DestroyImmediate(root);

            return prefab.GetComponent<NetworkObject>();
        }

        private static PhysicsMaterial GetOrCreateBallPhysicsMaterial()
        {
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BallPhysicsMaterialPath);
            if (material == null)
            {
                material = new PhysicsMaterial("Phys_Ball");
                AssetDatabase.CreateAsset(material, BallPhysicsMaterialPath);
            }

            // PhysicsMaterial 沿用 PhysicMaterial 的分法：动/静摩擦分开设置（没有统一的 friction 属性）。
            material.dynamicFriction = PhysicsTuning.Friction;
            material.staticFriction = PhysicsTuning.Friction;
            material.bounciness = PhysicsTuning.Bounciness;
            // 取最大值：球撞墙/柱时以球的弹性为准，避免默认平均把弹回打折。
            material.bounceCombine = PhysicsMaterialCombine.Maximum;
            material.frictionCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(material);
            return material;
        }

        #endregion

        #region 场景

        /// <summary>
        /// Room 场景：Cube 拼地板 / 四墙 / 顶棚 + 4 根障碍柱 + GameManager（球生成器）。
        /// 不放相机与 AudioListener，避免与玩家自带的相机冲突。
        /// </summary>
        private static void BuildRoomScene(NetworkObject ballPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateDirectionalLight();
            CreateRoomGeometry();
            CreateGameManager(ballPrefab);

            EditorSceneManager.SaveScene(scene, RoomScenePath);
            RegisterScenesToBuildSettings();
        }

        /// <summary>
        /// Boot 场景：常驻 NetworkManager（Tugboat + TickRate 50 + PlayerSpawner + 联机入口）+ 主菜单 UI。
        /// </summary>
        private static void BuildBootScene(NetworkObject playerPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateMenuCamera();
            CreateNetworkManagerObject(playerPrefab);
            CreateMenuUi();
            CreateEventSystem();

            EditorSceneManager.SaveScene(scene, BootScenePath);
            RegisterScenesToBuildSettings();
        }

        private static void CreateDirectionalLight()
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        /// <summary>
        /// 房间几何：Cube 拼地板 / 四墙 / 顶棚 / 4 柱。静态 Collider，不带 NetworkObject
        /// —— Room 是全局场景，各端各自加载同一份静态几何。
        /// </summary>
        private static void CreateRoomGeometry()
        {
            // URP 下 CreatePrimitive 自带的是内置管线材质（会渲染成品红），必须换成 URP/Lit 材质。
            Material floorMaterial = GetOrCreateUrpMaterial(FloorMaterialPath, new Color(0.55f, 0.56f, 0.60f));
            Material wallMaterial = GetOrCreateUrpMaterial(WallMaterialPath, new Color(0.38f, 0.40f, 0.46f));
            Material pillarMaterial = GetOrCreateUrpMaterial(PillarMaterialPath, new Color(0.28f, 0.30f, 0.34f));
            PhysicsMaterial physicsMaterial = GetOrCreateBallPhysicsMaterial();

            GameObject room = new GameObject("Room");
            float half = RoomSize * 0.5f;
            float wallY = WallHeight * 0.5f;

            CreateCube(room.transform, "Floor", floorMaterial, physicsMaterial,
                new Vector3(0f, -0.5f, 0f), new Vector3(RoomSize, 1f, RoomSize));
            CreateCube(room.transform, "Ceiling", floorMaterial, physicsMaterial,
                new Vector3(0f, WallHeight, 0f), new Vector3(RoomSize, CeilingThickness, RoomSize));

            CreateCube(room.transform, "Wall North", wallMaterial, physicsMaterial,
                new Vector3(0f, wallY, half), new Vector3(RoomSize, WallHeight, WallThickness));
            CreateCube(room.transform, "Wall South", wallMaterial, physicsMaterial,
                new Vector3(0f, wallY, -half), new Vector3(RoomSize, WallHeight, WallThickness));
            CreateCube(room.transform, "Wall East", wallMaterial, physicsMaterial,
                new Vector3(half, wallY, 0f), new Vector3(WallThickness, WallHeight, RoomSize));
            CreateCube(room.transform, "Wall West", wallMaterial, physicsMaterial,
                new Vector3(-half, wallY, 0f), new Vector3(WallThickness, WallHeight, RoomSize));

            // 4 根柱子放在四角内侧：不挡出生点，又能保证“推球撞柱真实弹回”可验证。
            float pillarOffset = half * 0.5f;
            CreateCube(room.transform, "Pillar NE", pillarMaterial, physicsMaterial,
                new Vector3(pillarOffset, wallY, pillarOffset), new Vector3(PillarSize, WallHeight, PillarSize));
            CreateCube(room.transform, "Pillar NW", pillarMaterial, physicsMaterial,
                new Vector3(-pillarOffset, wallY, pillarOffset), new Vector3(PillarSize, WallHeight, PillarSize));
            CreateCube(room.transform, "Pillar SE", pillarMaterial, physicsMaterial,
                new Vector3(pillarOffset, wallY, -pillarOffset), new Vector3(PillarSize, WallHeight, PillarSize));
            CreateCube(room.transform, "Pillar SW", pillarMaterial, physicsMaterial,
                new Vector3(-pillarOffset, wallY, -pillarOffset), new Vector3(PillarSize, WallHeight, PillarSize));
        }

        /// <summary>
        /// GameManager：Room 场景内的 NetworkObject（服务器侧自动生成），当前只挂 BallSpawner。
        /// M8 的 TappedDispatcher 按 §8.1 以新增组件接入，不改本方法生成的既有组件。
        /// </summary>
        private static void CreateGameManager(NetworkObject ballPrefab)
        {
            GameObject manager = new GameObject("GameManager");
            manager.AddComponent<NetworkObject>();

            // 球生成点：绕房间中部均匀分布，Y 取 PhysicsTuning.BallSpawnHeight。
            GameObject spawnRoot = new GameObject("Ball Spawn Points");
            Transform[] spawnPoints = new Transform[BallSpawnPointCount];
            for (int i = 0; i < BallSpawnPointCount; i++)
            {
                float angle = (360f / BallSpawnPointCount) * i * Mathf.Deg2Rad;
                Vector3 position = new Vector3(Mathf.Cos(angle) * 4f, PhysicsTuning.BallSpawnHeight, Mathf.Sin(angle) * 4f);

                GameObject point = new GameObject($"Ball Spawn Point {i}");
                point.transform.SetParent(spawnRoot.transform, false);
                point.transform.position = position;
                spawnPoints[i] = point.transform;
            }

            BallSpawner spawner = manager.AddComponent<BallSpawner>();
            SetObjectReference(spawner, "_ballPrefab", ballPrefab);
            SetObjectReferenceArray(spawner, "_spawnPoints", spawnPoints);
            SetInt(spawner, "_initialBallCount", 1);
        }

        private static void CreateCube(Transform parent, string name, Material material, PhysicsMaterial physicsMaterial, Vector3 position, Vector3 scale)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;

            if (physicsMaterial != null)
                cube.GetComponent<Collider>().sharedMaterial = physicsMaterial;
        }

        /// <summary>
        /// 菜单相机：depth -1 且不带 AudioListener，玩家相机会盖在它上面渲染；
        /// 没有相机时 Game 视图会提示 "No cameras rendering"，所以保留一台。
        /// </summary>
        private static void CreateMenuCamera()
        {
            GameObject cameraObject = new GameObject("Menu Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.depth = -1f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            cameraObject.transform.position = new Vector3(0f, 1.5f, -6f);
        }

        /// <summary>
        /// NetworkManager 组装：NetworkManager + TimeManager(TickRate 50) + TransportManager + Tugboat
        /// + PlayerSpawner + NetworkBootstrap。
        /// 子管理器在运行时由 NetworkManager.GetOrCreateComponent 兜底创建，这里显式添加是为了让配置可序列化。
        /// </summary>
        private static void CreateNetworkManagerObject(NetworkObject playerPrefab)
        {
            GameObject managerObject = new GameObject("NetworkManager");

            NetworkManager networkManager = managerObject.AddComponent<NetworkManager>();

            TimeManager timeManager = managerObject.AddComponent<TimeManager>();
            SerializedObject timeSettings = new SerializedObject(timeManager);
            timeSettings.FindProperty("_tickRate").intValue = PhysicsTuning.TickRate;
            timeSettings.ApplyModifiedPropertiesWithoutUndo();

            TransportManager transportManager = managerObject.AddComponent<TransportManager>();
            Tugboat tugboat = managerObject.AddComponent<Tugboat>();
            transportManager.Transport = tugboat;

            PlayerSpawner playerSpawner = managerObject.AddComponent<PlayerSpawner>();
            playerSpawner.SetPlayerPrefab(playerPrefab);

            NetworkBootstrap bootstrap = managerObject.AddComponent<NetworkBootstrap>();
            SetObjectReference(bootstrap, "_networkManager", networkManager);
            SetString(bootstrap, "_address", "127.0.0.1");
            SetInt(bootstrap, "_port", DefaultPort);
        }

        private static void CreateEventSystem()
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            // 不指派 actionsAsset：模块在 OnEnable 发现没有 action 时会自动套用默认 UI actions。
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        private static void CreateMenuUi()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            GameObject canvasObject = new GameObject("Menu Canvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject panel = CreatePanel(canvasObject.transform, "Menu Panel", new Vector2(460f, 280f), new Vector2(0f, 60f));

            CreateText(panel.transform, "Title", "Sphere Room", font, 34, new Vector2(0f, 100f), new Vector2(400f, 50f));
            Button hostButton = CreateButton(panel.transform, "Host Button", "创建房间 (Host)", font, new Vector2(0f, 20f));
            Button joinButton = CreateButton(panel.transform, "Join Button", "加入房间 (127.0.0.1)", font, new Vector2(0f, -40f));
            Text statusText = CreateText(panel.transform, "Status Text", "未联机", font, 20, new Vector2(0f, -105f), new Vector2(420f, 40f));

            // 退出按钮挂在 Canvas 下（不在面板内），联机时面板隐藏它仍然可见。
            // 布局（2026-10-01 定案）：左上角；右上角留给 NetworkDebugHud 的调试面板。
            Button leaveButton = CreateButton(canvasObject.transform, "Leave Button", "退出游戏", font, Vector2.zero);
            RectTransform leaveRect = leaveButton.GetComponent<RectTransform>();
            leaveRect.anchorMin = new Vector2(0f, 1f);
            leaveRect.anchorMax = new Vector2(0f, 1f);
            leaveRect.pivot = new Vector2(0f, 1f);
            leaveRect.anchoredPosition = new Vector2(24f, -24f);
            leaveRect.sizeDelta = new Vector2(180f, 48f);
            leaveButton.gameObject.SetActive(false);

            // 绑定 MainMenuUI 的私有序列化字段
            NetworkBootstrap bootstrap = Object.FindFirstObjectByType<NetworkBootstrap>();
            MainMenuUI menuUi = canvasObject.AddComponent<MainMenuUI>();
            SetObjectReference(menuUi, "_bootstrap", bootstrap);
            SetObjectReference(menuUi, "_menuPanel", panel);
            SetObjectReference(menuUi, "_hostButton", hostButton);
            SetObjectReference(menuUi, "_joinButton", joinButton);
            SetObjectReference(menuUi, "_leaveButton", leaveButton);
            SetObjectReference(menuUi, "_statusText", statusText);
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 size, Vector2 position)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            return panel;
        }

        private static Button CreateButton(Transform parent, string name, string label, Font font, Vector2 position)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(360f, 52f);
            rect.anchoredPosition = position;

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.22f, 0.45f, 0.85f, 1f);

            CreateText(buttonObject.transform, "Label", label, font, 22, Vector2.zero, new Vector2(340f, 46f));
            return buttonObject.GetComponent<Button>();
        }

        private static Text CreateText(Transform parent, string name, string content, Font font, int fontSize, Vector2 position, Vector2 size)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Text text = textObject.GetComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return text;
        }

        private static void RegisterScenesToBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(RoomScenePath, true),
            };
        }

        #endregion

        #region 工具

        private static Material GetOrCreateUrpMaterial(string path, Color color)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[SphereRoom] 找不到 URP/Lit 着色器，请确认渲染管线为 URP。");
                shader = Shader.Find("Standard");
            }

            Material material = new Material(shader);
            material.color = color;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[SphereRoom] 字段不存在：{target.GetType().Name}.{propertyName}");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string propertyName, string value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[SphereRoom] 字段不存在：{target.GetType().Name}.{propertyName}");
                return;
            }

            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectReferenceArray(Object target, string propertyName, Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[SphereRoom] 字段不存在：{target.GetType().Name}.{propertyName}");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string propertyName, int value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[SphereRoom] 字段不存在：{target.GetType().Name}.{propertyName}");
                return;
            }

            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string propertyName, bool value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[SphereRoom] 字段不存在：{target.GetType().Name}.{propertyName}");
                return;
            }

            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        #endregion
    }
}
