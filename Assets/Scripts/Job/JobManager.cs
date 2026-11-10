using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BodgeSquad.Job
{
    public enum JobPhase { Renovation, Inspection, Results }

    // One job = one house: shared budget, renovation timer, then the inspector, then the payout.
    public class JobManager : MonoBehaviour
    {
        public static JobManager Instance { get; private set; }

        [SerializeField] Inspector inspector;
        [SerializeField] int randomSeed = 0; // 0 = new luck every job

        public JobPhase Phase { get; private set; } = JobPhase.Renovation;
        public int Budget { get; private set; } = RepairRules.StartingBudget;
        public int Spent { get; private set; }
        public float TimeLeft { get; private set; } = RepairRules.RenovationSeconds;
        public IReadOnlyList<Defect> Defects => _defects;
        public System.Random Luck { get; private set; }

        public event Action<JobPhase> PhaseChanged;
        public event Action<string> Announce;   // shared feed line ("Inspector taps the north wall…")
        public event Action BudgetChanged;

        readonly List<Defect> _defects = new();

        void Awake()
        {
            Instance = this;
            Luck = new System.Random(randomSeed != 0 ? randomSeed : Environment.TickCount);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Register(Defect d) { if (!_defects.Contains(d)) _defects.Add(d); }

        public bool TrySpend(int amount)
        {
            if (Phase != JobPhase.Renovation || amount > Budget) return false;
            Budget -= amount;
            Spent += amount;
            BudgetChanged?.Invoke();
            return true;
        }

        public void Say(string line)
        {
            Debug.Log($"[Job] {line}");
            Announce?.Invoke(line);
        }

        public void CallInspector()
        {
            if (Phase != JobPhase.Renovation) return;
            SetPhase(JobPhase.Inspection);
            Say("Ding-dong! The inspector has arrived.");
            inspector.Begin(_defects, () => SetPhase(JobPhase.Results));
        }

        void Update()
        {
            if (Phase == JobPhase.Renovation)
            {
                TimeLeft -= Time.deltaTime;
                if (TimeLeft <= 0f) { TimeLeft = 0f; Say("Time's up!"); CallInspector(); }
            }
            else if (Phase == JobPhase.Results && RestartPressed())
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        static bool RestartPressed()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) return true;
            foreach (var pad in Gamepad.all)
                if (pad.startButton.wasPressedThisFrame) return true;
            return false;
        }

        void SetPhase(JobPhase p)
        {
            Phase = p;
            Debug.Log($"[Job] Phase → {p}");
            PhaseChanged?.Invoke(p);
        }

        // ---- payout -------------------------------------------------------------------------
        public int Penalties
        {
            get { int sum = 0; foreach (var d in _defects) if (d.Caught) sum += RepairRules.Penalty(d.Severity); return sum; }
        }
        public int Payout => Mathf.Max(0, RepairRules.ContractFee - Penalties);
        public int Profit => Payout - Spent;
        public int CaughtCount { get { int n = 0; foreach (var d in _defects) if (d.Caught) n++; return n; } }
    }
}
