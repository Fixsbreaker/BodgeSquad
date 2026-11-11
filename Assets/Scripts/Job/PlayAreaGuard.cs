using System.Collections.Generic;
using BodgeSquad.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.Job
{
    // Safety net for the house: anyone (or anything) that ends up outside the room —
    // jumped off a wardrobe, clipped a wall, thrown too hard — is put back inside.
    public class PlayAreaGuard : MonoBehaviour
    {
        [SerializeField] Vector3 halfExtents = new(5.9f, 3.2f, 4.9f); // inside the walls, below the ceiling
        [SerializeField] float floorY = -1f;
        [SerializeField] Transform spawnRoot;

        readonly Dictionary<Carryable, Vector3> _home = new();
        float _nextItemCheck;

        void Start()
        {
            foreach (var c in FindObjectsByType<Carryable>()) _home[c] = c.transform.position;
        }

        bool Outside(Vector3 p) =>
            p.y < floorY || p.y > halfExtents.y || Mathf.Abs(p.x) > halfExtents.x || Mathf.Abs(p.z) > halfExtents.z;

        void Update()
        {
            foreach (var player in PlayerInput.all)
            {
                if (!Outside(player.transform.position)) continue;
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                var sp = spawnRoot != null && spawnRoot.childCount > 0 ? spawnRoot.GetChild(player.playerIndex % spawnRoot.childCount) : null;
                player.transform.SetPositionAndRotation(sp != null ? sp.position : Vector3.zero, sp != null ? sp.rotation : Quaternion.identity);
                if (cc != null) cc.enabled = true;
                Debug.Log($"[PlayArea] P{player.playerIndex + 1} left the house — put back at spawn");
            }

            if (Time.time < _nextItemCheck) return;
            _nextItemCheck = Time.time + 0.5f;
            foreach (var c in FindObjectsByType<Carryable>())
            {
                if (c.IsHeld || !Outside(c.transform.position)) continue;
                var home = _home.TryGetValue(c, out var h) ? h : new Vector3(0f, 1f, 0f);
                c.Body.linearVelocity = Vector3.zero;
                c.Body.angularVelocity = Vector3.zero;
                c.Body.position = home;
                c.transform.position = home;
                Debug.Log($"[PlayArea] {c.name} fell out — put back");
            }
        }
    }
}
