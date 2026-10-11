using UnityEngine;
using UnityEngine.InputSystem;

namespace ShooterMashup.Player
{
    /// <summary>
    /// Reads keyboard and mouse and turns them into PlayerCmds, the way ET's cl_input.c does.
    /// Mouse look is sampled every frame; commands are built once per simulation tick.
    /// </summary>
    public class LocalPlayerInput : MonoBehaviour
    {
        [Header("Mouse (ET/Quake scale, so you can bring your ET sensitivity)")]
        public float sensitivity = 5f;
        public float mYaw = 0.022f;
        public float mPitch = 0.022f;
        public bool invertPitch;

        [Header("Behaviour")]
        [Tooltip("Run by default; hold Walk to walk silently (ET's cl_run 1).")]
        public bool alwaysRun = true;
        [Tooltip("Press crouch to toggle instead of holding it.")]
        public bool crouchToggle;
        [Tooltip("ET: double-tap forward while crouched to go prone, double-tap back to get up.")]
        public bool doubleTapProne = true;
        public int doubleTapWindowMs = 250;
        public bool lockCursorOnStart = true;

        [Tooltip("Set by tools that use Q/E for something else (the build tool).")]
        public bool leanEnabled = true;

        /// <summary>A menu owns the mouse: no look, and clicks don't recapture the cursor.</summary>
        [HideInInspector] public bool uiOpen;

        public float Pitch { get; private set; }
        public float Yaw { get; private set; }

        InputAction forward, back, moveLeft, moveRight;
        InputAction jump, crouch, prone, sprint, walk, leanLeft, leanRight;

        bool crouchToggled;
        bool pendingProne;
        DoubleTap pendingDoubleTap;
        DoubleTap lastTapDir;
        double lastTapTime;

        void Awake()
        {
            forward = Key("Forward", "<Keyboard>/w", "<Keyboard>/upArrow");
            back = Key("Back", "<Keyboard>/s", "<Keyboard>/downArrow");
            moveLeft = Key("MoveLeft", "<Keyboard>/a");
            moveRight = Key("MoveRight", "<Keyboard>/d");
            jump = Key("Jump", "<Keyboard>/space");
            crouch = Key("Crouch", "<Keyboard>/c", "<Keyboard>/leftCtrl");
            prone = Key("Prone", "<Keyboard>/x");
            sprint = Key("Sprint", "<Keyboard>/leftShift");
            walk = Key("Walk", "<Keyboard>/leftAlt");
            leanLeft = Key("LeanLeft", "<Keyboard>/q");
            leanRight = Key("LeanRight", "<Keyboard>/e");
        }

        static InputAction Key(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (string b in bindings)
                action.AddBinding(b);
            return action;
        }

        void OnEnable()
        {
            foreach (InputAction a in All()) a.Enable();
            if (lockCursorOnStart) SetCursorLocked(true);
        }

        void OnDisable()
        {
            foreach (InputAction a in All()) a.Disable();
        }

        void OnDestroy()
        {
            foreach (InputAction a in All()) a.Dispose();
        }

        InputAction[] All() => new[] { forward, back, moveLeft, moveRight, jump, crouch, prone, sprint, walk, leanLeft, leanRight };

        public static bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Set the raw look angles, e.g. when spawning facing a given direction.</summary>
        public void SetAngles(float pitch, float yaw)
        {
            Pitch = pitch;
            Yaw = yaw;
        }

        /// <summary>Call once per rendered frame, before any BuildCmd calls that frame.</summary>
        public void SampleFrame()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            // While a menu is open it handles the cursor itself.
            if (!uiOpen)
            {
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                    SetCursorLocked(false);
                else if (mouse != null && !CursorLocked && mouse.leftButton.wasPressedThisFrame)
                    SetCursorLocked(true);
            }

            if (mouse != null && CursorLocked && !uiOpen)
            {
                Vector2 d = mouse.delta.ReadValue();
                Yaw += d.x * sensitivity * mYaw;
                Pitch += (invertPitch ? 1f : -1f) * d.y * sensitivity * mPitch;
            }

            if (crouch.WasPressedThisFrame())
                crouchToggled = !crouchToggled;
            if (prone.WasPressedThisFrame())
                pendingProne = true;

            if (doubleTapProne)
            {
                CheckDoubleTap(forward, DoubleTap.Forward);
                CheckDoubleTap(back, DoubleTap.Back);
                CheckDoubleTap(moveLeft, DoubleTap.Left);
                CheckDoubleTap(moveRight, DoubleTap.Right);
            }
        }

        void CheckDoubleTap(InputAction action, DoubleTap dir)
        {
            if (!action.WasPressedThisFrame())
                return;
            double now = Time.unscaledTimeAsDouble * 1000.0;
            if (lastTapDir == dir && now - lastTapTime < doubleTapWindowMs)
            {
                pendingDoubleTap = dir;
                lastTapDir = DoubleTap.None;
            }
            else
            {
                lastTapDir = dir;
                lastTapTime = now;
            }
        }

        /// <summary>Build the command for one simulation tick.</summary>
        public PlayerCmd BuildCmd(int serverTime)
        {
            bool walking = walk.IsPressed() ^ !alwaysRun;
            int speed = walking ? 64 : 127;

            int f = (forward.IsPressed() ? speed : 0) - (back.IsPressed() ? speed : 0);
            int r = (moveRight.IsPressed() ? speed : 0) - (moveLeft.IsPressed() ? speed : 0);
            bool crouching = crouchToggle ? crouchToggled : crouch.IsPressed();
            int u = (jump.IsPressed() ? 127 : 0) - (crouching ? 127 : 0);

            var buttons = CmdButtons.None;
            if (sprint.IsPressed()) buttons |= CmdButtons.Sprint;
            if (walking) buttons |= CmdButtons.Walk;
            if (leanEnabled && leanLeft.IsPressed()) buttons |= CmdButtons.LeanLeft;
            if (leanEnabled && leanRight.IsPressed()) buttons |= CmdButtons.LeanRight;
            if (pendingProne) buttons |= CmdButtons.Prone;

            var cmd = new PlayerCmd
            {
                serverTime = serverTime,
                forwardMove = (sbyte)f,
                rightMove = (sbyte)r,
                upMove = (sbyte)u,
                pitch = Pitch,
                yaw = Yaw,
                buttons = buttons,
                doubleTap = pendingDoubleTap,
            };

            // One-shot inputs are consumed by the first command that sees them.
            pendingProne = false;
            pendingDoubleTap = DoubleTap.None;
            return cmd;
        }
    }
}
