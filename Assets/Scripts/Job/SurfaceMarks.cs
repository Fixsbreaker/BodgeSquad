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

        // One material per kit, from the art bible
        static Material[] BuildMaterials()
        {
            var src = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            Material M(Color c, float smooth)
            {
                var m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                return m;
            }
            var mats = new Material[6];
            mats[(int)KitType.Filler] = M(Color.Lerp(BodgeSquad.Art.Palette.Walls, BodgeSquad.Art.Palette.Cream, 0.12f), 0.05f); // filled and touched up in the wall colour
            mats[(int)KitType.AntiMould] = M(Color.Lerp(BodgeSquad.Art.Palette.Cream, BodgeSquad.Art.Palette.AmbientSky, 0.5f), 0.6f); // wet spray
            mats[(int)KitType.WiringKit] = M(BodgeSquad.Art.Palette.Cream, 0.4f);         // white plastic cover plate
            mats[(int)KitType.PaintTin] = M(BodgeSquad.Art.Palette.FreshPaint, 0.35f);
            mats[(int)KitType.WallpaperRoll] = M(BodgeSquad.Art.Palette.Wallpaper, 0.05f);
            mats[(int)KitType.DuctTape] = M(BodgeSquad.Art.Palette.Tape, 0.55f);
            return mats;
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
        public void Place(KitType kit, RaycastHit hit, float size, float roll)
        {
            var m = new Mark { Size = size, Roll = roll, Kit = (byte)kit, Parent = World, Pos = hit.point, Normal = hit.normal };
            if (hit.rigidbody != null && hit.rigidbody.GetComponent<RepairKit>() != null) return; // not on tins and rolls
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
        void AddRpc(Mark m)
        {
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

        void ClearDrawn()
        {
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
            q.transform.localPosition = pos + n * (0.003f + _layerBump * 0.00002f);
            // quads show their -Z side: look into the surface
            q.transform.localRotation = Quaternion.LookRotation(-n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up) * Quaternion.Euler(0, 0, m.Roll);
            var kit = (KitType)m.Kit;
            q.transform.localScale = kit == KitType.DuctTape ? new Vector3(m.Size, m.Size * 0.18f, 1) : new Vector3(m.Size, m.Size * 0.9f, 1);
            var r = q.GetComponent<Renderer>();
            int k = Mathf.Clamp(m.Kit, 0, kitMaterials.Length - 1);
            if (kitMaterials.Length > 0 && kitMaterials[k] != null) r.sharedMaterial = kitMaterials[k];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }
    }
}
