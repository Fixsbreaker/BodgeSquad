using System.IO;
using BodgeSquad.Interaction;
using BodgeSquad.Job;
using BodgeSquad.Net;
using BodgeSquad.Players;
using BodgeSquad.UI;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace ProjectBootstrap
{
    // Online co-op wiring. Full rebuild, in order (Editor closed, batch mode):
    //   1. NetworkSetup.BuildPlayer   — Bodger prefab skeleton (network, input, camera)
    //   2. JobSetup.Run               — house, defects, shop, inspector, HUD
    //   3. KeyArtPass.Run             — look, furniture, bodger body + hats
    //   4. NetworkSetup.WireScene     — NetworkManager, menu, network components on scene objects
    public static class NetworkSetup
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string PlayerPrefab = "Assets/Prefabs/Bodger.prefab";
        const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
        const string PrefabListPath = "Assets/Prefabs/BodgeNetworkPrefabs.asset";

        // ------------------------------------------------------------------ 1. player prefab
        public static void BuildPlayer()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            var root = new GameObject("Bodger");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.35f; cc.skinWidth = 0.04f;

            var no = root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner; // each player drives their own bodger
            nt.SyncRotAngleX = false; nt.SyncRotAngleZ = false;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;

            var visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false);
            var eye = new GameObject("Eye").transform; eye.SetParent(root.transform, false);
            eye.localPosition = new Vector3(0, 1.62f, 0.05f);
            var hold = new GameObject("HoldPoint").transform; hold.SetParent(eye, false);
            hold.localPosition = new Vector3(0.15f, -0.25f, 1.25f);

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = eye.localPosition;
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.fieldOfView = 65f;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();
            var brain = camGo.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

            var vcamGo = new GameObject("FirstPersonCam");
            vcamGo.transform.SetParent(root.transform, false);
            vcamGo.transform.localPosition = eye.localPosition;
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Target.TrackingTarget = eye;
            var lens = vcam.Lens; lens.FieldOfView = 65f; lens.NearClipPlane = 0.05f; vcam.Lens = lens;
            vcamGo.AddComponent<CinemachineHardLockToTarget>();
            vcamGo.AddComponent<CinemachineRotateWithFollowTarget>();

            var input = root.AddComponent<BodgerInput>();
            Set(input, "actions", actions);
            var fpc = root.AddComponent<FirstPersonController>();
            Set(fpc, "eye", eye);
            var carry = root.AddComponent<CarryInteractor>();
            Set(carry, "eye", eye);
            Set(carry, "holdPoint", hold);
            var motion = visual.gameObject.AddComponent<BodgerMotion>();
            Set(motion, "controller", fpc);
            var nb = root.AddComponent<NetworkBodger>();
            Set(nb, "cameraRig", camGo);
            Set(nb, "vcam", vcam);
            Set(nb, "visualRoot", visual);

            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPrefab));
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab, out bool ok);
            Object.DestroyImmediate(root);
            Debug.Log($"[NetSetup] Player prefab skeleton saved: {ok}");
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // ------------------------------------------------------------------ 4. scene
        public static void WireScene()
        {
            // NGO wants a domain reload on entering Play mode
            EditorSettings.enterPlayModeOptionsEnabled = false;

            // Player prefab: owner-only behaviours (the HUD was added by JobSetup)
            var prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            var nb = prefabRoot.GetComponent<NetworkBodger>();
            var so = new SerializedObject(nb);
            var owners = so.FindProperty("ownerOnly");
            var list = new Behaviour[] { prefabRoot.GetComponent<FirstPersonController>(), prefabRoot.GetComponent<BodgerInput>(), prefabRoot.GetComponent<PlayerHud>() };
            owners.arraySize = list.Length;
            for (int i = 0; i < list.Length; i++) owners.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefab);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);

            // Kit prefabs become network objects the host can spawn
            var prefabList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (prefabList == null) { prefabList = ScriptableObject.CreateInstance<NetworkPrefabsList>(); AssetDatabase.CreateAsset(prefabList, PrefabListPath); }
            var plSo = new SerializedObject(prefabList);
            plSo.FindProperty("List").ClearArray();
            plSo.ApplyModifiedPropertiesWithoutUndo();
            prefabList.Add(new NetworkPrefab { Prefab = player });
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Kits" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var kitRoot = PrefabUtility.LoadPrefabContents(path);
                MakeNetworkCarryable(kitRoot);
                PrefabUtility.SaveAsPrefabAsset(kitRoot, path);
                PrefabUtility.UnloadPrefabContents(kitRoot);
                prefabList.Add(new NetworkPrefab { Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path) });
            }
            EditorUtility.SetDirty(prefabList);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Split-screen leftovers out
            var pim = GameObject.Find("PlayerManager");
            if (pim != null) Object.DestroyImmediate(pim);

            // NetworkManager + transport + session glue (one object, survives reloads)
            var old = GameObject.Find("NetworkManager");
            if (old != null) Object.DestroyImmediate(old);
            var nmGo = new GameObject("NetworkManager");
            nmGo.AddComponent<NetworkManagerGuard>();
            var transport = nmGo.AddComponent<UnityTransport>();
            var nm = nmGo.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = player,
                ConnectionApproval = true,
                EnableSceneManagement = true,
            };
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);
            nmGo.AddComponent<NetSession>();
            nmGo.AddComponent<NetSmokeTest>();

            // Menu
            var oldMenu = GameObject.Find("MainMenu");
            if (oldMenu != null) Object.DestroyImmediate(oldMenu);
            const string artPath = "Assets/Art/UI/MenuBackdrop.png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(artPath);
            if (ti != null && ti.textureType != TextureImporterType.Sprite)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
            var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
            Set(menu, "backdrop", AssetDatabase.LoadAssetAtPath<Sprite>(artPath));

            // Job, defects, inspector
            var job = Object.FindAnyObjectByType<JobManager>();
            Ensure<NetworkObject>(job.gameObject);
            foreach (var d in Object.FindObjectsByType<Defect>()) Ensure<NetworkObject>(d.gameObject);
            var insp = Object.FindAnyObjectByType<Inspector>(FindObjectsInactive.Include);
            insp.gameObject.SetActive(true);
            Ensure<NetworkObject>(insp.gameObject);
            var it = Ensure<NetworkTransform>(insp.gameObject);
            it.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            it.SyncScaleX = it.SyncScaleY = it.SyncScaleZ = false;
            var body = insp.transform.Find("Body");
            var bt = Ensure<NetworkTransform>(body.gameObject);
            bt.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            bt.InLocalSpace = true;
            bt.SyncScaleX = bt.SyncScaleY = bt.SyncScaleZ = false;

            // Everything you can carry
            int carried = 0;
            foreach (var c in Object.FindObjectsByType<Carryable>()) { MakeNetworkCarryable(c.gameObject); carried++; }

            // The guard no longer needs spawn points wired (it finds the local bodger)
            var guard = Object.FindAnyObjectByType<PlayAreaGuard>();
            if (guard != null)
            {
                var gso = new SerializedObject(guard);
                gso.FindProperty("halfExtents").vector3Value = new Vector3(RoomDims.InX - 0.05f, 3.2f, RoomDims.InZ - 0.05f);
                gso.ApplyModifiedPropertiesWithoutUndo();
            }

            // Overview camera = menu backdrop
            var main = Camera.main;
            if (main != null && !main.TryGetComponent<JoinCamera>(out _)) main.gameObject.AddComponent<JoinCamera>();
            if (main != null) main.transform.SetPositionAndRotation(new Vector3(-5.4f, 2.6f, -4.4f), Quaternion.Euler(22, 45, 0));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // Scene must be in the build for builds and reloads
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[NetSetup] Scene wired: NetworkManager + NetSession + MainMenu, {Object.FindObjectsByType<Defect>().Length} defects, {carried} carryables, inspector.");
            EditorApplication.Exit(0);
        }

        // Test build: Builds/Mac/BodgeSquad.app
        public static void BuildMac()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Mac/BodgeSquad.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            });
            Debug.Log($"[NetSetup] Build: {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB");
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        // Release build for friends / itch.io: Builds/Windows/BodgeSquad.exe
        public static void BuildWindows()
        {
            PlayerSettings.productName = "Bodge Squad";
            PlayerSettings.companyName = "Bodge Squad Team";
            PlayerSettings.runInBackground = true; // keep the host ticking when alt-tabbed
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/BodgeSquad.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"[NetSetup] Windows build: {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB");
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        static void MakeNetworkCarryable(GameObject go)
        {
            var no = Ensure<NetworkObject>(go);
            no.DontDestroyWithOwner = true;   // a dropped item stays when its last carrier leaves
            no.AutoObjectParentSync = false;
            var t = Ensure<NetworkTransform>(go);
            t.AuthorityMode = NetworkTransform.AuthorityModes.Owner; // the carrier simulates it
            t.SyncScaleX = t.SyncScaleY = t.SyncScaleZ = false;
            Ensure<NetworkRigidbody>(go);
        }

        static T Ensure<T>(GameObject go) where T : Component =>
            go.TryGetComponent<T>(out var c) ? c : go.AddComponent<T>();

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[NetSetup] No field '{field}' on {target.GetType().Name}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
