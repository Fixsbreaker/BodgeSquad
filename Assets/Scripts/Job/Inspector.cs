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
        public enum Mode { Away, Patrol, LookAround, Investigate, Radio, Suspicious, Scold, Leaving, Final }

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
        public bool Watching => Present && CurrentMode != Mode.Final && CurrentMode != Mode.Away;

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
            _visible.OnValueChanged += (_, v) => ShowBody(v);
            ShowBody(_visible.Value);
            BuildCone();
            BuildIcon();
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
            if (!IsServer) return;
            _work[clientId] = new Work { Time = Time.time, Kit = kit, Point = point };
        }

        // Anyone: something crashed (thrown item hitting hard)
        [Rpc(SendTo.Server)]
        public void NoiseRpc(Vector3 point, float loudness)
        {
            var m = CurrentMode;
            if (!Present || m is Mode.Final or Mode.Leaving or Mode.Scold or Mode.Radio || Time.time < _noiseCooldown) return;
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
            if (m is Mode.Scold or Mode.Away or Mode.Final) return;

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
            _icon.rectTransform.sizeDelta = new Vector2(2f, 1f);
            _icon.text = "";
        }

        void UpdateLook()
        {
            if (_cone == null) return;
            var m = CurrentMode;
            bool show = Present && m != Mode.Final && m != Mode.Away;
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
