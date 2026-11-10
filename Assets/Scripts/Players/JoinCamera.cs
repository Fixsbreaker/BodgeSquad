using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.Players
{
    // The overview camera shown before anyone joins. Turns itself (and its AudioListener) off
    // as soon as the first bodger joins, and back on if everyone leaves.
    [RequireComponent(typeof(Camera))]
    public class JoinCamera : MonoBehaviour
    {
        [SerializeField] PlayerInputManager manager;

        Camera _cam;
        AudioListener _listener;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _listener = GetComponent<AudioListener>();
        }

        void OnEnable()
        {
            manager.onPlayerJoined += OnChanged;
            manager.onPlayerLeft += OnChanged;
            Refresh();
        }

        void OnDisable()
        {
            manager.onPlayerJoined -= OnChanged;
            manager.onPlayerLeft -= OnChanged;
        }

        void OnChanged(PlayerInput _) => Refresh();

        void Refresh()
        {
            bool nobody = manager.playerCount == 0;
            _cam.enabled = nobody;
            if (_listener != null) _listener.enabled = nobody;
        }
    }
}
