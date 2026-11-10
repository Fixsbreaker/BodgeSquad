using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.Players
{
    // First-person walker for one local player. Reads its own PlayerInput (one per split-screen slot).
    // Yaw turns the body, pitch tilts the Eye; the Cinemachine camera hard-locks to the Eye.
    [RequireComponent(typeof(CharacterController), typeof(PlayerInput))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] Transform eye;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 3.6f;
        [SerializeField] float sprintSpeed = 5.6f;
        [SerializeField] float acceleration = 22f;   // m/s² toward target velocity
        [SerializeField] float deceleration = 28f;
        [SerializeField] float jumpHeight = 0.9f;
        [SerializeField] float gravity = -22f;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 0.12f;  // degrees per pixel
        [SerializeField] float stickLookSpeed = 170f;     // degrees per second at full tilt
        [SerializeField] float pitchLimit = 80f;

        [Header("Head bob (feel)")]
        [SerializeField] float bobAmplitude = 0.035f;
        [SerializeField] float bobFrequency = 1.9f;       // steps per second at walk speed

        CharacterController _cc;
        PlayerInput _input;
        InputAction _move, _look, _jump, _sprint;
        Vector3 _planarVelocity;
        float _verticalVelocity, _pitch, _bobPhase, _bobBlend;
        Vector3 _eyeRest;

        public Vector3 Velocity => _cc != null ? _cc.velocity : Vector3.zero;
        public float NormalizedSpeed => Mathf.Clamp01(new Vector3(_planarVelocity.x, 0, _planarVelocity.z).magnitude / walkSpeed);
        public bool IsGrounded => _cc != null && _cc.isGrounded;
        public Transform Eye => eye;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _eyeRest = eye.localPosition;
        }

        void Start() // PlayerInput clones its actions per player on enable; bind after that
        {
            _input = GetComponent<PlayerInput>();
            _move = _input.actions["Move"];
            _look = _input.actions["Look"];
            _jump = _input.actions["Jump"];
            _sprint = _input.actions["Sprint"];
        }

        void Update()
        {
            if (_input == null) return;
            Look();
            Move();
            HeadBob();
        }

        void Look()
        {
            Vector2 look = _look.ReadValue<Vector2>();
            bool pointer = _input.currentControlScheme == "Keyboard&Mouse";
            Vector2 delta = pointer ? look * mouseSensitivity : look * (stickLookSpeed * Time.deltaTime);

            transform.Rotate(0f, delta.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - delta.y, -pitchLimit, pitchLimit);
            eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move()
        {
            Vector2 m = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
            float speed = _sprint.IsPressed() ? sprintSpeed : walkSpeed;
            Vector3 target = (transform.right * m.x + transform.forward * m.y) * speed;

            float rate = target.sqrMagnitude > 0.01f ? acceleration : deceleration;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, target, rate * Time.deltaTime);

            if (_cc.isGrounded)
            {
                if (_verticalVelocity < 0f) _verticalVelocity = -2f; // keep snapped to floor
                if (_jump.WasPressedThisFrame())
                    _verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            }
            _verticalVelocity += gravity * Time.deltaTime;

            _cc.Move((_planarVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }

        void HeadBob()
        {
            float speedN = _cc.isGrounded ? NormalizedSpeed : 0f;
            _bobBlend = Mathf.MoveTowards(_bobBlend, speedN, Time.deltaTime / 0.3f); // settle over 0.3 s
            _bobPhase += Time.deltaTime * bobFrequency * Mathf.Max(speedN, 0.2f) * Mathf.PI * 2f;
            float y = Mathf.Sin(_bobPhase * 2f) * bobAmplitude * _bobBlend;   // two dips per stride
            float x = Mathf.Sin(_bobPhase) * bobAmplitude * 0.5f * _bobBlend; // weight shift
            eye.localPosition = _eyeRest + new Vector3(x, y, 0f);
        }

        // Shove loose props when walking into them — cheap slapstick.
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var rb = hit.rigidbody;
            if (rb == null || rb.isKinematic || hit.moveDirection.y < -0.3f) return;
            rb.AddForceAtPosition(new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z) * 1.5f, hit.point, ForceMode.Impulse);
        }
    }
}
