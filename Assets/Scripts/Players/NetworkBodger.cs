using System.Collections.Generic;
using BodgeSquad.Art;
using BodgeSquad.Interaction;
using BodgeSquad.UI;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Players
{
    // One bodger in the online session. Every machine has a copy of every bodger;
    // only the owner's copy gets the camera, input, HUD and audio listener.
    // The host hands out a slot (0-3) that picks the hat, the tint and the spawn point.
    public class NetworkBodger : NetworkBehaviour
    {
        public const int LocalBodyLayer = 8; // "Player1": the local player's own body, hidden from its own camera

        [SerializeField] GameObject cameraRig;          // PlayerCamera (Camera + CinemachineBrain + AudioListener)
        [SerializeField] CinemachineCamera vcam;
        [SerializeField] Transform visualRoot;
        [SerializeField] Renderer[] tintedRenderers;
        [SerializeField] GameObject[] hatVariants;
        [SerializeField] Behaviour[] ownerOnly;         // FirstPersonController, BodgerInput, PlayerHud

        public readonly NetworkVariable<int> Slot = new(-1);
        // Flat on the floor (slipped in paint, hit by flying junk). Written by the owner, drawn everywhere.
        readonly NetworkVariable<bool> _down = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public bool IsDown => _down.Value;
        float _tip, _tipVel;
        float _downUntil;

        public static NetworkBodger Local { get; private set; }
        public static readonly List<NetworkBodger> All = new();
        public static event System.Action LocalSpawned;

        public int Index => Mathf.Max(0, Slot.Value);

        void Awake()
        {
            // Off until we know whose bodger this is
            foreach (var b in ownerOnly) if (b != null) b.enabled = false;
            if (cameraRig != null) cameraRig.SetActive(false);
            if (vcam != null) vcam.gameObject.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            if (IsServer) Slot.Value = FreeSlot();
            Slot.OnValueChanged += (_, _) => ApplySlot();
            ApplySlot();

            if (!IsOwner) return;
            Local = this;
            foreach (var b in ownerOnly) if (b != null) b.enabled = true;
            cameraRig.SetActive(true);
            vcam.gameObject.SetActive(true);
            SetLayer(transform, LocalBodyLayer); // the whole body incl. its capsule: never in the way of our own rays
            cameraRig.GetComponent<Camera>().cullingMask &= ~(1 << LocalBodyLayer);
            GetComponent<CarryInteractor>().SetIgnoredLayer(LocalBodyLayer);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (Slot.Value >= 0) GoToSpawn();
            LocalSpawned?.Invoke();
            Debug.Log($"[Net] Local bodger spawned (client {OwnerClientId})");
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this) Local = null;
        }

        int FreeSlot()
        {
            for (int s = 0; s < 4; s++)
            {
                bool taken = false;
                foreach (var b in All) if (b != this && b.Slot.Value == s) taken = true;
                if (!taken) return s;
            }
            return 0;
        }

        bool _placed;
        void ApplySlot()
        {
            int i = Index;
            name = $"Bodger_P{i + 1}";
            for (int h = 0; h < hatVariants.Length; h++)
                if (hatVariants[h] != null) hatVariants[h].SetActive(h == i % hatVariants.Length);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", Palette.PlayerTints[i % Palette.PlayerTints.Length]);
            foreach (var r in tintedRenderers) if (r != null) r.SetPropertyBlock(block);
            if (IsOwner && Slot.Value >= 0 && !_placed) GoToSpawn();
            Debug.Log($"[Net] Client {OwnerClientId} is P{i + 1}");
        }

        public void GoToSpawn()
        {
            _placed = true;
            if (IsOwner && _down.Value) { _down.Value = false; _downUntil = 0f; }
            var root = GameObject.Find("SpawnPoints");
            if (root == null || root.transform.childCount == 0) return;
            var sp = root.transform.GetChild(Index % root.transform.childCount);
            GetComponent<FirstPersonController>().Teleport(sp.position, sp.rotation);
        }

        // Anyone → owner: you've been hit by something / slipped
        [Rpc(SendTo.Owner)]
        public void KnockDownRpc(Vector3 shove, string why) => KnockDown(shove, why);

        public void KnockDown(Vector3 shove, string why)
        {
            if (!IsOwner || _down.Value) return;
            _down.Value = true;
            _downUntil = Time.time + 2.6f;
            GetComponent<CarryInteractor>().Fumble();
            GetComponent<CarryInteractor>().Flash(why);
            GetComponent<FirstPersonController>().KnockDown(shove, 2.6f);
            Debug.Log($"[Bodger] P{Index + 1} down: {why}");
        }

        void Update()
        {
            if (IsOwner && _down.Value && Time.time >= _downUntil) _down.Value = false;
            // a springy topple backwards, pivoting on the feet; and back up again
            float target = _down.Value ? -92f : 0f;
            float k = 160f, damp = 14f;
            _tipVel += ((target - _tip) * k - _tipVel * damp) * Time.deltaTime;
            _tip += _tipVel * Time.deltaTime;
            if (visualRoot != null) visualRoot.localRotation = Quaternion.Euler(_tip, 0, _tip * -0.15f);
        }

        // Host → this bodger's owner: back to the start (new job)
        [Rpc(SendTo.Owner)]
        public void ResetToSpawnRpc() => GoToSpawn();

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }
    }
}
