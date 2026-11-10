using BodgeSquad.Job;
using TMPro;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // One shelf slot that always has a fresh item of its kit on it. Picking it up pays for it
    // (CarryInteractor asks RepairKit.TryPurchase); the slot restocks once the item leaves.
    public class SupplyShelf : MonoBehaviour
    {
        [SerializeField] KitType kit = KitType.PaintTin;
        [SerializeField] GameObject kitPrefab;
        [SerializeField] Transform slot;
        [SerializeField] TextMeshPro priceTag;
        [SerializeField] float restockDelay = 0.8f;

        RepairKit _stocked;
        float _restockAt = -1f;

        public KitType Kit => kit;

        void Start()
        {
            if (priceTag != null) priceTag.text = $"{RepairRules.KitName(kit)}\n<b>${RepairRules.Price(kit)}</b>";
            Restock();
        }

        void Update()
        {
            if (_stocked != null && (_stocked.Paid || (_stocked.transform.position - slot.position).sqrMagnitude > 0.25f))
            {
                _stocked = null;
                _restockAt = Time.time + restockDelay;
            }
            if (_stocked == null && _restockAt > 0f && Time.time >= _restockAt) Restock();
        }

        void Restock()
        {
            _restockAt = -1f;
            var job = JobManager.Instance;
            if (kitPrefab == null || (job != null && job.Phase != JobPhase.Renovation)) return;
            var go = Instantiate(kitPrefab, slot.position, slot.rotation);
            go.name = $"{RepairRules.KitName(kit)} (new)";
            _stocked = go.GetComponent<RepairKit>();
            _stocked.Configure(kit);
        }
    }
}
