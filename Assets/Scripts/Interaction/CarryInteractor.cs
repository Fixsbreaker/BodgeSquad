using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.Interaction
{
    // Interact: pick up / drop the Carryable you're looking at. Attack: throw it.
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
        Carryable _held;
        int _rayMask = ~0;

        public Carryable Held => _held;

        void Start()
        {
            var input = GetComponent<PlayerInput>();
            _interact = input.actions["Interact"];
            _attack = input.actions["Attack"];
        }

        public void SetIgnoredLayer(int layer) => _rayMask = ~(1 << layer);

        void Update()
        {
            if (_interact == null) return;
            if (_interact.WasPressedThisFrame())
            {
                if (_held != null) Release(Vector3.zero);
                else TryGrab();
            }
            else if (_held != null && _attack.WasPressedThisFrame())
            {
                Release(eye.forward * throwSpeed / _held.CarryWeight);
            }
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

        void TryGrab()
        {
            if (!Physics.Raycast(eye.position, eye.forward, out var hit, reach, _rayMask, QueryTriggerInteraction.Ignore)) return;
            var c = hit.collider.GetComponentInParent<Carryable>();
            if (c == null || c.IsHeld) return;

            _held = c;
            c.Holder = transform;
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

        void OnDisable()
        {
            if (_held != null) Release(Vector3.zero);
        }
    }
}
