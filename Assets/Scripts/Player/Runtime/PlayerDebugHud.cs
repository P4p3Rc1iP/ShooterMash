using UnityEngine;
using UnityEngine.InputSystem;

namespace ShooterMashup.Player
{
    /// <summary>
    /// Developer overlay: speedometer, stance, stamina, health, crosshair, plus debug keys
    /// (F1 noclip, F2 respawn, F3 toggle overlay, F4 ET-exact / rounded values).
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerDebugHud : MonoBehaviour
    {
        public bool show = true;

        PlayerMotor motor;
        PlayerVitals vitals;
        GUIStyle style;
        GUIStyle bigStyle;
        float smoothedFps;
        float peakSpeed;
        float peakTimer;
        string lastEvent = "";
        float lastEventTime;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            vitals = GetComponent<PlayerVitals>();
        }

        void OnEnable() => motor.MoveEvent += OnMoveEvent;
        void OnDisable() => motor.MoveEvent -= OnMoveEvent;

        void OnMoveEvent(PmEvent e)
        {
            if (e.type == PmEventType.Footstep)
                return;
            lastEvent = e.ToString();
            lastEventTime = Time.time;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null)
                return;

            if (kb.f1Key.wasPressedThisFrame)
                motor.SetPmType(motor.State.pmType == PmType.Noclip ? PmType.Normal : PmType.Noclip);
            if (kb.f2Key.wasPressedThisFrame)
            {
                if (vitals != null) vitals.Respawn();
                else motor.Respawn();
            }
            if (kb.f3Key.wasPressedThisFrame)
                show = !show;
            if (kb.f4Key.wasPressedThisFrame)
            {
                motor.SetPreset(motor.Preset == MovePreset.EtExact ? MovePreset.Rounded : MovePreset.EtExact);
                lastEvent = $"values: {motor.Preset}";
                lastEventTime = Time.time;
            }

            smoothedFps = Mathf.Lerp(smoothedFps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
        }

        void OnGUI()
        {
            if (!show)
                return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
                style.normal.textColor = Color.white;
                bigStyle = new GUIStyle(style) { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }

            PlayerState s = motor.State;
            float speed = new Vector2(s.velocity.x, s.velocity.z).magnitude;

            peakTimer -= Time.deltaTime;
            if (speed > peakSpeed || peakTimer <= 0f)
            {
                peakSpeed = speed;
                peakTimer = 2f;
            }

            // Crosshair.
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.DrawTexture(new Rect(cx - 1, cy - 8, 2, 6), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1, cy + 2, 2, 6), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 8, cy - 1, 6, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 2, cy - 1, 6, 2), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Speedometer.
            GUI.Label(new Rect(cx - 100, Screen.height - 110, 200, 40), speed.ToString("0.0"), bigStyle);

            string stance = s.pmType == PmType.Noclip ? "NOCLIP"
                : s.IsProne ? "prone"
                : s.IsDucked ? "crouch"
                : s.IsOnLadder ? "ladder"
                : "stand";

            string text =
                $"<b>{Mathf.RoundToInt(smoothedFps)} fps</b>  tick {motor.tickRate} Hz   <b>values: {(motor.Preset == MovePreset.EtExact ? "ET exact" : "rounded")}</b>\n" +
                $"speed  {speed:0.00} m/s  ({speed * 3.6f:0.0} km/h, {speed / 0.0254f:0} ET ups)  peak {peakSpeed:0.00}\n" +
                $"vert   {s.velocity.y:0.00} m/s\n" +
                $"stance {stance}   ground {(s.onGround ? "yes" : "no")}   water {s.waterLevel}\n" +
                $"lean   {s.leanf:0.00} m   stamina {s.stamina:0.00} / {s.modifiers.staminaMax:0.00}\n" +
                $"pos    {s.origin.x:0.00} {s.origin.y:0.00} {s.origin.z:0.00} m\n" +
                $"event  {(Time.time - lastEventTime < 2f ? lastEvent : "")}\n" +
                "\n<color=#aaa>WASD move, Space jump, C/Ctrl crouch, X prone,\n" +
                "Shift sprint, Alt walk, Q/E lean, double-tap W crouched = prone\n" +
                "F1 noclip, F2 respawn, F3 hide, F4 ET-exact/rounded, Esc free mouse</color>";
            GUI.Label(new Rect(12, 10, 520, 260), text, style);

            // Stamina and health bars, bottom left like ET.
            DrawBar(new Rect(16, Screen.height - 40, 200 * Mathf.Max(0f, s.modifiers.staminaMax), 12), s.StaminaFraction, new Color(1f, 0.85f, 0.2f));
            if (vitals != null)
            {
                DrawBar(new Rect(16, Screen.height - 22, 200, 12), vitals.Health / (float)vitals.maxHealth, new Color(0.9f, 0.2f, 0.2f));
                if (vitals.IsDead)
                    GUI.Label(new Rect(cx - 200, cy - 60, 400, 40), "DEAD", bigStyle);
            }
        }

        static void DrawBar(Rect r, float fill, Color color)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill), r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
