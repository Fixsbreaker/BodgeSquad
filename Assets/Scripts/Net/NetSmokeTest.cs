using System.Collections;
using BodgeSquad.Interaction;
using BodgeSquad.Job;
using BodgeSquad.Players;
using UnityEngine;

namespace BodgeSquad.Net
{
    // Automated check for builds: with -bodgeSmoke, this client buys filler, fixes a crack and
    // rings the doorbell, logging what it sees. Does nothing in normal play.
    public class NetSmokeTest : MonoBehaviour
    {
        string _shots;
        void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            int sd = System.Array.IndexOf(args, "-bodgeShotsDir");
            if (sd >= 0 && sd + 1 < args.Length) _shots = args[sd + 1];
            if (System.Array.IndexOf(args, "-bodgeSmoke") >= 0) StartCoroutine(Run());
            int shot = System.Array.IndexOf(args, "-bodgeShot");
            if (shot >= 0 && shot + 1 < args.Length) StartCoroutine(Shot(args[shot + 1]));
        }

        // -bodgeShot <file.png>: screenshot (UI included) after a few seconds, then quit
        IEnumerator Shot(string file)
        {
            yield return new WaitForSeconds(4f);
            ScreenCapture.CaptureScreenshot(file);
            yield return new WaitForSeconds(1f);
            Debug.Log($"[Smoke] Screenshot {file}");
            Application.Quit();
        }

        IEnumerator Run()
        {
            while (NetworkBodger.Local == null) yield return null;
            yield return new WaitForSeconds(3f);
            var me = NetworkBodger.Local;
            var job = JobManager.Instance;
            Debug.Log($"[Smoke] I am P{me.Index + 1}; players seen: {NetworkBodger.All.Count}; budget {job.Budget}");

            // Pick-up from awkward angles: stand 1.4 m from the houseplant and look at it from above, side, low
            var carryTest = me.GetComponent<CarryInteractor>();
            var fpcTest = me.GetComponent<FirstPersonController>();
            var plant = GameObject.Find("Houseplant");
            if (plant != null)
            {
                int ok = 0, total = 0;
                foreach (var (dir, height) in new[] { (Vector3.back, 0f), (Vector3.left, 0f), (Vector3.forward, 0f), (Vector3.right, 0f), (new Vector3(1, 0, 1).normalized, 0f) })
                {
                    var p = plant.transform.position;
                    var spot = new Vector3(p.x, 0f, p.z) - dir * 1.4f;
                    spot.x = Mathf.Clamp(spot.x, -5.5f, 5.5f); spot.z = Mathf.Clamp(spot.z, -4.5f, 4.5f);
                    fpcTest.Teleport(spot, Quaternion.LookRotation(new Vector3(p.x - spot.x, 0, p.z - spot.z)));
                    fpcTest.enabled = false;
                    yield return null;
                    var b = plant.GetComponentInChildren<Renderer>().bounds;
                    carryTest.Eye.LookAt(b.center);
                    yield return null; yield return null;
                    total++;
                    bool seen = carryTest.LookCarryable != null && carryTest.LookCarryable.gameObject == plant;
                    if (seen) ok++;
                    Debug.Log($"[Smoke] Look at plant from {dir}: {(seen ? "can grab" : "NOT grabbable")} (pitch {carryTest.Eye.eulerAngles.x:0}) ray: {carryTest.DebugLook}");
                    if (_shots != null) { ScreenCapture.CaptureScreenshot($"{_shots}/angle_{total}.png"); yield return null; yield return null; }
                    fpcTest.enabled = true;
                }
                Debug.Log($"[Smoke] Grab angles OK {ok}/{total}");
                // straight down onto the plank lying on the floor, standing next to it
                var plank = GameObject.Find("Plank");
                if (plank != null)
                {
                    var pp = plank.transform.position;
                    fpcTest.Teleport(new Vector3(pp.x + 0.6f, 0f, pp.z), Quaternion.identity);
                    fpcTest.enabled = false;
                    yield return null;
                    carryTest.Eye.LookAt(plank.GetComponent<Collider>().bounds.center);
                    yield return null; yield return null;
                    Debug.Log($"[Smoke] Plank from above: {(carryTest.LookCarryable != null ? "can grab " + carryTest.LookCarryable.name : "NOT grabbable")} ray: {carryTest.DebugLook}");
                    if (_shots != null) { ScreenCapture.CaptureScreenshot($"{_shots}/plank.png"); yield return null; yield return null; }
                    fpcTest.enabled = true;
                }
            }

            // Drop a half-used tool: it must stay in the world with what's left in it
            {
                RepairKit paint = null;
                foreach (var k in FindObjectsByType<RepairKit>()) if (k.Type == KitType.PaintTin && !k.Paid) paint = k;
                if (paint != null)
                {
                    var c0 = me.GetComponent<CarryInteractor>();
                    var at = paint.transform.position + Vector3.back * 1.4f; at.y = 0f;
                    me.GetComponent<FirstPersonController>().Teleport(at, Quaternion.LookRotation(Vector3.forward));
                    yield return new WaitForSeconds(0.3f);
                    c0.TestGrab(paint.GetComponent<Carryable>());
                    yield return new WaitForSeconds(1f);
                    paint.Spend(2f);
                    yield return new WaitForSeconds(0.3f);
                    float before = paint.Left;
                    c0.TestDrop();
                    yield return new WaitForSeconds(2f);
                    bool alive = paint != null && paint.gameObject.activeInHierarchy && paint.IsSpawned;
                    Debug.Log($"[Smoke] Dropped paint with {before:P0} left → still there: {alive}, left now {(alive ? paint.Left : -1f):P0}, at {(alive ? paint.transform.position.ToString("F2") : "-")}");
                }
            }

            RepairKit filler = null;
            foreach (var k in FindObjectsByType<RepairKit>()) if (k.Type == KitType.Filler && !k.Paid) filler = k;
            if (filler == null) { Debug.LogError("[Smoke] FAIL: no filler on the shelf"); yield break; }
            var carry = me.GetComponent<CarryInteractor>();
            var stand = filler.transform.position + Vector3.back * 1.4f; stand.y = 0f;
            me.GetComponent<FirstPersonController>().Teleport(stand, Quaternion.LookRotation(Vector3.forward));
            yield return new WaitForSeconds(0.5f);
            carry.TestGrab(filler.GetComponent<Carryable>());
            yield return new WaitForSeconds(1.5f);
            Debug.Log($"[Smoke] Holding: {(carry.Held != null ? carry.Held.name : "nothing")}; owner? {(carry.Held != null && carry.Held.IsOwner)}; budget now {job.Budget} (spent {job.Spent})");

            Defect crack = null;
            foreach (var d in job.Defects) if (d.Kind == DefectKind.Crack) { crack = d; break; }
            // paint a sloppy patch on the wall next to the crack and look at it (screenshot)
            {
                var carryP = carry; var fpcP = me.GetComponent<FirstPersonController>();
                var hole = crack;
                foreach (var d in job.Defects) if (d.Label == "north wall") hole = d;
                var front = hole.transform.position + hole.transform.forward * 1.8f; front.y = 0f;
                fpcP.Teleport(front, Quaternion.LookRotation(-hole.transform.forward));
                fpcP.enabled = false;
                yield return null;
                for (int i = 0; i < 26; i++)
                {
                    var p = hole.transform.position + hole.transform.right * (Mathf.Sin(i * 0.7f) * 0.45f) + hole.transform.up * (Mathf.Cos(i * 0.45f) * 0.3f);
                    if (Physics.Raycast(p + hole.transform.forward * 0.5f, -hole.transform.forward, out var h, 1f))
                        BodgeSquad.Job.SurfaceMarks.Instance.Place(KitType.PaintTin, h, 0.26f, i * 37f);
                }
                yield return new WaitForSeconds(0.5f);
                carryP.Eye.LookAt(hole.transform.position);
                yield return null; yield return null;
                if (_shots != null) { ScreenCapture.CaptureScreenshot($"{_shots}/paint.png"); yield return null; yield return null; }
                fpcP.enabled = true;
            }
            carry.TestSweep(crack, 3f);
            yield return new WaitForSeconds(1.5f);
            Debug.Log($"[Smoke] {crack.DisplayName} state seen here: {crack.State} (fixed {crack.FixProgress:P0}) by P{crack.ResponsiblePlayer + 1}; filler left {(carry.HeldKit != null ? carry.HeldKit.Left : -1f):P0}");

            // ---- the early inspector: bodge in front of him and get caught; noise; radio
            {
                var insp = Inspector.Instance;
                var fpcI = me.GetComponent<FirstPersonController>();
                float t0 = Time.time;
                while (!insp.Present && Time.time - t0 < 20f) yield return null;
                yield return new WaitForSeconds(1.5f);
                Debug.Log($"[Smoke] Inspector present: {insp.Present}, mode {insp.CurrentMode}, at {insp.transform.position:F1}");
                if (_shots != null)
                {
                    var side = insp.transform.position - insp.transform.forward * 1.5f + insp.transform.right * 2.5f; side.y = 0f;
                    side.x = Mathf.Clamp(side.x, -5.3f, 5.3f); side.z = Mathf.Clamp(side.z, -4.3f, 4.3f);
                    fpcI.Teleport(side, Quaternion.LookRotation(insp.transform.position + insp.transform.forward * 2f - side));
                    fpcI.enabled = false;
                    yield return null;
                    me.GetComponent<CarryInteractor>().Eye.LookAt(insp.transform.position + insp.transform.forward * 2f);
                    yield return new WaitForSeconds(0.3f);
                    ScreenCapture.CaptureScreenshot($"{_shots}/inspector_cone.png");
                    yield return null; yield return null;
                    fpcI.enabled = true;
                }
                t0 = Time.time;
                float peak = 0f;
                var mode0 = insp.CurrentMode;
                while (job.Fines == 0 && Time.time - t0 < 10f)
                {
                    var front = insp.transform.position + insp.transform.forward * 2.2f; front.y = 0f;
                    fpcI.Teleport(front, Quaternion.LookRotation(insp.transform.position - front));
                    var h = new RaycastHit { point = front + Vector3.up * 1.1f, normal = -insp.transform.forward };
                    SurfaceMarks.Instance.Place(KitType.PaintTin, h, 0.2f, 0f);
                    peak = Mathf.Max(peak, insp.Suspicion);
                    if (insp.CurrentMode != mode0) { Debug.Log($"[Smoke] Inspector mode → {insp.CurrentMode} (suspicion {insp.Suspicion:P0})"); mode0 = insp.CurrentMode; }
                    yield return new WaitForSeconds(0.1f);
                }
                Debug.Log($"[Smoke] Bodging in front of the inspector: fines {job.Fines} after {Time.time - t0:0.0}s (peak suspicion seen {peak:P0}); noted defects: {System.Linq.Enumerable.Count(job.Defects, d => d.Noted)}");
                if (_shots != null) { ScreenCapture.CaptureScreenshot($"{_shots}/caught.png"); yield return null; yield return null; }
                fpcI.Teleport(new Vector3(-1f, 0f, 0f), Quaternion.identity);
                yield return new WaitForSeconds(6f); // scold + grace

                if (Unity.Netcode.NetworkManager.Singleton.IsServer)
                {
                    insp.ToggleRadioRpc(me.Index);
                    bool went = false; t0 = Time.time;
                    yield return new WaitForSeconds(0.3f);
                    bool on = insp.RadioOn;
                    while (insp.RadioOn && Time.time - t0 < 15f) { went |= insp.CurrentMode == Inspector.Mode.Radio; yield return null; }
                    Debug.Log($"[Smoke] Radio: switched on {on}, inspector went over {went}, off again {!insp.RadioOn} after {Time.time - t0:0.0}s");
                    yield return new WaitForSeconds(1.5f);
                    // sneak up behind him and lob a paint tin at his head
                    RepairKit tin = null;
                    foreach (var k in FindObjectsByType<RepairKit>()) if (k.Type == KitType.PaintTin && !k.Paid) tin = k;
                    var ci = me.GetComponent<CarryInteractor>();
                    if (tin != null)
                    {
                        var at = tin.transform.position + Vector3.back * 1.2f; at.y = 0f;
                        fpcI.Teleport(at, Quaternion.LookRotation(Vector3.forward));
                        yield return new WaitForSeconds(0.3f);
                        ci.TestGrab(tin.GetComponent<Carryable>());
                        yield return new WaitForSeconds(0.8f);
                        int puddles0 = Spills.Instance.Count;
                        var behind = insp.transform.position - insp.transform.forward * 2.0f; behind.y = 0f;
                        fpcI.Teleport(behind, Quaternion.LookRotation(insp.transform.position - behind));
                        fpcI.enabled = false;
                        yield return null;
                        ci.Eye.LookAt(insp.transform.position + Vector3.up * 1.4f);
                        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
                        ci.TestThrow();
                        bool blinded = false; t0 = Time.time;
                        while (Time.time - t0 < 2f) { blinded |= insp.CurrentMode == Inspector.Mode.Blinded; if (blinded) break; yield return null; }
                        fpcI.enabled = true;
                        Debug.Log($"[Smoke] Paint tin at the inspector's head: blinded {blinded}, fines now {job.Fines}, puddles {puddles0} → {Spills.Instance.Count}");
                        yield return new WaitForSeconds(3.5f);
                        Debug.Log($"[Smoke] Inspector after blinding: {insp.CurrentMode}");
                    }
                }
                else
                {
                    insp.NoiseRpc(new Vector3(3.5f, 0.2f, -3.5f), 1f);
                    yield return new WaitForSeconds(0.5f);
                    Debug.Log($"[Smoke] Noise: inspector mode now {insp.CurrentMode}");
                    // a puddle in the middle of the room, and a run straight through it
                    Spills.Instance.SpillRpc(new Vector3(0.5f, 0f, -0.5f), 0.8f);
                    yield return new WaitForSeconds(0.6f);
                    fpcI.Teleport(new Vector3(-0.3f, 0f, -0.5f), Quaternion.LookRotation(Vector3.right));
                    yield return null;
                    fpcI.TestRun(new Vector3(6f, 0f, 0f));
                    bool down = false; t0 = Time.time;
                    while (Time.time - t0 < 1f) { down |= me.IsDown; if (down) break; yield return null; }
                    if (_shots != null) { yield return new WaitForSeconds(0.4f); ScreenCapture.CaptureScreenshot($"{_shots}/slipped.png"); yield return null; yield return null; }
                    Debug.Log($"[Smoke] Ran into a paint puddle ({Spills.Instance.Count} puddles): fell over {down}");
                    yield return new WaitForSeconds(2.6f);
                    Debug.Log($"[Smoke] Back on feet: {!me.IsDown && !fpcI.IsDown}");
                    yield return new WaitForSeconds(6f);
                }
            }

            job.RingDoorbellRpc(me.Index);
            yield return new WaitForSeconds(2f);
            Debug.Log($"[Smoke] Phase seen here: {job.Phase}");
            while (job.Phase != JobPhase.Results) yield return null;
            Debug.Log($"[Smoke] Report seen here: caught {job.CaughtCount}, payout {job.Payout}, spent {job.Spent}, profit {job.Profit}");
            Debug.Log("[Smoke] PASS");
        }
    }
}
