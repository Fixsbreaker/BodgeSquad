using System.IO;
using BodgeSquad.Art;
using BodgeSquad.Interaction;
using BodgeSquad.Job;
using BodgeSquad.Players;
using BodgeSquad.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace ProjectBootstrap
{
    // Vertical slice: defects, supplies, doorbell, wardrobe, inspector, HUDs.
    // Step 1 (once): -executeMethod ProjectBootstrap.JobSetup.ImportTmp   (TMP Essentials, non-interactive)
    // Step 2:        -executeMethod ProjectBootstrap.JobSetup.Run
    public static class JobSetup
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string MatDir = "Assets/Art/Materials";
        const string PrefabDir = "Assets/Prefabs";
        const string PlayerPrefab = "Assets/Prefabs/Bodger.prefab";

        public static void ImportTmp()
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
            {
                Debug.Log("[JobSetup] TMP Essentials already present.");
                EditorApplication.Exit(0);
                return;
            }
            // Package import is asynchronous: stay alive (run WITHOUT -quit) until it completes.
            AssetDatabase.importPackageCompleted += _ => { Debug.Log("[JobSetup] TMP Essentials imported."); EditorApplication.Exit(0); };
            AssetDatabase.importPackageFailed += (_, err) => { Debug.LogError($"[JobSetup] TMP import failed: {err}"); EditorApplication.Exit(1); };
            double deadline = EditorApplication.timeSinceStartup + 300;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup > deadline) { Debug.LogError("[JobSetup] TMP import timed out."); EditorApplication.Exit(2); }
            };
            TMP_PackageResourceImporter.ImportResources(true, false, false);
        }

        static Material _glass, _ink, _damage, _mould, _paint, _tape, _paper, _suit, _accent, _pass, _wood, _walls, _trim, _props, _blue, _skin;

        public static void Run()
        {
            if (TMP_Settings.defaultFontAsset == null) { Fail("TMP default font missing — run ImportTmp first."); return; }
            Materials();
            var kits = KitPrefabs();
            var flag = FlagPrefab();
            var tick = PassPrefab();
            PatchPlayerPrefab();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var room = GameObject.Find("LookDevRoom").transform;
            var frame = room.Find("WindowFrame");
            if (frame != null && frame.Find("Glass") == null)
            {
                // the dark slab read as a blackboard: give it glass and mullions so it reads as a window
                var g = Prim(frame, PrimitiveType.Cube, "Glass", new Vector3(0, 0, -0.6f), new Vector3(0.9f, 0.88f, 0.2f), _glass, false);
                Prim(frame, PrimitiveType.Cube, "MullionV", new Vector3(0, 0, -0.9f), new Vector3(0.05f, 0.9f, 0.3f), _trim, false);
                Prim(frame, PrimitiveType.Cube, "MullionH", new Vector3(0, 0, -0.9f), new Vector3(0.9f, 0.06f, 0.3f), _trim, false);
            }
            var oldFlag = room.Find("InspectorFlag");
            if (oldFlag != null) Object.DestroyImmediate(oldFlag.gameObject); // the real flags come from the inspector now

            var slice = GameObject.Find("JobSlice");
            if (slice != null) Object.DestroyImmediate(slice);
            var root = new GameObject("JobSlice").transform;

            // --- defects ------------------------------------------------------------------------
            var defects = new GameObject("Defects").transform; defects.SetParent(root, false);
            MakeDefect(defects, "north wall", DefectKind.Crack, Severity.Cosmetic, new Vector3(2.4f, 1.6f, 3.92f), Vector3.back);
            MakeDefect(defects, "east wall", DefectKind.Hole, Severity.FutureFailure, new Vector3(3.92f, 0.9f, 0.6f), Vector3.left);
            MakeDefect(defects, "south wall", DefectKind.Mould, Severity.FutureFailure, new Vector3(1.6f, 0.45f, -3.92f), Vector3.forward);
            MakeDefect(defects, "west wall", DefectKind.ExposedWiring, Severity.Safety, new Vector3(-3.92f, 1.2f, 1.0f), Vector3.right);
            MakeDefect(defects, "east wall, up high", DefectKind.Crack, Severity.Cosmetic, new Vector3(3.92f, 1.95f, -2.2f), Vector3.left);

            // --- door, entrance, doorbell -------------------------------------------------------
            var door = new GameObject("FrontDoor").transform; door.SetParent(root, false);
            Box(door, "Frame_L", new Vector3(-2.55f, 1.05f, -3.9f), new Vector3(0.1f, 2.1f, 0.12f), _trim);
            Box(door, "Frame_R", new Vector3(-1.45f, 1.05f, -3.9f), new Vector3(0.1f, 2.1f, 0.12f), _trim);
            Box(door, "Frame_Top", new Vector3(-2.0f, 2.12f, -3.9f), new Vector3(1.2f, 0.1f, 0.12f), _trim);
            Box(door, "Door", new Vector3(-2.0f, 1.03f, -3.89f), new Vector3(1.0f, 2.06f, 0.06f), _wood);
            Box(door, "Handle", new Vector3(-1.62f, 1.0f, -3.84f), new Vector3(0.12f, 0.04f, 0.05f), _ink);
            var entrance = new GameObject("InspectorEntrance").transform; entrance.SetParent(door, false);
            entrance.position = new Vector3(-2.0f, 0f, -3.3f); entrance.rotation = Quaternion.LookRotation(Vector3.forward);

            var bell = Box(root, "Doorbell", new Vector3(-1.05f, 1.35f, -3.89f), new Vector3(0.22f, 0.3f, 0.06f), _ink);
            var button = Prim(bell, PrimitiveType.Cylinder, "Button", new Vector3(0, 0.1f, 0.6f), new Vector3(0.45f, 0.4f, 0.3f), _accent, keepCollider: false);
            button.localRotation = Quaternion.Euler(90, 0, 0);
            var bellC = bell.gameObject.AddComponent<Doorbell>();
            Set(bellC, "button", button);
            WorldText(root, "DoorbellLabel", "CALL\nINSPECTOR", new Vector3(-1.05f, 1.68f, -3.85f), Quaternion.Euler(0, 180, 0), 1.1f, Palette.UiInk);

            // --- supplies shelf on the north wall -----------------------------------------------
            var shop = new GameObject("Supplies").transform; shop.SetParent(root, false);
            Box(shop, "Table", new Vector3(-2.0f, 0.82f, 3.55f), new Vector3(3.2f, 0.08f, 0.6f), _wood);
            foreach (var x in new[] { -3.5f, -0.5f }) foreach (var z in new[] { 3.3f, 3.8f })
                Box(shop, "Leg", new Vector3(x, 0.39f, z), new Vector3(0.07f, 0.78f, 0.07f), _trim);
            WorldText(shop, "Sign", "SUPPLIES  <size=70%>(pay on pickup)</size>", new Vector3(-2.0f, 2.35f, 3.88f), Quaternion.identity, 2.2f, Palette.UiInk);
            KitType[] order = { KitType.Filler, KitType.AntiMould, KitType.WiringKit, KitType.PaintTin, KitType.WallpaperRoll, KitType.DuctTape };
            for (int i = 0; i < order.Length; i++)
            {
                float x = -3.25f + i * 0.5f;
                var shelf = new GameObject($"Shelf_{order[i]}").transform; shelf.SetParent(shop, false);
                shelf.position = new Vector3(x, 0.86f, 3.5f);
                var slot = new GameObject("Slot").transform; slot.SetParent(shelf, false);
                slot.localPosition = new Vector3(0, 0.16f, 0);
                var tag = WorldText(shelf, "PriceTag", "", new Vector3(x, 1.25f, 3.3f), Quaternion.identity, 0.75f,
                                    RepairRules.IsProperFix(order[i]) ? Palette.CharacterSecondary : Palette.Accent);
                var s = shelf.gameObject.AddComponent<SupplyShelf>();
                Set(s, "kitPrefab", kits[(int)order[i]]);
                Set(s, "slot", slot);
                Set(s, "priceTag", tag);
                tag.text = $"{RepairRules.KitName(order[i])}\n<b>${RepairRules.Price(order[i])}</b>";
                var so = new SerializedObject(s); so.FindProperty("kit").enumValueIndex = (int)order[i]; so.ApplyModifiedPropertiesWithoutUndo();
            }

            // --- wardrobe: shove it in front of damage -------------------------------------------
            var wardrobe = Box(root, "Wardrobe", new Vector3(-0.6f, 0.9f, 0.4f), new Vector3(1.0f, 1.8f, 0.5f), _wood);
            Prim(wardrobe, PrimitiveType.Cube, "DoorLine", new Vector3(0, 0, 0.51f), new Vector3(0.015f, 0.95f, 0.02f), _trim, false);
            Prim(wardrobe, PrimitiveType.Cube, "Knob_L", new Vector3(-0.06f, 0.05f, 0.53f), new Vector3(0.03f, 0.03f, 0.04f), _ink, false);
            Prim(wardrobe, PrimitiveType.Cube, "Knob_R", new Vector3(0.06f, 0.05f, 0.53f), new Vector3(0.03f, 0.03f, 0.04f), _ink, false);
            var wrb = wardrobe.gameObject.AddComponent<Rigidbody>(); wrb.mass = 40f; wrb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var wc = wardrobe.gameObject.AddComponent<Carryable>();
            var wso = new SerializedObject(wc); wso.FindProperty("carryWeight").floatValue = 3f; wso.ApplyModifiedPropertiesWithoutUndo();
            wardrobe.gameObject.AddComponent<Concealer>();

            // --- inspector -----------------------------------------------------------------------
            var insp = new GameObject("Inspector").transform; insp.SetParent(root, false);
            insp.SetPositionAndRotation(entrance.position, entrance.rotation);
            var body = new GameObject("Body").transform; body.SetParent(insp, false);
            Prim(body, PrimitiveType.Capsule, "Suit", new Vector3(0, 0.9f, 0), new Vector3(0.55f, 0.62f, 0.42f), _suit, false);
            Prim(body, PrimitiveType.Cube, "Shirt", new Vector3(0, 1.22f, 0.17f), new Vector3(0.22f, 0.3f, 0.08f), _paper, false);
            Prim(body, PrimitiveType.Cube, "Tie", new Vector3(0, 1.15f, 0.22f), new Vector3(0.07f, 0.32f, 0.03f), _accent, false);
            Prim(body, PrimitiveType.Sphere, "Head", new Vector3(0, 1.68f, 0.02f), new Vector3(0.36f, 0.38f, 0.36f), _skin, false);
            Prim(body, PrimitiveType.Cube, "Glasses", new Vector3(0, 1.72f, 0.18f), new Vector3(0.3f, 0.06f, 0.04f), _ink, false);
            Prim(body, PrimitiveType.Cylinder, "HatBrim", new Vector3(0, 1.86f, 0.02f), new Vector3(0.5f, 0.015f, 0.5f), _suit, false);
            Prim(body, PrimitiveType.Cylinder, "HatTop", new Vector3(0, 1.94f, 0.02f), new Vector3(0.32f, 0.08f, 0.32f), _suit, false);
            var clip = Prim(body, PrimitiveType.Cube, "Clipboard", new Vector3(0.3f, 1.05f, 0.25f), new Vector3(0.24f, 0.32f, 0.03f), _wood, false);
            clip.localRotation = Quaternion.Euler(-25, -20, 0);
            Prim(clip, PrimitiveType.Cube, "Paper", new Vector3(0, -0.05f, 0.6f), new Vector3(0.85f, 0.8f, 0.3f), _paper, false);
            var inspector = insp.gameObject.AddComponent<Inspector>();
            Set(inspector, "body", body);
            Set(inspector, "entrance", entrance);
            Set(inspector, "flagPrefab", flag);
            Set(inspector, "passPrefab", tick);

            // --- managers + shared HUD ----------------------------------------------------------
            var jobGo = new GameObject("Job"); jobGo.transform.SetParent(root, false);
            var job = jobGo.AddComponent<JobManager>();
            Set(job, "inspector", inspector);
            var hud = jobGo.AddComponent<JobHud>();
            Set(hud, "players", Object.FindAnyObjectByType<PlayerInputManager>());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[JobSetup] Scene saved with JobSlice (5 defects, 6 supplies, doorbell, wardrobe, inspector, HUD).");

            Capture(scene, flag, tick);
            Debug.Log("[JobSetup] Done.");
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ defects
        static void MakeDefect(Transform parent, string label, DefectKind kind, Severity sev, Vector3 pos, Vector3 normal)
        {
            var root = new GameObject($"Defect_{kind}_{label.Replace(' ', '_').Replace(",", "")}").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(pos, Quaternion.LookRotation(normal));
            var trigger = root.gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(0.8f, 0.8f, 0.1f);

            var damage = new GameObject("Damage").transform; damage.SetParent(root, false);
            switch (kind)
            {
                case DefectKind.Crack:
                    Flat(damage, "C1", new Vector3(-0.14f, 0.24f, 0), new Vector3(0.055f, 0.32f), 22f, _damage);
                    Flat(damage, "C2", new Vector3(-0.04f, 0.0f, 0), new Vector3(0.05f, 0.3f), -28f, _damage);
                    Flat(damage, "C3", new Vector3(0.07f, -0.24f, 0), new Vector3(0.045f, 0.3f), 14f, _damage);
                    Flat(damage, "C4", new Vector3(0.08f, 0.02f, 0), new Vector3(0.035f, 0.2f), 75f, _damage);
                    Flat(damage, "C5", new Vector3(-0.2f, -0.12f, 0), new Vector3(0.03f, 0.16f), -60f, _damage);
                    break;
                case DefectKind.Hole:
                    Flat(damage, "Rim", Vector3.zero, new Vector3(0.42f, 0.36f), 8f, _trim);
                    Flat(damage, "Hole", new Vector3(0, 0, 0.004f), new Vector3(0.32f, 0.27f), 8f, _damage);
                    break;
                case DefectKind.Mould:
                    for (int i = 0; i < 7; i++)
                    {
                        var p = new Vector3(Mathf.Sin(i * 2.1f) * 0.22f, Mathf.Cos(i * 1.7f) * 0.15f, 0);
                        Flat(damage, $"Spot{i}", p, Vector3.one * (0.12f + (i % 3) * 0.06f), i * 25f, _mould);
                    }
                    break;
                case DefectKind.ExposedWiring:
                    Flat(damage, "Socket", Vector3.zero, new Vector3(0.24f, 0.24f), 0f, _damage);
                    var w1 = Prim(damage, PrimitiveType.Cylinder, "WireRed", new Vector3(-0.04f, -0.06f, 0.08f), new Vector3(0.025f, 0.12f, 0.025f), _accent, false);
                    w1.localRotation = Quaternion.Euler(70, 0, 20);
                    var w2 = Prim(damage, PrimitiveType.Cylinder, "WireBlue", new Vector3(0.05f, -0.04f, 0.07f), new Vector3(0.025f, 0.1f, 0.025f), _blue, false);
                    w2.localRotation = Quaternion.Euler(60, 0, -25);
                    break;
            }
            var paint = Flat(root, "PaintPatch", new Vector3(0, 0, 0.012f), new Vector3(0.62f, 0.55f), 3f, _paint).gameObject;
            var paper = Flat(root, "WallpaperPatch", new Vector3(0, 0, 0.012f), new Vector3(0.7f, 0.7f), 0f, _paper).gameObject;
            Flat(paper.transform, "Stripe1", new Vector3(-0.2f, 0, 0.005f), new Vector3(0.04f, 0.98f), 0f, _props);
            Flat(paper.transform, "Stripe2", new Vector3(0.2f, 0, 0.005f), new Vector3(0.04f, 0.98f), 0f, _props);
            var tape = new GameObject("TapePatch").transform; tape.SetParent(root, false);
            Flat(tape, "T1", new Vector3(0, 0, 0.012f), new Vector3(0.6f, 0.09f), 35f, _tape);
            Flat(tape, "T2", new Vector3(0, 0, 0.014f), new Vector3(0.6f, 0.09f), -35f, _tape);
            var fixedP = Flat(root, "FixedPatch", new Vector3(0, 0, 0.008f), new Vector3(0.6f, 0.55f), 0f, _walls).gameObject;

            var d = root.gameObject.AddComponent<Defect>();
            var so = new SerializedObject(d);
            so.FindProperty("label").stringValue = label;
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("severity").enumValueIndex = (int)sev;
            so.FindProperty("damage").objectReferenceValue = damage.gameObject;
            so.FindProperty("paintPatch").objectReferenceValue = paint;
            so.FindProperty("wallpaperPatch").objectReferenceValue = paper;
            so.FindProperty("tapePatch").objectReferenceValue = tape.gameObject;
            so.FindProperty("fixedPatch").objectReferenceValue = fixedP;
            so.ApplyModifiedPropertiesWithoutUndo();
            paint.SetActive(false); paper.SetActive(false); tape.gameObject.SetActive(false); fixedP.SetActive(false);
        }

        // ------------------------------------------------------------------ prefabs
        static GameObject[] KitPrefabs()
        {
            var result = new GameObject[6];
            foreach (KitType k in System.Enum.GetValues(typeof(KitType)))
            {
                var root = new GameObject($"Kit_{k}");
                Transform shape;
                switch (k)
                {
                    case KitType.Filler: shape = Prim(root.transform, PrimitiveType.Cube, "Tub", Vector3.zero, new Vector3(0.22f, 0.2f, 0.22f), _paper, true); Prim(shape, PrimitiveType.Cube, "Label", new Vector3(0, 0, 0.51f), new Vector3(0.8f, 0.4f, 0.02f), _blue, false); break;
                    case KitType.AntiMould: shape = Prim(root.transform, PrimitiveType.Capsule, "Spray", Vector3.zero, new Vector3(0.12f, 0.16f, 0.12f), _props, true); Prim(shape, PrimitiveType.Cylinder, "Nozzle", new Vector3(0, 1.1f, 0), new Vector3(0.5f, 0.25f, 0.5f), _ink, false); break;
                    case KitType.WiringKit: shape = Prim(root.transform, PrimitiveType.Cube, "Toolbox", Vector3.zero, new Vector3(0.32f, 0.16f, 0.18f), _accent, true); Prim(shape, PrimitiveType.Cube, "Handle", new Vector3(0, 0.65f, 0), new Vector3(0.5f, 0.2f, 0.15f), _ink, false); break;
                    case KitType.PaintTin: shape = Prim(root.transform, PrimitiveType.Cylinder, "Tin", Vector3.zero, new Vector3(0.2f, 0.1f, 0.2f), _paint, true); Prim(shape, PrimitiveType.Cylinder, "Lid", new Vector3(0, 1f, 0), new Vector3(1.02f, 0.1f, 1.02f), _blue, false); break;
                    case KitType.WallpaperRoll: shape = Prim(root.transform, PrimitiveType.Cylinder, "Roll", Vector3.zero, new Vector3(0.12f, 0.2f, 0.12f), _paper, true); shape.localRotation = Quaternion.Euler(0, 0, 90); break;
                    default: shape = Prim(root.transform, PrimitiveType.Cylinder, "Tape", Vector3.zero, new Vector3(0.16f, 0.035f, 0.16f), _tape, true); break;
                }
                var rb = root.AddComponent<Rigidbody>(); rb.mass = 1f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                root.AddComponent<Carryable>();
                var kit = root.AddComponent<RepairKit>();
                var so = new SerializedObject(kit); so.FindProperty("type").enumValueIndex = (int)k; so.ApplyModifiedPropertiesWithoutUndo();
                result[(int)k] = SavePrefab(root, $"{PrefabDir}/Kits/Kit_{k}.prefab");
            }
            return result;
        }

        static GameObject FlagPrefab()
        {
            var root = new GameObject("RejectionFlag");
            Prim(root.transform, PrimitiveType.Cylinder, "Pole", new Vector3(0, 0.1f, 0), new Vector3(0.025f, 0.3f, 0.025f), _ink, false);
            Prim(root.transform, PrimitiveType.Cube, "Flag", new Vector3(0.13f, 0.32f, 0), new Vector3(0.24f, 0.16f, 0.015f), _accent, false);
            return SavePrefab(root, $"{PrefabDir}/RejectionFlag.prefab");
        }

        static GameObject PassPrefab()
        {
            var root = new GameObject("PassTick");
            Prim(root.transform, PrimitiveType.Cube, "Short", new Vector3(-0.05f, 0.0f, 0), new Vector3(0.04f, 0.12f, 0.02f), _pass, false).localRotation = Quaternion.Euler(0, 0, 40);
            Prim(root.transform, PrimitiveType.Cube, "Long", new Vector3(0.05f, 0.05f, 0), new Vector3(0.04f, 0.24f, 0.02f), _pass, false).localRotation = Quaternion.Euler(0, 0, -30);
            return SavePrefab(root, $"{PrefabDir}/PassTick.prefab");
        }

        static void PatchPlayerPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            if (!root.TryGetComponent<PlayerHud>(out var hud)) hud = root.AddComponent<PlayerHud>();
            Set(hud, "playerCamera", root.GetComponentInChildren<Camera>());
            Set(hud, "interactor", root.GetComponent<CarryInteractor>());
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            PrefabUtility.UnloadPrefabContents(root);
            Debug.Log("[JobSetup] Bodger prefab now has PlayerHud.");
        }

        // ------------------------------------------------------------------ materials
        static void Materials()
        {
            var urp = (UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            var src = urp.defaultMaterial;
            _walls = Load("M_Walls"); _wood = Load("M_Floor"); _trim = Load("M_Trim"); _props = Load("M_Props");
            _blue = Load("M_CharacterSecondary"); _accent = Load("M_Accent"); _skin = _walls;
            _ink = Mat("M_Ink", src, Palette.UiInk, 0.2f);
            _damage = Mat("M_DamageDark", src, Palette.DamageDark, 0.05f);
            _mould = Mat("M_Mould", src, Palette.Mould, 0.0f);
            _paint = Mat("M_FreshPaint", src, Palette.FreshPaint, 0.35f);
            _tape = Mat("M_DuctTape", src, Palette.Tape, 0.55f);
            _paper = Mat("M_Wallpaper", src, Palette.Wallpaper, 0.05f);
            _suit = Mat("M_InspectorSuit", src, Palette.InspectorSuit, 0.15f);
            _pass = Mat("M_Pass", src, Palette.Pass, 0.2f);
            _pass.EnableKeyword("_EMISSION"); _pass.SetColor("_EmissionColor", Palette.Pass * 0.6f);
            _accent.SetColor("_EmissionColor", Palette.Accent * 0.45f); // red must read red, not bloom to pink
            EditorUtility.SetDirty(_accent);
            _glass = Mat("M_WindowGlass", src, Palette.AmbientSky, 0.9f);
            _glass.EnableKeyword("_EMISSION"); _glass.SetColor("_EmissionColor", Palette.AmbientSky * 0.7f);
            AssetDatabase.SaveAssets();
        }

        static Material Load(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        static Material Mat(string name, Material src, Color c, float smooth)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ capture
        static void Capture(UnityEngine.SceneManagement.Scene scene, GameObject flag, GameObject tick)
        {
            var cam = Camera.main;
            Shot(cam, new Vector3(-3.3f, 2.4f, -3.3f), Quaternion.Euler(22, 45, 0), "Docs/slice_overview.png");
            Shot(cam, new Vector3(-1.2f, 1.6f, 1.2f), Quaternion.Euler(8, -15, 0), "Docs/slice_supplies.png");

            // Mock the end of a job (not saved): some bodges, a flag and a tick
            var defects = Object.FindObjectsByType<Defect>();
            foreach (var d in defects)
            {
                if (d.Kind == DefectKind.Hole) d.Apply(KitType.PaintTin, 1);
                if (d.Kind == DefectKind.Crack && d.Severity == Severity.Cosmetic && d.Label == "east wall, up high") d.Apply(KitType.DuctTape, 0);
                if (d.Kind == DefectKind.Mould) d.Apply(KitType.AntiMould, 2);
            }
            foreach (var d in defects)
            {
                if (d.Kind == DefectKind.Hole) { d.Reveal(); Object.Instantiate(flag, d.transform.position + d.transform.forward * 0.12f + Vector3.up * 0.25f, Quaternion.LookRotation(d.transform.forward)); }
                if (d.Kind == DefectKind.Mould) Object.Instantiate(tick, d.transform.position + d.transform.forward * 0.12f + Vector3.up * 0.25f, Quaternion.LookRotation(d.transform.forward));
            }
            Shot(cam, new Vector3(1.0f, 1.5f, -1.5f), Quaternion.Euler(10, 70, 0), "Docs/slice_flagged.png");
        }

        static void Shot(Camera cam, Vector3 pos, Quaternion rot, string file)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            for (int i = 0; i < 4; i++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            Directory.CreateDirectory("Docs");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            Debug.Log($"[JobSetup] Captured {file}");
        }

        // ------------------------------------------------------------------ helpers
        static TextMeshPro WorldText(Transform parent, string name, string text, Vector3 pos, Quaternion rot, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(4, 1);
            return t;
        }

        static Transform Box(Transform parent, string name, Vector3 worldPos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        static Transform Flat(Transform parent, string name, Vector3 localPos, Vector3 size, float rollDeg, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos + new Vector3(0, 0, 0.004f);
            go.transform.localRotation = Quaternion.Euler(0, 180, rollDeg); // quad faces -Z; flip to face out of the wall
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        static Transform Prim(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material m, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        static GameObject SavePrefab(GameObject root, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var p = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return p;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[JobSetup] No field '{field}' on {target.GetType().Name}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Fail(string msg)
        {
            Debug.LogError($"[JobSetup] {msg}");
            EditorApplication.Exit(1);
        }
    }
}
