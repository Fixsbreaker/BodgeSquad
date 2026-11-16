using UnityEngine;
using UnityEngine.InputSystem;

namespace BodgeSquad.Players
{
    // Input for the one local bodger on this machine. Only the owner's copy is enabled
    // (NetworkBodger turns it on), so remote players never read this PC's keyboard or pads.
    public class BodgerInput : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;

        InputActionAsset _instance;
        public InputAction Move { get; private set; }
        public InputAction Look { get; private set; }
        public InputAction Jump { get; private set; }
        public InputAction Sprint { get; private set; }
        public InputAction Interact { get; private set; }
        public InputAction Attack { get; private set; }

        // Mouse vs stick look, and which button names to show in prompts
        public bool UsesKeyboard { get; private set; } = true;

        // Menus (pause, typing a code) switch gameplay input off
        public static bool Blocked { get; set; }

        public bool Ready => _instance != null;

        void OnEnable()
        {
            if (_instance == null)
            {
                _instance = Instantiate(actions);
                var map = _instance.FindActionMap("Player", true);
                Move = map["Move"]; Look = map["Look"]; Jump = map["Jump"]; Sprint = map["Sprint"];
                Interact = map["Interact"]; Attack = map["Attack"];
            }
            _instance.FindActionMap("Player").Enable();
        }

        void OnDisable() { if (_instance != null) _instance.FindActionMap("Player").Disable(); }

        void OnDestroy() { if (_instance != null) Destroy(_instance); }

        void Update()
        {
            if (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame &&
                (Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.1f || Gamepad.current.rightStick.ReadValue().sqrMagnitude > 0.1f ||
                 Gamepad.current.buttonNorth.isPressed || Gamepad.current.buttonSouth.isPressed))
                UsesKeyboard = false;
            else if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
                     (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 4f))
                UsesKeyboard = true;
        }

        // Gameplay reads go through these so menus can block them
        public Vector2 ReadMove() => Blocked ? Vector2.zero : Move.ReadValue<Vector2>();
        public Vector2 ReadLook() => Blocked ? Vector2.zero : Look.ReadValue<Vector2>();
        public bool JumpPressed() => !Blocked && Jump.WasPressedThisFrame();
        public bool SprintHeld() => !Blocked && Sprint.IsPressed();
        public bool InteractPressed() => !Blocked && Interact.WasPressedThisFrame();
        public bool AttackPressed() => !Blocked && Attack.WasPressedThisFrame();
        // Read the button itself: in the Editor the release can get lost when the Game view loses focus
        public bool AttackHeld()
        {
            if (Blocked || !Application.isFocused) return false;
            if (UsesKeyboard) return Mouse.current != null && Mouse.current.leftButton.isPressed;
            return Gamepad.current != null && Gamepad.current.buttonWest.isPressed;
        }
    }
}
