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
        float _downUntil, _downStart = -99f, _getUp, _downFor = 1f;
        bool _wasSlippery;
        int _paintySteps;
        float _stepDist;
        bool _footLeft;
        public bool IsDown => Time.time < _downUntil;
        const float GetUpTime = 0.8f;

        public void KnockDown(Vector3 shove, float seconds)
        {
            _downStart = Time.time;
            _downFor = seconds;
            _downUntil = Time.time + seconds;
            _planarVelocity = new Vector3(shove.x, 0, shove.z);
            _verticalVelocity = Mathf.Max(_verticalVelocity, 3.2f); // feet go up first
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
            // wet paint: like ice, and hitting it with any pace puts you on your back
            var spills = BodgeSquad.Job.Spills.Instance;
            bool slippery = _cc.isGrounded && spills != null && spills.IsSlippery(transform.position);
            if (slippery)
            {
                _paintySteps = 10; // you'll leave footprints for a while
                float speedNow = new Vector3(_planarVelocity.x, 0, _planarVelocity.z).magnitude;
                if (!_wasSlippery && speedNow > 2.0f)
                {
                    _wasSlippery = true;
                    var dir = _planarVelocity.normalized;
                    GetComponent<NetworkBodger>()?.KnockDown(dir * Mathf.Max(speedNow * 1.4f, 4f), "WHOOPS! Slipped in the paint!");
                    return;
                }
                rate = 1.5f;
                target *= 1.25f; // and you skate faster than you meant to
            }
            _wasSlippery = slippery;
            if (!slippery) Footprints();
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

        // Painty boots: a trail of prints after you've been through a puddle
        void Footprints()
        {
            if (_paintySteps <= 0 || !_cc.isGrounded) return;
            _stepDist += new Vector3(_planarVelocity.x, 0, _planarVelocity.z).magnitude * Time.deltaTime;
            if (_stepDist < 0.5f) return;
            _stepDist = 0f;
            _footLeft = !_footLeft;
            var from = transform.position + transform.right * (_footLeft ? -0.12f : 0.12f) + Vector3.up * 0.3f;
            if (!Physics.Raycast(from, Vector3.down, out var hit, 0.8f, ~((1 << NetworkBodger.LocalBodyLayer) | (1 << 2)), QueryTriggerInteraction.Ignore)) return;
            _paintySteps--;
            BodgeSquad.Job.SurfaceMarks.Instance?.Place(BodgeSquad.Job.KitType.PaintTin, hit, 0.17f, transform.eulerAngles.y, wet: false);
        }

        // Down: feet fly up, you hit the floor, slide on your back through the paint, then climb up again
        void Sprawl()
        {
            bool down = IsDown;
            if (down) _getUp = GetUpTime; else _getUp = Mathf.Max(0f, _getUp - Time.deltaTime);
            var spills = BodgeSquad.Job.Spills.Instance;
            bool wet = spills != null && spills.IsSlippery(transform.position);
            if (wet) _paintySteps = 10;
            float friction = !down ? 20f : wet ? 1.2f : 4f;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, Vector3.zero, friction * Time.deltaTime);
            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;
            _cc.Move((_planarVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime);

            float t = Time.time - _downStart;
            Vector3 pos; Quaternion rot;
            if (down && t < 0.3f)
            {
                // whoosh: legs go out, view flips up to the ceiling line
                float u = t / 0.3f;
                pos = Vector3.Lerp(_eyeRest, new Vector3(0, 0.9f, -0.3f), u);
                rot = Quaternion.Euler(Mathf.Lerp(_pitch, -55f, u), 0, Mathf.Lerp(0, 20f, u));
            }
            else if (down)
            {
                // flat on the floor, a little bounce on landing, the room on its side
                float bounce = Mathf.Exp(-(t - 0.3f) * 9f) * Mathf.Sin((t - 0.3f) * 30f) * 0.06f;
                pos = new Vector3(0.2f, 0.25f + bounce, -0.5f);
                rot = Quaternion.Euler(-10f + bounce * 60f, Mathf.Sin(t * 1.3f) * 4f, 78f);
            }
            else
            {
                // getting up: wobbly, back to normal
                float u = 1f - _getUp / GetUpTime;
                float sway = Mathf.Sin(u * Mathf.PI * 3f) * (1f - u) * 12f;
                pos = Vector3.Lerp(new Vector3(0.2f, 0.25f, -0.5f), _eyeRest, Mathf.SmoothStep(0, 1, u));
                rot = Quaternion.Slerp(Quaternion.Euler(-10f, 0, 78f), Quaternion.Euler(_pitch, 0, 0), Mathf.SmoothStep(0, 1, u)) * Quaternion.Euler(0, 0, sway);
            }
            eye.localPosition = Vector3.Lerp(eye.localPosition, pos, 18f * Time.deltaTime);
            eye.localRotation = Quaternion.Slerp(eye.localRotation, rot, 16f * Time.deltaTime);
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
