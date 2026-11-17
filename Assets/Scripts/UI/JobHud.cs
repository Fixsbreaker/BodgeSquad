using System.Text;
using BodgeSquad.Art;
using BodgeSquad.Job;
using BodgeSquad.Players;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.UI
{
    // Shared HUD over the whole screen: budget + timer, the inspection feed, the join hint,
    // and the final inspection report.
    public class JobHud : MonoBehaviour
    {

        TextMeshProUGUI _top, _feed, _report, _inspText;
        GameObject _inspBar;
        UnityEngine.UI.Image _suspFill;
        GameObject _reportPanel, _help, _orderPanel;
        float _autoHelpUntil = -1f;
        TextMeshProUGUI _order, _helpHint;
        bool _helpToggled;
        float _nextOrderRefresh;
        float _feedUntil;

        void Start()
        {
            var canvas = UiKit.Canvas("JobHudCanvas", transform, RenderMode.ScreenSpaceOverlay, null, 20);
            var root = canvas.transform;

            var bar = UiKit.Panel("TopBar", root, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -12), new Vector2(760, 64), UiKit.WithAlpha(Palette.UiInk, 0.82f));
            _top = UiKit.Text("Status", bar.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(740, 60), 34, TextAlignmentOptions.Center);
            _feed = UiKit.Text("Feed", root, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -136), new Vector2(1500, 60), 36, TextAlignmentOptions.Top);

            // Where the inspector is and how suspicious he is, under the money bar
            var ib = UiKit.Panel("InspectorBar", root, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -80), new Vector2(760, 46), UiKit.WithAlpha(Palette.UiInk, 0.72f));
            _inspBar = ib.gameObject;
            _inspText = UiKit.Text("InspectorText", ib.transform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(14, 0), new Vector2(470, 42), 26, TextAlignmentOptions.Left);
            _inspText.outlineWidth = 0f;
            var meterBg = UiKit.Panel("SuspicionBg", ib.transform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-14, 0), new Vector2(250, 20), UiKit.WithAlpha(Palette.UiPaper, 0.15f));
            ((RectTransform)meterBg.transform).pivot = new Vector2(1, .5f);
            _suspFill = UiKit.Panel("SuspicionFill", meterBg.transform, new Vector2(0, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(0, 20), Palette.TintYellow);
            ((RectTransform)_suspFill.transform).pivot = new Vector2(0, .5f);

            // Work order: the task list, top-left
            var orderBg = UiKit.Panel("WorkOrder", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16), new Vector2(520, 330), UiKit.WithAlpha(Palette.UiInk, 0.78f));
            _orderPanel = orderBg.gameObject;
            _order = UiKit.Text("WorkOrderText", orderBg.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -12), new Vector2(490, 310), 24, TextAlignmentOptions.TopLeft);
            _order.outlineWidth = 0f;

            // How to play: shown on the join screen, toggled in game
            var help = UiKit.Panel("HowToPlay", root, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -136), new Vector2(1280, 540), UiKit.WithAlpha(Palette.UiInk, 0.9f));
            _help = help.gameObject;
            var helpText = UiKit.Text("HowToPlayText", help.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1220, 510), 26, TextAlignmentOptions.TopLeft);
            helpText.outlineWidth = 0f;
            string y = Hex(Palette.TintYellow), r = Hex(Palette.Accent), g = Hex(Palette.Pass), b = Hex(Palette.PlayerTints[2]);
            helpText.text =
                $"<size=125%><b><color=#{y}>HOW TO PLAY</color></b></size>\n" +
                $"<b>Goal:</b> get the house past the inspector. Deal with the cracks, holes, mould and bad wiring, then ring the <b>doorbell</b>. Every red flag costs money.\n\n" +
                $"<color=#{y}>1.</color> Grab an item from the <b>SUPPLIES</b> table (it's paid from the shared budget). <color=#{b}>Blue tag</color> = proper fix. <color=#{r}>Red tag</color> = cheap bodge.\n" +
                $"<color=#{y}>2.</color> <b>Hold [LMB]</b> and sweep the tool over the damage. Tape: <b>click</b> per strip. Tools run dry.\n" +
                $"<color=#{y}>3.</color> <color=#{r}><b>The inspector drops in while you work.</b></color> If he <b>sees</b> you painting, papering or taping (the striped cone on the floor), " +
                $"his suspicion fills and you're <b>caught red-handed</b>: −${RepairRules.RedHandedFine} and that defect is flagged for sure. Proper fixes are fine in front of him.\n" +
                $"<color=#{y}>4.</color> <color=#{g}><b>Distract him:</b></color> throw something so it crashes (he goes to look), switch on the <b>radio</b>, or <b>throw it AT him</b>: stunned 4s, a paint tin blinds him 3s (−${RepairRules.AssaultFine} if he sees who threw it).\n" +
                $"     Careful: paint tins burst into <b>slippery puddles</b>. Run through one and you're on your back, and so is the inspector if he walks into it!\n" +
                $"<color=#{y}>5.</color> Free cover: push furniture in front of a defect, or hang a picture with <b>[E]</b>.\n\n" +
                $"<size=85%>Move <b>WASD</b> · Look <b>mouse</b> · Run <b>Shift</b> · Jump <b>Space</b> · Grab/Drop/Use <b>E</b> · Work <b>LMB</b> · Throw <b>RMB</b> · Help <b>Tab</b>     Gamepad: <b>Y</b> grab · <b>X</b> work · <b>LB</b> throw · <b>Select</b> help</size>";
            _help.SetActive(false);
            _helpHint = UiKit.Text("HelpHint", root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 10), new Vector2(520, 40), 24, TextAlignmentOptions.BottomRight);
            _helpHint.text = "[Tab] / (Select)  How to play";

            var panel = UiKit.Panel("Report", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1240, 820), UiKit.WithAlpha(Palette.UiInk, 0.92f));
            _reportPanel = panel.gameObject;
            _report = UiKit.Text("ReportText", panel.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1160, 760), 34, TextAlignmentOptions.TopLeft);
            _report.outlineWidth = 0f;
            _reportPanel.SetActive(false);

            var job = JobManager.Instance;
            job.Announce += OnAnnounce;
            job.PhaseChanged += OnPhase;
        }

        void OnDestroy()
        {
            var job = JobManager.Instance;
            if (job == null) return;
            job.Announce -= OnAnnounce;
            job.PhaseChanged -= OnPhase;
        }

        void OnAnnounce(string line)
        {
            _feed.text = line;
            _feedUntil = Time.time + 4f;
        }

        void OnPhase(JobPhase p)
        {
            if (p == JobPhase.Results) ShowReport();
        }

        void Update()
        {
            var job = JobManager.Instance;
            if (job == null || _top == null) return;

            bool lobby = NetworkBodger.Local == null; // the main menu is up
            if (!lobby && _autoHelpUntil < 0f) _autoHelpUntil = Time.time + 12f; // show the basics right after joining
            if (lobby) _autoHelpUntil = -1f;
            if (HelpPressed())
            {
                if (Time.time < _autoHelpUntil) { _autoHelpUntil = 0f; _helpToggled = false; }
                else _helpToggled = !_helpToggled;
            }
            bool inGame = job.Phase == JobPhase.Renovation;
            _help.SetActive(!lobby && inGame && (_helpToggled || Time.time < _autoHelpUntil));
            _top.transform.parent.gameObject.SetActive(!lobby);
            _helpHint.gameObject.SetActive(!lobby && inGame);
            _orderPanel.SetActive(false); // no checklist: finding the damage is part of the job
            if (Time.time > _feedUntil) _feed.text = "";
            UpdateInspectorBar(job, !lobby && inGame);

            string money = $"BUDGET <b>${job.Budget}</b>   SPENT ${job.Spent}";
            _top.text = job.Phase switch
            {
                JobPhase.Renovation => $"{money}   ·   {(int)(job.TimeLeft / 60):0}:{(int)(job.TimeLeft % 60):00}",
                JobPhase.Inspection => $"{money}   ·   <color=#{Hex(Palette.Accent)}>INSPECTION</color>",
                _ => $"{money}   ·   REPORT",
            };
        }

        void UpdateInspectorBar(JobManager job, bool show)
        {
            var insp = Inspector.Instance;
            _inspBar.SetActive(show && insp != null);
            if (!show || insp == null) return;
            string r = Hex(Palette.Accent), y = Hex(Palette.TintYellow), g = Hex(Palette.Pass);
            int secs = Mathf.Max(0, Mathf.CeilToInt(insp.NextVisitAt - job.Elapsed));
            _inspText.text = insp.CurrentMode switch
            {
                Inspector.Mode.Away => $"INSPECTOR: <color=#{g}>outside</color>  <size=85%>back in ~{secs}s</size>",
                Inspector.Mode.Patrol or Inspector.Mode.LookAround => $"INSPECTOR: <color=#{y}>looking around</color>",
                Inspector.Mode.Investigate => $"INSPECTOR: <color=#{g}>checking a noise</color>",
                Inspector.Mode.Radio => $"INSPECTOR: <color=#{g}>off to kill the radio</color>",
                Inspector.Mode.Suspicious => $"INSPECTOR: <color=#{r}><b>SUSPICIOUS</b></color>",
                Inspector.Mode.Scold => $"INSPECTOR: <color=#{r}><b>CAUGHT YOU!</b></color>",
                Inspector.Mode.Leaving => $"INSPECTOR: <color=#{g}>leaving for a bit</color>",
                Inspector.Mode.Stunned => $"INSPECTOR: <color=#{g}><b>SEEING STARS</b></color>",
                Inspector.Mode.Blinded => $"INSPECTOR: <color=#{g}><b>BLINDED BY PAINT</b></color>",
                Inspector.Mode.Slipped => $"INSPECTOR: <color=#{g}><b>FLAT ON HIS BACK</b></color>",
                _ => "INSPECTOR",
            };
            float s = insp.Suspicion;
            ((RectTransform)_suspFill.transform).sizeDelta = new Vector2(250f * s, 20);
            _suspFill.color = Color.Lerp(Palette.TintYellow, Palette.Accent, s);
        }

        void ShowReport()
        {
            var job = JobManager.Instance;
            var sb = new StringBuilder();
            sb.AppendLine("<size=140%><b>INSPECTION REPORT</b></size>\n");
            foreach (var d in job.Defects)
            {
                string who = d.ResponsiblePlayer >= 0 ? $" <color=#{Hex(Palette.PlayerTints[d.ResponsiblePlayer])}>P{d.ResponsiblePlayer + 1}</color>" : "";
                string verdict = d.State == RepairState.Fixed
                    ? $"<color=#{Hex(Palette.Pass)}>PASS</color>"
                    : d.Caught
                        ? $"<color=#{Hex(Palette.Accent)}>FLAGGED −${RepairRules.Penalty(d.Severity)}</color>"
                        : $"<color=#{Hex(Palette.TintYellow)}>GOT AWAY WITH IT</color>";
                if (d.Noted && d.State != RepairState.Fixed) verdict += $" <size=80%><color=#{Hex(Palette.Accent)}>(he saw you)</color></size>";
                sb.AppendLine($"• {d.DisplayName} <size=80%>[{RepairRules.SeverityName(d.Severity)}]</size> — {RepairRules.StateVerb(d.EffectiveState)}{who}   {verdict}");
            }
            bool passed = job.CaughtCount == 0;
            sb.AppendLine();
            sb.AppendLine($"ROOM: {(passed ? $"<color=#{Hex(Palette.Pass)}>PASSED</color>" : $"<color=#{Hex(Palette.Accent)}>FAILED ({job.CaughtCount} flags)</color>")}");
            if (job.Fines > 0) sb.AppendLine($"<color=#{Hex(Palette.Accent)}>Caught red-handed: −${job.Fines} in fines</color>");
            sb.AppendLine($"Contract ${RepairRules.ContractFee}  −  penalties ${job.Penalties}{(job.Fines > 0 ? $"  −  fines ${job.Fines}" : "")}  =  paid <b>${job.Payout}</b>");
            sb.AppendLine($"Materials spent ${job.Spent}");
            var profitColor = job.Profit >= 0 ? Palette.Pass : Palette.Accent;
            sb.AppendLine($"<size=130%>PROFIT <b><color=#{Hex(profitColor)}>${job.Profit}</color></b></size>\n");
            sb.AppendLine(Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer
                ? "<size=80%>Press R / Start to take another job</size>" : "<size=80%>Waiting for the host to start another job…</size>");
            _report.text = sb.ToString();
            _reportPanel.SetActive(true);
            Debug.Log($"[Job] Report: caught {job.CaughtCount}, payout {job.Payout}, spent {job.Spent}, profit {job.Profit}");
        }

        static bool HelpPressed()
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.tabKey.wasPressedThisFrame || kb.f1Key.wasPressedThisFrame)) return true;
            foreach (var pad in Gamepad.all) if (pad.selectButton.wasPressedThisFrame) return true;
            return false;
        }

        void RefreshOrder(JobManager job)
        {
            int handled = 0;
            var sb = new StringBuilder();
            foreach (var d in job.Defects)
            {
                var st = d.EffectiveState;
                string mark, note;
                if (st == RepairState.Fixed) { mark = $"<color=#{Hex(Palette.Pass)}>[OK]</color>"; note = $"<color=#{Hex(Palette.Pass)}>fixed</color>"; handled++; }
                else if (st == RepairState.Untouched)
                {
                    mark = "<color=#888888>[  ]</color>";
                    var k = RepairRules.ProperKitFor(d.Kind);
                    note = d.FixProgress > 0f
                        ? $"<color=#{Hex(Palette.TintYellow)}>fixing {d.FixProgress:P0}</color>"
                        : $"<color=#AAAAAA>needs {RepairRules.KitName(k)}</color>";
                }
                else
                {
                    handled++;
                    mark = $"<color=#{Hex(Palette.TintYellow)}>[~]</color>";
                    note = $"<color=#{Hex(UiKit.Risk(d.Obviousness))}>{RepairRules.StateVerb(st).Replace(" over", "").Replace("hidden behind furniture", "behind furniture")} {d.Coverage:P0} · risk {d.Obviousness:P0}</color>";
                }
                string sev = d.Severity == Severity.Safety ? $" <color=#{Hex(Palette.Accent)}>!</color>" : "";
                sb.AppendLine($"{mark} {RepairRules.KindName(d.Kind)}{sev} <size=80%>· {d.Label}</size>  <size=85%>{note}</size>");
            }
            string head = job.Phase == JobPhase.Renovation
                ? (handled == job.Defects.Count ? $"<color=#{Hex(Palette.Pass)}>All done: ring the doorbell!</color>" : $"{handled}/{job.Defects.Count} handled")
                : "Inspection in progress…";
            _order.text = $"<b><color=#{Hex(Palette.TintYellow)}>WORK ORDER</color></b>  <size=85%>{head}</size>\n" + sb;
            var rt = (RectTransform)_orderPanel.transform;
            rt.sizeDelta = new Vector2(520, 56 + 30 * job.Defects.Count);
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
