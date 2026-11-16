using BodgeSquad.Job;
using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // A consumable repair item (filler, paint, tape…). Paid for (on the host) the first time
    // someone picks it up.
    [RequireComponent(typeof(Carryable))]
    public class RepairKit : NetworkBehaviour
    {
        [SerializeField] KitType type = KitType.PaintTin;

        public readonly NetworkVariable<bool> PaidVar = new(false);

        // How much is left (1 = new). Written by whoever is holding it; empty → it's gone.
        public readonly NetworkVariable<float> Amount = new(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public float Left => Amount.Value;
        public bool Empty => Amount.Value <= 0.0001f;

        // Owner: spend some (seconds of use for Hold tools, strips for Click tools)
        public void Spend(float units)
        {
            if (!IsOwner) return;
            Amount.Value = Mathf.Max(0f, Amount.Value - units / RepairRules.Capacity(type));
        }

        public KitType Type => type;
        public int Price => RepairRules.Price(type);
        public bool Paid => PaidVar.Value;

        // Host only
        public bool TryPurchase()
        {
            if (Paid) return true;
            var job = JobManager.Instance;
            if (job == null || !job.TrySpend(Price)) return false;
            PaidVar.Value = true;
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
