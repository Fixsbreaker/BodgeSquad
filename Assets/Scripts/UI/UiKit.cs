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

        // Green → yellow → red for the obviousness meter
        public static Color Risk(float t) =>
            t < 0.5f ? Color.Lerp(Palette.Pass, Palette.TintYellow, t * 2f) : Color.Lerp(Palette.TintYellow, Palette.Accent, (t - 0.5f) * 2f);
    }
}
