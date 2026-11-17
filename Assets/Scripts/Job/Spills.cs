using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Job
{
    // Paint on the floor. A tin that hits the ground hard bursts and leaves a puddle; puddles are
    // slippery (run through one and you go flying). The host keeps the list, everyone draws it.
    public class Spills : NetworkBehaviour
    {
        public struct Puddle : INetworkSerializeByMemcpy, IEquatable<Puddle>
        {
            public Vector3 Pos;
            public float Radius;
            public int Seed;
            public bool Equals(Puddle o) => Pos == o.Pos && Radius == o.Radius && Seed == o.Seed;
        }

        const int Max = 24;
        public static Spills Instance { get; private set; }

        NetworkList<Puddle> _list;
        readonly List<GameObject> _drawn = new();
        Transform _root;
        Material _mat;

        void Awake()
        {
            Instance = this;
            _list = new NetworkList<Puddle>();
        }

        public override void OnNetworkSpawn()
        {
            _root = new GameObject("Spills").transform;
            var src = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            _mat = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _mat.SetColor("_BaseColor", BodgeSquad.Art.Palette.FreshPaint);
            _mat.SetFloat("_Smoothness", 0.92f); // wet
            _list.OnListChanged += OnChanged;
            Redraw();
        }

        public override void OnNetworkDespawn()
        {
            _list.OnListChanged -= OnChanged;
            foreach (var g in _drawn) if (g != null) Destroy(g);
            _drawn.Clear();
            if (_root != null) Destroy(_root.gameObject);
        }

        public int Count => _list != null ? _list.Count : 0;

        // Anyone: splash here
        [Rpc(SendTo.Server)]
        public void SpillRpc(Vector3 pos, float radius)
        {
            if (_list.Count >= Max) _list.RemoveAt(0);
            _list.Add(new Puddle { Pos = pos, Radius = radius, Seed = UnityEngine.Random.Range(1, 99999) });
        }

        // Host: new job
        public void ClearAll() { if (IsServer) _list.Clear(); }

        // Is this spot (feet) in a puddle?
        public bool IsSlippery(Vector3 feet)
        {
            if (_list == null) return false;
            for (int i = 0; i < _list.Count; i++)
            {
                var p = _list[i];
                if (Mathf.Abs(feet.y - p.Pos.y) > 0.35f) continue;
                float dx = feet.x - p.Pos.x, dz = feet.z - p.Pos.z;
                if (dx * dx + dz * dz < p.Radius * p.Radius * 0.8f) return true;
            }
            return false;
        }

        void OnChanged(NetworkListEvent<Puddle> e) => Redraw();

        void Redraw()
        {
            foreach (var g in _drawn) if (g != null) Destroy(g);
            _drawn.Clear();
            for (int i = 0; i < _list.Count; i++) _drawn.Add(Draw(_list[i], i));
        }

        GameObject Draw(Puddle p, int index)
        {
            var go = new GameObject("Puddle");
            go.transform.SetParent(_root, false);
            go.transform.position = p.Pos + Vector3.up * (0.006f + index * 0.0006f);
            var mesh = new Mesh { name = "PuddleBlob" };
            var rnd = new System.Random(p.Seed);
            const int seg = 28;
            var v = new List<Vector3> { Vector3.zero };
            var n = new List<Vector3> { Vector3.up };
            var t = new List<int>();
            // a lumpy splat: a few big lobes plus small wobble
            float l1 = (float)rnd.NextDouble() * 6.28f, l2 = (float)rnd.NextDouble() * 6.28f;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float r = p.Radius * (0.75f + 0.22f * Mathf.Sin(a * 3f + l1) + 0.12f * Mathf.Sin(a * 5f + l2) + 0.08f * (float)rnd.NextDouble());
                v.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r));
                n.Add(Vector3.up);
            }
            for (int i = 0; i < seg; i++) { t.Add(0); t.Add(1 + (i + 1) % seg); t.Add(1 + i); }
            // a couple of loose droplets around it
            for (int d = 0; d < 4; d++)
            {
                float a = (float)rnd.NextDouble() * 6.28f, dist = p.Radius * (1.05f + (float)rnd.NextDouble() * 0.5f), rr = p.Radius * (0.08f + (float)rnd.NextDouble() * 0.1f);
                var c = new Vector3(Mathf.Cos(a) * dist, 0, Mathf.Sin(a) * dist);
                int b = v.Count;
                v.Add(c); n.Add(Vector3.up);
                for (int i = 0; i < 8; i++) { float aa = i / 8f * 6.28f; v.Add(c + new Vector3(Mathf.Cos(aa) * rr, 0, Mathf.Sin(aa) * rr)); n.Add(Vector3.up); }
                for (int i = 0; i < 8; i++) { t.Add(b); t.Add(b + 1 + (i + 1) % 8); t.Add(b + 1 + i); }
            }
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
