using System.IO;
using BodgeSquad.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectBootstrap
{
    // One-shot visual baseline for SampleScene (craft-game-floors: art-direction + scene-lighting).
    // Run with: Unity -batchmode -projectPath <path> -executeMethod ProjectBootstrap.BaselineSetup.Run -quit
    public static class BaselineSetup
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string MatDir = "Assets/Art/Materials";
        const string ProfilePath = "Assets/Settings/BodgeSquad_GlobalVolume.asset";

        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // --- Render pipeline / quality ---------------------------------------------------
            var names = QualitySettings.names;
            Debug.Log($"[Baseline] Quality tiers: {string.Join(", ", names)}");
            QualitySettings.SetQualityLevel(names.Length - 1, true); // desktop → highest tier
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) { Fail("Current render pipeline is not URP."); return; }
            urp.supportsHDR = true;      // bloom thresholds need HDR
            urp.shadowDistance = 30f;    // sized to one house, not the 50 m default
            EditorUtility.SetDirty(urp);
            Debug.Log($"[Baseline] Pipeline asset: {urp.name}");

            // --- Materials from the palette (URP Lit via the pipeline's default material) -------
            Directory.CreateDirectory(MatDir);
            var baseMat = urp.defaultMaterial;
            if (baseMat == null) { Fail("URP default material missing."); return; }
            var mFloor = Mat("M_Floor", baseMat, Palette.Floor, 0.15f);
            var mWalls = Mat("M_Walls", baseMat, Palette.Walls, 0.05f);
            var mTrim = Mat("M_Trim", baseMat, Palette.Shadow, 0.2f);
            var mProps = Mat("M_Props", baseMat, Palette.Props, 0.1f);
            var mCharA = Mat("M_CharacterPrimary", baseMat, Palette.CharacterPrimary, 0.2f);
            var mCharB = Mat("M_CharacterSecondary", baseMat, Palette.CharacterSecondary, 0.2f);
            var mAccent = Mat("M_Accent", baseMat, Palette.Accent, 0.3f);
            mAccent.EnableKeyword("_EMISSION");
            mAccent.SetColor("_EmissionColor", Palette.Accent * 1.8f); // HDR glow → bloom
            mAccent.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            // --- Key light --------------------------------------------------------------------
            var sun = Object.FindFirstObjectByType<Light>();
            if (sun == null) sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.KeyLight;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            // --- Ambient + sky ------------------------------------------------------------------
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.AmbientSky;
            RenderSettings.ambientEquatorColor = Palette.AmbientEquator;
            RenderSettings.ambientGroundColor = Palette.AmbientGround;
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            if (sky.shader == null || sky.shader.name != "Skybox/Procedural") { Fail("Skybox/Procedural shader not found."); return; }
            sky.SetColor("_SkyTint", Palette.AmbientSky);
            sky.SetColor("_GroundColor", Palette.Shadow);
            sky.SetFloat("_AtmosphereThickness", 0.8f);
            sky.SetFloat("_Exposure", 1.1f);
            sky.SetFloat("_SunSize", 0.03f);
            Save(sky, "M_Sky_Palette");
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(Palette.Walls, Palette.AmbientSky, 0.5f);
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = 45f;

            // --- Global Volume ------------------------------------------------------------------
            AssetDatabase.DeleteAsset(ProfilePath); // rebuild from scratch each run
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            var tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = Get<Bloom>(profile);
            bloom.intensity.Override(0.35f);
            bloom.threshold.Override(1.1f);
            var vig = Get<Vignette>(profile);
            vig.intensity.Override(0.22f);
            var ca = Get<ColorAdjustments>(profile);
            ca.saturation.Override(8f);
            ca.contrast.Override(5f);
            var wb = Get<WhiteBalance>(profile);
            wb.temperature.Override(8f);
            EditorUtility.SetDirty(profile);

            var volume = Object.FindFirstObjectByType<Volume>();
            if (volume == null) volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1;
            volume.sharedProfile = profile;

            // --- Camera (first-person eye height; split-screen rig comes with gameplay) --------
            var cam = Camera.main;
            if (cam == null) { Fail("No Main Camera in scene."); return; }
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.transform.position = new Vector3(-2.6f, 1.6f, -2.6f);
            cam.transform.rotation = Quaternion.Euler(10f, 40f, 0f);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // --- Look-dev room (temporary, replaced by the real house) ------------------------
            var old = GameObject.Find("LookDevRoom");
            if (old != null) Object.DestroyImmediate(old);
            var room = new GameObject("LookDevRoom").transform;
            Box(room, "Floor", new Vector3(0, -0.05f, 0), new Vector3(8, 0.1f, 8), 0, mFloor);
            Box(room, "Wall_North", new Vector3(0, 1.4f, 4), new Vector3(8, 2.8f, 0.15f), 0, mWalls);
            Box(room, "Wall_East", new Vector3(4, 1.4f, 0), new Vector3(0.15f, 2.8f, 8), 0, mWalls);
            Box(room, "Baseboard_North", new Vector3(0, 0.06f, 3.9f), new Vector3(8, 0.12f, 0.06f), 0, mTrim);
            Box(room, "Baseboard_East", new Vector3(3.9f, 0.06f, 0), new Vector3(0.06f, 0.12f, 8), 0, mTrim);
            // Window frame (light-story element) on the north wall
            Box(room, "WindowFrame", new Vector3(-1.5f, 1.6f, 3.9f), new Vector3(1.4f, 1.1f, 0.08f), 0, mTrim);
            // Crooked props: paint tins, a crate, a ladder-ish plank
            Box(room, "Crate", new Vector3(2.6f, 0.35f, 2.4f), new Vector3(0.8f, 0.7f, 0.8f), 7f, mProps);
            Box(room, "Plank", new Vector3(1.4f, 0.9f, 3.6f), new Vector3(0.25f, 1.8f, 0.06f), -9f, mFloor, 12f);
            Cyl(room, "PaintTin_A", new Vector3(1.9f, 0.15f, 1.7f), new Vector3(0.32f, 0.15f, 0.32f), mCharB);
            Cyl(room, "PaintTin_B", new Vector3(2.25f, 0.15f, 1.5f), new Vector3(0.32f, 0.15f, 0.32f), mProps);
            // Inspector's red flag on the wall — the accent
            Box(room, "InspectorFlag", new Vector3(0.6f, 1.3f, 3.88f), new Vector3(0.3f, 0.22f, 0.02f), 4f, mAccent);
            // Bodger stand-in (two-tone primitive, slightly tilted)
            var bodger = new GameObject("Bodger_Placeholder").transform;
            bodger.SetParent(room, false);
            bodger.localPosition = new Vector3(0.9f, 0, 1.6f);
            bodger.localRotation = Quaternion.Euler(0, -150f, 3f);
            Prim(bodger, PrimitiveType.Capsule, "Body", new Vector3(0, 0.85f, 0), new Vector3(0.55f, 0.6f, 0.45f), mCharB);
            Prim(bodger, PrimitiveType.Cube, "HiVis", new Vector3(0, 1.05f, 0), new Vector3(0.58f, 0.45f, 0.48f), mCharA);
            Prim(bodger, PrimitiveType.Sphere, "Head", new Vector3(0, 1.62f, 0.03f), new Vector3(0.36f, 0.34f, 0.36f), mWalls);
            Prim(bodger, PrimitiveType.Cylinder, "HardHat", new Vector3(0, 1.8f, 0.03f), new Vector3(0.42f, 0.06f, 0.42f), mCharA);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            // --- Verify: no default material, palette sky, post on; then capture ---------------
            int defaults = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.sharedMaterial == null || r.sharedMaterial.name == "Default-Material")
                { defaults++; Debug.LogWarning($"[Baseline] Default material on {r.name}"); }
            Debug.Log($"[Baseline] Default-material renderers: {defaults}; skybox: {RenderSettings.skybox.name}; postFX: {camData.renderPostProcessing}; HDR: {urp.supportsHDR}");

            Capture(cam, "baseline.png", 1600, 900);
            volume.enabled = false;
            Capture(cam, "baseline_nopost.png", 1600, 900);
            volume.enabled = true;

            Debug.Log("[Baseline] Done.");
            EditorApplication.Exit(0);
        }

        static void Capture(Camera cam, string file, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            for (int i = 0; i < 4; i++) cam.Render(); // warm-up: the color-grading LUT builds on the first frames
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = prev;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Debug.Log($"[Baseline] Captured {file}");
        }

        static T Get<T>(VolumeProfile p) where T : VolumeComponent
        {
            if (!p.TryGet<T>(out var c))
            {
                c = p.Add<T>(false); // override only the parameters set explicitly
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            c.active = true;
            return c;
        }

        static Material Mat(string name, Material src, Color color, float smooth)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Save(Material m, string name)
        {
            var path = $"{MatDir}/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
        }

        static void Box(Transform parent, string name, Vector3 pos, Vector3 scale, float yaw, Material m, float roll = 0f)
        {
            var t = Prim(parent, PrimitiveType.Cube, name, pos, scale, m);
            t.localRotation = Quaternion.Euler(0, yaw, roll);
        }

        static void Cyl(Transform parent, string name, Vector3 pos, Vector3 scale, Material m) =>
            Prim(parent, PrimitiveType.Cylinder, name, pos, scale, m);

        static Transform Prim(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go.transform;
        }

        static void Fail(string msg)
        {
            Debug.LogError($"[Baseline] {msg}");
            EditorApplication.Exit(1);
        }
    }
}
