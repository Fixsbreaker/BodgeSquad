using UnityEngine;

namespace BodgeSquad.Players
{
    // First-person walker for the local player (enabled only on the owner's machine).
    // Yaw turns the body, pitch tilts the Eye; the Cinemachine camera hard-locks to the Eye.
    [RequireComponent(typeof(CharacterController), typeof(BodgerInput))]
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
        BodgerInput _input;
        Vector3 _planarVelocity;
        float _verticalVelocity, _pitch, _bobPhase, _bobBlend;
        Vector3 _eyeRest;

        // Teleport safely (the CharacterController overrides transform writes while enabled)
        public void Teleport(Vector3 pos, Quaternion rot)
        {
            _cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            _cc.enabled = true;
            _planarVelocity = Vector3.zero; _verticalVelocity = 0f;
        }

        // ---- slapstick: paint puddles and pratfalls ---------------------------------------
        float _downUntil, _getUp;
        bool _wasSlippery;
        public bool IsDown => Time.time < _downUntil;

        public void KnockDown(Vector3 shove, float seconds)
        {
            _downUntil = Time.time + seconds;
            _planarVelocity = new Vector3(shove.x, 0, shove.z) + new Vector3(_planarVelocity.x, 0, _planarVelocity.z) * 0.6f;
            _verticalVelocity = Mathf.Max(_verticalVelocity, 2.5f);
        }

        // Test hook: running at this speed right now
        public void TestRun(Vector3 velocity) => _planarVelocity = velocity;

        public Vector3 Velocity => _cc != null ? _cc.velocity : Vector3.zero;
        public float NormalizedSpeed => Mathf.Clamp01(new Vector3(_planarVelocity.x, 0, _planarVelocity.z).magnitude / walkSpeed);
        public bool IsGrounded => _cc != null && _cc.isGrounded;
        public Transform Eye => eye;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _eyeRest = eye.localPosition;
        }

        void Start() => _input = GetComponent<BodgerInput>();

        void Update()
        {
            if (_input == null || !_input.Ready) return;
            if (IsDown || _getUp > 0f) { Sprawl(); return; }
            Look();
            Move();
            HeadBob();
        }

        void Look()
        {
            Vector2 look = _input.ReadLook();
            bool pointer = _input.UsesKeyboard;
            Vector2 delta = pointer ? look * mouseSensitivity : look * (stickLookSpeed * Time.deltaTime);

            transform.Rotate(0f, delta.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - delta.y, -pitchLimit, pitchLimit);
            eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void Move()
        {
            Vector2 m = Vector2.ClampMagnitude(_input.ReadMove(), 1f);
            float speed = _input.SprintHeld() ? sprintSpeed : walkSpeed;
            Vector3 target = (transform.right * m.x + transform.forward * m.y) * speed;

            float rate = target.sqrMagnitude > 0.01f ? acceleration : deceleration;
            // wet paint: like ice, and running into it at speed puts you on your back
            var spills = BodgeSquad.Job.Spills.Instance;
            bool slippery = _cc.isGrounded && spills != null && spills.IsSlippery(transform.position);
            if (slippery)
            {
                float speedNow = new Vector3(_planarVelocity.x, 0, _planarVelocity.z).magnitude;
                if (!_wasSlippery && speedNow > walkSpeed * 1.05f)
                {
                    _wasSlippery = true;
                    GetComponent<NetworkBodger>()?.KnockDown(_planarVelocity * 0.6f, "Whoops! Slipped in the paint!");
                    return;
                }
                rate = 2.5f;
            }
            _wasSlippery = slippery;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, target, rate * Time.deltaTime);

            if (_cc.isGrounded)
            {
                if (_verticalVelocity < 0f) _verticalVelocity = -2f; // keep snapped to floor
                if (_input.JumpPressed())
                    _verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            }
            _verticalVelocity += gravity * Time.deltaTime;

            _cc.Move((_planarVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }

        // Lying on the floor: slide to a stop, the view rolls over and looks at the ceiling, then you get up
        void Sprawl()
        {
            bool down = IsDown;
            _getUp = down ? 0.45f : Mathf.Max(0f, _getUp - Time.deltaTime);
            if (down && _getUp == 0f) _getUp = 0.45f;
            float lie = down ? 1f : _getUp / 0.45f;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, Vector3.zero, 5f * Time.deltaTime);
            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;
            _cc.Move((_planarVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime);
            var lyingPos = new Vector3(0.2f, 0.25f, -0.5f);
            var lyingRot = Quaternion.Euler(-10f, 0f, 78f); // cheek on the floor, the room on its side
            eye.localPosition = Vector3.Lerp(eye.localPosition, Vector3.Lerp(_eyeRest, lyingPos, lie), 12f * Time.deltaTime);
            eye.localRotation = Quaternion.Slerp(eye.localRotation, Quaternion.Slerp(Quaternion.Euler(_pitch, 0, 0), lyingRot, lie), 10f * Time.deltaTime);
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
