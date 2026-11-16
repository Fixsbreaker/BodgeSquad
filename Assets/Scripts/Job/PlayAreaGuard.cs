using BodgeSquad.Interaction;
using BodgeSquad.Players;
using UnityEngine;

namespace BodgeSquad.Job
{
    // Safety net for the house: anyone (or anything) that ends up outside the room —
    // jumped off a wardrobe, clipped a wall, thrown too hard — is put back inside.
    // Each machine guards what it simulates: its own bodger and the items it owns.
    public class PlayAreaGuard : MonoBehaviour
    {
        [SerializeField] Vector3 halfExtents = new(5.9f, 3.2f, 4.9f); // inside the walls, below the ceiling
        [SerializeField] float floorY = -1f;

        float _nextItemCheck;

        void Start()
        {
            // the invisible lid must not catch paint strokes or block the view rays
            var lid = GameObject.Find("Ceiling_Invisible");
            if (lid != null) lid.layer = 2; // Ignore Raycast
        }

        bool Outside(Vector3 p) =>
            p.y < floorY || p.y > halfExtents.y || Mathf.Abs(p.x) > halfExtents.x || Mathf.Abs(p.z) > halfExtents.z;

        void Update()
        {
            var me = NetworkBodger.Local;
            if (me != null && Outside(me.transform.position))
            {
                me.GoToSpawn();
                Debug.Log($"[PlayArea] P{me.Index + 1} left the house — put back at spawn");
            }

            if (Time.time < _nextItemCheck) return;
            _nextItemCheck = Time.time + 0.5f;
            foreach (var c in FindObjectsByType<Carryable>())
            {
                if (!c.IsSpawned || !c.IsOwner || c.Holder != null || !Outside(c.transform.position)) continue;
                c.Warp(c.HomePosition, c.HomeRotation);
                Debug.Log($"[PlayArea] {c.name} fell out — put back");
            }
        }
    }
}
