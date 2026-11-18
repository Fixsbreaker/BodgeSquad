using System;
using System.Collections;
using System.Collections.Generic;
using BodgeSquad.Art;
using BodgeSquad.Interaction;
using BodgeSquad.Players;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Job
{
    // The inspector. He doesn't wait politely for the doorbell any more:
    //  • Renovation: he lets himself in early "for a quick look around", wanders the room, peers at
    //    walls, steps out for phone calls and comes back. If he SEES someone painting, papering or
    //    taping (a vision cone on the floor shows where he looks), his suspicion climbs; when it
    //    fills, that player is caught red-handed: a fine, and the defect goes in his notebook.
    //    Distract him: throw something so it crashes (he goes to check the noise) or switch on the
    //    radio in the corner (he storms over to turn it off).
    //  • Inspection (doorbell / time up): the formal walk, defect by defect, as before.
    // Online: the host runs all of it; NetworkTransform moves him, network variables carry his
    // mood, suspicion and the radio to every screen.
    public class Inspector : NetworkBehaviour
    {
        public enum Mode { Away, Patrol, LookAround, Investigate, Radio, Suspicious, Scold, Leaving, Final, Stunned, Blinded, Slipped }

        [SerializeField] Transform body;          // leans/taps
        [SerializeField] Transform entrance;      // where he appears
        [SerializeField] GameObject flagPrefab;
        [SerializeField] GameObject passPrefab;   // small green tick for honest work
        [SerializeField] float walkSpeed = 1.6f;
        [SerializeField] float turnSpeed = 360f;
        [SerializeField] float inspectSeconds = 1.6f;

        public static Inspector Instance { get; private set; }
        public Defect Current { get; private set; }

        public static readonly Vector3 RadioSpot = new(-4.95f, 0f, -4.35f);

        Vector3 _bodyRest;

        readonly NetworkVariable<bool> _visible = new(false);
        readonly NetworkVariable<int> _mode = new((int)Mode.Away);
        readonly NetworkVariable<float> _suspicion = new(0f);
        readonly NetworkVariable<bool> _radioOn = new(false);
        readonly NetworkVariable<float> _nextVisit = new(RepairRules.FirstVisitAt); // job seconds
        Coroutine _run;

        public Mode CurrentMode => (Mode)_mode.Value;
        public float Suspicion => _suspicion.Value;
        public bool Present => _visible.Value;
        public bool RadioOn => _radioOn.Value;
        public float NextVisitAt => _nextVisit.Value;
        public bool Watching => Present && CurrentMode is not (Mode.Final or Mode.Away or Mode.Stunned or Mode.Blinded or Mode.Slipped);

        void Awake()
        {
            Instance = this;
            _bodyRest = body.localPosition;
            ShowBody(false); // objects stay active (they're networked); only the look is hidden
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            IgnoreWork = false; // a static: never carry the trailer's setting into a real game
            _visible.OnValueChanged += (_, v) => ShowBody(v);
            ShowBody(_visible.Value);
            BuildCone();
            BuildIcon();
            BuildDaze();
            // something to hit: thrown junk bounces off him (kinematic, moved by the host / NetworkTransform)
            if (!TryGetComponent<Collider>(out _))
            {
                var cap = gameObject.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0, 0.95f, 0); cap.height = 1.9f; cap.radius = 0.3f;
            }
            if (!TryGetComponent<Rigidbody>(out var rb)) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            _voice = gameObject.AddComponent<AudioSource>();
            _voice.spatialBlend = 0.8f; _voice.minDistance = 2.5f; _voice.maxDistance = 30f; _voice.volume = 0.9f;
            ShowBody(_visible.Value);
            _radio = Radio.Build(RadioSpot, this);
            if (IsServer)
            {
                var args = Environment.GetCommandLineArgs();
                if (Array.IndexOf(args, "-bodgeSmoke") >= 0) _nextVisit.Value = 4f; // automated test: right away
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_radio != null) Destroy(_radio.gameObject);
        }

        void ShowBody(bool on)
        {
            foreach (var r in body.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            if (TryGetComponent<Collider>(out var col)) col.enabled = on;
        }

        // ================================================================ final inspection
        // Host only
        public void Begin(IReadOnlyList<Defect> defects, Action onDone)
        {
            if (!_visible.Value) transform.SetPositionAndRotation(entrance.position, entrance.rotation);
            _visible.Value = true;
            _mode.Value = (int)Mode.Final;
            _suspicion.Value = 0f;
            _radioOn.Value = false;
            body.localPosition = _bodyRest;
            body.localRotation = Quaternion.identity;
            _run = StartCoroutine(Run(new List<Defect>(defects), onDone));
        }

        // Host: new job
        public void ResetInspector()
        {
            if (_run != null) StopCoroutine(_run);
            _run = null;
            Current = null;
            transform.SetPositionAndRotation(entrance.position, entrance.rotation);
            body.localPosition = _bodyRest;
            body.localRotation = Quaternion.identity;
            _visible.Value = false;
            _mode.Value = (int)Mode.Away;
            _suspicion.Value = 0f;
            _radioOn.Value = false;
            _visits = 0;
            _radioTrips = 0;
            _warned = false;
            IgnoreWork = false;
            _work.Clear();
            _nextVisit.Value = Array.IndexOf(Environment.GetCommandLineArgs(), "-bodgeSmoke") >= 0 ? 4f : RepairRules.FirstVisitAt;
        }

        IEnumerator Run(List<Defect> todo, Action onDone)
        {
            var job = JobManager.Instance;
            yield return new WaitForSeconds(0.8f);

            while (todo.Count > 0)
            {
                // nearest next — reads as a person walking the room, not a list
                todo.Sort((a, b) => (a.InspectPoint - transform.position).sqrMagnitude.CompareTo((b.InspectPoint - transform.position).sqrMagnitude));
                var d = todo[0];
                todo.RemoveAt(0);
                Current = d;

                yield return WalkTo(d.InspectPoint);
                yield return Face(d.transform.position);

                var state = d.EffectiveState;
                float risk = d.Obviousness; // from how much is actually covered, and with what
                job.Say(d.Noted && state != RepairState.Fixed
                    ? $"Inspector flips through his notes… \"Ah yes, the {d.Label}. I SAW that.\""
                    : state == RepairState.HiddenByFurniture
                        ? $"Inspector peers behind the furniture at the {d.Label}…"
                        : $"Inspector taps the {d.Label}…");
                yield return Tap();

                bool caught = state != RepairState.Fixed && (d.Noted || job.Luck.NextDouble() < risk);
                d.Inspected = true;
                if (caught)
                {
                    d.Reveal();
                    Plant(flagPrefab, d);
                    string who = d.ResponsiblePlayer >= 0 ? $" — P{d.ResponsiblePlayer + 1}'s handiwork" : "";
                    job.Say(state == RepairState.Untouched
                        ? $"FLAGGED: {d.DisplayName}, not even touched!"
                        : $"FLAGGED: {d.DisplayName}, {RepairRules.StateVerb(state)}{who}! −${RepairRules.Penalty(d.Severity)}");
                }
                else if (state == RepairState.Fixed)
                {
                    Plant(passPrefab, d);
                    job.Say($"Inspector nods at the {d.Label}. Proper job.");
                }
                else
                {
                    job.Say($"Inspector shrugs and moves on…");
                }
                yield return new WaitForSeconds(0.6f);
            }

            Current = null;
            yield return WalkTo(entrance.position);
            job.Say("Inspector is writing up the report.");
            yield return new WaitForSeconds(1f);
            onDone?.Invoke();
        }

        IEnumerator WalkTo(Vector3 target)
        {
            while (!StepTowards(target, walkSpeed)) yield return null;
            body.localPosition = _bodyRest;
            body.localRotation = Quaternion.identity;
        }

        IEnumerator Face(Vector3 point)
        {
            var dir = point - transform.position; dir.y = 0;
            var look = Quaternion.LookRotation(dir);
            while (Quaternion.Angle(transform.rotation, look) > 1f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
                yield return null;
            }
        }

        IEnumerator Tap()
        {
            // lean in, three suspicious taps, lean back
            float t = 0f;
            while (t < inspectSeconds)
            {
                t += Time.deltaTime;
                float lean = Mathf.Sin(Mathf.Clamp01(t / inspectSeconds) * Mathf.PI) * 14f;
                float tap = Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f * 2.2f)) * 3f;
                body.localRotation = Quaternion.Euler(lean + tap, 0, 0);
                yield return null;
            }
            body.localRotation = Quaternion.identity;
        }

        // One step of walking with a little bob; true once there
        float _phase;
        bool StepTowards(Vector3 target, float speed)
        {
            target.y = transform.position.y;
            var dir = target - transform.position;
            if (dir.sqrMagnitude < 0.01f)
            {
                body.localPosition = _bodyRest;
                body.localRotation = Quaternion.identity;
                return true;
            }
            var look = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            _phase += Time.deltaTime * 2f * Mathf.PI * 1.8f * (speed / walkSpeed);
            body.localPosition = _bodyRest + Vector3.up * Mathf.Abs(Mathf.Sin(_phase)) * 0.04f;
            body.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(_phase) * 2.5f);
            return false;
        }

        void Plant(GameObject prefab, Defect d) =>
            PlantRpc(new NetworkObjectReference(d.NetworkObject), prefab == flagPrefab);

        [Rpc(SendTo.Everyone)]
        void PlantRpc(NetworkObjectReference defectRef, bool flag)
        {
            var prefab = flag ? flagPrefab : passPrefab;
            if (prefab == null || !defectRef.TryGet(out var no)) return;
            var d = no.GetComponent<Defect>();
            var pos = d.transform.position + d.transform.forward * 0.12f + Vector3.up * 0.25f;
            var go = Instantiate(prefab, pos, Quaternion.LookRotation(d.transform.forward));
            go.AddComponent<InspectionMarker>();
            StartCoroutine(Pop(go.transform));
        }

        static IEnumerator Pop(Transform t)
        {
            // ease-out-back pop so the flag lands with a punch
            var end = t.localScale;
            float a = 0f;
            while (a < 1f)
            {
                a = Mathf.Min(1f, a + Time.deltaTime / 0.35f);
                const float c1 = 1.70158f, c3 = c1 + 1f;
                float e = 1f + c3 * Mathf.Pow(a - 1f, 3) + c1 * Mathf.Pow(a - 1f, 2);
                t.localScale = end * e;
                yield return null;
            }
        }

        // ================================================================ the surprise visit (host)
        struct Work { public float Time; public KitType Kit; public Vector3 Point; }
        readonly Dictionary<ulong, Work> _work = new();
        Radio _radio;
        Vector3 _goal;
        float _modeUntil, _leaveAt, _lookYaw, _lookT, _nextVision, _noiseCooldown, _immuneUntil;
        ulong _suspect;
        int _visits, _radioTrips;
        bool _warned;
        Defect _goalDefect;

        void SetMode(Mode m, float seconds = 0f)
        {
            _mode.Value = (int)m;
            _modeUntil = Time.time + seconds;
            if (m == Mode.LookAround || m == Mode.Scold) { _lookYaw = transform.eulerAngles.y; _lookT = 0f; }
        }

        // Host: someone just used a tool here (every stroke reports in)
        public void ReportWork(ulong clientId, KitType kit, Vector3 point)
        {
            if (!IsServer || IgnoreWork) return;
            _work[clientId] = new Work { Time = Time.time, Kit = kit, Point = point };
        }

        // ---- scripted hooks (the trailer director stages scenes with these; normal play never calls them)
        public static bool IgnoreWork;                     // brush strokes don't count as bodging in front of him
        public static event Action<int> Yelled;           // every sound he makes (kind), for the trailer soundtrack
        public void ScriptHoldOutside() { if (IsServer) _nextVisit.Value = 99999f; }
        public void ScriptArriveNow()
        {
            var job = JobManager.Instance;
            if (!IsServer || job == null) return;
            Arrive(job);
            _leaveAt = float.MaxValue; // no phone calls during the take
        }
        public void ScriptGoTo(Vector3 p) { if (!IsServer) return; _goal = ClampToRoom(p); _goalDefect = null; SetMode(Mode.Patrol); }
        public void ScriptLookAt(Vector3 p, float seconds)
        {
            if (!IsServer) return;
            var d = p - transform.position; d.y = 0;
            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
            SetMode(Mode.LookAround, seconds);
            _lookYaw = transform.eulerAngles.y; _lookT = 0f; // the head-sweep starts pointing straight at it
        }
        // ready to be beaned again (and not about to skid in the puddle he's standing in)
        public void ScriptClearImmunity() { _stunImmuneUntil = 0f; _immuneUntil = 0f; _slipImmuneUntil = Mathf.Max(_slipImmuneUntil, Time.time + 8f); }
        public void ScriptAllowSlip() => _slipImmuneUntil = 0f;
        public void ScriptWarp(Vector3 p, Vector3 lookAt)
        {
            if (!IsServer) return;
            p.y = transform.position.y;
            var d = lookAt - p; d.y = 0;
            transform.SetPositionAndRotation(p, d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d) : transform.rotation);
        }

        // Anyone: something crashed (thrown item hitting hard)
        [Rpc(SendTo.Server)]
        public void NoiseRpc(Vector3 point, float loudness)
        {
            var m = CurrentMode;
            if (!Present || m is Mode.Final or Mode.Leaving or Mode.Scold or Mode.Radio or Mode.Stunned or Mode.Blinded or Mode.Slipped || Time.time < _noiseCooldown) return;
            if (loudness < 0.3f) return;
            _noiseCooldown = Time.time + 3f;
            _goal = ClampToRoom(point);
            _goalDefect = null;
            SetMode(Mode.Investigate);
            string[] lines = { "\"What was that?!\"", "\"Hello? Who's breaking things?\"", "\"Was that the… what WAS that?\"", "\"I heard that!\"" };
            JobManager.Instance?.Say($"Inspector: {lines[UnityEngine.Random.Range(0, lines.Length)]}");
        }

        // Anyone: flick the radio switch
        [Rpc(SendTo.Server)]
        public void ToggleRadioRpc(int slot)
        {
            var job = JobManager.Instance;
            if (job == null || job.Phase != JobPhase.Renovation) return;
            _radioOn.Value = !_radioOn.Value;
            if (_radioOn.Value) job.Say($"P{slot + 1} turned the radio on. ♪");
        }

        static Vector3 ClampToRoom(Vector3 p) => new(Mathf.Clamp(p.x, -5.2f, 5.2f), 0f, Mathf.Clamp(p.z, -4.2f, 4.2f));

        void Update()
        {
            UpdateLook();
            if (!IsServer || !IsSpawned) return;
            var job = JobManager.Instance;
            if (job == null || !job.IsSpawned || job.Phase != JobPhase.Renovation) return;
            var mode = CurrentMode;
            if (mode == Mode.Final) return;

            if (mode == Mode.Away)
            {
                if (!_warned && _visits == 0 && job.Elapsed >= _nextVisit.Value - 8f)
                {
                    _warned = true;
                    job.Say("A car pulls up outside… the inspector is EARLY!");
                }
                if (job.Elapsed >= _nextVisit.Value) Arrive(job);
                return;
            }

            Vision(job);
            mode = CurrentMode;
            // walking about in wet paint has consequences for him too
            if (mode is Mode.Patrol or Mode.Investigate or Mode.Radio or Mode.Blinded or Mode.Leaving or Mode.LookAround
                && Time.time >= _slipImmuneUntil && Spills.Instance != null && Spills.Instance.IsSlippery(transform.position))
            {
                _slipImmuneUntil = Time.time + 9f;
                _slideVel = transform.forward * 2.2f;
                SetMode(Mode.Slipped, 3.4f);
                YellRpc(3);
                job.Say("WHOA! The inspector slipped in the paint and went flat on his back!");
                Debug.Log("[Inspector] Slipped in a puddle");
                mode = Mode.Slipped;
            }
            switch (mode)
            {
                case Mode.Patrol:
                    if (_radioOn.Value) { GoToRadio(); break; }
                    if (StepTowards(_goal, walkSpeed * 0.8f))
                    {
                        if (_goalDefect != null)
                        {
                            var d = _goalDefect.transform.position - transform.position; d.y = 0;
                            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
                        }
                        SetMode(Mode.LookAround, UnityEngine.Random.Range(2.5f, 4.5f));
                    }
                    break;

                case Mode.LookAround:
                    if (_radioOn.Value) { GoToRadio(); break; }
                    _lookT += Time.deltaTime;
                    transform.rotation = Quaternion.Euler(0, _lookYaw + Mathf.Sin(_lookT * 1.1f) * 65f, 0);
                    if (Time.time >= _modeUntil)
                    {
                        if (job.Elapsed >= _leaveAt) Leave(job);
                        else PickPatrolGoal(job);
                    }
                    break;

                case Mode.Investigate:
                    if (StepTowards(_goal, walkSpeed * 1.5f) || (_goal - transform.position).sqrMagnitude < 0.6f)
                        SetMode(Mode.LookAround, 3f);
                    break;

                case Mode.Radio:
                    if (!_radioOn.Value) { PickPatrolGoal(job); break; }
                    if (StepTowards(_goal, walkSpeed * 1.4f) || (_goal - transform.position).sqrMagnitude < 0.3f)
                    {
                        _radioOn.Value = false;
                        _radioTrips++;
                        string[] lines = { "\"Who put THIS on?\"", "\"Again? Really?\"", "\"Very funny. VERY funny.\"", "\"I'm taking the batteries out next time.\"" };
                        job.Say($"Inspector clicks the radio off. {lines[Mathf.Min(_radioTrips - 1, lines.Length - 1)]}");
                        YellRpc(2);
                        SetMode(Mode.LookAround, 2f);
                    }
                    break;

                case Mode.Suspicious:
                    if (NetworkManager.ConnectedClients.TryGetValue(_suspect, out var cl) && cl.PlayerObject != null)
                    {
                        var d = cl.PlayerObject.transform.position - transform.position; d.y = 0;
                        if (d.sqrMagnitude > 0.01f)
                            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), turnSpeed * 0.5f * Time.deltaTime);
                    }
                    if (_suspicion.Value < 0.1f)
                    {
                        job.Say("Inspector: \"Hm. …Carry on.\"");
                        PickPatrolGoal(job);
                    }
                    break;

                case Mode.Scold:
                    _lookT += Time.deltaTime;
                    body.localRotation = Quaternion.Euler(Mathf.Sin(_lookT * 30f) * 6f, 0, 0); // furious scribbling
                    if (Time.time >= _modeUntil)
                    {
                        body.localRotation = Quaternion.identity;
                        _suspicion.Value = 0f;
                        _immuneUntil = Time.time + 3f;
                        PickPatrolGoal(job);
                    }
                    break;

                case Mode.Stunned:
                case Mode.Blinded:
                    _lookT += Time.deltaTime;
                    if (mode == Mode.Blinded)
                    {
                        // staggers about, arms out, can't see a thing
                        if (Time.time >= _stumbleNext) { _stumbleNext = Time.time + 0.9f; _goal = ClampToRoom(transform.position + new Vector3(UnityEngine.Random.Range(-1f, 1f), 0, UnityEngine.Random.Range(-1f, 1f))); }
                        StepTowards(_goal, walkSpeed * 0.45f);
                        body.localRotation = Quaternion.Euler(Mathf.Sin(_lookT * 7f) * 6f, 0, Mathf.Sin(_lookT * 4.3f) * 9f);
                    }
                    else body.localRotation = Quaternion.Euler(Mathf.Sin(_lookT * 3.1f) * 9f, 0, Mathf.Sin(_lookT * 2.3f) * 14f); // woozy sway
                    if (Time.time >= _modeUntil)
                    {
                        body.localRotation = Quaternion.identity;
                        string[] back = mode == Mode.Blinded
                            ? new[] { "Inspector wipes his glasses. \"WHO. THREW. THAT.\"", "Inspector blinks paint out of his eyes. \"Unbelievable.\"" }
                            : new[] { "Inspector shakes it off. \"I'm FINE. I'm fine.\"", "Inspector comes round. \"…Where was I?\"" };
                        job.Say(back[UnityEngine.Random.Range(0, back.Length)]);
                        YellRpc(2);
                        PickPatrolGoal(job);
                    }
                    break;

                case Mode.Slipped:
                {
                    // feet out, flat on his back, slides a bit, lies there, then clambers up
                    float left = _modeUntil - Time.time, since = 3.4f - left;
                    transform.position += _slideVel * Time.deltaTime;
                    _slideVel = Vector3.MoveTowards(_slideVel, Vector3.zero, 2.2f * Time.deltaTime);
                    var p = ClampToRoom(transform.position); p.y = transform.position.y; transform.position = p;
                    float tip = since < 0.25f ? Mathf.Lerp(0f, -95f, since / 0.25f)
                              : left > 0.6f ? -92f + Mathf.Exp(-(since - 0.25f) * 8f) * Mathf.Sin((since - 0.25f) * 25f) * 8f
                              : Mathf.Lerp(-92f, 0f, 1f - left / 0.6f);
                    body.localRotation = Quaternion.Euler(tip, 0, left > 0.6f ? Mathf.Sin(since * 2f) * 4f : 0f);
                    if (left <= 0f)
                    {
                        body.localRotation = Quaternion.identity;
                        string[] up = { "Inspector climbs up, dripping. \"Not. One. Word.\"", "Inspector gets up and straightens his hat. \"I meant to do that.\"" };
                        job.Say(up[UnityEngine.Random.Range(0, up.Length)]);
                        YellRpc(2);
                        PickPatrolGoal(job);
                    }
                    break;
                }

                case Mode.Leaving:
                    if (StepTowards(entrance.position, walkSpeed * 1.2f))
                    {
                        _visible.Value = false;
                        _suspicion.Value = 0f;
                        _nextVisit.Value = job.Elapsed + UnityEngine.Random.Range(18f, 28f);
                        SetMode(Mode.Away);
                    }
                    break;
            }
        }

        void Arrive(JobManager job)
        {
            transform.SetPositionAndRotation(entrance.position, entrance.rotation);
            _visible.Value = true;
            _visits++;
            _leaveAt = job.Elapsed + UnityEngine.Random.Range(45f, 70f);
            _goal = new Vector3(-2.5f, 0f, 2.6f);
            _goalDefect = null;
            SetMode(Mode.Patrol);
            job.Say(_visits == 1
                ? "The inspector let himself in for a \"quick look around\". Don't let him SEE you bodge!"
                : "The inspector is back from his phone call!");
        }

        void Leave(JobManager job)
        {
            string[] lines = { "\"Sorry, I have to take this…\" He steps outside.", "\"My car alarm?!\" He rushes out.", "\"Just grabbing my good pen.\" He pops out." };
            job.Say($"Inspector: {lines[(_visits - 1) % lines.Length]}");
            SetMode(Mode.Leaving);
        }

        void GoToRadio()
        {
            var to = RadioSpot - transform.position; to.y = 0;
            _goal = RadioSpot - to.normalized * 0.7f;
            SetMode(Mode.Radio);
        }

        void PickPatrolGoal(JobManager job)
        {
            // mostly the damage (he knows where to look), sometimes just the middle of the room
            var defects = job.Defects;
            if (defects.Count > 0 && UnityEngine.Random.value < 0.75f)
            {
                Defect pick = null;
                for (int i = 0; i < 4; i++)
                {
                    pick = defects[UnityEngine.Random.Range(0, defects.Count)];
                    if (pick != _goalDefect) break;
                }
                _goalDefect = pick;
                _goal = ClampToRoom(pick.transform.position + pick.transform.forward * 1.6f);
            }
            else
            {
                _goalDefect = null;
                _goal = new Vector3(UnityEngine.Random.Range(-3f, 3f), 0f, UnityEngine.Random.Range(-2.5f, 2.5f));
            }
            SetMode(Mode.Patrol);
        }

        // ---------------------------------------------------------------- what he sees
        readonly RaycastHit[] _losHits = new RaycastHit[12];

        public Vector3 EyePosition => transform.position + Vector3.up * 1.65f;

        // Works on every machine (his transform is synced): the HUD uses it to warn you
        public bool CanSee(Vector3 point, Transform target = null)
        {
            if (!Present) return false;
            var eye = EyePosition;
            var to = point - eye;
            float d = to.magnitude;
            if (d > RepairRules.ViewRange || d < 0.01f) return false;
            var flat = new Vector3(to.x, 0, to.z);
            if (flat.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, flat) > RepairRules.ViewHalfAngle) return false;
            int n = Physics.RaycastNonAlloc(eye, to / d, _losHits, d, ~(1 << 2), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = _losHits[i];
                if (h.distance > d - 0.3f) continue;                                 // the thing itself
                if (h.collider.transform.IsChildOf(transform)) continue;             // his own hat
                if (target != null && h.collider.transform.IsChildOf(target)) continue;
                if (h.collider.GetComponentInParent<NetworkBodger>() != null) continue; // people don't hide people
                var rb = h.collider.attachedRigidbody;
                if (rb != null && rb.TryGetComponent<Carryable>(out var c) && (c.IsHeld || rb.mass < 3f)) continue; // a tin in your hand is no wall
                return false;
            }
            return true;
        }

        void Vision(JobManager job)
        {
            if (Time.time < _nextVision) return;
            float dt = 0.1f;
            _nextVision = Time.time + dt;
            var m = CurrentMode;
            if (m is Mode.Scold or Mode.Away or Mode.Final or Mode.Stunned or Mode.Blinded or Mode.Slipped) return;

            NetworkBodger caughtBy = null;
            Work caughtWork = default;
            float rate = 0f;
            foreach (var b in NetworkBodger.All)
            {
                if (b == null || !_work.TryGetValue(b.OwnerClientId, out var w)) continue;
                if (Time.time - w.Time > 0.45f || !RepairRules.IsBodge(w.Kit)) continue;
                var head = b.transform.position + Vector3.up * 1.2f;
                bool seen = CanSee(head, b.transform) || CanSee(w.Point + (EyePosition - w.Point).normalized * 0.15f);
                if (!seen) continue;
                float dist = Vector3.Distance(EyePosition, head);
                float r = 0.35f * (1f + (1f - Mathf.Clamp01(dist / RepairRules.ViewRange)));
                if (r > rate) { rate = r; caughtBy = b; caughtWork = w; }
            }

            if (caughtBy != null && Time.time >= _immuneUntil)
            {
                _suspicion.Value = Mathf.Min(1f, _suspicion.Value + rate * dt);
                if (m != Mode.Suspicious && _suspicion.Value >= 0.25f)
                {
                    _suspect = caughtBy.OwnerClientId;
                    SetMode(Mode.Suspicious);
                    string[] lines = { "\"Hm? What are you doing there?\"", "\"Hold on… is that PAINT?\"", "\"Excuse me. EXCUSE me.\"" };
                    job.Say($"Inspector: {lines[UnityEngine.Random.Range(0, lines.Length)]}");
                }
                if (_suspicion.Value >= 1f) CatchRedHanded(job, caughtBy, caughtWork);
            }
            else
            {
                _suspicion.Value = Mathf.Max(0f, _suspicion.Value - (m == Mode.Suspicious ? 0.22f : 0.12f) * dt);
            }
        }

        void CatchRedHanded(JobManager job, NetworkBodger who, Work w)
        {
            job.AddFine(RepairRules.RedHandedFine);
            Defect nearest = null;
            float best = 2.5f * 2.5f;
            foreach (var d in job.Defects)
            {
                if (d.State == RepairState.Fixed) continue;
                float sq = (d.transform.position - w.Point).sqrMagnitude;
                if (sq < best) { best = sq; nearest = d; }
            }
            nearest?.Note();
            string verb = w.Kit switch { KitType.PaintTin => "painting over", KitType.WallpaperRoll => "papering over", _ => "taping up" };
            job.Say(nearest != null
                ? $"CAUGHT RED-HANDED! P{who.Index + 1} was {verb} the {nearest.Label}. −${RepairRules.RedHandedFine}, and it's in his notebook!"
                : $"CAUGHT RED-HANDED! P{who.Index + 1} was {verb} a perfectly good wall?! −${RepairRules.RedHandedFine}");
            Debug.Log($"[Inspector] Caught P{who.Index + 1} {verb} {(nearest != null ? nearest.Label : "a wall")}");
            var d2 = who.transform.position - transform.position; d2.y = 0;
            if (d2.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d2);
            SetMode(Mode.Scold, 2.5f);
            YellRpc(2);
        }

        // ---------------------------------------------------------------- things thrown at him
        float _stunImmuneUntil, _stumbleNext, _slipImmuneUntil;
        Vector3 _slideVel;

        // The thrower's machine reports the hit (it runs the physics of what it threw)
        [Rpc(SendTo.Server)]
        public void HitRpc(int kit, RpcParams p = default) => ServerHit(p.Receive.SenderClientId, (KitType)kit);

        public void ServerHit(ulong sender, KitType kit)
        {
            if (!IsServer) return;
            var job = JobManager.Instance;
            var m = CurrentMode;
            if (job == null || job.Phase != JobPhase.Renovation || !Present || m is Mode.Final or Mode.Away) return;
            bool paint = kit == KitType.PaintTin;
            YellRpc(paint ? 1 : 0);
            if (m is Mode.Stunned or Mode.Blinded or Mode.Slipped || Time.time < _stunImmuneUntil)
            {
                job.Say("Inspector: \"OW! Will you STOP that!\"");
                return;
            }
            NetworkBodger thrower = null;
            foreach (var b in NetworkBodger.All) if (b != null && b.OwnerClientId == sender) thrower = b;
            bool saw = thrower != null && CanSee(thrower.transform.position + Vector3.up * 1.2f, thrower.transform);
            if (saw) job.AddFine(RepairRules.AssaultFine);
            float secs = paint ? 3f : 4f;
            SetMode(paint ? Mode.Blinded : Mode.Stunned, secs);
            _stunImmuneUntil = Time.time + secs + 5f;
            _slipImmuneUntil = Mathf.Max(_slipImmuneUntil, Time.time + 1.5f); // the splash lands under him: give him a moment before he skids in it
            _suspicion.Value = Mathf.Max(0f, _suspicion.Value - 0.5f);
            string who = thrower != null ? $"P{thrower.Index + 1}" : "Someone";
            string fine = saw ? $" He saw {who} do it: −${RepairRules.AssaultFine}." : $" He never saw who did it. Go go go!";
            job.Say(paint ? $"SPLAT! {who} got paint in the inspector's eyes! Blind for {secs:0}s.{fine}"
                          : $"BONK! {who} beaned the inspector. He's seeing stars for {secs:0}s.{fine}");
            Debug.Log($"[Inspector] Hit by {kit} from {who}, saw thrower: {saw}");
        }

        // ---------------------------------------------------------------- his voice
        AudioSource _voice;
        static AudioClip[] _grunts;

        [Rpc(SendTo.Everyone)]
        void YellRpc(int kind)
        {
            Yelled?.Invoke(kind);
            if (_voice == null) return;
            _grunts ??= new[] { Grunt(0), Grunt(1), Grunt(2), Grunt(3) };
            _voice.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
            _voice.PlayOneShot(_grunts[Mathf.Clamp(kind, 0, _grunts.Length - 1)]);
        }

        // Cartoon sound effects, made on the spot (no audio files):
        // 0 = BONK + boing + tweeting birds (hit), 1 = SPLAT + spluttering (paint),
        // 2 = angry gibberish grumble (caught you / radio / coming round), 3 = "whoa-oa" + thud (slipped)
        static AudioClip Grunt(int kind)
        {
            const int rate = 22050;
            float len = kind switch { 0 => 1.3f, 1 => 1.0f, 2 => 0.9f, _ => 1.0f };
            int n = (int)(len * rate);
            var d = new float[n];
            var rnd = new System.Random(7 + kind * 13);
            float Noise() => (float)rnd.NextDouble() * 2f - 1f;

            void Tone(float start, float dur, System.Func<float, float> freq, float vol, int wave = 0, float attack = 0.005f)
            {
                float ph = 0f;
                int i0 = (int)(start * rate), i1 = Mathf.Min(n, (int)((start + dur) * rate));
                for (int i = i0; i < i1; i++)
                {
                    float u = (i - i0) / (float)rate;
                    ph += freq(u / dur) / rate;
                    float w = wave switch
                    {
                        1 => Mathf.Repeat(ph, 1f) < 0.5f ? 0.6f : -0.6f,                       // square-ish (voice)
                        2 => 1f - 4f * Mathf.Abs(Mathf.Repeat(ph + 0.25f, 1f) - 0.5f),        // triangle
                        _ => Mathf.Sin(ph * 2f * Mathf.PI),
                    };
                    float env = Mathf.Clamp01(u / attack) * Mathf.Clamp01((dur - u) / (dur * 0.35f));
                    d[i] += w * env * vol;
                }
            }
            void Thud(float start, float dur, float vol, float tone)
            {
                float lp = 0f;
                int i0 = (int)(start * rate), i1 = Mathf.Min(n, (int)((start + dur) * rate));
                for (int i = i0; i < i1; i++)
                {
                    float u = (i - i0) / (float)rate;
                    lp += (Noise() - lp) * tone;               // low-passed noise: softer = lower tone
                    d[i] += lp * Mathf.Exp(-u * 18f / dur) * vol;
                }
            }
            void Gibber(float start, int syllables, float baseHz, float vol)
            {
                float t = start;
                for (int k = 0; k < syllables; k++)
                {
                    float dur = 0.06f + (float)rnd.NextDouble() * 0.06f;
                    float f0 = baseHz * (0.8f + (float)rnd.NextDouble() * 0.6f), f1 = f0 * (0.85f + (float)rnd.NextDouble() * 0.3f);
                    Tone(t, dur, u => Mathf.Lerp(f0, f1, u), vol, 1, 0.01f);
                    t += dur + 0.015f + (float)rnd.NextDouble() * 0.03f;
                }
            }

            switch (kind)
            {
                case 0: // BONK on the head, a sproingy boing, then little birds
                    Tone(0f, 0.12f, u => Mathf.Lerp(900f, 420f, u), 0.6f, 2, 0.001f);
                    Thud(0f, 0.08f, 0.9f, 0.5f);
                    Tone(0.08f, 0.55f, u => (260f + 220f * u) * (1f + 0.12f * Mathf.Sin(u * 60f)), 0.35f);
                    for (int b = 0; b < 4; b++)
                        Tone(0.65f + b * 0.15f, 0.09f, u => 2600f + 900f * Mathf.Sin(u * Mathf.PI), 0.35f);
                    break;
                case 1: // wet SPLAT, then a disgusted splutter
                    Thud(0f, 0.25f, 1.1f, 0.35f);
                    Thud(0.02f, 0.12f, 0.6f, 0.9f);
                    Tone(0.3f, 0.18f, u => Mathf.Lerp(320f, 220f, u), 0.25f, 1);
                    Thud(0.3f, 0.18f, 0.25f, 0.8f);                     // "pfft"
                    Gibber(0.55f, 4, 260f, 0.4f);
                    break;
                case 2: // angry mumble, rising to a final "HMPH"
                    Gibber(0f, 6, 210f, 0.24f);
                    Tone(0.62f, 0.22f, u => Mathf.Lerp(300f, 170f, u), 0.3f, 1);
                    break;
                default: // "whoa-oa-oa" wobbling up, then a thud on the floor
                    Tone(0f, 0.55f, u => (230f + 160f * u) * (1f + 0.15f * Mathf.Sin(u * 45f)), 0.28f, 1);
                    Tone(0.5f, 0.3f, u => Mathf.Lerp(600f, 150f, u), 0.25f, 2);   // slide whistle down
                    Thud(0.78f, 0.2f, 1.2f, 0.25f);
                    break;
            }
            // soften the edges a touch and normalise
            float peak = 0.001f, lpo = 0f;
            for (int i = 0; i < n; i++) { lpo += (d[i] - lpo) * 0.6f; d[i] = lpo; peak = Mathf.Max(peak, Mathf.Abs(lpo)); }
            float g = 0.8f / peak;
            for (int i = 0; i < n; i++) d[i] *= g;
            var clip = AudioClip.Create($"Sfx{kind}", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        // ================================================================ visuals (every machine)
        Transform _cone;
        Material _coneMat;
        TextMeshPro _icon;

        Mesh _coneMesh;
        readonly List<Vector3> _cv = new();
        readonly List<int> _ct = new();
        readonly List<Vector3> _cn = new();

        void BuildCone()
        {
            var go = new GameObject("VisionCone");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 0.025f, 0);
            _coneMesh = new Mesh { name = "ViewFan" };
            _coneMesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _coneMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var src = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            _coneMat = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _coneMat.SetFloat("_Smoothness", 0f);
            mr.sharedMaterial = _coneMat;
            _cone = go.transform;
            _cone.gameObject.SetActive(false);
        }

        // The view as geometry, no special shader: outline + arcs rippling outward like a radar
        void RebuildCone(float t)
        {
            _cv.Clear(); _ct.Clear();
            float R = RepairRules.ViewRange - 0.15f, half = RepairRules.ViewHalfAngle;
            const float period = 0.6f, band = 0.17f, edge = 0.08f;
            void Arc(float r0, float r1)
            {
                const int seg = 18;
                int start = _cv.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = Mathf.Deg2Rad * Mathf.Lerp(-half, half, i / (float)seg);
                    var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    _cv.Add(dir * r0); _cv.Add(dir * r1);
                    if (i < seg)
                    {
                        int k = start + i * 2;
                        _ct.Add(k); _ct.Add(k + 1); _ct.Add(k + 3);
                        _ct.Add(k); _ct.Add(k + 3); _ct.Add(k + 2);
                    }
                }
            }
            void Side(float sign)
            {
                float a = Mathf.Deg2Rad * half * sign;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var n = Vector3.Cross(Vector3.up, dir) * edge * 0.5f;
                int k = _cv.Count;
                _cv.Add(dir * 0.3f - n); _cv.Add(dir * 0.3f + n); _cv.Add(dir * R - n); _cv.Add(dir * R + n);
                _ct.Add(k); _ct.Add(k + 2); _ct.Add(k + 3); _ct.Add(k); _ct.Add(k + 3); _ct.Add(k + 1);
            }
            Side(-1f); Side(1f);
            Arc(R - edge, R);
            for (float r = 0.5f + Mathf.Repeat(t * 0.9f, period); r + band < R - 0.1f; r += period) Arc(r, r + band);
            _coneMesh.Clear();
            _coneMesh.SetVertices(_cv);
            _coneMesh.SetTriangles(_ct, 0);
            // flat on the floor: every normal straight up (recalculated ones can come out zero → NaN → bloom blows the screen white)
            _cn.Clear();
            for (int i = 0; i < _cv.Count; i++) _cn.Add(Vector3.up);
            _coneMesh.SetNormals(_cn);
            _coneMesh.RecalculateBounds();
        }

        void BuildIcon()
        {
            var go = new GameObject("MoodIcon");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 2.45f, 0);
            _icon = go.AddComponent<TextMeshPro>();
            _icon.alignment = TextAlignmentOptions.Center;
            _icon.fontSize = 6f;
            _icon.fontStyle = FontStyles.Bold;
            _icon.outlineWidth = 0.25f;
            _icon.outlineColor = Palette.UiInk;
            _icon.rectTransform.sizeDelta = new Vector2(5f, 1.2f);
            _icon.textWrappingMode = TextWrappingModes.NoWrap;
            _icon.overflowMode = TextOverflowModes.Overflow;
            _icon.text = "";
        }

        Transform _stars, _splat;

        void BuildDaze()
        {
            var src = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            Material M(Color c, float smooth)
            {
                var m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); return m;
            }
            GameObject Blob(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
            {
                var g = GameObject.CreatePrimitive(type);
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(parent, false);
                g.transform.localPosition = pos; g.transform.localScale = scale;
                var r = g.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return g;
            }
            _stars = new GameObject("DazeStars").transform;
            _stars.SetParent(transform, false);
            _stars.localPosition = new Vector3(0, 2.05f, 0);
            var star = M(Palette.TintYellow, 0.6f);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f;
                Blob(_stars, PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 0.38f, Mathf.Sin(a * 2f) * 0.05f, Mathf.Sin(a) * 0.38f), Vector3.one * 0.09f, star)
                    .transform.localRotation = Quaternion.Euler(45, 45, 0);
            }
            _stars.gameObject.SetActive(false);

            // a face full of fresh paint, with drips (rides on the body so it leans with him)
            _splat = new GameObject("PaintSplat").transform;
            _splat.SetParent(body, false);
            var paint = M(Palette.FreshPaint, 0.85f);
            Blob(_splat, PrimitiveType.Sphere, new Vector3(0, 1.53f, 0.27f), new Vector3(0.46f, 0.3f, 0.07f), paint);
            Blob(_splat, PrimitiveType.Sphere, new Vector3(0.12f, 1.62f, 0.25f), new Vector3(0.2f, 0.16f, 0.06f), paint);
            Blob(_splat, PrimitiveType.Cube, new Vector3(-0.08f, 1.36f, 0.27f), new Vector3(0.04f, 0.2f, 0.03f), paint);
            Blob(_splat, PrimitiveType.Cube, new Vector3(0.1f, 1.33f, 0.26f), new Vector3(0.035f, 0.26f, 0.03f), paint);
            Blob(_splat, PrimitiveType.Cube, new Vector3(0.02f, 1.18f, 0.22f), new Vector3(0.03f, 0.2f, 0.03f), paint);
            _splat.gameObject.SetActive(false);
        }

        void UpdateLook()
        {
            if (_stars != null)
            {
                var dm = CurrentMode;
                bool stars = Present && dm == Mode.Stunned, splat = Present && dm == Mode.Blinded;
                if (_stars.gameObject.activeSelf != stars) _stars.gameObject.SetActive(stars);
                if (_splat.gameObject.activeSelf != splat) _splat.gameObject.SetActive(splat);
                if (stars) _stars.localRotation = Quaternion.Euler(0, Time.time * 260f, Mathf.Sin(Time.time * 3f) * 10f);
            }
            if (_cone == null) return;
            var m = CurrentMode;
            bool show = Watching;
            if (_cone.gameObject.activeSelf != show) _cone.gameObject.SetActive(show);
            float s = _suspicion.Value;
            var c = m == Mode.Scold ? Palette.Accent : Color.Lerp(Palette.TintYellow, Palette.Accent, Mathf.Clamp01(s * 1.3f));
            if (show)
            {
                _coneMat.SetColor("_BaseColor", c);
                RebuildCone(Time.time * (1f + s * 2f)); // ripples faster as he gets suspicious
            }

            if (_icon != null)
            {
                string txt = !Present ? "" : m switch
                {
                    Mode.Scold => "!!",
                    Mode.Suspicious => s > 0.6f ? "!" : "?",
                    Mode.Investigate => "?",
                    Mode.Radio => "#!",
                    Mode.Blinded => "@#$%!",
                    Mode.Slipped => "!?",
                    _ => "",
                };
                _icon.text = txt;
                _icon.color = txt.StartsWith("?") ? Palette.TintYellow : Palette.Accent;
                var cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0) cam = Camera.allCameras[0];
                if (txt.Length > 0 && cam != null)
                {
                    _icon.transform.rotation = Quaternion.LookRotation(_icon.transform.position - cam.transform.position);
                    float pulse = 1f + Mathf.Sin(Time.time * 10f) * 0.08f;
                    _icon.transform.localScale = Vector3.one * pulse;
                }
            }
        }
    }
}
