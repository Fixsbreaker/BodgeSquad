using UnityEngine;

namespace BodgeSquad.Players
{
    // The overview camera behind the main menu. Turns itself (and its AudioListener) off
    // while this machine has its own bodger in the game.
    [RequireComponent(typeof(Camera))]
    public class JoinCamera : MonoBehaviour
    {
        Camera _cam;
        AudioListener _listener;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _listener = GetComponent<AudioListener>();
        }

        void LateUpdate()
        {
            bool nobody = NetworkBodger.Local == null;
            if (_cam.enabled != nobody) _cam.enabled = nobody;
            if (_listener != null && _listener.enabled != nobody) _listener.enabled = nobody;
        }
    }
}
