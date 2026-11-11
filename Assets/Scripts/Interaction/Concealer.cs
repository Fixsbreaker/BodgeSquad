using BodgeSquad.Job;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // Furniture or a picture that can hide a defect for free. Hold it, look at a defect, press
    // Interact: it snaps in front of the defect, back to the wall (pictures hang on the wall).
    // The inspector then judges the defect as "hidden behind furniture" if it actually covers it.
    // Local +Z is the furniture's front.
    [RequireComponent(typeof(Carryable))]
    public class Concealer : MonoBehaviour
    {
        [SerializeField] string displayName = "Wardrobe";
        [SerializeField] bool wallHung;   // pictures: hang at the defect's height

        public string DisplayName => displayName;
        public bool WallHung => wallHung;

        // Returns true if, once placed, it really covers the defect.
        public bool PlaceAgainst(Defect d)
        {
            var rb = GetComponent<Rigidbody>();
            var f = d.transform.forward; f.y = 0f; f.Normalize();
            Vector3 dp = d.transform.position;

            rb.isKinematic = true; // move it without physics fighting back
            transform.rotation = Quaternion.LookRotation(f);
            transform.position = new Vector3(dp.x, transform.position.y, dp.z);
            Physics.SyncTransforms();

            var b = WorldBounds();
            float centerAlong = Vector3.Dot(b.center - dp, f);
            float extentAlong = Mathf.Abs(f.x) * b.extents.x + Mathf.Abs(f.z) * b.extents.z;
            Vector3 lateral = b.center - dp; lateral -= f * Vector3.Dot(lateral, f); lateral.y = 0f;
            Vector3 pos = transform.position + f * (0.04f + extentAlong - centerAlong) - lateral;
            pos.y = wallHung
                ? transform.position.y + (dp.y - b.center.y)   // picture centred on the damage
                : transform.position.y - b.min.y + 0.005f;     // furniture stands on the floor
            transform.position = pos;
            rb.position = pos;
            rb.rotation = transform.rotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = wallHung; // pictures stay on the wall until someone takes them down
            Physics.SyncTransforms();

            bool covers = d.IsBehindFurniture();
            Debug.Log($"[Concealer] {displayName} placed in front of {d.DisplayName} → {(covers ? "covers it" : "too small to cover it")}");
            return covers;
        }

        Bounds WorldBounds()
        {
            var cols = GetComponentsInChildren<Collider>();
            var b = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (var c in cols)
            {
                if (c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            return b;
        }
    }
}
