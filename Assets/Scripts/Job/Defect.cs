using BodgeSquad.Interaction;
using UnityEngine;

namespace BodgeSquad.Job
{
    // One piece of damage on a surface. Local +Z points out of the surface.
    // Children: Damage (the visible flaw), one patch per bodge, Fixed (the honest repair).
    public class Defect : MonoBehaviour
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

        public string Label => label;
        public DefectKind Kind => kind;
        public Severity Severity => severity;
        public RepairState State { get; private set; } = RepairState.Untouched;
        public int ResponsiblePlayer { get; private set; } = -1; // 0-based, -1 = nobody
        public bool Inspected { get; set; }
        public bool Caught { get; set; }

        public string DisplayName => $"{kind switch { DefectKind.ExposedWiring => "Exposed wiring", DefectKind.Mould => "Mould", DefectKind.Hole => "Hole", _ => "Crack" }} ({label})";

        void Start()
        {
            JobManager.Instance?.Register(this);
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
            if (RepairRules.IsProperFix(kit) && RepairRules.ProperKitFor(kind) != kit) { reason = $"{RepairRules.KitName(kit)} won't fix {kind}"; return false; }
            return true;
        }

        public bool Apply(KitType kit, int playerIndex)
        {
            if (!CanApply(kit, out _)) return false;
            State = RepairRules.IsProperFix(kit) ? RepairState.Fixed : RepairRules.ConcealWith(kit);
            ResponsiblePlayer = playerIndex;
            Refresh();
            Debug.Log($"[Defect] P{playerIndex + 1} {RepairRules.StateVerb(State)} {DisplayName} → obviousness {Obviousness:P0}");
            return true;
        }

        // Inspector found it: peel the bodge back so everyone sees what was hidden.
        public void Reveal()
        {
            Caught = true;
            if (State != RepairState.Fixed)
            {
                SetActive(paintPatch, false);
                SetActive(wallpaperPatch, false);
                SetActive(tapePatch, false);
                SetActive(damage, true);
            }
        }

        public bool IsBehindFurniture()
        {
            var origin = transform.position + transform.forward * 0.03f;
            foreach (var hit in Physics.RaycastAll(origin, transform.forward, furnitureCheckDistance, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<Concealer>() != null) return true;
            return false;
        }

        void Refresh()
        {
            SetActive(damage, State != RepairState.Fixed);
            SetActive(paintPatch, State == RepairState.PaintedOver);
            SetActive(wallpaperPatch, State == RepairState.Wallpapered);
            SetActive(tapePatch, State == RepairState.Taped);
            SetActive(fixedPatch, State == RepairState.Fixed);
            // A concealed defect hides its damage under the patch
            if (State is RepairState.PaintedOver or RepairState.Wallpapered or RepairState.Taped) SetActive(damage, false);
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
