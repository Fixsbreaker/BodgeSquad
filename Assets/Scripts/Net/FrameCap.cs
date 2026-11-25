using UnityEngine;

namespace BodgeSquad.Net
{
    // Keeps the game from rendering flat out: VSync on and 60 fps, 30 fps when the window isn't
    // focused (a host alt-tabbed to Discord keeps the game running, but cool). Without this the game
    // drew hundreds of frames a second, pegged the GPU and overheated laptops — bad enough that
    // plugging in a projector mid-game made macOS's window server hang.
    public static class FrameCap
    {
        public const int Focused = 60, Background = 30;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            QualitySettings.vSyncCount = 1;          // match the display (no tearing, no wasted frames)
            Application.targetFrameRate = Focused;   // backup cap where VSync is ignored
            Application.focusChanged -= OnFocus;
            Application.focusChanged += OnFocus;
            if (Debug.isDebugBuild) new GameObject("FpsProbe") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<FpsProbe>();
        }

        // Development builds: log the real frame rate every 5 s (to check the cap works)
        class FpsProbe : MonoBehaviour
        {
            float _t; int _frames;
            void Update()
            {
                _frames++; _t += Time.unscaledDeltaTime;
                if (_t < 5f) return;
                Debug.Log($"[FrameCap] {_frames / _t:0} fps (vSync {QualitySettings.vSyncCount}, target {Application.targetFrameRate}, focused {Application.isFocused})");
                _t = 0f; _frames = 0;
            }
        }

        static void OnFocus(bool focused)
        {
            if (Time.captureFramerate > 0) return; // trailer recording runs on its own clock
            QualitySettings.vSyncCount = focused ? 1 : 0;
            Application.targetFrameRate = focused ? Focused : Background;
        }
    }
}
