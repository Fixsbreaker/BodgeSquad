using System;
using System.Collections.Generic;
using BodgeSquad.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Job
{
    // Every dab of paint, scrap of wallpaper, strip of tape or smear of filler anyone puts
    // anywhere: walls, floor, the sofa, the inspector's clipboard if you're quick. The host keeps
    // the list; every machine draws it. Marks on furniture stick to the furniture.
    public class SurfaceMarks : NetworkBehaviour
    {
        public struct Mark : INetworkSerializeByMemcpy, IEquatable<Mark>
        {
            public Vector3 Pos;      // world, or local to Parent
            public Vector3 Normal;   // same space as Pos
            public ulong Parent;     // NetworkObjectId, or ulong.MaxValue for the static house
            public float Size;
            public float Roll;
            public byte Kit;         // KitType
            public float WetUntil;   // fresh paint on the floor: slippery until this server time (0 = dry)
            public bool Equals(Mark o) => Pos == o.Pos && Normal == o.Normal && Parent == o.Parent && Kit == o.Kit && Roll == o.Roll;
        }

        public const ulong World = ulong.MaxValue;
        const int MaxMarks = 1500;

        [SerializeField] Material[] kitMaterials = new Material[6]; // by KitType

        public static SurfaceMarks Instance { get; private set; }

        NetworkList<Mark> _marks;
        readonly List<GameObject> _drawn = new();
        Transform _root;
        int _layerBump;

        void Awake()
        {
            Instance = this;
            _marks = new NetworkList<Mark>();
            if (kitMaterials == null || kitMaterials.Length < 6 || kitMaterials[0] == null) kitMaterials = BuildMaterials();
        }

        // One material per kit: soft round dabs, close to the wall colour so a tidy job looks tidy
        static Material[] BuildMaterials()
        {
            var src = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            var round = RoundTexture();
            Material M(Color c, float smooth, bool dab = true)
            {
                var m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                if (dab)
                {
                    m.SetTexture("_BaseMap", round);
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", 0.5f);
                    m.EnableKeyword("_ALPHATEST_ON");
                }
                return m;
            }
            var wall = BodgeSquad.Art.Palette.Walls;
            var mats = new Material[6];
            mats[(int)KitType.Filler] = M(Color.Lerp(wall, Color.white, 0.10f), 0.05f);                       // filled, a touch lighter
            mats[(int)KitType.AntiMould] = M(Color.Lerp(wall, BodgeSquad.Art.Palette.AmbientSky, 0.18f), 0.6f); // damp sheen
            mats[(int)KitType.WiringKit] = M(BodgeSquad.Art.Palette.Cream, 0.4f, dab: false);                // square cover plate
            mats[(int)KitType.PaintTin] = M(Color.Lerp(wall, Color.white, 0.06f), 0.3f);                     // "close enough" teal
            mats[(int)KitType.WallpaperRoll] = M(Color.Lerp(wall, BodgeSquad.Art.Palette.WallsDark, 0.25f), 0.05f, dab: false); // a strip of paper
            mats[(int)KitType.DuctTape] = M(BodgeSquad.Art.Palette.Tape, 0.55f, dab: false);
            return mats;
        }

        public Material MaterialFor(KitType k) => kitMaterials[(int)k];

        static Texture2D RoundTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "RoundDab" };
            var rnd = new System.Random(3);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float ang = Mathf.Atan2(dy, dx);
                    float edge = 0.46f + 0.03f * Mathf.Sin(ang * 5f) + 0.015f * Mathf.Sin(ang * 13f); // a slightly wobbly brush edge
                    float a = Mathf.Sqrt(dx * dx + dy * dy) < edge ? 1f : 0f;
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return t;
        }

        public override void OnNetworkSpawn()
        {
            _root = new GameObject("SurfaceMarks").transform;
            _marks.OnListChanged += OnChanged;
            for (int i = 0; i < _marks.Count; i++) _drawn.Add(Draw(_marks[i]));
        }

        public override void OnNetworkDespawn()
        {
            _marks.OnListChanged -= OnChanged;
            ClearDrawn();
            if (_root != null) Destroy(_root.gameObject);
        }

        // Anyone: put a mark down (sent to the host, then drawn everywhere)
        float _nextFacePaint;
        public const float WetSeconds = 30f;

        public void Place(KitType kit, RaycastHit hit, float size, float roll, bool wet = true)
        {
            var m = new Mark { Size = size, Roll = roll, Kit = (byte)kit, Parent = World, Pos = hit.point, Normal = hit.normal };
            // paint brushed onto the floor stays wet (and slippery) for a while; footprints don't count
            bool floor = hit.normal.y > 0.7f && hit.rigidbody == null; // the house's own floor (and rug), not furniture
            if (wet && kit == KitType.PaintTin && floor) m.WetUntil = 1f;
            if (hit.rigidbody != null && hit.rigidbody.GetComponent<RepairKit>() != null) return; // not on tins and rolls
            // never stuck on the inspector (he walks off and they'd hang in the air); paint in his face blinds him instead
            var insp = hit.collider != null ? hit.collider.GetComponentInParent<Inspector>() : null;
            if (insp != null)
            {
                if (kit == KitType.PaintTin && insp.transform.InverseTransformPoint(hit.point).y > 1.25f && Time.time >= _nextFacePaint && insp.IsSpawned)
                {
                    _nextFacePaint = Time.time + 1.2f;
                    insp.HitRpc((int)KitType.PaintTin);
                }
                return;
            }
            var no = hit.rigidbody != null ? hit.rigidbody.GetComponent<NetworkObject>() : null;
            if (no != null && no.IsSpawned)
            {
                m.Parent = no.NetworkObjectId;
                m.Pos = no.transform.InverseTransformPoint(hit.point);
                m.Normal = no.transform.InverseTransformDirection(hit.normal);
            }
            AddRpc(m);
        }

        [Rpc(SendTo.Server)]
        void AddRpc(Mark m, RpcParams p = default)
        {
            var world = m.Pos;
            if (m.Parent != World && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(m.Parent, out var po)) world = po.transform.TransformPoint(m.Pos);
            if (m.WetUntil > 0f) m.WetUntil = m.Parent == World ? NetworkManager.ServerTime.TimeAsFloat + WetSeconds : 0f;
            Inspector.Instance?.ReportWork(p.Receive.SenderClientId, (KitType)m.Kit, world);
            if (_marks.Count >= MaxMarks) _marks.RemoveAt(0);
            _marks.Add(m);
        }

        // Host: new job
        public void ClearAll() { if (IsServer) _marks.Clear(); }

        void OnChanged(NetworkListEvent<Mark> e)
        {
            switch (e.Type)
            {
                case NetworkListEvent<Mark>.EventType.Add:
                    _drawn.Add(Draw(e.Value));
                    break;
                case NetworkListEvent<Mark>.EventType.RemoveAt:
                    if (e.Index < _drawn.Count) { if (_drawn[e.Index] != null) Destroy(_drawn[e.Index]); _drawn.RemoveAt(e.Index); }
                    break;
                case NetworkListEvent<Mark>.EventType.Clear:
                    ClearDrawn();
                    _layerBump = 0;
                    break;
                default: // anything else: redraw from scratch
                    ClearDrawn();
                    for (int i = 0; i < _marks.Count; i++) _drawn.Add(Draw(_marks[i]));
                    break;
            }
        }

        // An unscaled child that follows the object: marks under it never stretch or shear,
        // even on a box scaled 1 x 1.8 x 0.5 like the wardrobe.
        static Transform Anchor(Transform target)
        {
            var a = target.Find("__marks");
            if (a == null)
            {
                a = new GameObject("__marks").transform;
                a.SetParent(target, false);
            }
            var s = target.lossyScale;
            a.localPosition = Vector3.zero;
            a.localRotation = Quaternion.identity;
            a.localScale = new Vector3(1f / Mathf.Max(s.x, 0.001f), 1f / Mathf.Max(s.y, 0.001f), 1f / Mathf.Max(s.z, 0.001f));
            return a;
        }

        // Fresh floor paint under these feet? Two wet dabs within reach (a single fleck won't floor you).
        public bool IsWetAt(Vector3 feet)
        {
            if (_marks == null || !IsSpawned) return false;
            float now = NetworkManager.ServerTime.TimeAsFloat;
            int hits = 0;
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                var m = _marks[i];
                if (m.WetUntil <= now || m.Parent != World) continue;
                if (Mathf.Abs(feet.y - m.Pos.y) > 0.35f) continue;
                float dx = feet.x - m.Pos.x, dz = feet.z - m.Pos.z, r = m.Size * 0.55f + 0.12f;
                if (dx * dx + dz * dz < r * r && ++hits >= 2) return true;
            }
            return false;
        }

        // Wet paint is glossy; it dries to the normal matte colour
        Material _wetPaint;
        readonly List<(Renderer r, float until)> _wet = new();

        void Update()
        {
            if (_wet.Count == 0 || !IsSpawned) return;
            float now = NetworkManager.ServerTime.TimeAsFloat;
            for (int i = _wet.Count - 1; i >= 0; i--)
            {
                var (r, until) = _wet[i];
                if (r == null) { _wet.RemoveAt(i); continue; }
                if (now >= until) { r.sharedMaterial = kitMaterials[(int)KitType.PaintTin]; _wet.RemoveAt(i); }
            }
        }

        void ClearDrawn()
        {
            _wet.Clear();
            foreach (var g in _drawn) if (g != null) Destroy(g);
            _drawn.Clear();
        }

        GameObject Draw(Mark m)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Mark";
            Transform parent = _root;
            Vector3 pos = m.Pos;
            if (m.Parent != World && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(m.Parent, out var no))
            {
                parent = Anchor(no.transform);
                pos = Vector3.Scale(m.Pos, no.transform.lossyScale); // stored in the object's scaled space
            }
            q.transform.SetParent(parent, false);
            var n = m.Normal.sqrMagnitude > 0.01f ? m.Normal.normalized : Vector3.up;
            // every new mark sits a hair in front of all earlier ones, so you can always paint over
            _layerBump = (_layerBump + 1) % MaxMarks;
            q.transform.localPosition = pos + n * (0.012f + _layerBump * 0.00002f); // in front of wall decals and defect cells
            // quads show their -Z side: look into the surface
            q.transform.localRotation = Quaternion.LookRotation(-n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up) * Quaternion.Euler(0, 0, m.Roll);
            var kit = (KitType)m.Kit;
            q.transform.localScale = kit == KitType.DuctTape ? new Vector3(m.Size, m.Size * 0.18f, 1) : new Vector3(m.Size, m.Size * 0.9f, 1);
            var r = q.GetComponent<Renderer>();
            int k = Mathf.Clamp(m.Kit, 0, kitMaterials.Length - 1);
            if (kitMaterials.Length > 0 && kitMaterials[k] != null) r.sharedMaterial = kitMaterials[k];
            if (m.WetUntil > NetworkManager.ServerTime.TimeAsFloat && kitMaterials[k] != null)
            {
                if (_wetPaint == null)
                {
                    _wetPaint = new Material(kitMaterials[k]) { name = "WetPaint" };
                    _wetPaint.SetFloat("_Smoothness", 0.95f);
                    _wetPaint.SetColor("_BaseColor", Color.Lerp(kitMaterials[k].GetColor("_BaseColor"), BodgeSquad.Art.Palette.FreshPaint, 0.6f));
                }
                r.sharedMaterial = _wetPaint;
                _wet.Add((r, m.WetUntil));
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }
    }
}
