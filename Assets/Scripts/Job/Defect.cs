using BodgeSquad.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Job
{
    // One piece of damage on a surface. Local +Z points out of the surface.
    // Children: Damage (the visible flaw), one patch per bodge, Fixed (the honest repair).
    // Networked: the host decides its state; every machine shows the same patch.
    public class Defect : NetworkBehaviour
    {
        [SerializeField] string label = "north wall";
        [SerializeField] DefectKind kind = DefectKind.Crack;
        [SerializeField] Severity severity = Severity.Cosmetic;

        [Header("Visual states")]
        [SerializeField] GameObject damage;
        [SerializeField] GameObject paintPatch;
        [SerializeField] GameObject wallpaperPatch;
        [SerializeField] GameObject tapePatch;
        [SerializeField] GameObject fixedPatch;

        [Header("Inspection")]
        [SerializeField] float furnitureCheckDistance = 1.1f;

        readonly NetworkVariable<int> _state = new((int)RepairState.Untouched);
        readonly NetworkVariable<int> _responsible = new(-1);
        readonly NetworkVariable<bool> _caught = new(false);

        public string Label => label;
        public DefectKind Kind => kind;
        public Severity Severity => severity;
        public RepairState State => (RepairState)_state.Value;
        public int ResponsiblePlayer => _responsible.Value; // 0-based, -1 = nobody
        public bool Inspected { get; set; }
        public bool Caught => _caught.Value;

        public string DisplayName => $"{RepairRules.KindName(kind)} ({label})";

        void Start()
        {
            JobManager.Instance?.Register(this);
            Refresh();
        }

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += (_, _) => Refresh();
            _caught.OnValueChanged += (_, _) => Refresh();
            Refresh();
        }

        // What the inspector will judge right now — furniture is checked live.
        public RepairState EffectiveState =>
            State == RepairState.Untouched && IsBehindFurniture() ? RepairState.HiddenByFurniture : State;

        public float Obviousness => RepairRules.Obviousness(EffectiveState, severity);

        public bool CanApply(KitType kit, out string reason)
        {
            reason = null;
            if (JobManager.Instance != null && JobManager.Instance.Phase != JobPhase.Renovation) { reason = "Too late"; return false; }
            if (State == RepairState.Fixed) { reason = "Already fixed"; return false; }
            if (RepairRules.IsProperFix(kit) && RepairRules.ProperKitFor(kind) != kit) { reason = $"{RepairRules.KitName(kit)} won't fix {RepairRules.KindName(kind)}"; return false; }
            return true;
        }

        // Host only
        public bool Apply(KitType kit, int playerIndex)
        {
            if (!IsServer || !CanApply(kit, out _)) return false;
            _state.Value = (int)(RepairRules.IsProperFix(kit) ? RepairState.Fixed : RepairRules.ConcealWith(kit));
            _responsible.Value = playerIndex;
            Debug.Log($"[Defect] P{playerIndex + 1} {RepairRules.StateVerb(State)} {DisplayName} → obviousness {Obviousness:P0}");
            return true;
        }

        // Host: the inspector found it — everyone sees the bodge peeled back.
        public void Reveal()
        {
            if (IsServer) _caught.Value = true;
        }

        // Host: new job
        public void ResetDefect()
        {
            if (!IsServer) return;
            _state.Value = (int)RepairState.Untouched;
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

        // Also used by the edit-time captures (no network): shows a state directly.
        public void PreviewState(RepairState s, bool caught)
        {
            _previewState = s; _previewCaught = caught; _preview = true;
            Refresh();
        }
        bool _preview, _previewCaught;
        RepairState _previewState;

        void Refresh()
        {
            var s = _preview ? _previewState : State;
            bool caught = _preview ? _previewCaught : Caught;
            bool concealed = s is RepairState.PaintedOver or RepairState.Wallpapered or RepairState.Taped;
            SetActive(fixedPatch, s == RepairState.Fixed);
            SetActive(paintPatch, s == RepairState.PaintedOver && !caught);
            SetActive(wallpaperPatch, s == RepairState.Wallpapered && !caught);
            SetActive(tapePatch, s == RepairState.Taped && !caught);
            SetActive(damage, s != RepairState.Fixed && (!concealed || caught));
        }

        static void SetActive(GameObject go, bool on) { if (go != null) go.SetActive(on); }

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
