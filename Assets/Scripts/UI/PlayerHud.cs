using BodgeSquad.Art;
using BodgeSquad.Interaction;
using BodgeSquad.Job;
using TMPro;
using UnityEngine;

namespace BodgeSquad.UI
{
    // Per-player HUD inside that player's split-screen viewport (Screen Space - Camera):
    // crosshair, context prompt, feedback line and the Flaw Obviousness meter.
    public class PlayerHud : MonoBehaviour
    {
        [SerializeField] Camera playerCamera;
        [SerializeField] CarryInteractor interactor;

        TextMeshProUGUI _prompt, _message, _meterLabel;
        UnityEngine.UI.Image _crosshair, _meterFill;
        GameObject _meter;
        RectTransform[] _brackets;
        RectTransform _rootRect;
        readonly Vector3[] _corners = new Vector3[8];

        void Start()
        {
            // Lives at scene root on the UI layer: the player's own body layer is culled by its camera.
            var canvas = UiKit.Canvas($"PlayerHudCanvas_{name}", null, RenderMode.ScreenSpaceCamera, playerCamera, 10);
            _canvasGo = canvas.gameObject;
            var root = canvas.transform;

            _crosshair = UiKit.Panel("Crosshair", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(10, 10), UiKit.WithAlpha(Palette.UiPaper, 0.9f));
            _prompt = UiKit.Text("Prompt", root, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 60), new Vector2(1600, 140), 44, TextAlignmentOptions.Bottom);
            _message = UiKit.Text("Message", root, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 210), new Vector2(1500, 70), 40, TextAlignmentOptions.Bottom, Palette.Accent);

            // Obviousness meter, right of the crosshair
            var meterBg = UiKit.Panel("ObviousnessMeter", root, new Vector2(.5f, .5f), new Vector2(0, .5f), new Vector2(60, -40), new Vector2(330, 26), UiKit.WithAlpha(Palette.UiInk, 0.75f));
            _meter = meterBg.gameObject;
            _meterFill = UiKit.Panel("Fill", meterBg.transform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(3, 0), new Vector2(0, 20), Palette.Pass);
            _meterLabel = UiKit.Text("Label", meterBg.transform, new Vector2(0, 1), new Vector2(0, 0), new Vector2(0, 4), new Vector2(560, 50), 32, TextAlignmentOptions.BottomLeft);
            _meter.SetActive(false);

            // Target brackets: four yellow corners around whatever E would pick up
            _brackets = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                var corner = UiKit.Rect($"Bracket{i}", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(34, 34));
                bool right = i % 2 == 1, top = i >= 2;
                UiKit.Panel("H", corner, new Vector2(right ? 1 : 0, top ? 1 : 0), new Vector2(right ? 1 : 0, top ? 1 : 0), Vector2.zero, new Vector2(34, 6), Palette.TintYellow);
                UiKit.Panel("V", corner, new Vector2(right ? 1 : 0, top ? 1 : 0), new Vector2(right ? 1 : 0, top ? 1 : 0), Vector2.zero, new Vector2(6, 34), Palette.TintYellow);
                _brackets[i] = corner;
            }
            _rootRect = (RectTransform)root;

            int uiLayer = LayerMask.NameToLayer("UI");
            foreach (var t in _canvasGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = uiLayer;
            playerCamera.cullingMask |= 1 << uiLayer;
        }

        GameObject _canvasGo;

        void PlaceBrackets(Carryable target)
        {
            Vector2 min = default, max = default;
            bool show = target != null && TryScreenRect(target, out min, out max);
            foreach (var b in _brackets) b.gameObject.SetActive(show);
            if (!show) return;
            float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.06f;
            Vector2 c = (min + max) / 2, half = (max - min) / 2 * pulse + new Vector2(14, 14);
            _brackets[0].anchoredPosition = c + new Vector2(-half.x + 17, -half.y + 17);
            _brackets[1].anchoredPosition = c + new Vector2(half.x - 17, -half.y + 17);
            _brackets[2].anchoredPosition = c + new Vector2(-half.x + 17, half.y - 17);
            _brackets[3].anchoredPosition = c + new Vector2(half.x - 17, half.y - 17);
        }

        bool TryScreenRect(Carryable t, out Vector2 min, out Vector2 max)
        {
            min = max = Vector2.zero;
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return false;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            Vector3 lo = b.min, hi = b.max;
            int k = 0;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                _corners[k++] = new Vector3(x == 0 ? lo.x : hi.x, y == 0 ? lo.y : hi.y, z == 0 ? lo.z : hi.z);
            min = new Vector2(float.MaxValue, float.MaxValue); max = new Vector2(float.MinValue, float.MinValue);
            foreach (var w in _corners)
            {
                var sp = playerCamera.WorldToScreenPoint(w);
                if (sp.z < 0.05f) return false;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rootRect, sp, playerCamera, out var lp)) return false;
                min = Vector2.Min(min, lp); max = Vector2.Max(max, lp);
            }
            return true;
        }

        void OnDestroy()
        {
            if (_canvasGo != null) Destroy(_canvasGo);
        }

        void Update()
        {
            if (_prompt == null || interactor == null) return;
            bool kb = interactor.UsesKeyboard;
            string use = kb ? "[E]" : "[Y]";
            string toss = kb ? "[LMB]" : "[X]";

            var held = interactor.Held;
            var kit = held != null ? held.GetComponent<RepairKit>() : null;
            var cover = held != null ? held.GetComponent<Concealer>() : null;
            var defect = interactor.LookDefect;
            string prompt = "";
            float? meter = null;
            string meterText = null;
            string accent = ColorUtility.ToHtmlStringRGB(Palette.Accent);

            string work = kb ? "[LMB]" : "[X]";
            if (kit != null)
            {
                bool click = RepairRules.Mode(kit.Type) == RepairRules.ToolMode.Click;
                string how = click ? $"{work} {RepairRules.UseVerb(kit.Type)}" : $"Hold {work} and sweep to {RepairRules.UseVerb(kit.Type)}";
                string left = $"{RepairRules.KitName(kit.Type)} left: {(click ? $"{Mathf.CeilToInt(kit.Left * RepairRules.Capacity(kit.Type))} strips" : $"{kit.Left:P0}")}";
                if (defect != null && defect.State != RepairState.Fixed)
                {
                    bool wrongFix = RepairRules.IsProperFix(kit.Type) && RepairRules.ProperKitFor(defect.Kind) != kit.Type;
                    string progress = wrongFix ? $"<color=#{accent}>won't fix {RepairRules.KindName(defect.Kind)}, it just smears over it</color>"
                        : RepairRules.IsProperFix(kit.Type) ? $"fixed {defect.FixProgress:P0} (needs {RepairRules.FixThreshold:P0})"
                        : $"covered {defect.Coverage:P0}";
                    prompt = $"{how}  ·  {progress}\n<size=75%>{left}   ·   {use} Drop</size>";
                }
                else prompt = $"{how} on anything\n<size=75%>{left}   ·   {use} Drop</size>";
            }
            else if (cover != null && cover.WallHung)
            {
                prompt = $"{use} Hang it on the wall where you look   {toss} Throw";
            }
            else if (held != null)
            {
                prompt = $"{use} Drop   {toss} Throw";
                if (cover != null) prompt = $"Holding the {cover.DisplayName}: put it down in front of a defect  ·  " + prompt;
            }
            else if (interactor.LookPressable?.PressPrompt is string press)
            {
                prompt = $"{use} {char.ToUpper(press[0])}{press.Substring(1)}";
            }
            else if (interactor.LookCarryable != null)
            {
                var c = interactor.LookCarryable;
                var k = c.GetComponent<RepairKit>();
                var conc = c.GetComponent<Concealer>();
                if (k != null && !k.Paid) prompt = $"{use} Buy {RepairRules.KitName(k.Type)}  ${k.Price}\n<size=75%>{RepairRules.KitBlurb(k.Type)}</size>";
                else if (k != null) prompt = $"{use} Pick up {RepairRules.KitName(k.Type)}\n<size=75%>{RepairRules.KitBlurb(k.Type)}</size>";
                else if (conc != null) prompt = conc.WallHung
                    ? $"{use} Pick up {conc.DisplayName}\n<size=75%>Free cover: hang it over a defect</size>"
                    : $"{use} Pick up {conc.DisplayName}\n<size=75%>Free cover: put it down in front of a defect</size>";
                else prompt = $"{use} Pick up";
            }
            else if (defect != null && defect.State != RepairState.Fixed)
            {
                var proper = RepairRules.ProperKitFor(defect.Kind);
                prompt = $"{defect.DisplayName}  ·  covered {defect.Coverage:P0}\n<size=75%>Fix: {RepairRules.KitName(proper)} ${RepairRules.Price(proper)}   ·   Hide: paint, wallpaper, tape or furniture</size>";
            }

            if (defect != null && meter == null)
            {
                meter = defect.Obviousness;
                meterText = $"{RepairRules.SeverityName(defect.Severity)}  ·  {defect.Obviousness:P0} chance to get caught";
            }

            _prompt.text = prompt;
            _message.text = interactor.Message ?? "";
            var target = held == null ? interactor.LookCarryable : null;
            _crosshair.color = UiKit.WithAlpha(target != null ? Palette.TintYellow : defect != null ? Palette.Accent : Palette.UiPaper, 0.9f);
            PlaceBrackets(target);

            _meter.SetActive(meter.HasValue);
            if (meter.HasValue)
            {
                _meterFill.rectTransform.sizeDelta = new Vector2(324f * meter.Value, 20);
                _meterFill.color = UiKit.Risk(meter.Value);
                _meterLabel.text = meterText;
            }
        }
    }
}
