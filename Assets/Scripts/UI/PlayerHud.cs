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
            _prompt = UiKit.Text("Prompt", root, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 70), new Vector2(1500, 90), 46, TextAlignmentOptions.Bottom);
            _message = UiKit.Text("Message", root, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 170), new Vector2(1500, 70), 40, TextAlignmentOptions.Bottom, Palette.Accent);

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
            var defect = interactor.LookDefect;
            string prompt = "";
            float? meter = null;
            string meterText = null;

            if (kit != null && defect != null)
            {
                if (defect.CanApply(kit.Type, out var why))
                {
                    var after = RepairRules.IsProperFix(kit.Type) ? RepairState.Fixed : RepairRules.ConcealWith(kit.Type);
                    float risk = RepairRules.Obviousness(after, defect.Severity);
                    prompt = $"{use} {(after == RepairState.Fixed ? "Fix" : "Bodge")} {defect.DisplayName} with {RepairRules.KitName(kit.Type)}";
                    meter = risk;
                    meterText = $"After: {risk:P0} obvious";
                }
                else prompt = $"<color=#{ColorUtility.ToHtmlStringRGB(Palette.Accent)}>{why}</color>";
            }
            else if (held != null)
            {
                prompt = $"{use} Drop   {toss} Throw";
                if (kit != null) prompt = $"Holding {RepairRules.KitName(kit.Type)}  ·  " + prompt;
            }
            else if (interactor.LookPressable?.PressPrompt is string press)
            {
                prompt = $"{use} {char.ToUpper(press[0])}{press.Substring(1)}";
            }
            else if (interactor.LookCarryable != null)
            {
                var k = interactor.LookCarryable.GetComponent<RepairKit>();
                prompt = k != null && !k.Paid ? $"{use} Buy {RepairRules.KitName(k.Type)}  ${k.Price}" : $"{use} Pick up";
            }

            if (defect != null && meter == null)
            {
                meter = defect.Obviousness;
                meterText = $"{defect.DisplayName}: {RepairRules.SeverityName(defect.Severity)}  ·  {defect.Obviousness:P0} obvious";
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
