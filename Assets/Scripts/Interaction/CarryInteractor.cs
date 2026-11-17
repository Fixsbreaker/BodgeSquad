using BodgeSquad.Job;
using BodgeSquad.Players;
using Unity.Netcode;
using UnityEngine;

namespace BodgeSquad.Interaction
{
    // Interact (E / Y):
    //   holding a picture + looking at a wall → hang it right there
    //   holding anything                      → drop it
    //   looking at a pressable (doorbell)      → press it
    //   looking at a carryable                 → pick it up (the host charges for unpaid kits)
    // Attack (LMB / X):
    //   holding a tool → work with it by hand: hold and sweep to paint/fill/spray, click to tape.
    //                    Cells you sweep get covered; the tool runs dry as you use it (even on misses).
    //   holding anything else → throw it.
    // Online: the local player asks the host to pick something up; the host checks it's free and
    // affordable, then hands over ownership so this machine drives its physics while carrying.
    public class CarryInteractor : NetworkBehaviour
    {
        [SerializeField] Transform eye;
        [SerializeField] Transform holdPoint;
        [SerializeField] float reach = 2.6f;
        [SerializeField] float followStrength = 14f;
        [SerializeField] float maxCarrySpeed = 12f;
        [SerializeField] float throwSpeed = 9f;
        [SerializeField] float dropDistance = 2.2f; // snaps free if it gets wedged too far away

        BodgerInput _input;
        NetworkBodger _bodger;
        Carryable _held;
        bool _pending;
        int _rayMask = ~0;
        float _messageUntil;

        public Carryable Held => _held;
        public int PlayerIndex => _bodger != null ? _bodger.Index : 0;
        public bool UsesKeyboard => _input == null || _input.UsesKeyboard;

        // What the crosshair is on (refreshed every frame, read by the player HUD)
        public Defect LookDefect { get; private set; }
        public Carryable LookCarryable { get; private set; }
        public IPressable LookPressable { get; private set; }
        public string Message { get; private set; }   // short-lived feedback ("Not enough money")
        public RepairKit HeldKit => _held != null && _held.TryGetComponent<RepairKit>(out var k) ? k : null;
        public bool Working { get; private set; }     // tool is touching a surface this frame

        [SerializeField] float toolReach = 3.5f;
        readonly System.Collections.Generic.Dictionary<Defect, ulong> _pendingCells = new();
        float _nextSend;

        void Awake()
        {
            reach = Mathf.Max(reach, 3.5f);
            toolReach = Mathf.Max(toolReach, 3.5f);
            dropDistance = Mathf.Max(dropDistance, 3f);
            _input = GetComponent<BodgerInput>();
            _bodger = GetComponent<NetworkBodger>();
        }

        // Own body and invisible blockers (the ceiling lid sits on Ignore Raycast) never get in the way
        public void SetIgnoredLayer(int layer) => _rayMask = ~((1 << layer) | (1 << 2));

        // What the centre of the screen actually touches (shown in the debug line)
        public string DebugLook { get; private set; }

        void Update()
        {
            if (!IsOwner || _input == null || !_input.Ready || !_input.enabled) return;
            if (_held != null && (_held == null || !_held.IsSpawned)) _held = null; // consumed or despawned
            ScanLook();
            if (Time.time > _messageUntil) Message = null;

            if (_input.InteractPressed()) OnInteract();
            else if (_held != null && _input.ThrowPressed())
                Release(eye.forward * throwSpeed / _held.CarryWeight);
            else if (HeldKit != null) UseTool(HeldKit);
            else if (_held != null && _input.AttackPressed())
                Release(eye.forward * throwSpeed / _held.CarryWeight);
            if (HeldKit == null) Working = false;
            FlushCells();
        }

        readonly RaycastHit[] _hits = new RaycastHit[16];

        // Forgiving pick-up: anything roughly in the middle of the screen and within reach counts,
        // not just what the exact centre pixel touches. Defects and the doorbell still use the ray.
        const float GrabCone = 15f;            // degrees off the crosshair
        readonly Collider[] _near = new Collider[48];

        void ScanLook()
        {
            LookDefect = null; LookCarryable = null; LookPressable = null;
            int n = Physics.RaycastNonAlloc(eye.position, eye.forward, _hits, reach, _rayMask, QueryTriggerInteraction.Collide);
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                if (_held != null && _hits[i].rigidbody == _held.Body) continue;
                if (_hits[i].collider.transform.IsChildOf(transform)) continue; // our own capsule
                if (best < 0 || _hits[i].distance < _hits[best].distance) best = i;
            }
            DebugLook = best >= 0 ? $"{_hits[best].collider.name}{(_hits[best].collider.isTrigger ? " (trigger)" : "")} {_hits[best].distance:0.0} m" : $"nothing within {reach:0.0} m";
            if (best >= 0)
            {
                var hit = _hits[best];
                LookDefect = hit.collider.GetComponentInParent<Defect>();
                var c = hit.collider.GetComponentInParent<Carryable>();
                if (c != null && c != _held) LookCarryable = c;
                LookPressable = hit.collider.GetComponentInParent<IPressable>();
            }
            if (LookCarryable == null && _held == null) LookCarryable = AimAssist();
        }

        Carryable AimAssist()
        {
            Carryable bestC = null;
            float bestScore = float.MaxValue;
            int n = Physics.OverlapSphereNonAlloc(eye.position, reach, _near, _rayMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = _near[i].attachedRigidbody;
                if (rb == null || !rb.TryGetComponent<Carryable>(out var c) || c == _held) continue;
                Vector3 centre = _near[i].bounds.center;
                Vector3 to = centre - eye.position;
                float dist = to.magnitude;
                if (dist > reach) continue;
                // bigger things get a wider cone (aim anywhere on the sofa)
                float size = _near[i].bounds.extents.magnitude;
                float cone = GrabCone + Mathf.Rad2Deg * Mathf.Atan2(size, Mathf.Max(dist, 0.1f));
                float angle = Vector3.Angle(eye.forward, to);
                if (angle > cone) continue;
                // nothing solid in between (a wall, the table top is fine: it's under the item)
                if (Physics.Raycast(eye.position, to / dist, out var block, dist - 0.05f, _rayMask, QueryTriggerInteraction.Ignore)
                    && block.rigidbody != rb && block.collider.attachedRigidbody != rb && block.distance < dist - size
                    && !block.collider.transform.IsChildOf(transform)) continue;
                float score = angle + dist * 4f;
                if (score < bestScore) { bestScore = score; bestC = c; }
            }
            return bestC;
        }

        void OnInteract()
        {
            var cover = _held != null ? _held.GetComponent<Concealer>() : null;
            if (cover != null && cover.WallHung && Physics.Raycast(eye.position, eye.forward, out var wall, reach, _rayMask, QueryTriggerInteraction.Ignore)
                && wall.rigidbody != _held.Body && Mathf.Abs(wall.normal.y) < 0.3f)
            {
                var c = _held;
                Release(Vector3.zero);
                cover.HangAt(wall.point, wall.normal);
                Flash("Hung on the wall");
                return;
            }
            if (_held != null) { Release(Vector3.zero); return; }
            if (LookPressable != null && LookPressable.PressPrompt != null) { LookPressable.Press(PlayerIndex); return; }
            if (LookCarryable != null) TryGrab(LookCarryable);
        }

        // ------------------------------------------------------------ hands-on tools
        // Tools work on anything: walls, floor, furniture. Every stroke leaves a mark everyone sees;
        // strokes that land on a defect also cover its cells (that's what the inspector judges).
        float _nextMark;
        Vector3 _lastMark;

        void UseTool(RepairKit kit)
        {
            Working = false;
            var job = JobManager.Instance;
            if (job != null && job.Phase != JobPhase.Renovation) return;
            bool click = RepairRules.Mode(kit.Type) == RepairRules.ToolMode.Click;
            bool active = click ? _input.AttackPressed() : _input.AttackHeld();
            if (!active) return;
            if (!ToolRay(out var hit))
            {
                if (click) Flash("Too far: get closer");
                return;
            }
            Working = true;
            kit.Spend(click ? 1f : Time.deltaTime); // every second (or strip) counts, on target or not

            // the visible stroke
            float brushM = Mathf.Max(0.08f, RepairRules.Brush(kit.Type) * 0.1f * 2f);
            if (click)
            {
                float roll = Random.Range(-25f, 25f);
                SurfaceMarks.Instance?.Place(kit.Type, hit, 0.42f, roll);
                CoverDefects(kit.Type, hit.point, roll, click: true);
            }
            else if (Time.time >= _nextMark || (hit.point - _lastMark).sqrMagnitude > brushM * brushM * 0.25f)
            {
                _nextMark = Time.time + 0.07f;
                _lastMark = hit.point;
                SurfaceMarks.Instance?.Place(kit.Type, hit, brushM * Random.Range(0.9f, 1.15f), Random.Range(0f, 360f));
            }
            if (!click) CoverDefects(kit.Type, hit.point, 0f, click: false);

            if (kit.Empty)
            {
                FlushCells(force: true);
                var used = _held;
                DropLocal(Vector3.zero);
                used.gameObject.SetActive(false);
                ConsumeRpc(new NetworkObjectReference(used.NetworkObject));
                Flash($"{RepairRules.KitName(kit.Type)} is used up! Buy another");
                Working = false;
            }
        }

        // Nearest solid surface under the crosshair, looking past the tool in your own hand
        bool ToolRay(out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(eye.position, eye.forward, _hits, toolReach, _rayMask, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (_held != null && _hits[i].rigidbody == _held.Body) continue;
                if (_hits[i].collider.transform.IsChildOf(transform)) continue;
                if (_hits[i].distance < bestD) { bestD = _hits[i].distance; best = _hits[i]; }
            }
            return bestD < float.MaxValue;
        }

        void CoverDefects(KitType kit, Vector3 point, float roll, bool click)
        {
            var job = JobManager.Instance;
            if (job == null) return;
            foreach (var d in job.Defects)
            {
                if ((d.transform.position - point).sqrMagnitude > 0.9f * 0.9f) continue;
                if (d.State == RepairState.Fixed) continue;
                ulong cells = click ? d.StripMask(point, roll) : d.BrushMask(point, RepairRules.Brush(kit));
                if (cells == 0) continue;
                _pendingCells.TryGetValue(d, out var acc);
                _pendingCells[d] = acc | cells;
            }
        }

        // Batch covered cells to the host a few times a second
        void FlushCells(bool force = false)
        {
            if (_pendingCells.Count == 0 || (!force && Time.time < _nextSend)) return;
            _nextSend = Time.time + 0.08f;
            var kit = HeldKit;
            int type = kit != null ? (int)kit.Type : _lastKitType;
            foreach (var kv in _pendingCells)
                if (kv.Key != null && kv.Key.IsSpawned) PaintRpc(new NetworkObjectReference(kv.Key.NetworkObject), type, kv.Value);
            _pendingCells.Clear();
        }
        int _lastKitType;

        [Rpc(SendTo.Server)]
        void PaintRpc(NetworkObjectReference defectRef, int kitType, ulong cells, RpcParams p = default)
        {
            if (!defectRef.TryGet(out var no) || !no.TryGetComponent<Defect>(out var d)) return;
            var kit = (KitType)kitType;
            Inspector.Instance?.ReportWork(p.Receive.SenderClientId, kit, d.transform.position + d.transform.forward * 0.05f);
            var job = JobManager.Instance;
            if (job != null && job.Phase != JobPhase.Renovation) return;
            if (d.State == RepairState.Fixed) return;
            // the right kit really fixes it; any other proper kit just smears over it, like paint
            var layer = RepairRules.IsProperFix(kit) && RepairRules.ProperKitFor(d.Kind) != kit ? RepairRules.Layer.Paint : RepairRules.LayerOf(kit);
            d.AddCells(layer, cells, PlayerIndex);
        }

        [Rpc(SendTo.Server)]
        void ConsumeRpc(NetworkObjectReference kitRef)
        {
            if (kitRef.TryGet(out var no) && no.IsSpawned) no.Despawn(true);
        }

        void FixedUpdate()
        {
            if (!IsOwner || _held == null || !_held.IsOwner) return;
            var rb = _held.Body;
            // tools sit firmly in the right hand (no wobble, no bumping); bigger things swing out in front
            if (_held.TryGetComponent<RepairKit>(out _))
            {
                if (!rb.isKinematic) rb.isKinematic = true;
                rb.MovePosition(eye.position + eye.forward * 0.55f + eye.right * 0.28f - eye.up * 0.24f);
                rb.MoveRotation(eye.rotation);
                return;
            }
            if (rb.isKinematic) rb.isKinematic = false;
            Vector3 target = holdPoint.position;
            Vector3 toTarget = target - rb.worldCenterOfMass;
            if (toTarget.magnitude > dropDistance) { Release(Vector3.zero); return; }

            Vector3 desired = Vector3.ClampMagnitude(toTarget * followStrength / _held.CarryWeight, maxCarrySpeed);
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, desired, 0.5f);
            rb.angularVelocity *= 0.85f;
        }

        // ------------------------------------------------------------ test hooks (smoke test)
        public void TestGrab(Carryable c) => TryGrab(c);
        public Transform Eye => eye;
        public void TestDrop() => Release(Vector3.zero);
        // Sweep the held tool over a whole defect, as a player would, for `seconds` of use
        public void TestSweep(Defect d, float seconds)
        {
            var kit = HeldKit;
            if (kit == null) return;
            kit.Spend(seconds);
            PaintRpc(new NetworkObjectReference(d.NetworkObject), (int)kit.Type, ulong.MaxValue);
        }

        // ------------------------------------------------------------ grab
        void TryGrab(Carryable c)
        {
            if (_pending || c.IsHeld) return;
            _pending = true;
            RequestGrabRpc(new NetworkObjectReference(c.NetworkObject));
        }

        [Rpc(SendTo.Server)]
        void RequestGrabRpc(NetworkObjectReference itemRef, RpcParams p = default)
        {
            ulong sender = p.Receive.SenderClientId;
            if (!itemRef.TryGet(out var no) || !no.TryGetComponent<Carryable>(out var c)) { GrabResultRpc(itemRef, false, ""); return; }
            if (c.IsHeld && c.HolderClient.Value != sender) { GrabResultRpc(itemRef, false, "Someone else is holding that"); return; }
            if (no.TryGetComponent<RepairKit>(out var kit) && !kit.TryPurchase())
            {
                GrabResultRpc(itemRef, false, $"Not enough money for {RepairRules.KitName(kit.Type)}");
                return;
            }
            c.HolderClient.Value = sender;
            if (no.OwnerClientId != sender) no.ChangeOwnership(sender);
            GrabResultRpc(itemRef, true, "");
        }

        [Rpc(SendTo.Owner)]
        void GrabResultRpc(NetworkObjectReference itemRef, bool ok, string message)
        {
            _pending = false;
            if (!ok) { Flash(message); return; }
            if (!itemRef.TryGet(out var no) || !no.TryGetComponent<Carryable>(out var c)) return;
            if (_held != null) Release(Vector3.zero);
            _held = c;
            c.Holder = transform;
            IgnoreSelf(c, true);
            c.Body.useGravity = false;
            c.Body.isKinematic = false; // pictures hang kinematic on the wall
            c.Body.constraints = RigidbodyConstraints.None; // shelf stock is frozen until picked up
            c.Body.interpolation = RigidbodyInterpolation.Interpolate;
            Debug.Log($"[Carry] {name} picked up {c.name}");
        }

        // ------------------------------------------------------------ release
        void DropLocal(Vector3 velocity)
        {
            var c = _held;
            if (c != null && c.TryGetComponent<RepairKit>(out var k)) { _lastKitType = (int)k.Type; FlushCells(force: true); }
            _held = null;
            if (c == null) return;
            IgnoreSelf(c, false);
            c.Holder = null;
            c.Body.isKinematic = false;
            c.Body.useGravity = true;
            if (velocity != Vector3.zero) c.Body.linearVelocity = velocity;
        }

        void Release(Vector3 velocity)
        {
            var c = _held;
            DropLocal(velocity);
            if (c == null) return;
            if (IsSpawned && c.IsSpawned) ReleaseRpc(new NetworkObjectReference(c.NetworkObject));
            Debug.Log($"[Carry] {name} {(velocity == Vector3.zero ? "dropped" : "threw")} {c.name}");
        }

        [Rpc(SendTo.Server)]
        void ReleaseRpc(NetworkObjectReference itemRef, RpcParams p = default)
        {
            if (itemRef.TryGet(out var no) && no.TryGetComponent<Carryable>(out var c) && c.HolderClient.Value == p.Receive.SenderClientId)
                c.HolderClient.Value = Carryable.NoHolder;
        }

        [Rpc(SendTo.Owner)]
        void FlashRpc(string msg) => Flash(msg);

        // A held thing never collides with the one holding it
        void IgnoreSelf(Carryable c, bool ignore)
        {
            var me = GetComponent<CharacterController>();
            if (me == null || c == null) return;
            foreach (var col in c.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(me, col, ignore);
        }

        public static bool CanHide(Defect d) =>
            d.State != RepairState.Fixed && (JobManager.Instance == null || JobManager.Instance.Phase == JobPhase.Renovation);

        void Flash(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            Message = msg;
            _messageUntil = Time.time + 2f;
        }

        public override void OnNetworkDespawn()
        {
            if (_held != null) DropLocal(Vector3.zero);
        }
    }
}
