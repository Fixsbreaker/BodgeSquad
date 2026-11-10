using BodgeSquad.Job;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // A consumable repair item (filler, paint, tape…). Paid for the first time someone picks it up.
    [RequireComponent(typeof(Carryable))]
    public class RepairKit : MonoBehaviour
    {
        [SerializeField] KitType type = KitType.PaintTin;

        public KitType Type => type;
        public int Price => RepairRules.Price(type);
        public bool Paid { get; private set; }

        public void Configure(KitType t) => type = t;

        public bool TryPurchase()
        {
            if (Paid) return true;
            var job = JobManager.Instance;
            if (job == null || !job.TrySpend(Price)) return false;
            Paid = true;
            Debug.Log($"[Shop] Bought {RepairRules.KitName(type)} for ${Price}");
            return true;
        }
    }

    // Anything you press Interact on without picking it up (doorbell…).
    public interface IPressable
    {
        string PressPrompt { get; }
        void Press(int playerIndex);
    }
}
