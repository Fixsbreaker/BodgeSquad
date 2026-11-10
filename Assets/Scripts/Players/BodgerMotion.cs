using UnityEngine;

namespace BodgeSquad.Players
{
    // Procedural motion for the primitive bodger seen by the *other* players:
    // gait bob + weight-shift roll while walking, lean into acceleration,
    // and a slow breathe when idle (never statuary).
    public class BodgerMotion : MonoBehaviour
    {
        [SerializeField] FirstPersonController controller;
        [SerializeField] float stepFrequency = 1.9f;   // strides per second at walk speed
        [SerializeField] float bobAmplitude = 0.05f;
        [SerializeField] float rollDegrees = 3f;
        [SerializeField] float leanDegrees = 8f;
        [SerializeField] float breatheAmplitude = 0.012f;
        [SerializeField] float breatheFrequency = 0.35f;

        Vector3 _rest;
        float _phase, _gait, _lean;
        Vector3 _lastVel;

        void Awake() => _rest = transform.localPosition;

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float speedN = controller.IsGrounded ? controller.NormalizedSpeed : 0f;
            _gait = Mathf.MoveTowards(_gait, speedN, dt / 0.3f); // settle, never freeze
            _phase += dt * stepFrequency * Mathf.Max(speedN, 0.25f) * Mathf.PI * 2f;

            Vector3 vel = controller.Velocity; vel.y = 0;
            float accel = Vector3.Dot((vel - _lastVel) / dt, controller.transform.forward);
            _lastVel = vel;
            _lean = Mathf.Lerp(_lean, Mathf.Clamp(accel / 20f, -1f, 1f) * leanDegrees, dt * 6f);

            float bob = Mathf.Abs(Mathf.Sin(_phase)) * bobAmplitude * _gait;
            float breathe = Mathf.Sin(Time.time * breatheFrequency * Mathf.PI * 2f) * breatheAmplitude * (1f - _gait);
            float roll = Mathf.Sin(_phase) * rollDegrees * _gait;

            transform.localPosition = _rest + Vector3.up * (bob + breathe);
            transform.localRotation = Quaternion.Euler(_lean, 0f, roll);
        }
    }
}
