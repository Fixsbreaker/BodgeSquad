using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.Players
{
    // Moves each newly joined bodger onto its spawn point (child N of spawnRoot).
    public class SpawnOnJoin : MonoBehaviour
    {
        [SerializeField] PlayerInputManager manager;
        [SerializeField] Transform spawnRoot;

        void OnEnable() => manager.onPlayerJoined += OnJoined;
        void OnDisable() => manager.onPlayerJoined -= OnJoined;

        void OnJoined(PlayerInput player)
        {
            if (spawnRoot == null || spawnRoot.childCount == 0) return;
            var sp = spawnRoot.GetChild(player.playerIndex % spawnRoot.childCount);
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false; // CharacterController overrides teleports while enabled
            player.transform.SetPositionAndRotation(sp.position, sp.rotation);
            if (cc != null) cc.enabled = true;
        }
    }
}
