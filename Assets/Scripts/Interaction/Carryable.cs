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
        float _nextNoise;
        void OnCollisionEnter(Collision c)
        {
            if (!IsSpawned || !IsOwner || Holder != null || Time.time < _nextNoise) return;
            float speed = c.relativeVelocity.magnitude;
            if (speed < 4.5f) return;
            _nextNoise = Time.time + 1.5f;
            var insp = BodgeSquad.Job.Inspector.Instance;
            if (insp != null && insp.IsSpawned) insp.NoiseRpc(c.GetContact(0).point, Mathf.Clamp01(speed / 9f) * Mathf.Clamp(Body.mass, 0.8f, 3f));
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
