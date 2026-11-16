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
        void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
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
            carry.TestUseHeldOn(crack);
            yield return new WaitForSeconds(1.5f);
            Debug.Log($"[Smoke] {crack.DisplayName} state seen here: {crack.State} by P{crack.ResponsiblePlayer + 1}");

            job.RingDoorbellRpc(me.Index);
            yield return new WaitForSeconds(2f);
            Debug.Log($"[Smoke] Phase seen here: {job.Phase}");
            while (job.Phase != JobPhase.Results) yield return null;
            Debug.Log($"[Smoke] Report seen here: caught {job.CaughtCount}, payout {job.Payout}, spent {job.Spent}, profit {job.Profit}");
            Debug.Log("[Smoke] PASS");
        }
    }
}
