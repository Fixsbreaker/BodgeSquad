using BodgeSquad.Job;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.Interaction
{
    // Interact (E / Y), in priority order:
    //   holding a repair kit + looking at a defect → use the kit on it
    //   holding anything                          → drop it
    //   looking at a pressable (doorbell)          → press it
    //   looking at a carryable                     → pick it up (pays for unpaid kits)
    // Attack (LMB / X) while holding: throw.
    // The held item is driven by velocity toward the hold point, so it still collides
    // (jams in doorways, knocks things over) instead of ghosting through walls.
    [RequireComponent(typeof(PlayerInput))]
    public class CarryInteractor : MonoBehaviour
    {
        [SerializeField] Transform eye;
        [SerializeField] Transform holdPoint;
        [SerializeField] float reach = 2.6f;
        [SerializeField] float followStrength = 14f;
        [SerializeField] float maxCarrySpeed = 12f;
        [SerializeField] float throwSpeed = 9f;
        [SerializeField] float dropDistance = 2.2f; // snaps free if it gets wedged too far away

        InputAction _interact, _attack;
        PlayerInput _input;
        Carryable _held;
        int _rayMask = ~0;
        float _messageUntil;

        public Carryable Held => _held;
        public int PlayerIndex => _input != null ? _input.playerIndex : 0;
        public bool UsesKeyboard => _input != null && _input.currentControlScheme == "Keyboard&Mouse";

        // What the crosshair is on (refreshed every frame, read by the player HUD)
        public Defect LookDefect { get; private set; }
        public Carryable LookCarryable { get; private set; }
        public IPressable LookPressable { get; private set; }
        public string Message { get; private set; }   // short-lived feedback ("Not enough money")

        void Start()
        {
            _input = GetComponent<PlayerInput>();
            _interact = _input.actions["Interact"];
            _attack = _input.actions["Attack"];
        }

        public void SetIgnoredLayer(int layer) => _rayMask = ~(1 << layer);

        void Update()
        {
            if (_interact == null) return;
            ScanLook();
            if (Time.time > _messageUntil) Message = null;

            if (_interact.WasPressedThisFrame()) OnInteract();
            else if (_held != null && _attack.WasPressedThisFrame())
                Release(eye.forward * throwSpeed / _held.CarryWeight);
        }

        readonly RaycastHit[] _hits = new RaycastHit[16];

        void ScanLook()
        {
            LookDefect = null; LookCarryable = null; LookPressable = null;
            int n = Physics.RaycastNonAlloc(eye.position, eye.forward, _hits, reach, _rayMask, QueryTriggerInteraction.Collide);
            // nearest hit that isn't the thing in our hands
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
                if (LookDefect.CanApply(kit.Type, out var why) && LookDefect.Apply(kit.Type, PlayerIndex))
                {
                    var used = _held;
                    Release(Vector3.zero);
                    Destroy(used.gameObject); // consumed
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
            if (_held == null) return;
            var rb = _held.Body;
            Vector3 toTarget = holdPoint.position - rb.worldCenterOfMass;
            if (toTarget.magnitude > dropDistance) { Release(Vector3.zero); return; }

            Vector3 desired = Vector3.ClampMagnitude(toTarget * followStrength / _held.CarryWeight, maxCarrySpeed);
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, desired, 0.5f);
            rb.angularVelocity *= 0.85f;
        }

        void TryGrab(Carryable c)
        {
            if (c.IsHeld) return;
            var kit = c.GetComponent<RepairKit>();
            if (kit != null && !kit.TryPurchase()) { Flash($"Not enough money for {RepairRules.KitName(kit.Type)}"); return; }

            _held = c;
            c.Holder = transform;
            c.Body.isKinematic = false; // pictures hang kinematic on the wall
            c.Body.useGravity = false;
            c.Body.interpolation = RigidbodyInterpolation.Interpolate;
            Debug.Log($"[Carry] {name} picked up {c.name}");
        }

        void Release(Vector3 velocity)
        {
            var c = _held;
            _held = null;
            c.Holder = null;
            c.Body.useGravity = true;
            if (velocity != Vector3.zero) c.Body.linearVelocity = velocity;
            Debug.Log($"[Carry] {name} {(velocity == Vector3.zero ? "dropped" : "threw")} {c.name}");
        }

        public static bool CanHide(Defect d) =>
            d.State != RepairState.Fixed && (JobManager.Instance == null || JobManager.Instance.Phase == JobPhase.Renovation);

        void Flash(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            Message = msg;
            _messageUntil = Time.time + 2f;
        }

        void OnDisable()
        {
            if (_held != null) Release(Vector3.zero);
        }
    }
}
