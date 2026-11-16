using BodgeSquad.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Job
{
    // One piece of damage on a surface (local +Z points out of the surface), covered by hand.
    // The 0.8 m patch is an 8x8 grid; every tool covers cells where you sweep it, on its own layer:
    //   Fix (filler / anti-mould / wiring) · Paint · Wallpaper · Tape (top-most layer wins).
    // How obvious it is = the average over cells: uncovered = 100%, covered = that material's risk.
    // A proper fix counts once 90% of the cells are done. The host owns the masks; all see them.
    public class Defect : NetworkBehaviour
    {
        [SerializeField] string label = "north wall";
        [SerializeField] DefectKind kind = DefectKind.Crack;
        [SerializeField] Severity severity = Severity.Cosmetic;

        [Header("Visual states")]
        [SerializeField] GameObject damage;
        [SerializeField] GameObject paintPatch;      // hidden templates: their materials paint the cells
        [SerializeField] GameObject wallpaperPatch;
        [SerializeField] GameObject tapePatch;
        [SerializeField] GameObject fixedPatch;

        [Header("Inspection")]
        [SerializeField] float furnitureCheckDistance = 1.1f;

        const int Cells = RepairRules.GridSize * RepairRules.GridSize;
        const float Patch = 0.8f;

        // Separate fields on purpose: Netcode only syncs NetworkVariables declared as fields (not in arrays)
        readonly NetworkVariable<ulong> _fixMask = new(0UL);
        readonly NetworkVariable<ulong> _paintMask = new(0UL);
        readonly NetworkVariable<ulong> _paperMask = new(0UL);
        readonly NetworkVariable<ulong> _tapeMask = new(0UL);
        NetworkVariable<ulong>[] _masksCache;
        NetworkVariable<ulong>[] _masks => _masksCache ??= new[] { _fixMask, _paintMask, _paperMask, _tapeMask };
        readonly NetworkVariable<int> _responsible = new(-1);
        readonly NetworkVariable<bool> _caught = new(false);

        public string Label => label;
        public DefectKind Kind => kind;
        public Severity Severity => severity;
        public int ResponsiblePlayer => _responsible.Value; // 0-based, -1 = nobody
        public bool Inspected { get; set; }
        public bool Caught => _caught.Value;
        public string DisplayName => $"{RepairRules.KindName(kind)} ({label})";

        ulong Mask(RepairRules.Layer l) => _preview ? _previewMasks[(int)l] : _masks[(int)l].Value;

        // ------------------------------------------------------------ state from the cells
        public float FixProgress => Count(Mask(RepairRules.Layer.Fix)) / (float)Cells;
        public float Coverage => Count(Mask(RepairRules.Layer.Fix) | Mask(RepairRules.Layer.Paint) | Mask(RepairRules.Layer.Wallpaper) | Mask(RepairRules.Layer.Tape)) / (float)Cells;

        public RepairState State
        {
            get
            {
                if (FixProgress >= RepairRules.FixThreshold) return RepairState.Fixed;
                int paint = Count(Mask(RepairRules.Layer.Paint)), paper = Count(Mask(RepairRules.Layer.Wallpaper)), tape = Count(Mask(RepairRules.Layer.Tape));
                if (paint + paper + tape == 0) return RepairState.Untouched;
                if (tape >= paint && tape >= paper) return RepairState.Taped;
                return paper >= paint ? RepairState.Wallpapered : RepairState.PaintedOver;
            }
        }

        // What the inspector will judge right now — furniture is checked live.
        public RepairState EffectiveState =>
            State == RepairState.Untouched && IsBehindFurniture() ? RepairState.HiddenByFurniture : State;

        public float Obviousness
        {
            get
            {
                if (State == RepairState.Fixed) return 0f;
                ulong fix = Mask(RepairRules.Layer.Fix), paint = Mask(RepairRules.Layer.Paint), paper = Mask(RepairRules.Layer.Wallpaper), tape = Mask(RepairRules.Layer.Tape);
                float pb = RepairRules.Obviousness(RepairState.PaintedOver, severity);
                float wb = RepairRules.Obviousness(RepairState.Wallpapered, severity);
                float tb = RepairRules.Obviousness(RepairState.Taped, severity);
                float sum = 0f;
                for (int i = 0; i < Cells; i++)
                {
                    ulong bit = 1UL << i;
                    sum += (tape & bit) != 0 ? tb : (paper & bit) != 0 ? wb : (paint & bit) != 0 ? pb : (fix & bit) != 0 ? 0f : 1f;
                }
                float obv = sum / Cells;
                if (IsBehindFurniture()) obv = Mathf.Min(obv, RepairRules.Obviousness(RepairState.HiddenByFurniture, severity));
                return obv;
            }
        }

        // ------------------------------------------------------------ lifecycle
        void Start()
        {
            JobManager.Instance?.Register(this);
            Refresh();
        }

        public override void OnNetworkSpawn()
        {
            foreach (var m in _masks) m.OnValueChanged += (_, _) => Refresh();
            _caught.OnValueChanged += (_, _) => Refresh();
            Refresh();
        }

        // ------------------------------------------------------------ applying (cells)
        // Which tool may go on which defect
        public bool CanUse(KitType kit, out string reason)
        {
            reason = null;
            if (JobManager.Instance != null && JobManager.Instance.Phase != JobPhase.Renovation) { reason = "Too late"; return false; }
            if (State == RepairState.Fixed) { reason = "Already fixed"; return false; }
            if (RepairRules.IsProperFix(kit) && RepairRules.ProperKitFor(kind) != kit) { reason = $"{RepairRules.KitName(kit)} won't fix {RepairRules.KindName(kind)}"; return false; }
            return true;
        }

        // Cells under a round brush at a world point (0 if the point is off the patch)
        public ulong BrushMask(Vector3 worldPoint, float radiusCells)
        {
            Vector3 lp = transform.InverseTransformPoint(worldPoint);
            float gx = (lp.x / Patch + 0.5f) * RepairRules.GridSize - 0.5f;
            float gy = (lp.y / Patch + 0.5f) * RepairRules.GridSize - 0.5f;
            if (gx < -radiusCells - 0.5f || gy < -radiusCells - 0.5f || gx > RepairRules.GridSize - 0.5f + radiusCells || gy > RepairRules.GridSize - 0.5f + radiusCells) return 0;
            ulong mask = 0;
            for (int y = 0; y < RepairRules.GridSize; y++)
                for (int x = 0; x < RepairRules.GridSize; x++)
                {
                    float dx = x - gx, dy = y - gy;
                    if (dx * dx + dy * dy <= radiusCells * radiusCells + 0.25f) mask |= 1UL << (y * RepairRules.GridSize + x);
                }
            return mask;
        }

        // A strip of tape: a 4-cell horizontal band across the aim point, 2 cells tall
        public ulong StripMask(Vector3 worldPoint, float tiltDeg)
        {
            Vector3 lp = transform.InverseTransformPoint(worldPoint);
            float gx = (lp.x / Patch + 0.5f) * RepairRules.GridSize - 0.5f;
            float gy = (lp.y / Patch + 0.5f) * RepairRules.GridSize - 0.5f;
            float t = Mathf.Tan(tiltDeg * Mathf.Deg2Rad);
            ulong mask = 0;
            for (int y = 0; y < RepairRules.GridSize; y++)
                for (int x = 0; x < RepairRules.GridSize; x++)
                {
                    float dx = x - gx;
                    if (Mathf.Abs(dx) > 2.6f) continue;
                    if (Mathf.Abs(y - (gy + dx * t)) <= 0.75f) mask |= 1UL << (y * RepairRules.GridSize + x);
                }
            return mask;
        }

        // Host: cells covered by a player
        public void AddCells(RepairRules.Layer layer, ulong cells, int playerIndex)
        {
            if (!IsServer || cells == 0) return;
            var v = _masks[(int)layer];
            ulong before = v.Value;
            v.Value = before | cells;
            if (v.Value != before && _responsible.Value < 0) _responsible.Value = playerIndex;
            if (layer == RepairRules.Layer.Fix && before != v.Value && FixProgress >= RepairRules.FixThreshold)
                Debug.Log($"[Defect] P{playerIndex + 1} properly fixed {DisplayName}");
        }

        // Host: the inspector found it — the bodge is peeled back for everyone.
        public void Reveal() { if (IsServer) _caught.Value = true; }

        // Host: new job
        public void ResetDefect()
        {
            if (!IsServer) return;
            foreach (var m in _masks) m.Value = 0;
            _responsible.Value = -1;
            _caught.Value = false;
            Inspected = false;
        }

        public bool IsBehindFurniture()
        {
            var origin = transform.position + transform.forward * 0.03f;
            foreach (var hit in Physics.RaycastAll(origin, transform.forward, furnitureCheckDistance, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<Concealer>() != null) return true;
            return false;
        }

        // Edit-time captures (no network): show a layer fully applied
        public void PreviewState(RepairState s, bool caught)
        {
            _preview = true; _previewCaught = caught;
            _previewMasks = new ulong[4];
            var full = ulong.MaxValue;
            switch (s)
            {
                case RepairState.Fixed: _previewMasks[0] = full; break;
                case RepairState.PaintedOver: _previewMasks[1] = full; break;
                case RepairState.Wallpapered: _previewMasks[2] = full; break;
                case RepairState.Taped: _previewMasks[3] = full; break;
            }
            Refresh();
        }
        public void PreviewMasks(ulong fix, ulong paint, ulong paper, ulong tape)
        {
            _preview = true; _previewCaught = false;
            _previewMasks = new[] { fix, paint, paper, tape };
            Refresh();
        }
        bool _preview, _previewCaught;
        ulong[] _previewMasks = new ulong[4];

        // ------------------------------------------------------------ visuals: one small quad per covered cell
        Renderer[,] _cells;

        void Refresh()
        {
            if (_cells == null) BuildCells();
            bool caught = _preview ? _previewCaught : Caught;
            bool fixedDone = State == RepairState.Fixed;
            for (int l = 0; l < 4; l++)
            {
                ulong m = Mask((RepairRules.Layer)l);
                bool peel = caught && l != 0; // the inspector peels bodges off
                for (int i = 0; i < Cells; i++)
                    if (_cells[l, i] != null) _cells[l, i].enabled = !peel && (m & (1UL << i)) != 0;
            }
            if (damage != null) damage.SetActive(!fixedDone);
        }

        void BuildCells()
        {
            _cells = new Renderer[4, Cells];
            var mats = new Material[4];
            mats[0] = Mat(fixedPatch); mats[1] = Mat(paintPatch); mats[2] = Mat(wallpaperPatch); mats[3] = Mat(tapePatch);
            var stripe = wallpaperPatch != null && wallpaperPatch.transform.childCount > 0
                ? wallpaperPatch.transform.GetChild(0).GetComponent<Renderer>()?.sharedMaterial : null;
            foreach (var p in new[] { paintPatch, wallpaperPatch, tapePatch, fixedPatch }) if (p != null) p.SetActive(false);

            var root = new GameObject("Cells").transform;
            root.SetParent(transform, false);
            float cell = Patch / RepairRules.GridSize;
            var rnd = new System.Random(name.GetHashCode());
            for (int l = 0; l < 4; l++)
            {
                if (mats[l] == null) continue;
                float z = 0.009f + 0.0025f * l;
                for (int y = 0; y < RepairRules.GridSize; y++)
                    for (int x = 0; x < RepairRules.GridSize; x++)
                    {
                        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                        if (Application.isPlaying) Destroy(q.GetComponent<Collider>()); else DestroyImmediate(q.GetComponent<Collider>());
                        q.name = $"L{l}_{x}_{y}";
                        q.transform.SetParent(root, false);
                        q.transform.localPosition = new Vector3((x + 0.5f) * cell - Patch / 2, (y + 0.5f) * cell - Patch / 2, z);
                        float jitter = l == 3 ? (float)(rnd.NextDouble() * 16 - 8) : (float)(rnd.NextDouble() * 30 - 15);
                        q.transform.localRotation = Quaternion.Euler(0, 180, jitter);
                        float s = l == 3 ? 1.15f : 1.3f; // overlap so a covered area reads as one surface
                        q.transform.localScale = new Vector3(cell * s, cell * s, 1);
                        var r = q.GetComponent<Renderer>();
                        r.sharedMaterial = l == 2 && stripe != null && x % 3 == 1 ? stripe : mats[l];
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        r.enabled = false;
                        _cells[l, y * RepairRules.GridSize + x] = r;
                    }
            }
        }

        static Material Mat(GameObject template) =>
            template != null && template.TryGetComponent<Renderer>(out var r) ? r.sharedMaterial
            : template != null ? template.GetComponentInChildren<Renderer>(true)?.sharedMaterial : null;

        static int Count(ulong v)
        {
            int n = 0;
            while (v != 0) { v &= v - 1; n++; }
            return n;
        }

        // Where the inspector stands to look at it.
        public Vector3 InspectPoint
        {
            get
            {
                var p = transform.position + transform.forward * 1.1f;
                p.y = 0f;
                return p;
            }
        }
    }
}
