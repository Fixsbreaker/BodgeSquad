using BodgeSquad.Job;
using BodgeSquad.Players;
using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // Interact (E / Y), in priority order:
    //   holding furniture/picture + looking at a defect → snap it in front of the defect (free cover)
    //   holding a repair kit + looking at a defect     → use the kit on it
    //   holding anything                               → drop it
    //   looking at a pressable (doorbell)               → press it
    //   looking at a carryable                          → pick it up (the host charges for unpaid kits)
    // Attack (LMB / X) while holding: throw.
    // Online: the local player asks the host to pick something up; the host checks it's free and
    // affordable, then hands over ownership so this machine drives its physics while carrying.
    public class CarryInteractor : NetworkBehaviour
    {
        [SerializeField] Transform eye;
        [SerializeField] Transform holdPoint;
        [SerializeField] float reach = 2.6f;
        [SerializeField] float followStrength = 14f;
        [SerializeField] float maxCarrySpeed = 12f;
        [SerializeField] float throwSpeed = 9f;
        [SerializeField] float dropDistance = 2.2f; // snaps free if it gets wedged too far away

        BodgerInput _input;
        NetworkBodger _bodger;
        Carryable _held;
        bool _pending;
        int _rayMask = ~0;
        float _messageUntil;

        public Carryable Held => _held;
        public int PlayerIndex => _bodger != null ? _bodger.Index : 0;
        public bool UsesKeyboard => _input == null || _input.UsesKeyboard;

        // What the crosshair is on (refreshed every frame, read by the player HUD)
        public Defect LookDefect { get; private set; }
        public Carryable LookCarryable { get; private set; }
        public IPressable LookPressable { get; private set; }
        public string Message { get; private set; }   // short-lived feedback ("Not enough money")

        void Awake()
        {
            _input = GetComponent<BodgerInput>();
            _bodger = GetComponent<NetworkBodger>();
        }

        public void SetIgnoredLayer(int layer) => _rayMask = ~(1 << layer);

        void Update()
        {
            if (!IsOwner || _input == null || !_input.Ready || !_input.enabled) return;
            if (_held != null && (_held == null || !_held.IsSpawned)) _held = null; // consumed or despawned
            ScanLook();
            if (Time.time > _messageUntil) Message = null;

            if (_input.InteractPressed()) OnInteract();
            else if (_held != null && _input.AttackPressed())
                Release(eye.forward * throwSpeed / _held.CarryWeight);
        }

        readonly RaycastHit[] _hits = new RaycastHit[16];

        void ScanLook()
        {
            LookDefect = null; LookCarryable = null; LookPressable = null;
            int n = Physics.RaycastNonAlloc(eye.position, eye.forward, _hits, reach, _rayMask, QueryTriggerInteraction.Collide);
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                if (_held != null && _hits[i].rigidbody == _held.Body) continue;
                if (best < 0 || _hits[i].distance < _hits[best].distance) best = i;
            }
            if (best < 0) return;
            var hit = _hits[best];
            LookDefect = hit.collider.GetComponentInParent<Defect>();
            var c = hit.collider.GetComponentInParent<Carryable>();
            if (c != null && c != _held) LookCarryable = c;
            LookPressable = hit.collider.GetComponentInParent<IPressable>();
        }

        void OnInteract()
        {
            var kit = _held != null ? _held.GetComponent<RepairKit>() : null;
            var cover = _held != null ? _held.GetComponent<Concealer>() : null;
            if (cover != null && LookDefect != null && CanHide(LookDefect))
            {
                var d = LookDefect;
                Release(Vector3.zero);
                bool covers = cover.PlaceAgainst(d);
                Flash(covers ? $"{d.DisplayName} hidden behind the {cover.DisplayName}"
                             : $"The {cover.DisplayName} is too small to hide that one");
                return;
            }
            if (kit != null && LookDefect != null)
            {
                if (LookDefect.CanApply(kit.Type, out var why))
                {
                    var used = _held;
                    DropLocal(Vector3.zero);
                    used.gameObject.SetActive(false); // vanish now; the host despawns it
                    ApplyKitRpc(new NetworkObjectReference(LookDefect.NetworkObject), new NetworkObjectReference(used.NetworkObject));
                }
                else Flash(why);
                return;
            }
            if (_held != null) { Release(Vector3.zero); return; }
            if (LookPressable != null && LookPressable.PressPrompt != null) { LookPressable.Press(PlayerIndex); return; }
            if (LookCarryable != null) TryGrab(LookCarryable);
        }

        void FixedUpdate()
        {
            if (!IsOwner || _held == null || !_held.IsOwner) return;
            var rb = _held.Body;
            if (rb.isKinematic) rb.isKinematic = false;
            Vector3 toTarget = holdPoint.position - rb.worldCenterOfMass;
            if (toTarget.magnitude > dropDistance) { Release(Vector3.zero); return; }

            Vector3 desired = Vector3.ClampMagnitude(toTarget * followStrength / _held.CarryWeight, maxCarrySpeed);
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, desired, 0.5f);
            rb.angularVelocity *= 0.85f;
        }

        // ------------------------------------------------------------ test hooks (smoke test)
        public void TestGrab(Carryable c) => TryGrab(c);
        public void TestUseHeldOn(Defect d)
        {
            if (_held == null || !_held.TryGetComponent<RepairKit>(out var kit) || !d.CanApply(kit.Type, out _)) return;
            var used = _held;
            DropLocal(Vector3.zero);
            used.gameObject.SetActive(false);
            ApplyKitRpc(new NetworkObjectReference(d.NetworkObject), new NetworkObjectReference(used.NetworkObject));
        }

        // ------------------------------------------------------------ grab
        void TryGrab(Carryable c)
        {
            if (_pending || c.IsHeld) return;
            _pending = true;
            RequestGrabRpc(new NetworkObjectReference(c.NetworkObject));
        }

        [Rpc(SendTo.Server)]
        void RequestGrabRpc(NetworkObjectReference itemRef, RpcParams p = default)
        {
            ulong sender = p.Receive.SenderClientId;
            if (!itemRef.TryGet(out var no) || !no.TryGetComponent<Carryable>(out var c)) { GrabResultRpc(itemRef, false, ""); return; }
            if (c.IsHeld && c.HolderClient.Value != sender) { GrabResultRpc(itemRef, false, "Someone else is holding that"); return; }
            if (no.TryGetComponent<RepairKit>(out var kit) && !kit.TryPurchase())
            {
                GrabResultRpc(itemRef, false, $"Not enough money for {RepairRules.KitName(kit.Type)}");
                return;
            }
            c.HolderClient.Value = sender;
            if (no.OwnerClientId != sender) no.ChangeOwnership(sender);
            GrabResultRpc(itemRef, true, "");
        }

        [Rpc(SendTo.Owner)]
        void GrabResultRpc(NetworkObjectReference itemRef, bool ok, string message)
        {
            _pending = false;
            if (!ok) { Flash(message); return; }
            if (!itemRef.TryGet(out var no) || !no.TryGetComponent<Carryable>(out var c)) return;
            if (_held != null) Release(Vector3.zero);
            _held = c;
            c.Holder = transform;
            c.Body.useGravity = false;
            c.Body.isKinematic = false; // pictures hang kinematic on the wall
            c.Body.interpolation = RigidbodyInterpolation.Interpolate;
            Debug.Log($"[Carry] {name} picked up {c.name}");
        }

        // ------------------------------------------------------------ release
        void DropLocal(Vector3 velocity)
        {
            var c = _held;
            _held = null;
            if (c == null) return;
            c.Holder = null;
            c.Body.useGravity = true;
            if (velocity != Vector3.zero) c.Body.linearVelocity = velocity;
        }

        void Release(Vector3 velocity)
        {
            var c = _held;
            DropLocal(velocity);
            if (c == null) return;
            if (IsSpawned && c.IsSpawned) ReleaseRpc(new NetworkObjectReference(c.NetworkObject));
            Debug.Log($"[Carry] {name} {(velocity == Vector3.zero ? "dropped" : "threw")} {c.name}");
        }

        [Rpc(SendTo.Server)]
        void ReleaseRpc(NetworkObjectReference itemRef, RpcParams p = default)
        {
            if (itemRef.TryGet(out var no) && no.TryGetComponent<Carryable>(out var c) && c.HolderClient.Value == p.Receive.SenderClientId)
                c.HolderClient.Value = Carryable.NoHolder;
        }

        // ------------------------------------------------------------ use a kit
        [Rpc(SendTo.Server)]
        void ApplyKitRpc(NetworkObjectReference defectRef, NetworkObjectReference kitRef, RpcParams p = default)
        {
            if (!kitRef.TryGet(out var kitNo) || !kitNo.TryGetComponent<RepairKit>(out var kit)) return;
            bool ok = defectRef.TryGet(out var dNo) && dNo.TryGetComponent<Defect>(out var d) && d.Apply(kit.Type, PlayerIndex);
            if (!ok) { FlashRpc("Too late for that"); }
            kitNo.Despawn(true); // consumed either way: it's been opened
        }

        [Rpc(SendTo.Owner)]
        void FlashRpc(string msg) => Flash(msg);

        public static bool CanHide(Defect d) =>
            d.State != RepairState.Fixed && (JobManager.Instance == null || JobManager.Instance.Phase == JobPhase.Renovation);

        void Flash(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            Message = msg;
            _messageUntil = Time.time + 2f;
        }

        public override void OnNetworkDespawn()
        {
            if (_held != null) DropLocal(Vector3.zero);
        }
    }
}
