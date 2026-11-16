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

        TextMeshProUGUI _top, _feed, _report;
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
            _feed = UiKit.Text("Feed", root, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -86), new Vector2(1400, 60), 36, TextAlignmentOptions.Top);

            // Work order: the task list, top-left
            var orderBg = UiKit.Panel("WorkOrder", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16), new Vector2(520, 330), UiKit.WithAlpha(Palette.UiInk, 0.78f));
            _orderPanel = orderBg.gameObject;
            _order = UiKit.Text("WorkOrderText", orderBg.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -12), new Vector2(490, 310), 24, TextAlignmentOptions.TopLeft);
            _order.outlineWidth = 0f;

            // How to play: shown on the join screen, toggled in game
            var help = UiKit.Panel("HowToPlay", root, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 40), new Vector2(1240, 380), UiKit.WithAlpha(Palette.UiInk, 0.9f));
            _help = help.gameObject;
            var helpText = UiKit.Text("HowToPlayText", help.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1180, 350), 27, TextAlignmentOptions.TopLeft);
            helpText.outlineWidth = 0f;
            string y = Hex(Palette.TintYellow), r = Hex(Palette.Accent), g = Hex(Palette.Pass), b = Hex(Palette.PlayerTints[2]);
            helpText.text =
                $"<size=125%><b><color=#{y}>HOW TO PLAY</color></b></size>\n" +
                $"<b>Goal:</b> get the house past the inspector. Deal with every defect on the <b>work order</b>, then ring the <b>doorbell</b>. Every red flag costs you money.\n\n" +
                $"<color=#{y}>1.</color> Grab an item from the <b>SUPPLIES</b> table. Picking it up pays for it from the shared budget.\n" +
                $"     <color=#{b}>Blue tag</color> = proper fix (safe, expensive).   <color=#{r}>Red tag</color> = bodge (cheap, might get caught).\n" +
                $"<color=#{y}>2.</color> Look at a defect (the crosshair turns red) and press <b>[E] / (Y)</b> to use the item on it.\n" +
                $"<color=#{y}>3.</color> <color=#{g}><b>Free trick:</b></color> pick up furniture or a picture, look at a defect and press <b>[E]</b> to hide it behind it.\n" +
                $"<color=#{y}>4.</color> The meter next to the crosshair shows the chance the inspector catches it.\n\n" +
                $"<size=85%>Move <b>WASD</b> · Look <b>mouse</b> · Run <b>Shift</b> · Jump <b>Space</b> · Use/Grab/Drop <b>E</b> · Throw <b>LMB</b> · Help <b>Tab</b>     Gamepad: <b>Y</b> use · <b>X</b> throw · <b>Select</b> help</size>";
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
            _orderPanel.SetActive(!lobby && job.Phase != JobPhase.Results);
            if (Time.time >= _nextOrderRefresh) { _nextOrderRefresh = Time.time + 0.25f; RefreshOrder(job); }
            if (Time.time > _feedUntil) _feed.text = "";

            string money = $"BUDGET <b>${job.Budget}</b>   SPENT ${job.Spent}";
            _top.text = job.Phase switch
            {
                JobPhase.Renovation => $"{money}   ·   {(int)(job.TimeLeft / 60):0}:{(int)(job.TimeLeft % 60):00}",
                JobPhase.Inspection => $"{money}   ·   <color=#{Hex(Palette.Accent)}>INSPECTION</color>",
                _ => $"{money}   ·   REPORT",
            };
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
                sb.AppendLine($"• {d.DisplayName} <size=80%>[{RepairRules.SeverityName(d.Severity)}]</size> — {RepairRules.StateVerb(d.EffectiveState)}{who}   {verdict}");
            }
            bool passed = job.CaughtCount == 0;
            sb.AppendLine();
            sb.AppendLine($"ROOM: {(passed ? $"<color=#{Hex(Palette.Pass)}>PASSED</color>" : $"<color=#{Hex(Palette.Accent)}>FAILED ({job.CaughtCount} flags)</color>")}");
            sb.AppendLine($"Contract ${RepairRules.ContractFee}  −  penalties ${job.Penalties}  =  paid <b>${job.Payout}</b>");
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
                    note = $"<color=#AAAAAA>needs {RepairRules.KitName(k)}</color>";
                }
                else
                {
                    handled++;
                    mark = $"<color=#{Hex(Palette.TintYellow)}>[~]</color>";
                    note = $"<color=#{Hex(UiKit.Risk(d.Obviousness))}>{RepairRules.StateVerb(st).Replace(" over", "").Replace("hidden behind furniture", "behind furniture")} {d.Obviousness:P0}</color>";
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
