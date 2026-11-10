using System.Text;
using BodgeSquad.Art;
using BodgeSquad.Job;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.UI
{
    // Shared HUD over the whole screen: budget + timer, the inspection feed, the join hint,
    // and the final inspection report.
    public class JobHud : MonoBehaviour
    {
        [SerializeField] PlayerInputManager players;

        TextMeshProUGUI _top, _feed, _join, _report;
        GameObject _reportPanel;
        float _feedUntil;

        void Start()
        {
            var canvas = UiKit.Canvas("JobHudCanvas", transform, RenderMode.ScreenSpaceOverlay, null, 20);
            var root = canvas.transform;

            var bar = UiKit.Panel("TopBar", root, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -12), new Vector2(760, 64), UiKit.WithAlpha(Palette.UiInk, 0.82f));
            _top = UiKit.Text("Status", bar.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(740, 60), 34, TextAlignmentOptions.Center);
            _feed = UiKit.Text("Feed", root, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -86), new Vector2(1400, 60), 36, TextAlignmentOptions.Top);
            _join = UiKit.Text("JoinHint", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -60), new Vector2(1400, 200), 56, TextAlignmentOptions.Center);
            _join.text = "BODGE SQUAD\n<size=60%>Press any button to join (up to 4 players)</size>";

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

            _join.gameObject.SetActive(players != null && players.playerCount == 0);
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
            sb.AppendLine("<size=80%>Press R / Start to take another job</size>");
            _report.text = sb.ToString();
            _reportPanel.SetActive(true);
            Debug.Log($"[Job] Report: caught {job.CaughtCount}, payout {job.Payout}, spent {job.Spent}, profit {job.Profit}");
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
