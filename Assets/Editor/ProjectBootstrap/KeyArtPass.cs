using System.IO;
using BodgeSquad.Art;
using BodgeSquad.Interaction;
using BodgeSquad.Players;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ProjectBootstrap
{
    // Pulls the look toward the itch.io key art (Docs/KeyArt/):
    //   palette materials, chunky big-headed bodgers with a signature hat per player,
    //   teal peeling wallpaper room with sofa, rug, lamp, plant and duct-tape X's.
    // Run AFTER JobSetup.Run:  -executeMethod ProjectBootstrap.KeyArtPass.Run
    public static class KeyArtPass
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string PrefabPath = "Assets/Prefabs/Bodger.prefab";
        const string MatDir = "Assets/Art/Materials";

        static Material _floor, _walls, _stripe, _plaster, _plasterEdge, _wood, _trim, _sofa, _rug, _leaf, _pot, _cream,
                        _vest, _overalls, _skin, _glove, _ink, _tape, _tint, _glass, _lampShade, _props;

        public static void Run()
        {
            Materials();
            BuildBodgerVisual();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Lighting();
            Room();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[KeyArt] Scene saved.");
            Captures();
            Debug.Log("[KeyArt] Done.");
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ materials
        static void Materials()
        {
            var src = ((UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline).defaultMaterial;
            _floor = Mat("M_Floor", src, Palette.Floor, 0.2f);
            _walls = Mat("M_Walls", src, Palette.Walls, 0.05f);
            _stripe = Mat("M_WallsDark", src, Palette.WallsDark, 0.05f);
            _plaster = Mat("M_Plaster", src, Palette.Plaster, 0.0f);
            _plasterEdge = Mat("M_PlasterEdge", src, Palette.PlasterEdge, 0.0f);
            _wood = Mat("M_Wood", src, Palette.Wood, 0.2f);
            _trim = Mat("M_Trim", src, Palette.Trim, 0.2f);
            _sofa = Mat("M_Sofa", src, Palette.Sofa, 0.05f);
            _rug = Mat("M_Rug", src, Palette.Rug, 0.0f);
            _leaf = Mat("M_Leaf", src, Palette.Leaf, 0.25f);
            _pot = Mat("M_Terracotta", src, Palette.Terracotta, 0.1f);
            _cream = Mat("M_Cream", src, Palette.Cream, 0.1f);
            _vest = Mat("M_CharacterPrimary", src, Palette.CharacterPrimary, 0.2f);
            _overalls = Mat("M_CharacterSecondary", src, Palette.CharacterSecondary, 0.15f);
            _skin = Mat("M_Skin", src, Palette.Skin, 0.25f);
            _glove = Mat("M_Glove", src, Palette.Glove, 0.1f);
            _ink = Mat("M_Ink", src, Palette.UiInk, 0.2f);
            _tape = Mat("M_DuctTape", src, Palette.Tape, 0.55f);
            _props = Mat("M_Props", src, Palette.Props, 0.1f);
            _tint = Mat("M_HatTint", src, Palette.PlayerTints[0], 0.35f); // per-player color comes from a property block
            _glass = Mat("M_GogglesGlass", src, Palette.AmbientSky, 0.9f);
            Mat("M_FreshPaint", src, Palette.FreshPaint, 0.35f);
            Mat("M_Wallpaper", src, Palette.Wallpaper, 0.05f);
            Mat("M_Mould", src, Palette.Mould, 0.0f);
            Mat("M_DamageDark", src, Palette.DamageDark, 0.05f);
            Mat("M_InspectorSuit", src, Palette.InspectorSuit, 0.15f);
            var accent = Mat("M_Accent", src, Palette.Accent, 0.3f);
            accent.SetColor("_EmissionColor", Palette.Accent * 0.45f);
            _lampShade = Mat("M_LampShade", src, Palette.Cream, 0.1f);
            _lampShade.EnableKeyword("_EMISSION");
            _lampShade.SetColor("_EmissionColor", Palette.LampLight * 1.4f);
            _lampShade.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ bodger
        // Chunky, big-headed, determined. Local +Z is forward. CharacterController is 1.8 m tall, eye at 1.62.
        static void BuildBodgerVisual()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            var visual = root.transform.Find("Visual");
            for (int i = visual.childCount - 1; i >= 0; i--) Object.DestroyImmediate(visual.GetChild(i).gameObject);

            // legs, boots
            P(visual, PrimitiveType.Cube, "Leg_L", new Vector3(-0.13f, 0.33f, 0), new Vector3(0.22f, 0.46f, 0.24f), _overalls);
            P(visual, PrimitiveType.Cube, "Leg_R", new Vector3(0.13f, 0.33f, 0), new Vector3(0.22f, 0.46f, 0.24f), _overalls);
            P(visual, PrimitiveType.Cube, "Boot_L", new Vector3(-0.13f, 0.07f, 0.05f), new Vector3(0.24f, 0.14f, 0.34f), _glove);
            P(visual, PrimitiveType.Cube, "Boot_R", new Vector3(0.13f, 0.07f, 0.05f), new Vector3(0.24f, 0.14f, 0.34f), _glove);
            // torso: overalls bib under a hi-vis vest with a reflective band
            P(visual, PrimitiveType.Cube, "Torso", new Vector3(0, 0.8f, 0), new Vector3(0.62f, 0.6f, 0.42f), _overalls);
            P(visual, PrimitiveType.Cube, "Vest", new Vector3(0, 0.92f, 0), new Vector3(0.65f, 0.38f, 0.45f), _vest);
            P(visual, PrimitiveType.Cube, "VestBand", new Vector3(0, 0.86f, 0), new Vector3(0.66f, 0.05f, 0.46f), _cream);
            P(visual, PrimitiveType.Cube, "ToolBelt", new Vector3(0, 0.56f, 0), new Vector3(0.64f, 0.07f, 0.44f), _trim);
            P(visual, PrimitiveType.Cube, "Pouch", new Vector3(0.22f, 0.5f, 0.2f), new Vector3(0.14f, 0.14f, 0.08f), _wood);
            // arms + big gloves
            P(visual, PrimitiveType.Cube, "Arm_L", new Vector3(-0.41f, 0.86f, 0), new Vector3(0.17f, 0.42f, 0.2f), _vest).localRotation = Quaternion.Euler(0, 0, -10);
            P(visual, PrimitiveType.Cube, "Arm_R", new Vector3(0.41f, 0.86f, 0), new Vector3(0.17f, 0.42f, 0.2f), _vest).localRotation = Quaternion.Euler(0, 0, 10);
            P(visual, PrimitiveType.Cube, "Glove_L", new Vector3(-0.46f, 0.6f, 0.02f), new Vector3(0.2f, 0.18f, 0.22f), _glove);
            P(visual, PrimitiveType.Cube, "Glove_R", new Vector3(0.46f, 0.6f, 0.02f), new Vector3(0.2f, 0.18f, 0.22f), _glove);
            // big head, gritted-teeth determination
            P(visual, PrimitiveType.Sphere, "Head", new Vector3(0, 1.42f, 0.02f), new Vector3(0.6f, 0.56f, 0.56f), _skin);
            P(visual, PrimitiveType.Sphere, "Nose", new Vector3(0, 1.4f, 0.3f), Vector3.one * 0.11f, _skin);
            P(visual, PrimitiveType.Sphere, "Eye_L", new Vector3(-0.11f, 1.47f, 0.275f), Vector3.one * 0.07f, _ink);
            P(visual, PrimitiveType.Sphere, "Eye_R", new Vector3(0.11f, 1.47f, 0.275f), Vector3.one * 0.07f, _ink);
            P(visual, PrimitiveType.Cube, "Brow_L", new Vector3(-0.11f, 1.545f, 0.255f), new Vector3(0.13f, 0.035f, 0.03f), _ink).localRotation = Quaternion.Euler(0, 0, -14);
            P(visual, PrimitiveType.Cube, "Brow_R", new Vector3(0.11f, 1.545f, 0.255f), new Vector3(0.13f, 0.035f, 0.03f), _ink).localRotation = Quaternion.Euler(0, 0, 14);
            P(visual, PrimitiveType.Cube, "Mouth", new Vector3(0, 1.32f, 0.27f), new Vector3(0.18f, 0.07f, 0.03f), _ink);
            P(visual, PrimitiveType.Cube, "Teeth", new Vector3(0, 1.32f, 0.282f), new Vector3(0.15f, 0.045f, 0.02f), _cream);

            // signature hats — one active per player (PlayerSlot picks by index)
            var hats = new GameObject("Hats").transform; hats.SetParent(visual, false);
            var tinted = new System.Collections.Generic.List<Renderer>();
            Transform T(Transform t) { tinted.Add(t.GetComponent<Renderer>()); return t; }

            var hard = Group(hats, "Hat_HardHat");
            T(P(hard, PrimitiveType.Sphere, "Dome", new Vector3(0, 1.6f, 0.02f), new Vector3(0.64f, 0.4f, 0.62f), _tint));
            T(P(hard, PrimitiveType.Cylinder, "Brim", new Vector3(0, 1.6f, 0.06f), new Vector3(0.72f, 0.015f, 0.74f), _tint));
            T(P(hard, PrimitiveType.Cube, "Ridge", new Vector3(0, 1.78f, 0.02f), new Vector3(0.1f, 0.06f, 0.5f), _tint));

            var cap = Group(hats, "Hat_BackwardsCap");
            T(P(cap, PrimitiveType.Sphere, "Crown", new Vector3(0, 1.6f, 0f), new Vector3(0.62f, 0.34f, 0.6f), _tint));
            T(P(cap, PrimitiveType.Cube, "Bill", new Vector3(0, 1.58f, -0.38f), new Vector3(0.34f, 0.03f, 0.26f), _tint)).localRotation = Quaternion.Euler(-8, 0, 0);
            P(cap, PrimitiveType.Cube, "Strap", new Vector3(0, 1.6f, 0.28f), new Vector3(0.2f, 0.05f, 0.03f), _cream);

            var beanie = Group(hats, "Hat_Beanie");
            T(P(beanie, PrimitiveType.Sphere, "Knit", new Vector3(0, 1.68f, 0f), new Vector3(0.63f, 0.42f, 0.61f), _tint));
            T(P(beanie, PrimitiveType.Cylinder, "Cuff", new Vector3(0, 1.62f, 0.01f), new Vector3(0.63f, 0.05f, 0.61f), _tint));
            T(P(beanie, PrimitiveType.Sphere, "Pompom", new Vector3(0, 1.92f, 0f), Vector3.one * 0.15f, _tint));

            var goggles = Group(hats, "Hat_GogglesBun");
            T(P(goggles, PrimitiveType.Sphere, "Hair", new Vector3(0, 1.56f, -0.02f), new Vector3(0.63f, 0.48f, 0.6f), _tint));
            T(P(goggles, PrimitiveType.Sphere, "Bun", new Vector3(0, 1.84f, -0.06f), new Vector3(0.26f, 0.22f, 0.26f), _tint));
            P(goggles, PrimitiveType.Cylinder, "Strap", new Vector3(0, 1.6f, 0.0f), new Vector3(0.64f, 0.025f, 0.61f), _ink);
            P(goggles, PrimitiveType.Cylinder, "Lens_L", new Vector3(-0.1f, 1.6f, 0.31f), new Vector3(0.13f, 0.03f, 0.13f), _glass).localRotation = Quaternion.Euler(70, 0, 0);
            P(goggles, PrimitiveType.Cylinder, "Lens_R", new Vector3(0.1f, 1.6f, 0.31f), new Vector3(0.13f, 0.03f, 0.13f), _glass).localRotation = Quaternion.Euler(70, 0, 0);

            var slot = root.GetComponent<PlayerSlot>();
            var so = new SerializedObject(slot);
            var tr = so.FindProperty("tintedRenderers");
            tr.arraySize = tinted.Count;
            for (int i = 0; i < tinted.Count; i++) tr.GetArrayElementAtIndex(i).objectReferenceValue = tinted[i];
            var hv = so.FindProperty("hatVariants");
            hv.arraySize = hats.childCount;
            for (int i = 0; i < hats.childCount; i++)
            {
                hv.GetArrayElementAtIndex(i).objectReferenceValue = hats.GetChild(i).gameObject;
                hats.GetChild(i).gameObject.SetActive(i == 0);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            int variants = hats.childCount;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            Debug.Log($"[KeyArt] Bodger rebuilt: {tinted.Count} tinted parts, {variants} hat variants.");
        }

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // ------------------------------------------------------------------ lighting
        static void Lighting()
        {
            var sun = RenderSettings.sun;
            if (sun != null) { sun.color = Palette.KeyLight; sun.intensity = 1.35f; }
            RenderSettings.ambientSkyColor = Palette.AmbientSky;
            RenderSettings.ambientEquatorColor = Palette.AmbientEquator;
            RenderSettings.ambientGroundColor = Palette.AmbientGround;
        }

        // ------------------------------------------------------------------ room dressing
        static void Room()
        {
            var room = GameObject.Find("LookDevRoom").transform;
            foreach (var n in new[] { "Floor" }) Recolor(room.Find(n), _floor);
            foreach (var n in new[] { "Wall_North", "Wall_East", "Wall_South", "Wall_West" }) Recolor(room.Find(n), _walls);
            foreach (var n in new[] { "Baseboard_North", "Baseboard_East", "Baseboard_South", "Baseboard_West", "WindowFrame" }) Recolor(room.Find(n), _trim);
            Recolor(room.Find("Crate"), _wood);
            Recolor(room.Find("Plank"), _wood);

            var old = GameObject.Find("Decor");
            if (old != null) Object.DestroyImmediate(old);
            var decor = new GameObject("Decor").transform;

            // floor tiles: a darker grid so the floor reads as worn tiles, not a flat plane
            var grout = Mat("M_FloorGrout", ((UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline).defaultMaterial,
                            Color.Lerp(Palette.Floor, Palette.Trim, 0.45f), 0.1f);
            var tiles = Group(decor, "FloorGrout");
            for (float v = -3.5f; v <= 3.51f; v += 1f)
            {
                Flat(tiles, "GroutX", new Vector3(0, 0.002f, v), Quaternion.Euler(90, 0, 0), new Vector2(7.85f, 0.025f), grout);
                Flat(tiles, "GroutZ", new Vector3(v, 0.002f, 0), Quaternion.Euler(90, 0, 0), new Vector2(0.025f, 7.85f), grout);
            }

            // cornice along the top of every wall
            var cornice = Group(decor, "Cornice");
            BoxD(cornice, "N", new Vector3(0, 2.74f, 3.89f), new Vector3(8f, 0.12f, 0.08f), _trim);
            BoxD(cornice, "S", new Vector3(0, 2.74f, -3.89f), new Vector3(8f, 0.12f, 0.08f), _trim);
            BoxD(cornice, "E", new Vector3(3.89f, 2.74f, 0), new Vector3(0.08f, 0.12f, 8f), _trim);
            BoxD(cornice, "W", new Vector3(-3.89f, 2.74f, 0), new Vector3(0.08f, 0.12f, 8f), _trim);

            // vertical wallpaper stripes high on the walls (above every defect, below the cornice)
            var stripes = Group(decor, "WallpaperStripes");
            for (float a = -3.6f; a <= 3.61f; a += 0.6f)
            {
                WallFlat(stripes, Vector3.forward, new Vector3(a, 2.45f, 0), new Vector2(0.12f, 0.5f), 0, _stripe, 0.003f);
                WallFlat(stripes, Vector3.back, new Vector3(a, 2.45f, 0), new Vector2(0.12f, 0.5f), 0, _stripe, 0.003f);
                WallFlat(stripes, Vector3.right, new Vector3(0, 2.45f, a), new Vector2(0.12f, 0.5f), 0, _stripe, 0.003f);
                WallFlat(stripes, Vector3.left, new Vector3(0, 2.45f, a), new Vector2(0.12f, 0.5f), 0, _stripe, 0.003f);
            }
            BoxD(stripes, "PictureRail_N", new Vector3(0, 2.18f, 3.9f), new Vector3(8f, 0.05f, 0.05f), _trim);
            BoxD(stripes, "PictureRail_S", new Vector3(0, 2.18f, -3.9f), new Vector3(8f, 0.05f, 0.05f), _trim);
            BoxD(stripes, "PictureRail_E", new Vector3(3.9f, 2.18f, 0), new Vector3(0.05f, 0.05f, 8f), _trim);
            BoxD(stripes, "PictureRail_W", new Vector3(-3.9f, 2.18f, 0), new Vector3(0.05f, 0.05f, 8f), _trim);

            // peeling wallpaper: torn plaster patches, kept clear of defects, window, door and shop
            var peel = Group(decor, "PeelingPatches");
            Peel(peel, Vector3.forward, new Vector3(0.7f, 1.75f, 0), 0.9f, 12f);
            Peel(peel, Vector3.forward, new Vector3(3.3f, 0.55f, 0), 0.6f, -20f);
            Peel(peel, Vector3.right, new Vector3(0, 1.4f, 2.6f), 0.8f, 30f);
            Peel(peel, Vector3.right, new Vector3(0, 0.5f, -1.0f), 0.55f, -8f);
            Peel(peel, Vector3.back, new Vector3(0.1f, 1.7f, 0), 0.75f, 18f);
            Peel(peel, Vector3.back, new Vector3(3.1f, 1.3f, 0), 0.6f, -25f);
            Peel(peel, Vector3.left, new Vector3(0, 1.85f, -0.8f), 0.6f, 5f);
            Peel(peel, Vector3.left, new Vector3(0, 0.6f, 3.0f), 0.65f, -15f);

            // sofa: heavy, carryable, hides what's behind it (concealer). Duct-tape X on the cushion.
            var sofa = new GameObject("Sofa").transform; sofa.SetParent(decor, false);
            sofa.SetPositionAndRotation(new Vector3(1.2f, 0, 2.3f), Quaternion.Euler(0, 180, 0)); // faces south, back to the north wall
            Pc(sofa, "Base", new Vector3(0, 0.27f, 0), new Vector3(1.8f, 0.34f, 0.84f), _sofa);
            Pc(sofa, "Back", new Vector3(0, 0.66f, -0.31f), new Vector3(1.8f, 0.5f, 0.24f), _sofa);
            Pc(sofa, "Arm_L", new Vector3(-0.8f, 0.5f, 0.02f), new Vector3(0.24f, 0.36f, 0.82f), _sofa);
            Pc(sofa, "Arm_R", new Vector3(0.8f, 0.5f, 0.02f), new Vector3(0.24f, 0.36f, 0.82f), _sofa);
            var cushionCol = Color.Lerp(Palette.Sofa, Palette.Cream, 0.12f);
            var mCushion = Mat("M_SofaCushion", _sofa, cushionCol, 0.05f);
            P(sofa, PrimitiveType.Cube, "Cushion_L", new Vector3(-0.34f, 0.49f, 0.06f), new Vector3(0.66f, 0.12f, 0.62f), mCushion);
            P(sofa, PrimitiveType.Cube, "Cushion_R", new Vector3(0.34f, 0.49f, 0.06f), new Vector3(0.66f, 0.12f, 0.62f), mCushion);
            foreach (var x in new[] { -0.8f, 0.8f }) foreach (var z in new[] { -0.36f, 0.36f })
                P(sofa, PrimitiveType.Cube, "Foot", new Vector3(x, 0.05f, z), new Vector3(0.08f, 0.1f, 0.08f), _trim);
            Flat(sofa, "TapeA", new Vector3(0.34f, 0.556f, 0.06f), Quaternion.Euler(90, 40, 0), new Vector2(0.55f, 0.08f), _tape, local: true);
            Flat(sofa, "TapeB", new Vector3(0.34f, 0.557f, 0.06f), Quaternion.Euler(90, -40, 0), new Vector2(0.55f, 0.08f), _tape, local: true);
            Flat(sofa, "TapeArm", new Vector3(-0.8f, 0.682f, 0.1f), Quaternion.Euler(90, 15, 0), new Vector2(0.2f, 0.07f), _tape, local: true);
            MakeCarryable(sofa.gameObject, 40f, 3f, concealer: true);

            // pink rug under the middle of the room (no collider: nothing to trip on)
            var rug = Flat(decor, "Rug", new Vector3(0.3f, 0.004f, -0.7f), Quaternion.Euler(90, 0, 0), new Vector2(2.6f, 1.7f), _rug);
            var rugBorder = Mat("M_RugBorder", _rug, Color.Lerp(Palette.Rug, Palette.TintYellow, 0.45f), 0f);
            Flat(rug, "Border", new Vector3(0, 0, -0.001f), Quaternion.identity, new Vector2(0.9f, 0.85f), rugBorder, local: true);
            Flat(rug, "Inner", new Vector3(0, 0, -0.002f), Quaternion.identity, new Vector2(0.86f, 0.78f), _rug, local: true);

            // floor lamp: warm pool of light by the sofa
            var lamp = new GameObject("FloorLamp").transform; lamp.SetParent(decor, false);
            lamp.position = new Vector3(-0.15f, 0, 2.75f);
            P(lamp, PrimitiveType.Cylinder, "Foot", new Vector3(0, 0.02f, 0), new Vector3(0.32f, 0.02f, 0.32f), _trim);
            P(lamp, PrimitiveType.Cylinder, "Pole", new Vector3(0, 0.75f, 0), new Vector3(0.04f, 0.75f, 0.04f), _trim);
            P(lamp, PrimitiveType.Cylinder, "Shade", new Vector3(0, 1.58f, 0), new Vector3(0.46f, 0.16f, 0.46f), _lampShade);
            var lightGo = new GameObject("LampLight"); lightGo.transform.SetParent(lamp, false);
            lightGo.transform.localPosition = new Vector3(0, 1.4f, 0);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point; l.color = Palette.LampLight; l.range = 3.5f; l.intensity = 1.6f; l.shadows = LightShadows.None;
            var lampCol = lamp.gameObject.AddComponent<CapsuleCollider>(); lampCol.center = new Vector3(0, 0.85f, 0); lampCol.height = 1.7f; lampCol.radius = 0.16f;

            // houseplant: light, carryable, hides low damage (concealer) — the key-art hiding spot
            var plant = new GameObject("Houseplant").transform; plant.SetParent(decor, false);
            plant.position = new Vector3(-3.25f, 0, -1.3f);
            Pc(plant, "Pot", new Vector3(0, 0.19f, 0), new Vector3(0.38f, 0.19f, 0.38f), _pot, PrimitiveType.Cylinder);
            P(plant, PrimitiveType.Cylinder, "Rim", new Vector3(0, 0.38f, 0), new Vector3(0.42f, 0.025f, 0.42f), _pot);
            for (int i = 0; i < 7; i++)
            {
                float yaw = i * 360f / 7f + (i % 2) * 20f;
                var leaf = P(plant, PrimitiveType.Sphere, $"Leaf{i}", Vector3.zero, new Vector3(0.16f, 0.5f, 0.06f), _leaf);
                leaf.localRotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(30f + (i % 3) * 8f, 0, 0);
                leaf.localPosition = new Vector3(0, 0.58f + (i % 3) * 0.06f, 0) + leaf.localRotation * new Vector3(0, 0.12f, 0);
            }
            var foliage = plant.gameObject.AddComponent<BoxCollider>(); foliage.center = new Vector3(0, 0.75f, 0); foliage.size = new Vector3(0.6f, 0.55f, 0.6f);
            MakeCarryable(plant.gameObject, 6f, 1.4f, concealer: true);

            // clutter: mug + pizza box (light carryables)
            var mug = new GameObject("Mug").transform; mug.SetParent(decor, false); mug.position = new Vector3(2.0f, 0.06f, 1.4f);
            Pc(mug, "Cup", Vector3.zero, new Vector3(0.1f, 0.06f, 0.1f), _cream, PrimitiveType.Cylinder);
            P(mug, PrimitiveType.Cube, "Handle", new Vector3(0.065f, 0, 0), new Vector3(0.03f, 0.07f, 0.02f), _cream);
            MakeCarryable(mug.gameObject, 0.3f, 1f, concealer: false);
            var pizza = new GameObject("PizzaBox").transform; pizza.SetParent(decor, false);
            pizza.SetPositionAndRotation(new Vector3(-0.9f, 0.03f, -2.0f), Quaternion.Euler(0, 25, 0));
            Pc(pizza, "Box", Vector3.zero, new Vector3(0.42f, 0.05f, 0.42f), Mat("M_Cardboard", _wood, Color.Lerp(Palette.Plaster, Palette.Wood, 0.35f), 0f));
            P(pizza, PrimitiveType.Cube, "Logo", new Vector3(0, 0.026f, 0), new Vector3(0.18f, 0.002f, 0.18f), Mat("M_Accent", _wood, Palette.Accent, 0.3f));
            MakeCarryable(pizza.gameObject, 0.5f, 1f, concealer: false);
            // spare duct tape roll on the floor
            var roll = new GameObject("TapeRoll").transform; roll.SetParent(decor, false); roll.position = new Vector3(0.9f, 0.04f, -2.6f);
            Pc(roll, "Roll", Vector3.zero, new Vector3(0.16f, 0.04f, 0.16f), _tape, PrimitiveType.Cylinder);
            MakeCarryable(roll.gameObject, 0.3f, 1f, concealer: false);

            Debug.Log("[KeyArt] Room dressed: teal wallpaper + stripes, peeling patches, cornice, tiles, sofa, rug, lamp, plant, clutter.");
        }

        static void Peel(Transform parent, Vector3 wallNormal, Vector3 along, float size, float roll)
        {
            // an irregular torn blob: overlapping rotated plaster scraps with a few darker torn edges peeking out
            var rnd = new System.Random((int)(along.x * 100 + along.y * 37 + along.z * 13));
            float R() => (float)rnd.NextDouble() * 2f - 1f;
            for (int i = 0; i < 4; i++)
            {
                var o = new Vector3(R(), R() * 0.6f, R()) * size * 0.28f;
                WallFlat(parent, wallNormal, along + o + new Vector3(0, -0.02f, 0), new Vector2(size * (0.42f + 0.1f * R()), size * (0.3f + 0.08f * R())), roll + R() * 40f, _plasterEdge, 0.004f);
            }
            for (int i = 0; i < 5; i++)
            {
                var o = new Vector3(R(), R() * 0.6f, R()) * size * 0.25f;
                WallFlat(parent, wallNormal, along + o, new Vector2(size * (0.4f + 0.1f * R()), size * (0.28f + 0.08f * R())), roll + R() * 45f, _plaster, 0.006f);
            }
        }

        // A quad lying on the inner face of a wall. wallNormal points into the room.
        static void WallFlat(Transform parent, Vector3 wallNormal, Vector3 along, Vector2 size, float roll, Material m, float offset)
        {
            const float inner = 3.925f;
            Vector3 pos = along;
            if (wallNormal == Vector3.back) pos.z = inner - offset;            // north wall faces -z
            else if (wallNormal == Vector3.forward) { pos = new Vector3(along.x, along.y, -inner + offset); } // south wall
            else if (wallNormal == Vector3.left) pos.x = inner - offset;       // east wall faces -x
            else pos.x = -inner + offset;                                       // west wall
            // Unity quads are visible from their -Z side, so look *into* the wall
            var rot = Quaternion.LookRotation(-wallNormal) * Quaternion.Euler(0, 0, roll);
            Flat(parent, "Patch", pos, rot, size, m);
        }

        // ------------------------------------------------------------------ captures
        static void Captures()
        {
            var cam = Camera.main;
            Shot(cam, new Vector3(-3.3f, 2.4f, -3.3f), Quaternion.Euler(22, 45, 0), "Docs/keyart_overview.png");
            Shot(cam, new Vector3(-1.2f, 1.6f, 1.2f), Quaternion.Euler(8, -15, 0), "Docs/keyart_supplies.png");

            // line-up: the four bodgers + inspector facing the camera (not saved)
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var spawned = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < 4; i++)
            {
                var p = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                p.transform.SetPositionAndRotation(new Vector3(-1.35f + i * 0.9f, 0, -1.2f), Quaternion.Euler(0, 180 + (i - 1.5f) * 10f, 0));
                var hats = p.transform.Find("Visual/Hats");
                for (int h = 0; h < hats.childCount; h++) hats.GetChild(h).gameObject.SetActive(h == i);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", Palette.PlayerTints[i]);
                foreach (var r in hats.GetChild(i).GetComponentsInChildren<Renderer>())
                    if (r.sharedMaterial == _tint) r.SetPropertyBlock(block);
                foreach (var c in p.GetComponentsInChildren<Camera>()) c.enabled = false;
                spawned.Add(p);
            }
            var insp = Object.FindAnyObjectByType<BodgeSquad.Job.Inspector>(FindObjectsInactive.Include);
            bool wasActive = insp.gameObject.activeSelf;
            var inspPos = insp.transform.position; var inspRot = insp.transform.rotation;
            insp.gameObject.SetActive(true);
            insp.transform.SetPositionAndRotation(new Vector3(2.4f, 0, -0.6f), Quaternion.Euler(0, 215, 0));
            Shot(cam, new Vector3(0.4f, 1.35f, -4.0f + 0.25f), Quaternion.Euler(4, 8, 0), "Docs/keyart_lineup.png");
            Shot(cam, new Vector3(2.0f, 1.55f, -2.0f), Quaternion.Euler(4, 20, 0), "Docs/keyart_inspector.png");
            insp.transform.SetPositionAndRotation(inspPos, inspRot);
            insp.gameObject.SetActive(wasActive);
            foreach (var p in spawned) Object.DestroyImmediate(p);
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
            Debug.Log($"[KeyArt] Captured {file}");
        }

        // ------------------------------------------------------------------ helpers
        static void MakeCarryable(GameObject go, float mass, float weight, bool concealer)
        {
            var rb = go.AddComponent<Rigidbody>(); rb.mass = mass; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var c = go.AddComponent<Carryable>();
            var so = new SerializedObject(c); so.FindProperty("carryWeight").floatValue = weight; so.ApplyModifiedPropertiesWithoutUndo();
            if (concealer) go.AddComponent<Concealer>();
        }

        static void Recolor(Transform t, Material m)
        {
            if (t != null && t.TryGetComponent<Renderer>(out var r)) r.sharedMaterial = m;
        }

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

        // Visual-only primitive (no collider)
        static Transform P(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 scale, Material m)
        {
            var t = Pc(parent, name, localPos, scale, m, type);
            Object.DestroyImmediate(t.GetComponent<Collider>());
            return t;
        }

        // Primitive that keeps its collider
        static Transform Pc(Transform parent, string name, Vector3 localPos, Vector3 scale, Material m, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        static void BoxD(Transform parent, string name, Vector3 worldPos, Vector3 scale, Material m)
        {
            var t = P(parent, PrimitiveType.Cube, name, Vector3.zero, scale, m);
            t.position = worldPos;
        }

        static Transform Flat(Transform parent, string name, Vector3 pos, Quaternion rot, Vector2 size, Material m, bool local = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            if (local) { go.transform.localPosition = pos; go.transform.localRotation = rot; }
            else go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
