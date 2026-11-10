using System.IO;
using BodgeSquad.Interaction;
using BodgeSquad.Players;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace ProjectBootstrap
{
    // Builds the Bodger player prefab and wires SampleScene for 1–4 player split-screen.
    // Run with: Unity -batchmode -projectPath <path> -executeMethod ProjectBootstrap.SplitScreenSetup.Run
    public static class SplitScreenSetup
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string PrefabPath = "Assets/Prefabs/Bodger.prefab";
        const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
        const string MatDir = "Assets/Art/Materials";

        public static void Run()
        {
            EnsureLayers();
            var prefab = BuildPrefab();
            if (prefab == null) return;
            SetupScene(prefab);
            CaptureSplit(prefab);
            Debug.Log("[SplitScreen] Done.");
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ layers
        static void EnsureLayers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            for (int i = 0; i < 4; i++)
            {
                var p = layers.GetArrayElementAtIndex(8 + i);
                if (string.IsNullOrEmpty(p.stringValue)) p.stringValue = $"Player{i + 1}";
            }
            tm.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[SplitScreen] Layers: {LayerMask.LayerToName(8)}, {LayerMask.LayerToName(9)}, {LayerMask.LayerToName(10)}, {LayerMask.LayerToName(11)}");
        }

        // ------------------------------------------------------------------ prefab
        static GameObject BuildPrefab()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            if (actions == null) { Fail($"Input actions not found at {ActionsPath}"); return null; }
            var mBlue = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_CharacterSecondary.mat");
            var mOrange = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_CharacterPrimary.mat");
            var mSkin = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Walls.mat");
            var mDark = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Trim.mat");

            var root = new GameObject("Bodger");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0, 0.9f, 0);
            cc.stepOffset = 0.35f; cc.skinWidth = 0.04f;

            // PlayerInput: disabled on the prefab root is not allowed, so configure before adding the rest
            var input = root.AddComponent<PlayerInput>();
            input.actions = actions;
            input.defaultActionMap = "Player";
            input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

            // Visual (seen by other players)
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            Prim(visual, PrimitiveType.Capsule, "Body", new Vector3(0, 0.85f, 0), new Vector3(0.6f, 0.62f, 0.5f), mBlue);
            Prim(visual, PrimitiveType.Cube, "HiVis", new Vector3(0, 1.08f, 0), new Vector3(0.64f, 0.45f, 0.52f), mOrange);
            Prim(visual, PrimitiveType.Sphere, "Head", new Vector3(0, 1.62f, 0.02f), new Vector3(0.38f, 0.36f, 0.38f), mSkin);
            Prim(visual, PrimitiveType.Cube, "Nose", new Vector3(0, 1.6f, 0.2f), new Vector3(0.07f, 0.07f, 0.08f), mSkin);
            var hat = Prim(visual, PrimitiveType.Cylinder, "HardHat", new Vector3(0, 1.8f, 0.02f), new Vector3(0.44f, 0.06f, 0.44f), mOrange);
            var hatTop = Prim(visual, PrimitiveType.Sphere, "HardHatTop", new Vector3(0, 1.82f, 0.02f), new Vector3(0.38f, 0.2f, 0.38f), mOrange);
            Prim(visual, PrimitiveType.Capsule, "Boot_L", new Vector3(-0.14f, 0.08f, 0.05f), new Vector3(0.18f, 0.1f, 0.26f), mDark).localRotation = Quaternion.Euler(90, 0, 0);
            Prim(visual, PrimitiveType.Capsule, "Boot_R", new Vector3(0.14f, 0.08f, 0.05f), new Vector3(0.18f, 0.1f, 0.26f), mDark).localRotation = Quaternion.Euler(90, 0, 0);

            // Eye + hold point
            var eye = new GameObject("Eye").transform;
            eye.SetParent(root.transform, false);
            eye.localPosition = new Vector3(0, 1.62f, 0.05f);
            var hold = new GameObject("HoldPoint").transform;
            hold.SetParent(eye, false);
            hold.localPosition = new Vector3(0.15f, -0.25f, 1.25f);

            // Output camera (driven by its own brain) + first-person Cinemachine camera
            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = eye.localPosition;
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 65f;
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

            input.camera = cam;

            // Behaviour
            var fpc = root.AddComponent<FirstPersonController>();
            SetRef(fpc, "eye", eye);
            var carry = root.AddComponent<CarryInteractor>();
            SetRef(carry, "eye", eye);
            SetRef(carry, "holdPoint", hold);
            var motion = visual.gameObject.AddComponent<BodgerMotion>();
            SetRef(motion, "controller", fpc);
            var slot = root.AddComponent<PlayerSlot>();
            SetRef(slot, "playerCamera", cam);
            SetRef(slot, "brain", brain);
            SetRef(slot, "vcam", vcam);
            SetRef(slot, "visualRoot", visual);
            var so = new SerializedObject(slot);
            var arr = so.FindProperty("tintedRenderers");
            arr.arraySize = 2;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = hat.GetComponent<Renderer>();
            arr.GetArrayElementAtIndex(1).objectReferenceValue = hatTop.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok) { Fail("Prefab save failed."); return null; }
            Debug.Log($"[SplitScreen] Prefab saved: {PrefabPath}");
            return saved;
        }

        // ------------------------------------------------------------------ scene
        static void SetupScene(GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var room = GameObject.Find("LookDevRoom")?.transform;
            if (room == null) { Fail("LookDevRoom missing — run BaselineSetup first."); return; }
            var mWalls = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Walls.mat");
            var mTrim = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Trim.mat");

            // Close the room so nobody walks off the edge of the world
            if (room.Find("Wall_South") == null)
            {
                Box(room, "Wall_South", new Vector3(0, 1.4f, -4), new Vector3(8, 2.8f, 0.15f), mWalls);
                Box(room, "Wall_West", new Vector3(-4, 1.4f, 0), new Vector3(0.15f, 2.8f, 8), mWalls);
                Box(room, "Baseboard_South", new Vector3(0, 0.06f, -3.9f), new Vector3(8, 0.12f, 0.06f), mTrim);
                Box(room, "Baseboard_West", new Vector3(-3.9f, 0.06f, 0), new Vector3(0.06f, 0.12f, 8), mTrim);
            }

            // The static placeholder is replaced by real joined bodgers
            var placeholder = room.Find("Bodger_Placeholder");
            if (placeholder != null) Object.DestroyImmediate(placeholder.gameObject);

            // Loose props become carryable
            foreach (var (n, mass, weight) in new[] { ("Crate", 6f, 1.6f), ("Plank", 3f, 1.2f), ("PaintTin_A", 1.5f, 1f), ("PaintTin_B", 1.5f, 1f) })
            {
                var t = room.Find(n);
                if (t == null) continue;
                if (!t.TryGetComponent<Rigidbody>(out var rb)) rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass = mass;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                if (!t.TryGetComponent<Carryable>(out var c)) c = t.gameObject.AddComponent<Carryable>();
                var so = new SerializedObject(c);
                so.FindProperty("carryWeight").floatValue = weight;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // Spawn points in the four corners-ish
            var spawns = GameObject.Find("SpawnPoints")?.transform;
            if (spawns == null)
            {
                spawns = new GameObject("SpawnPoints").transform;
                var pts = new[] { new Vector3(-2.5f, 0, -2.5f), new Vector3(2.5f, 0, -2.5f), new Vector3(-2.5f, 0, 1f), new Vector3(1f, 0, -1f) };
                for (int i = 0; i < pts.Length; i++)
                {
                    var s = new GameObject($"Spawn_P{i + 1}").transform;
                    s.SetParent(spawns, false);
                    s.position = pts[i];
                    s.rotation = Quaternion.LookRotation(new Vector3(0.5f, 0, 1.5f) - pts[i]);
                }
            }

            // Player manager: join on any button, split the screen, up to 4
            var pimGo = GameObject.Find("PlayerManager");
            if (pimGo == null) pimGo = new GameObject("PlayerManager");
            if (!pimGo.TryGetComponent<PlayerInputManager>(out var pim)) pim = pimGo.AddComponent<PlayerInputManager>();
            var pso = new SerializedObject(pim);
            pso.FindProperty("m_PlayerPrefab").objectReferenceValue = prefab;
            pso.FindProperty("m_SplitScreen").boolValue = true;
            pso.FindProperty("m_MaxPlayerCount").intValue = 4;
            pso.FindProperty("m_JoinBehavior").enumValueIndex = (int)PlayerJoinBehavior.JoinPlayersWhenButtonIsPressed;
            pso.FindProperty("m_NotificationBehavior").enumValueIndex = (int)PlayerNotifications.InvokeCSharpEvents;
            pso.ApplyModifiedPropertiesWithoutUndo();
            if (!pimGo.TryGetComponent<SpawnOnJoin>(out var spawner)) spawner = pimGo.AddComponent<SpawnOnJoin>();
            var sso = new SerializedObject(spawner);
            sso.FindProperty("manager").objectReferenceValue = pim;
            sso.FindProperty("spawnRoot").objectReferenceValue = spawns;
            sso.ApplyModifiedPropertiesWithoutUndo();

            // Overview camera until someone joins
            var main = Camera.main;
            if (!main.TryGetComponent<JoinCamera>(out var jc)) jc = main.gameObject.AddComponent<JoinCamera>();
            var jso = new SerializedObject(jc);
            jso.FindProperty("manager").objectReferenceValue = pim;
            jso.ApplyModifiedPropertiesWithoutUndo();
            main.transform.position = new Vector3(-3.4f, 2.5f, -3.4f);
            main.transform.rotation = Quaternion.Euler(24f, 45f, 0f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[SplitScreen] Scene wired: PlayerManager (split-screen, max 4, join on button), JoinCamera, carryables, spawns.");
        }

        // ------------------------------------------------------------------ capture (edit-mode mock of a 4-way split)
        static void CaptureSplit(GameObject prefab)
        {
            var spawns = GameObject.Find("SpawnPoints").transform;
            var rects = new[] { new Rect(0, 0.5f, 0.5f, 0.5f), new Rect(0.5f, 0.5f, 0.5f, 0.5f), new Rect(0, 0, 0.5f, 0.5f), new Rect(0.5f, 0, 0.5f, 0.5f) };
            var players = new GameObject[4];
            var cams = new Camera[4];
            for (int i = 0; i < 4; i++)
            {
                var sp = spawns.GetChild(i);
                players[i] = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                players[i].transform.SetPositionAndRotation(sp.position, sp.rotation);
                int layer = 8 + i;
                SetLayer(players[i].transform, layer);
                var hat = players[i].transform.Find("Visual/HardHat").GetComponent<Renderer>();
                var top = players[i].transform.Find("Visual/HardHatTop").GetComponent<Renderer>();
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", BodgeSquad.Art.Palette.PlayerTints[i]);
                hat.SetPropertyBlock(block); top.SetPropertyBlock(block);
                cams[i] = players[i].GetComponentInChildren<Camera>();
                cams[i].GetComponent<CinemachineBrain>().enabled = false;
                var eye = players[i].transform.Find("Eye");
                cams[i].transform.SetPositionAndRotation(eye.position, eye.rotation);
                cams[i].cullingMask &= ~(1 << layer);
                cams[i].rect = rects[i];
            }

            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            for (int warm = 0; warm < 4; warm++)
                for (int i = 0; i < 4; i++) { cams[i].targetTexture = rt; cams[i].Render(); }
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            Directory.CreateDirectory("Docs");
            File.WriteAllBytes("Docs/splitscreen_4p.png", tex.EncodeToPNG());
            RenderTexture.active = null;
            foreach (var p in players) Object.DestroyImmediate(p);
            Object.DestroyImmediate(rt);
            Debug.Log("[SplitScreen] Captured Docs/splitscreen_4p.png (edit-mode mock, scene not saved with these players)");
        }

        // ------------------------------------------------------------------ helpers
        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[SplitScreen] No field '{field}' on {target.GetType().Name}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Transform Prim(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // the CharacterController is the body
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        static void Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        static void Fail(string msg)
        {
            Debug.LogError($"[SplitScreen] {msg}");
            EditorApplication.Exit(1);
        }
    }
}
