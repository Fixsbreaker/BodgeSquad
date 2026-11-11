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

        static Material _cream, _hair, _hat, _stripe, _glass, _ink, _damage, _mould, _paint, _tape, _paper, _suit, _accent, _pass, _wood, _walls, _trim, _props, _blue, _skin;

        public static void Run()
        {
            if (TMP_Settings.defaultFontAsset == null) { Fail("TMP default font missing — run ImportTmp first."); return; }
            Materials();
            var kits = KitPrefabs();
            var flag = FlagPrefab();
            var tick = PassPrefab();
            PatchPlayerPrefab();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var room = BuildShell();
            var slice = GameObject.Find("JobSlice");
            if (slice != null) Object.DestroyImmediate(slice);
            var root = new GameObject("JobSlice").transform;

            // --- defects ------------------------------------------------------------------------
            var defects = new GameObject("Defects").transform; defects.SetParent(root, false);
            const float X = RoomDims.DX, Z = RoomDims.DZ;
            MakeDefect(defects, "north wall", DefectKind.Crack, Severity.Cosmetic, new Vector3(2.6f, 1.7f, Z), Vector3.back);
            MakeDefect(defects, "north corner", DefectKind.Mould, Severity.FutureFailure, new Vector3(4.6f, 0.45f, Z), Vector3.back);
            MakeDefect(defects, "east wall", DefectKind.Hole, Severity.FutureFailure, new Vector3(X, 0.9f, 1.2f), Vector3.left);
            MakeDefect(defects, "east wall, up high", DefectKind.Crack, Severity.Cosmetic, new Vector3(X, 2.0f, -2.6f), Vector3.left);
            MakeDefect(defects, "south wall", DefectKind.Mould, Severity.FutureFailure, new Vector3(2.0f, 0.45f, -Z), Vector3.forward);
            MakeDefect(defects, "west wall", DefectKind.ExposedWiring, Severity.Safety, new Vector3(-X, 1.2f, 1.8f), Vector3.right);
            MakeDefect(defects, "west wall, low", DefectKind.Hole, Severity.FutureFailure, new Vector3(-X, 0.7f, -2.2f), Vector3.right);

            // --- door, entrance, doorbell -------------------------------------------------------
            var door = new GameObject("FrontDoor").transform; door.SetParent(root, false);
            float sz = -(RoomDims.InZ - 0.025f);
            Box(door, "Frame_L", new Vector3(-4.05f, 1.05f, sz), new Vector3(0.1f, 2.1f, 0.12f), _trim);
            Box(door, "Frame_R", new Vector3(-2.95f, 1.05f, sz), new Vector3(0.1f, 2.1f, 0.12f), _trim);
            Box(door, "Frame_Top", new Vector3(-3.5f, 2.12f, sz), new Vector3(1.2f, 0.1f, 0.12f), _trim);
            Box(door, "Door", new Vector3(-3.5f, 1.03f, sz + 0.01f), new Vector3(1.0f, 2.06f, 0.06f), _wood);
            Box(door, "Handle", new Vector3(-3.12f, 1.0f, sz + 0.06f), new Vector3(0.12f, 0.04f, 0.05f), _ink);
            var entrance = new GameObject("InspectorEntrance").transform; entrance.SetParent(door, false);
            entrance.position = new Vector3(-3.5f, 0f, sz + 0.6f); entrance.rotation = Quaternion.LookRotation(Vector3.forward);

            var bell = Box(root, "Doorbell", new Vector3(-2.55f, 1.35f, sz + 0.01f), new Vector3(0.22f, 0.3f, 0.06f), _ink);
            var button = Prim(bell, PrimitiveType.Cylinder, "Button", new Vector3(0, 0.1f, 0.6f), new Vector3(0.45f, 0.4f, 0.3f), _accent, keepCollider: false);
            button.localRotation = Quaternion.Euler(90, 0, 0);
            var bellC = bell.gameObject.AddComponent<Doorbell>();
            Set(bellC, "button", button);
            Card(root, "DoorbellCard", new Vector3(-2.55f, 1.72f, sz - 0.005f), Quaternion.Euler(0, 180, 0), new Vector2(0.62f, 0.3f));
            WorldText(root, "DoorbellLabel", "CALL\nINSPECTOR", new Vector3(-2.55f, 1.72f, sz + 0.04f), Quaternion.Euler(0, 180, 0), 1.1f, Palette.UiInk);

            // --- supplies shelf on the north wall -----------------------------------------------
            var shop = new GameObject("Supplies").transform; shop.SetParent(root, false);
            Box(shop, "Table", new Vector3(-3.8f, 0.82f, 4.55f), new Vector3(3.2f, 0.08f, 0.6f), _wood);
            foreach (var x in new[] { -5.3f, -2.3f }) foreach (var z in new[] { 4.3f, 4.8f })
                Box(shop, "Leg", new Vector3(x, 0.39f, z), new Vector3(0.07f, 0.78f, 0.07f), _trim);
            Card(shop, "SignBoard", new Vector3(-3.8f, 2.35f, RoomDims.InZ - 0.015f), Quaternion.identity, new Vector2(2.9f, 0.42f));
            WorldText(shop, "Sign", "SUPPLIES  <size=70%>(pay on pickup)</size>", new Vector3(-3.8f, 2.35f, RoomDims.InZ - 0.045f), Quaternion.identity, 2.2f, Palette.UiInk);
            KitType[] order = { KitType.Filler, KitType.AntiMould, KitType.WiringKit, KitType.PaintTin, KitType.WallpaperRoll, KitType.DuctTape };
            for (int i = 0; i < order.Length; i++)
            {
                float x = -5.05f + i * 0.5f;
                var shelf = new GameObject($"Shelf_{order[i]}").transform; shelf.SetParent(shop, false);
                shelf.position = new Vector3(x, 0.86f, 4.5f);
                var slot = new GameObject("Slot").transform; slot.SetParent(shelf, false);
                slot.localPosition = new Vector3(0, 0.16f, 0);
                Card(shelf, "TagCard", new Vector3(x, 1.28f, 4.315f), Quaternion.identity, new Vector2(0.46f, 0.3f));
                var tag = WorldText(shelf, "PriceTag", "", new Vector3(x, 1.28f, 4.3f), Quaternion.identity, 0.62f,
                                    RepairRules.IsProperFix(order[i]) ? Palette.CharacterSecondary : Palette.Accent);
                var s = shelf.gameObject.AddComponent<SupplyShelf>();
                Set(s, "kitPrefab", kits[(int)order[i]]);
                Set(s, "slot", slot);
                Set(s, "priceTag", tag);
                tag.text = $"{RepairRules.KitName(order[i])}\n<b>${RepairRules.Price(order[i])}</b>";
                tag.rectTransform.sizeDelta = new Vector2(0.46f, 0.3f);
                var so = new SerializedObject(s); so.FindProperty("kit").enumValueIndex = (int)order[i]; so.ApplyModifiedPropertiesWithoutUndo();
            }

            // --- wardrobe: shove it in front of damage -------------------------------------------
            var wardrobe = Box(root, "Wardrobe", new Vector3(-1.0f, 0.9f, 0.8f), new Vector3(1.0f, 1.8f, 0.5f), _wood);
            Prim(wardrobe, PrimitiveType.Cube, "DoorLine", new Vector3(0, 0, 0.51f), new Vector3(0.015f, 0.95f, 0.02f), _trim, false);
            Prim(wardrobe, PrimitiveType.Cube, "Knob_L", new Vector3(-0.06f, 0.05f, 0.53f), new Vector3(0.03f, 0.03f, 0.04f), _ink, false);
            Prim(wardrobe, PrimitiveType.Cube, "Knob_R", new Vector3(0.06f, 0.05f, 0.53f), new Vector3(0.03f, 0.03f, 0.04f), _ink, false);
            var wrb = wardrobe.gameObject.AddComponent<Rigidbody>(); wrb.mass = 40f; wrb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var wc = wardrobe.gameObject.AddComponent<Carryable>();
            var wso = new SerializedObject(wc); wso.FindProperty("carryWeight").floatValue = 3f; wso.ApplyModifiedPropertiesWithoutUndo();
            var wcon = wardrobe.gameObject.AddComponent<Concealer>();
            var wcso = new SerializedObject(wcon); wcso.FindProperty("displayName").stringValue = "Wardrobe"; wcso.ApplyModifiedPropertiesWithoutUndo();

            // --- inspector -----------------------------------------------------------------------
            var insp = new GameObject("Inspector").transform; insp.SetParent(root, false);
            insp.SetPositionAndRotation(entrance.position, entrance.rotation);
            var body = new GameObject("Body").transform; body.SetParent(insp, false);
            // Key-art inspector: plum suit, red tie, big head, angry brows, moustache, glasses, fedora, clipboard
            Prim(body, PrimitiveType.Cube, "Leg_L", new Vector3(-0.12f, 0.3f, 0), new Vector3(0.2f, 0.6f, 0.24f), _suit, false);
            Prim(body, PrimitiveType.Cube, "Leg_R", new Vector3(0.12f, 0.3f, 0), new Vector3(0.2f, 0.6f, 0.24f), _suit, false);
            Prim(body, PrimitiveType.Cube, "Shoe_L", new Vector3(-0.12f, 0.05f, 0.05f), new Vector3(0.22f, 0.1f, 0.32f), _ink, false);
            Prim(body, PrimitiveType.Cube, "Shoe_R", new Vector3(0.12f, 0.05f, 0.05f), new Vector3(0.22f, 0.1f, 0.32f), _ink, false);
            Prim(body, PrimitiveType.Cube, "Jacket", new Vector3(0, 0.9f, 0), new Vector3(0.62f, 0.66f, 0.4f), _suit, false);
            Prim(body, PrimitiveType.Cube, "Shirt", new Vector3(0, 1.08f, 0.17f), new Vector3(0.2f, 0.3f, 0.08f), _cream, false);
            Prim(body, PrimitiveType.Cube, "Tie", new Vector3(0, 1.02f, 0.215f), new Vector3(0.07f, 0.36f, 0.02f), _accent, false);
            var armL = Prim(body, PrimitiveType.Cube, "Arm_L", new Vector3(-0.4f, 0.92f, 0), new Vector3(0.16f, 0.5f, 0.2f), _suit, false); armL.localRotation = Quaternion.Euler(0, 0, -8);
            var armR = Prim(body, PrimitiveType.Cube, "Arm_R", new Vector3(0.38f, 0.98f, 0.12f), new Vector3(0.16f, 0.5f, 0.2f), _suit, false); armR.localRotation = Quaternion.Euler(-50, 0, 10);
            Prim(body, PrimitiveType.Sphere, "Hand_L", new Vector3(-0.43f, 0.64f, 0), Vector3.one * 0.15f, _skin, false);
            Prim(body, PrimitiveType.Sphere, "Head", new Vector3(0, 1.5f, 0.02f), new Vector3(0.56f, 0.56f, 0.52f), _skin, false);
            Prim(body, PrimitiveType.Sphere, "Nose", new Vector3(0, 1.48f, 0.29f), new Vector3(0.12f, 0.12f, 0.12f), _skin, false);
            Prim(body, PrimitiveType.Cube, "Moustache", new Vector3(0, 1.4f, 0.27f), new Vector3(0.26f, 0.07f, 0.06f), _hair, false);
            var browL = Prim(body, PrimitiveType.Cube, "Brow_L", new Vector3(-0.1f, 1.62f, 0.245f), new Vector3(0.13f, 0.035f, 0.03f), _hair, false); browL.localRotation = Quaternion.Euler(0, 0, -15);
            var browR = Prim(body, PrimitiveType.Cube, "Brow_R", new Vector3(0.1f, 1.62f, 0.245f), new Vector3(0.13f, 0.035f, 0.03f), _hair, false); browR.localRotation = Quaternion.Euler(0, 0, 15);
            foreach (var gx in new[] { -0.1f, 0.1f })
            {
                Prim(body, PrimitiveType.Cube, "GlassFrame", new Vector3(gx, 1.55f, 0.268f), new Vector3(0.15f, 0.1f, 0.02f), _ink, false);
                Prim(body, PrimitiveType.Cube, "GlassLens", new Vector3(gx, 1.55f, 0.28f), new Vector3(0.11f, 0.065f, 0.01f), _glass, false);
            }
            Prim(body, PrimitiveType.Cube, "GlassBridge", new Vector3(0, 1.56f, 0.285f), new Vector3(0.06f, 0.02f, 0.02f), _ink, false);
            Prim(body, PrimitiveType.Cylinder, "HatBrim", new Vector3(0, 1.74f, 0.02f), new Vector3(0.66f, 0.015f, 0.62f), _hat, false);
            Prim(body, PrimitiveType.Cylinder, "HatTop", new Vector3(0, 1.84f, 0.02f), new Vector3(0.42f, 0.1f, 0.4f), _hat, false);
            Prim(body, PrimitiveType.Cylinder, "HatBand", new Vector3(0, 1.77f, 0.02f), new Vector3(0.43f, 0.025f, 0.41f), _ink, false);
            var clip = Prim(body, PrimitiveType.Cube, "Clipboard", new Vector3(0.28f, 1.12f, 0.3f), new Vector3(0.26f, 0.34f, 0.03f), _wood, false);
            clip.localRotation = Quaternion.Euler(-35, -20, 0);
            Prim(clip, PrimitiveType.Cube, "Paper", new Vector3(0, -0.05f, 0.6f), new Vector3(0.85f, 0.8f, 0.3f), _cream, false);
            Prim(clip, PrimitiveType.Cube, "Clip", new Vector3(0, 0.45f, 0.8f), new Vector3(0.4f, 0.1f, 0.5f), _tape, false);
            var inspector = insp.gameObject.AddComponent<Inspector>();
            Set(inspector, "body", body);
            Set(inspector, "entrance", entrance);
            Set(inspector, "flagPrefab", flag);
            Set(inspector, "passPrefab", tick);

            // --- managers + shared HUD ----------------------------------------------------------
            var jobGo = new GameObject("Job"); jobGo.transform.SetParent(root, false);
            var job = jobGo.AddComponent<JobManager>();
            Set(job, "inspector", inspector);
            var guard = jobGo.AddComponent<PlayAreaGuard>();
            Set(guard, "spawnRoot", GameObject.Find("SpawnPoints").transform);
            var gso = new SerializedObject(guard);
            gso.FindProperty("halfExtents").vector3Value = new Vector3(RoomDims.InX - 0.05f, 3.2f, RoomDims.InZ - 0.05f);
            gso.ApplyModifiedPropertiesWithoutUndo();
            var hud = jobGo.AddComponent<JobHud>();
            Set(hud, "players", Object.FindAnyObjectByType<PlayerInputManager>());

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[JobSetup] Scene saved with JobSlice (7 defects, 6 supplies, doorbell, wardrobe, inspector, HUD).");

            Capture(scene, flag, tick);
            Debug.Log("[JobSetup] Done.");
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ room shell (12 x 10 m)
        static Transform BuildShell()
        {
            var old = GameObject.Find("LookDevRoom");
            if (old != null) Object.DestroyImmediate(old);
            var room = new GameObject("LookDevRoom").transform;
            float hx = RoomDims.HX, hz = RoomDims.HZ, h = RoomDims.Height, w = RoomDims.Wall;
            var floorMat = Load("M_Floor");
            Box(room, "Floor", new Vector3(0, -0.05f, 0), new Vector3(hx * 2, 0.1f, hz * 2), floorMat);
            Box(room, "Wall_North", new Vector3(0, h / 2, hz), new Vector3(hx * 2 + w, h, w), _walls);
            Box(room, "Wall_South", new Vector3(0, h / 2, -hz), new Vector3(hx * 2 + w, h, w), _walls);
            Box(room, "Wall_East", new Vector3(hx, h / 2, 0), new Vector3(w, h, hz * 2 + w), _walls);
            Box(room, "Wall_West", new Vector3(-hx, h / 2, 0), new Vector3(w, h, hz * 2 + w), _walls);
            float bx = RoomDims.InX - 0.03f, bz = RoomDims.InZ - 0.03f;
            Box(room, "Baseboard_North", new Vector3(0, 0.06f, bz), new Vector3(hx * 2, 0.12f, 0.06f), _trim);
            Box(room, "Baseboard_South", new Vector3(0, 0.06f, -bz), new Vector3(hx * 2, 0.12f, 0.06f), _trim);
            Box(room, "Baseboard_East", new Vector3(bx, 0.06f, 0), new Vector3(0.06f, 0.12f, hz * 2), _trim);
            Box(room, "Baseboard_West", new Vector3(-bx, 0.06f, 0), new Vector3(0.06f, 0.12f, hz * 2), _trim);

            // invisible lid: nobody jumps (or throws a sofa) out of the house
            var lid = new GameObject("Ceiling_Invisible", typeof(BoxCollider)).transform;
            lid.SetParent(room, false);
            lid.position = new Vector3(0, h + 0.05f, 0);
            lid.localScale = new Vector3(hx * 2 + 1f, 0.1f, hz * 2 + 1f);

            // window on the north wall
            var frame = Box(room, "WindowFrame", new Vector3(0f, 1.6f, RoomDims.InZ - 0.04f), new Vector3(1.4f, 1.1f, 0.08f), _trim);
            Prim(frame, PrimitiveType.Cube, "Glass", new Vector3(0, 0, -0.6f), new Vector3(0.9f, 0.88f, 0.2f), _glass, false);
            Prim(frame, PrimitiveType.Cube, "MullionV", new Vector3(0, 0, -0.9f), new Vector3(0.05f, 0.9f, 0.3f), _trim, false);
            Prim(frame, PrimitiveType.Cube, "MullionH", new Vector3(0, 0, -0.9f), new Vector3(0.9f, 0.06f, 0.3f), _trim, false);

            // loose props
            var crate = Box(room, "Crate", new Vector3(3.6f, 0.35f, 3.4f), new Vector3(0.8f, 0.7f, 0.8f), _wood);
            crate.rotation = Quaternion.Euler(0, 7, 0);
            Carry(crate.gameObject, 6f, 1.6f);
            var plank = Box(room, "Plank", new Vector3(1.0f, 0.9f, RoomDims.InZ - 0.2f), new Vector3(0.25f, 1.8f, 0.06f), _wood);
            plank.rotation = Quaternion.Euler(-9, 0, 12);
            Carry(plank.gameObject, 3f, 1.2f);

            // spawns near the front door, facing into the room
            var spawns = GameObject.Find("SpawnPoints")?.transform;
            if (spawns == null) { spawns = new GameObject("SpawnPoints").transform; }
            var pts = new[] { new Vector3(-3.6f, 0, -3.2f), new Vector3(-2.2f, 0, -3.2f), new Vector3(-3.6f, 0, -1.9f), new Vector3(-2.2f, 0, -1.9f) };
            for (int i = 0; i < pts.Length; i++)
            {
                var sp = i < spawns.childCount ? spawns.GetChild(i) : new GameObject($"Spawn_P{i + 1}").transform;
                sp.SetParent(spawns, false);
                sp.position = pts[i];
                sp.rotation = Quaternion.LookRotation(new Vector3(1.5f, 0, 2.5f) - pts[i]);
            }

            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(new Vector3(-5.4f, 2.6f, -4.4f), Quaternion.Euler(22, 45, 0));
            return room;
        }

        static void Carry(GameObject go, float mass, float weight)
        {
            var rb = go.AddComponent<Rigidbody>(); rb.mass = mass; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var c = go.AddComponent<Carryable>();
            var so = new SerializedObject(c); so.FindProperty("carryWeight").floatValue = weight; so.ApplyModifiedPropertiesWithoutUndo();
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
            Flat(paper.transform, "Stripe1", new Vector3(-0.2f, 0, 0.005f), new Vector3(0.04f, 0.98f), 0f, _stripe);
            Flat(paper.transform, "Stripe2", new Vector3(0.2f, 0, 0.005f), new Vector3(0.04f, 0.98f), 0f, _stripe);
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
            _walls = Mat("M_Walls", src, Palette.Walls, 0.05f); _trim = Mat("M_Trim", src, Palette.Trim, 0.2f); _props = Load("M_Props");
            _wood = Mat("M_Wood", src, Palette.Wood, 0.2f);
            _blue = Load("M_CharacterSecondary"); _accent = Load("M_Accent");
            _skin = Mat("M_Skin", src, Palette.Skin, 0.25f);
            _cream = Mat("M_Cream", src, Palette.Cream, 0.1f);
            _hair = Mat("M_Hair", src, Palette.Hair, 0.1f);
            _hat = Mat("M_InspectorHat", src, Palette.InspectorHat, 0.1f);
            _stripe = Mat("M_WallsDark", src, Palette.WallsDark, 0.05f);
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
            Shot(cam, new Vector3(-5.4f, 2.6f, -4.4f), Quaternion.Euler(22, 45, 0), "Docs/slice_overview.png");
            Shot(cam, new Vector3(-3.0f, 1.6f, 2.2f), Quaternion.Euler(8, -15, 0), "Docs/slice_supplies.png");

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
            Shot(cam, new Vector3(2.8f, 1.5f, -0.5f), Quaternion.Euler(10, 70, 0), "Docs/slice_flagged.png");
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

        // Cream backing card so world text reads on the teal wallpaper
        static void Card(Transform parent, string name, Vector3 worldPos, Quaternion rot, Vector2 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(worldPos, rot);
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            go.GetComponent<Renderer>().sharedMaterial = _cream;
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
