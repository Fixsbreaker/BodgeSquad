using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // Anything a bodger can pick up, carry and throw (materials, furniture, pictures, junk).
    // Networked: whoever carries it becomes its owner and drives its physics; everyone
    // else sees it through NetworkTransform/NetworkRigidbody. The host tracks who holds it.
    [RequireComponent(typeof(Rigidbody))]
    public class Carryable : NetworkBehaviour
    {
        [Tooltip("Heavier items lag behind the hold point and fly shorter.")]
        [SerializeField] float carryWeight = 1f;

        public readonly NetworkVariable<ulong> HolderClient = new(NoHolder);
        public const ulong NoHolder = ulong.MaxValue;

        public Rigidbody Body { get; private set; }
        public float CarryWeight => carryWeight;
        public Transform Holder { get; internal set; }  // local only, on the carrier's machine
        public bool IsHeld => HolderClient.Value != NoHolder;

        // Where it started (host): used when a job restarts or it falls out of the house
        public Vector3 HomePosition { get; private set; }
        public Quaternion HomeRotation { get; private set; }

        void Awake() => Body = GetComponent<Rigidbody>();

        public override void OnNetworkSpawn()
        {
            HomePosition = transform.position;
            HomeRotation = transform.rotation;
        }

        // Thrown things crash: the inspector hears it and comes to look (the owner simulates the physics)
        // ...and they hurt: bean the inspector (stun / paint in the eyes), knock a mate flat,
        // and a paint tin that hits the floor hard bursts into a slippery puddle.
        float _nextNoise, _nextHit, _flightSpeed;
        public float ThrownAt { get; set; } = -99f;   // set by whoever threw it (on their machine)
        bool Flying => Time.time - ThrownAt < 3f && _flightSpeed > 3f;

        void FixedUpdate() { if (IsOwner && Body != null && !Body.isKinematic) _flightSpeed = Body.linearVelocity.magnitude; }

        void OnCollisionEnter(Collision c)
        {
            if (!IsSpawned || !IsOwner || Holder != null) return;
            float speed = c.relativeVelocity.magnitude;
            bool flying = Flying; // only real throws hurt (not the inspector walking into a tin on the floor)
            var point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            TryGetComponent<RepairKit>(out var kit);

            var insp = c.collider.GetComponentInParent<BodgeSquad.Job.Inspector>();
            if (insp != null)
            {
                if (flying && Time.time >= _nextHit && insp.IsSpawned)
                {
                    _nextHit = Time.time + 1f;
                    ThrownAt = -99f; // one hit per throw
                    insp.HitRpc(kit != null ? (int)kit.Type : -1);
                    if (kit != null && kit.Type == BodgeSquad.Job.KitType.PaintTin) Burst(kit, point, 0.35f);
                }
                return;
            }
            var mate = c.collider.GetComponentInParent<BodgeSquad.Players.NetworkBodger>();
            if (mate != null)
            {
                if (flying && _flightSpeed > 4f && Time.time >= _nextHit && mate.OwnerClientId != OwnerClientId && mate.IsSpawned)
                {
                    _nextHit = Time.time + 1f;
                    var push = c.relativeVelocity.sqrMagnitude > 0.01f ? -c.relativeVelocity.normalized : transform.forward;
                    string what = name.Replace("(Clone)", "").Replace("(new)", "").Replace("Kit_", "").Trim();
                    mate.KnockDownRpc(push * Mathf.Clamp(speed * 0.5f, 2f, 5f), $"Knocked flat by a flying {what}!");
                }
                return;
            }

            if (speed < 4.5f || Time.time < _nextNoise) return;
            _nextNoise = Time.time + 1.5f;
            if (kit != null && kit.Type == BodgeSquad.Job.KitType.PaintTin && c.contactCount > 0 && c.GetContact(0).normal.y > 0.6f)
                Burst(kit, point, Mathf.Lerp(0.55f, 0.9f, Mathf.InverseLerp(4.5f, 10f, speed)));
            var ins = BodgeSquad.Job.Inspector.Instance;
            if (ins != null && ins.IsSpawned) ins.NoiseRpc(point, Mathf.Clamp01(speed / 9f) * Mathf.Clamp(Body.mass, 0.8f, 3f));
        }

        // A tin bursts: paint everywhere, and a chunk of what was left in it is gone
        void Burst(RepairKit kit, Vector3 point, float radius)
        {
            if (kit.Left < 0.05f) return;
            kit.Spend(BodgeSquad.Job.RepairRules.Capacity(kit.Type) * 0.35f);
            var spills = BodgeSquad.Job.Spills.Instance;
            if (spills == null || !spills.IsSpawned) return;
            var at = point; // the puddle lands on the floor under the impact
            if (Physics.Raycast(point + Vector3.up * 0.3f, Vector3.down, out var floor, 3f, ~(1 << 2), QueryTriggerInteraction.Ignore)) at = floor.point;
            spills.SpillRpc(at, radius);
        }

        // Owner only: put it somewhere instantly
        public void Warp(Vector3 pos, Quaternion rot)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.position = pos; Body.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
        }

        // Host → current owner: go home (new job / fell out of the map)
        [Rpc(SendTo.Owner)]
        public void SendHomeRpc()
        {
            Holder = null;
            Body.useGravity = true;
            Body.isKinematic = false;
            Warp(HomePosition, HomeRotation);
        }
    }
}
