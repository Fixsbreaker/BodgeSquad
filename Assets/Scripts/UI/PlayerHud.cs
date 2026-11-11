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

            int uiLayer = LayerMask.NameToLayer("UI");
            foreach (var t in _canvasGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = uiLayer;
            playerCamera.cullingMask |= 1 << uiLayer;
        }

        GameObject _canvasGo;

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

            if (kit != null && defect != null)
            {
                if (defect.CanApply(kit.Type, out var why))
                {
                    var after = RepairRules.IsProperFix(kit.Type) ? RepairState.Fixed : RepairRules.ConcealWith(kit.Type);
                    float risk = RepairRules.Obviousness(after, defect.Severity);
                    prompt = $"{use} {(after == RepairState.Fixed ? "Fix" : "Hide")} {defect.DisplayName} with {RepairRules.KitName(kit.Type)}";
                    meter = risk;
                    meterText = $"After: {risk:P0} chance to get caught";
                }
                else prompt = $"<color=#{accent}>{why}</color>";
            }
            else if (cover != null && defect != null && CarryInteractor.CanHide(defect))
            {
                float risk = RepairRules.Obviousness(RepairState.HiddenByFurniture, defect.Severity);
                prompt = $"{use} Hide {defect.DisplayName} behind the {cover.DisplayName}  <size=75%>(free)</size>";
                meter = risk;
                meterText = $"If it covers it: {risk:P0} chance to get caught";
            }
            else if (held != null)
            {
                prompt = $"{use} Drop   {toss} Throw";
                if (kit != null) prompt = $"Holding {RepairRules.KitName(kit.Type)}: look at a defect  ·  " + prompt;
                else if (cover != null) prompt = $"Holding {cover.DisplayName}: look at a defect to hide it  ·  " + prompt;
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
                else if (conc != null) prompt = $"{use} Pick up {conc.DisplayName}\n<size=75%>Free cover: carry it to a defect and press {use} again</size>";
                else prompt = $"{use} Pick up";
            }
            else if (defect != null && defect.State != RepairState.Fixed)
            {
                var proper = RepairRules.ProperKitFor(defect.Kind);
                prompt = $"{defect.DisplayName}\n<size=75%>Fix: {RepairRules.KitName(proper)} ${RepairRules.Price(proper)}   ·   Hide: paint, wallpaper, tape or furniture</size>";
            }

            if (defect != null && meter == null)
            {
                meter = defect.Obviousness;
                meterText = $"{RepairRules.SeverityName(defect.Severity)}  ·  {defect.Obviousness:P0} chance to get caught";
            }

            _prompt.text = prompt;
            _message.text = interactor.Message ?? "";
            _crosshair.color = UiKit.WithAlpha(defect != null ? Palette.Accent : Palette.UiPaper, 0.9f);

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
