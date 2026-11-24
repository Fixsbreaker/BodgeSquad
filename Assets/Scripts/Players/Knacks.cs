using BodgeSquad.Interaction;
using BodgeSquad.Job;
using UnityEngine;

namespace BodgeSquad.Players
{
    // Nobody has a fixed job: anyone can paint, fix, carry or distract. But when the inspector first
    // walks in, every bodger is dealt one random Knack for the rest of the job — a small, unique trick
    // that makes them the right person for one kind of moment.
    public enum Knack { None = -1, SmoothTalker = 0, SpeedPainter = 1, Strongback = 2, Lookout = 3 }

    public static class Knacks
    {
        public const int Count = 4;
        public const float TalkSeconds = 5f, TalkCooldown = 45f, TalkRange = 3.5f;
        public const float PaintSeconds = 30f, PaintCooldown = 60f;

        public static string Name(Knack k) => k switch
        {
            Knack.SmoothTalker => "Smooth Talker",
            Knack.SpeedPainter => "Speed Painter",
            Knack.Strongback => "Strongback",
            Knack.Lookout => "Lookout",
            _ => "",
        };

        public static string Blurb(Knack k) => k switch
        {
            Knack.SmoothTalker => $"get close to the inspector and keep him chatting for {TalkSeconds:0}s (he sees nothing)",
            Knack.SpeedPainter => $"your tools work twice as fast and last twice as long for {PaintSeconds:0}s",
            Knack.Strongback => "heavy furniture is light for you, and you throw harder",
            Knack.Lookout => "you always see where the inspector is, even through walls",
            _ => "",
        };

        public static bool IsActive(Knack k) => k is Knack.SmoothTalker or Knack.SpeedPainter;
    }

    // The local player's side of their Knack: the button, timers and the passive bonuses.
    // Added at runtime to the owner's bodger (see NetworkBodger).
    public class KnackAbility : MonoBehaviour
    {
        NetworkBodger _bodger;
        BodgerInput _input;
        CarryInteractor _carry;
        float _readyAt, _activeUntil;

        public Knack Current => _bodger != null ? _bodger.CurrentKnack : Knack.None;
        public bool SpeedPainting => Current == Knack.SpeedPainter && Time.time < _activeUntil;
        public bool Strong => Current == Knack.Strongback;
        public float ReadyIn => Mathf.Max(0f, _readyAt - Time.time);
        public float ActiveFor => Mathf.Max(0f, _activeUntil - Time.time);

        void Awake()
        {
            _bodger = GetComponent<NetworkBodger>();
            _input = GetComponent<BodgerInput>();
            _carry = GetComponent<CarryInteractor>();
        }

        // A new knack (or none, for a new job): fresh timers
        public void ResetTimers() { _readyAt = 0f; _activeUntil = 0f; }

        void Update()
        {
            if (_input == null || !_input.Ready || !_input.enabled) return;
            if (_input.KnackPressed()) TryUse();
        }

        public bool TryUse()
        {
            var k = Current;
            if (!Knacks.IsActive(k)) return false;
            if (Time.time < _readyAt) { _carry?.Flash($"{Knacks.Name(k)} is ready in {Mathf.CeilToInt(ReadyIn)}s"); return false; }
            if (k == Knack.SmoothTalker)
            {
                var insp = Inspector.Instance;
                if (insp == null || !insp.Watching) { _carry?.Flash("Nobody to talk to right now"); return false; }
                if (Vector3.Distance(insp.transform.position, transform.position) > Knacks.TalkRange) { _carry?.Flash("Get closer to the inspector to start chatting"); return false; }
                insp.SmoothTalkRpc();
                _readyAt = Time.time + Knacks.TalkCooldown;
                return true;
            }
            // Speed Painter
            _activeUntil = Time.time + Knacks.PaintSeconds;
            _readyAt = Time.time + Knacks.PaintCooldown;
            _carry?.Flash("SPEED PAINTER! Tools twice as fast for 30s");
            return true;
        }
    }
}
