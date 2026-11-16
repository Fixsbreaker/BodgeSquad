using BodgeSquad.Art;
using TMPro;
using UnityEngine;

namespace BodgeSquad.UI
{
    // Tiny helpers to build uGUI + TextMeshPro hierarchies from code with palette colors.
    public static class UiKit
    {
        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static UnityEngine.UI.Image Panel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            var rt = Rect(name, parent, anchor, pivot, pos, size);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
                                           float fontSize, TextAlignmentOptions align, Color? color = null)
        {
            var rt = Rect(name, parent, anchor, pivot, pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = color ?? Palette.UiPaper;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.outlineWidth = 0.18f;
            t.outlineColor = Palette.UiInk;
            return t;
        }

        public static Canvas Canvas(string name, Transform parent, RenderMode mode, Camera cam, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = mode;
            c.worldCamera = cam;
            c.planeDistance = 0.2f;
            c.sortingOrder = order;
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return c;
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        public static UnityEngine.UI.Button Button(string name, Transform parent, Vector2 pos, Vector2 size, string label, Color bg, Color fg, UnityEngine.Events.UnityAction onClick)
        {
            var img = Panel(name, parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), pos, size, bg);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<UnityEngine.UI.Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.8f);
            b.colors = colors;
            b.onClick.AddListener(onClick);
            var t = Text("Label", img.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, size, 34, TextAlignmentOptions.Center, fg);
            t.text = $"<b>{label}</b>";
            t.outlineWidth = 0f;
            return b;
        }

        public static TMP_InputField Input(string name, Transform parent, Vector2 pos, Vector2 size, string placeholder, int maxChars)
        {
            var bg = Panel(name, parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), pos, size, Palette.UiPaper);
            bg.raycastTarget = true;
            var area = Rect("TextArea", bg.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, size - new Vector2(24, 8));
            area.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var ph = Text("Placeholder", area, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, area.sizeDelta, 32, TextAlignmentOptions.Center, WithAlpha(Palette.UiInk, 0.45f));
            ph.text = placeholder; ph.outlineWidth = 0f; ph.fontStyle = FontStyles.Italic;
            var txt = Text("Text", area, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, area.sizeDelta, 36, TextAlignmentOptions.Center, Palette.UiInk);
            txt.outlineWidth = 0f;
            var field = bg.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = txt;
            field.placeholder = ph;
            field.characterLimit = maxChars;
            field.targetGraphic = bg;
            return field;
        }

        // Green → yellow → red for the obviousness meter
        public static Color Risk(float t) =>
            t < 0.5f ? Color.Lerp(Palette.Pass, Palette.TintYellow, t * 2f) : Color.Lerp(Palette.TintYellow, Palette.Accent, (t - 0.5f) * 2f);
    }
}
