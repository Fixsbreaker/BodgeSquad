using BodgeSquad.Art;
using BodgeSquad.Interaction;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.Players
{
    // Wires one joined player into its split-screen slot:
    //  - its Cinemachine camera and brain share a private output channel,
    //  - its own body sits on layer "PlayerN" that only *its* camera hides,
    //  - its hard hat gets the player's tint, only P1 keeps the AudioListener.
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerSlot : MonoBehaviour
    {
        [SerializeField] Camera playerCamera;
        [SerializeField] CinemachineBrain brain;
        [SerializeField] CinemachineCamera vcam;
        [SerializeField] Renderer[] tintedRenderers;   // every hat variant's tinted parts
        [SerializeField] GameObject[] hatVariants;     // hard hat, cap, beanie, goggles+bun — one per player
        [SerializeField] Transform visualRoot;

        public int Index { get; private set; }

        void Start() // PlayerInput assigns playerIndex/control scheme on enable, so read them here
        {
            var input = GetComponent<PlayerInput>();
            Index = Mathf.Clamp(input.playerIndex, 0, 3);
            name = $"Bodger_P{Index + 1}";

            // Cinemachine: Channel01..Channel04, one per player
            var channel = (OutputChannels)((int)OutputChannels.Channel01 << Index);
            vcam.OutputChannel = channel;
            brain.ChannelMask = channel;

            // Hide own body from own camera, keep it visible to the others
            int layer = LayerMask.NameToLayer($"Player{Index + 1}");
            if (layer >= 0)
            {
                SetLayer(transform, layer);
                playerCamera.cullingMask &= ~(1 << layer);
                GetComponent<CarryInteractor>()?.SetIgnoredLayer(layer);
            }

            // Signature hat + player tint
            for (int i = 0; i < hatVariants.Length; i++)
                if (hatVariants[i] != null) hatVariants[i].SetActive(i == Index % hatVariants.Length);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", Palette.PlayerTints[Index]);
            foreach (var r in tintedRenderers) r.SetPropertyBlock(block);

            // One AudioListener in the scene
            var listener = playerCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = Index == 0;

            // Lock the cursor for the keyboard & mouse player
            if (input.currentControlScheme == "Keyboard&Mouse")
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            Debug.Log($"[PlayerSlot] Joined P{Index + 1} with {input.currentControlScheme} on {channel}");
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }
    }
}
