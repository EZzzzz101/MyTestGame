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
            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, PhysicsTuning.PlayerEyeHeight, 0f);

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
            SetObjectReference(playerCamera, "_pitchPivot", pivot.transform);
            SetObjectReference(playerCamera, "_input", inputReader);
            SetObjectReference(playerCamera, "_motor", motor);

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

            material.friction = PhysicsTuning.Friction;
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

            // 断开按钮挂在 Canvas 下（不在面板内），联机时面板隐藏它仍然可见。
            Button leaveButton = CreateButton(canvasObject.transform, "Leave Button", "断开连接", font, Vector2.zero);
            RectTransform leaveRect = leaveButton.GetComponent<RectTransform>();
            leaveRect.anchorMin = new Vector2(1f, 1f);
            leaveRect.anchorMax = new Vector2(1f, 1f);
            leaveRect.pivot = new Vector2(1f, 1f);
            leaveRect.anchoredPosition = new Vector2(-24f, -24f);
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

        #endregion
    }
}
