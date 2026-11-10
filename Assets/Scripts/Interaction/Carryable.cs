using UnityEngine;

namespace BodgeSquad.Interaction
{
    // Anything a bodger can pick up, carry and throw (materials, tins, planks, furniture).
    [RequireComponent(typeof(Rigidbody))]
    public class Carryable : MonoBehaviour
    {
        [Tooltip("Heavier items lag behind the hold point and fly shorter.")]
        [SerializeField] float carryWeight = 1f;

        public Rigidbody Body { get; private set; }
        public float CarryWeight => carryWeight;
        public Transform Holder { get; internal set; }
        public bool IsHeld => Holder != null;

        void Awake() => Body = GetComponent<Rigidbody>();
    }
}
