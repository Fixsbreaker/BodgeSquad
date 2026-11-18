using System;
using System.Collections.Generic;
using BodgeSquad.Interaction;
using BodgeSquad.Players;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.Job
{
    public enum JobPhase { Renovation, Inspection, Results }

    // One job = one house: shared budget, renovation timer, then the inspector, then the payout.
    // The host runs it; every client sees the same numbers through network variables.
    public class JobManager : NetworkBehaviour
    {
        public static JobManager Instance { get; private set; }

        [SerializeField] Inspector inspector;
        [SerializeField] int randomSeed = 0; // 0 = new luck every job

        readonly NetworkVariable<int> _phase = new((int)JobPhase.Renovation);
        readonly NetworkVariable<int> _budget = new(RepairRules.StartingBudget);
        readonly NetworkVariable<int> _spent = new(0);
        readonly NetworkVariable<float> _timeLeft = new(RepairRules.RenovationSeconds);
        readonly NetworkVariable<int> _fines = new(0);

        public JobPhase Phase => (JobPhase)_phase.Value;
        public int Budget => _budget.Value;
        public int Spent => _spent.Value;
        public float TimeLeft => _timeLeft.Value;
        public float Elapsed => RepairRules.RenovationSeconds - _timeLeft.Value;
        public int Fines => _fines.Value;
        public Inspector Inspector => inspector;

        // Host only: caught red-handed
        public void AddFine(int amount) { if (IsServer) _fines.Value += amount; }
        public IReadOnlyList<Defect> Defects => _defects;
        public System.Random Luck { get; private set; }

        public event Action<JobPhase> PhaseChanged;
        public event Action<string> Announce;   // shared feed line ("Inspector taps the north wall…")

        readonly List<Defect> _defects = new();
        float _timeSync;

        void Awake()
        {
            Instance = this;
            // paint/tape marks live on the same network object (added before it spawns)
            if (!TryGetComponent<SurfaceMarks>(out _)) gameObject.AddComponent<SurfaceMarks>();
            if (!TryGetComponent<Spills>(out _)) gameObject.AddComponent<Spills>();
            NewLuck();
        }

        void NewLuck() => Luck = new System.Random(randomSeed != 0 ? randomSeed : Environment.TickCount);

        public override void OnNetworkSpawn()
        {
            if (IsServer) NetworkManager.OnClientDisconnectCallback += FreeItemsOf;
            _phase.OnValueChanged += (_, now) =>
            {
                Debug.Log($"[Job] Phase → {(JobPhase)now}");
                PhaseChanged?.Invoke((JobPhase)now);
            };
        }

        // Host: whatever a player was carrying when they left can be picked up again
        void FreeItemsOf(ulong clientId)
        {
            foreach (var c in FindObjectsByType<Carryable>())
                if (c.IsSpawned && c.HolderClient.Value == clientId) c.HolderClient.Value = Carryable.NoHolder;
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= FreeItemsOf;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public void Register(Defect d)
        {
            if (!_defects.Contains(d)) _defects.Add(d);
            _defects.Sort((a, b) => string.CompareOrdinal(a.name, b.name)); // same order on every machine
        }

        // Host only
        public bool TrySpend(int amount)
        {
            if (!IsServer || Phase != JobPhase.Renovation || amount > Budget) return false;
            _budget.Value -= amount;
            _spent.Value += amount;
            return true;
        }

        // Host only: everyone sees the line
        public void Say(string line)
        {
            if (IsServer) SayRpc(line);
        }

        [Rpc(SendTo.Everyone)]
        void SayRpc(string line)
        {
            Debug.Log($"[Job] {line}");
            Announce?.Invoke(line);
        }

        [Rpc(SendTo.Server)]
        public void RingDoorbellRpc(int slot)
        {
            if (Phase != JobPhase.Renovation) return;
            Say($"P{slot + 1} rang the doorbell!");
            CallInspector();
        }

        void CallInspector()
        {
            if (Phase != JobPhase.Renovation) return;
            _phase.Value = (int)JobPhase.Inspection;
            Say("Ding-dong! The inspector has arrived.");
            inspector.Begin(_defects, () => _phase.Value = (int)JobPhase.Results);
        }

        void Update()
        {
            if (!IsSpawned || !IsServer) return;
            if (Phase == JobPhase.Renovation)
            {
                _timeSync += Time.deltaTime;
                if (_timeSync >= 0.25f) // a few updates a second is plenty for a clock
                {
                    _timeLeft.Value = Mathf.Max(0f, _timeLeft.Value - _timeSync);
                    _timeSync = 0f;
                }
                if (_timeLeft.Value <= 0f) { Say("Time's up!"); CallInspector(); }
            }
            else if (Phase == JobPhase.Results && RestartPressed())
            {
                NewJob();
            }
        }

        static bool RestartPressed()
        {
            if (BodgerInput.Blocked) return false;
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) return true;
            foreach (var pad in Gamepad.all)
                if (pad.startButton.wasPressedThisFrame) return true;
            return false;
        }

        // Host: reset the same house for another go
        void NewJob()
        {
            NewLuck();
            inspector.ResetInspector();
            foreach (var d in _defects) d.ResetDefect();
            foreach (var kit in FindObjectsByType<RepairKit>())
                if (kit.IsSpawned) kit.NetworkObject.Despawn(true);
            foreach (var shelf in FindObjectsByType<SupplyShelf>()) shelf.ResetStock();
            foreach (var c in FindObjectsByType<Carryable>())
            {
                if (!c.IsSpawned) continue;
                c.HolderClient.Value = Carryable.NoHolder;
                c.SendHomeRpc();
            }
            foreach (var b in NetworkBodger.All) b.ResetToSpawnRpc();
            _budget.Value = RepairRules.StartingBudget;
            _spent.Value = 0;
            _fines.Value = 0;
            _timeLeft.Value = RepairRules.RenovationSeconds;
            _phase.Value = (int)JobPhase.Renovation;
            ClearMarkersRpc();
            SurfaceMarks.Instance?.ClearAll();
            Spills.Instance?.ClearAll();
            Say("New job! Same house, same budget, fresh luck.");
        }

        [Rpc(SendTo.Everyone)]
        void ClearMarkersRpc()
        {
            foreach (var m in FindObjectsByType<InspectionMarker>()) Destroy(m.gameObject);
        }

        // ---- payout (computed from synced defect state, same on every machine) ----------------
        public int Penalties
        {
            get { int sum = 0; foreach (var d in _defects) if (d.Caught) sum += RepairRules.Penalty(d.Severity); return sum; }
        }
        public int Payout => Mathf.Max(0, RepairRules.ContractFee - Penalties - Fines);
        public int Profit => Payout - Spent;
        public int CaughtCount { get { int n = 0; foreach (var d in _defects) if (d.Caught) n++; return n; } }
    }
}
