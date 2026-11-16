using BodgeSquad.Art;
using BodgeSquad.Net;
using BodgeSquad.Players;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace BodgeSquad.UI
{
    // Title screen (host / join) and the in-game pause panel (room code, resume, leave).
    public class MainMenu : MonoBehaviour
    {
        [SerializeField] Sprite backdrop;   // the itch.io key art

        GameObject _menu, _pause;
        RectTransform _art, _tagline;
        TMP_InputField _code, _ip;
        TextMeshProUGUI _status, _pauseInfo, _roomTag;
        UnityEngine.UI.Button[] _buttons;
        bool _paused;

        void Start()
        {
            EnsureEventSystem();
            var canvas = UiKit.Canvas("MenuCanvas", transform, RenderMode.ScreenSpaceOverlay, null, 50);
            canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var root = canvas.transform;

            // ---- title screen: key art full-bleed, menu column on the right
            _menu = UiKit.Rect("Menu", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1920, 1080)).gameObject;
            var m = _menu.transform;
            var ink = Palette.UiInk; var paper = Palette.UiPaper;

            UiKit.Panel("Fill", m, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(4000, 3000), ink);
            if (backdrop != null)
            {
                var art = UiKit.Panel("KeyArt", m, new Vector2(.5f, .5f), new Vector2(.42f, 1f), Vector2.zero, new Vector2(1920, 1080), Color.white);
                art.sprite = backdrop;
                art.preserveAspect = false;
                var fit = art.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
                fit.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = backdrop.rect.width / backdrop.rect.height;
                art.rectTransform.anchorMin = Vector2.zero; art.rectTransform.anchorMax = Vector2.one;
                _art = art.rectTransform;
            }

            // right-hand column with a slanted edge
            var col = UiKit.Panel("Column", m, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(60, 0), new Vector2(720, 1400), UiKit.WithAlpha(ink, 0.88f));
            col.rectTransform.localRotation = Quaternion.Euler(0, 0, -4f);
            var edge = UiKit.Panel("Edge", m, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-646, 0), new Vector2(14, 1400), Palette.TintYellow);
            edge.rectTransform.localRotation = Quaternion.Euler(0, 0, -4f);
            var c = UiKit.Rect("Content", m, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-60, 0), new Vector2(560, 1000));

            var tag = UiKit.Text("Tagline", c, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(10, -10), new Vector2(540, 60), 27, TextAlignmentOptions.Center, Palette.TintYellow);
            tag.text = "<b>Bodge it · Patch it · Hide it · Pass!?</b>";
            _tagline = tag.rectTransform;

            Head(c, "ONLINE", "anywhere, with a room code", 360);
            var hostOnline = Fancy(c, "HostOnline", new Vector2(0, 268), new Vector2(520, 84), "HOST GAME", Palette.Accent, paper, () => NetSession.Instance.HostOnline());
            _code = UiKit.Input("CodeInput", c, new Vector2(-95, 168), new Vector2(330, 72), "ROOM CODE", 8);
            _code.onValidateInput = (_, _, ch) => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '\0';
            var joinOnline = Fancy(c, "JoinOnline", new Vector2(175, 168), new Vector2(170, 72), "JOIN", Palette.TintYellow, ink, () => NetSession.Instance.JoinOnline(_code.text));

            Head(c, "SAME NETWORK", "same Wi-Fi, by IP", 60);
            var hostLan = Fancy(c, "HostLan", new Vector2(0, -32), new Vector2(520, 76), "HOST ON LAN", Palette.CharacterSecondary, paper, () => NetSession.Instance.HostLan());
            _ip = UiKit.Input("IpInput", c, new Vector2(-95, -128), new Vector2(330, 68), "192.168.1.20", 40);
            var joinLan = Fancy(c, "JoinLan", new Vector2(175, -128), new Vector2(170, 68), "JOIN", Palette.PlayerTints[2], ink, () => NetSession.Instance.JoinLan(_ip.text));

            var quit = Fancy(c, "Quit", new Vector2(0, -250), new Vector2(260, 60), "QUIT", UiKit.WithAlpha(paper, 0.15f), paper, Application.Quit);
            _buttons = new[] { hostOnline, joinOnline, hostLan, joinLan, quit };

            _status = UiKit.Text("Status", c, new Vector2(.5f, .5f), new Vector2(.5f, 1), new Vector2(0, -305), new Vector2(560, 110), 26, TextAlignmentOptions.Top, paper);
            var foot = UiKit.Text("Footer", c, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 10), new Vector2(560, 60), 22, TextAlignmentOptions.Bottom, UiKit.WithAlpha(paper, 0.7f));
            foot.text = "Up to 4 bodgers, each on their own PC.\nOne hosts, the others join.";

            // ---- in-game pause (Esc)
            _pause = UiKit.Panel("Pause", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(700, 400), UiKit.WithAlpha(ink, 0.93f)).gameObject;
            var pt = _pause.transform;
            var title = UiKit.Text("PauseTitle", pt, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 140), new Vector2(660, 60), 44, TextAlignmentOptions.Center, Palette.TintYellow);
            title.text = "<b>PAUSED</b>";
            _pauseInfo = UiKit.Text("PauseInfo", pt, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 60), new Vector2(660, 90), 30, TextAlignmentOptions.Center, paper);
            UiKit.Button("Resume", pt, new Vector2(0, -50), new Vector2(420, 72), "RESUME", Palette.Pass, ink, () => SetPaused(false));
            UiKit.Button("Leave", pt, new Vector2(0, -140), new Vector2(420, 72), "LEAVE GAME", Palette.Accent, paper, () => { SetPaused(false); NetSession.Instance.Leave(); });
            _pause.SetActive(false);

            _roomTag = UiKit.Text("RoomTag", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -16), new Vector2(520, 44), 26, TextAlignmentOptions.TopRight, paper);
        }

        static void Head(Transform parent, string title, string sub, float y)
        {
            var t = UiKit.Text(title, parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, y), new Vector2(520, 50), 34, TextAlignmentOptions.Left, Palette.UiPaper);
            t.text = $"<b>{title}</b>   <size=65%><color=#{ColorUtility.ToHtmlStringRGB(Palette.TintYellow)}>{sub}</color></size>";
            t.outlineWidth = 0f;
        }

        // Chunky button: ink drop shadow + a face that springs on hover/select
        static UnityEngine.UI.Button Fancy(Transform parent, string name, Vector2 pos, Vector2 size, string label, Color bg, Color fg, UnityEngine.Events.UnityAction onClick)
        {
            UiKit.Panel(name + "Shadow", parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), pos + new Vector2(6, -8), size, UiKit.WithAlpha(Palette.UiInk, 0.9f));
            var b = UiKit.Button(name, parent, pos, size, label, bg, fg, onClick);
            b.gameObject.AddComponent<ButtonJuice>();
            return b;
        }

        void Logo(Transform parent, Vector2 pos)
        {
            var logo = UiKit.Rect("Logo", parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), pos, new Vector2(900, 380));
            var shadow = UiKit.Panel("SignShadow", logo, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(10, 52), new Vector2(760, 210), Palette.UiInk);
            shadow.rectTransform.localRotation = Quaternion.Euler(0, 0, 3f);
            var sign = UiKit.Panel("Sign", logo, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 62), new Vector2(760, 210), Palette.Accent);
            sign.rectTransform.localRotation = Quaternion.Euler(0, 0, 3f);
            var bodge = UiKit.Text("Bodge", sign.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, -4), new Vector2(760, 210), 170, TextAlignmentOptions.Center, Palette.UiPaper);
            bodge.text = "<b>BODGE</b>"; bodge.outlineWidth = 0.3f; bodge.characterSpacing = 4f;
            var tape = UiKit.Panel("Tape", logo, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(40, -70), new Vector2(560, 120), Palette.TintYellow);
            tape.rectTransform.localRotation = Quaternion.Euler(0, 0, -2.5f);
            var squad = UiKit.Text("Squad", tape.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(560, 120), 96, TextAlignmentOptions.Center, Palette.UiInk);
            squad.text = "<b>SQUAD</b>"; squad.outlineWidth = 0f; squad.characterSpacing = 10f;
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(null);
        }

        void Update()
        {
            var net = NetSession.Instance;
            if (net == null || _menu == null) return;
            bool inGame = net.InGame;

            _menu.SetActive(!inGame);
            if (!inGame)
            {
                float t = Time.unscaledTime;
                if (_art != null) _art.localScale = Vector3.one * (1.04f + Mathf.Sin(t * 0.15f) * 0.03f); // slow drift
                if (_tagline != null) _tagline.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 1.3f) * 1.5f);
                _status.text = net.Status;
                foreach (var b in _buttons) b.interactable = !net.Busy;
                if (_paused) SetPaused(false);
                if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                BodgerInput.Blocked = true;
                _roomTag.text = "";
                return;
            }

            string where = !string.IsNullOrEmpty(net.RoomCode) ? $"Room code: <b>{net.RoomCode}</b>"
                         : Unity.Netcode.NetworkManager.Singleton.IsServer ? $"LAN host: <b>{NetSession.LocalIp()}</b>" : "LAN game";
            _roomTag.text = where;
            if (EscPressed()) SetPaused(!_paused);
            if (_paused) _pauseInfo.text = $"{where}\n<size=80%>{CountPlayers()} of {NetSession.MaxPlayers} bodgers</size>";
            BodgerInput.Blocked = _paused;
        }

        static int CountPlayers() => NetworkBodger.All.Count;

        static bool EscPressed()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
            foreach (var pad in Gamepad.all) if (pad.startButton.wasPressedThisFrame && Job.JobManager.Instance?.Phase != Job.JobPhase.Results) return true;
            return false;
        }

        void SetPaused(bool on)
        {
            _paused = on;
            _pause.SetActive(on);
            Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = on;
        }
    }
}
