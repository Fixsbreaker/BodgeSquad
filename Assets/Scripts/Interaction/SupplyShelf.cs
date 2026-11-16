using BodgeSquad.Job;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // One shelf slot that always has a fresh item of its kit on it. Picking it up pays for it;
    // the host restocks the slot once the item leaves. Items are network-spawned by the host.
    public class SupplyShelf : MonoBehaviour
    {
        [SerializeField] KitType kit = KitType.PaintTin;
        [SerializeField] GameObject kitPrefab;
        [SerializeField] Transform slot;
        [SerializeField] TextMeshPro priceTag;
        [SerializeField] float restockDelay = 0.8f;

        RepairKit _stocked;
        float _restockAt;

        public KitType Kit => kit;

        void Start()
        {
            if (priceTag != null) priceTag.text = $"{RepairRules.KitName(kit)}\n<b>${RepairRules.Price(kit)}</b>";
        }

        static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        void Update()
        {
            if (!IsHost) { _stocked = null; return; }
            if (_stocked != null && (_stocked.Paid || !_stocked.IsSpawned || (_stocked.transform.position - slot.position).sqrMagnitude > 0.25f))
            {
                _stocked = null;
                _restockAt = Time.time + restockDelay;
            }
            if (_stocked == null && Time.time >= _restockAt) Restock();
        }

        void Restock()
        {
            _restockAt = Time.time + restockDelay;
            var job = JobManager.Instance;
            if (kitPrefab == null || job == null || !job.IsSpawned || job.Phase != JobPhase.Renovation) return;
            var go = Instantiate(kitPrefab, slot.position, slot.rotation);
            go.name = $"{RepairRules.KitName(kit)} (new)";
            var no = go.GetComponent<NetworkObject>();
            no.DontDestroyWithOwner = true;
            no.Spawn(destroyWithScene: true);
            _stocked = go.GetComponent<RepairKit>();
            // stands still on the shelf (round rolls used to roll off and get restocked forever)
            go.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezeAll;
        }

        // Host, new job: forget the old stock so a fresh one appears
        public void ResetStock()
        {
            _stocked = null;
            _restockAt = 0f;
        }
    }
}
