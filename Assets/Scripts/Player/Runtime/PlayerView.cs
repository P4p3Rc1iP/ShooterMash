using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>
    /// First-person camera, modelled on ET's CG_OffsetFirstPersonView: view height, smoothed
    /// stance changes, smoothed stair steps, landing dip, view bob, velocity tilt and leaning.
    /// Mouse look is applied every frame, independent of the simulation tick rate.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PlayerView : MonoBehaviour
    {
        // Timings (ms) and limits from cg_local.h / cg_event.c, converted to metres.
        const float DuckTime = 100f;
        const float ProneTime = 500f;
        const float StepTime = 200f;
        const float MaxStepChange = 0.8128f;
        const float LandDeflectTime = 150f;
        const float LandReturnTime = 300f;
        const float HardLandingDip = -0.2032f;
        const float LightDamageDip = -0.4064f;
        const float HeavyDamageDip = -0.6096f;
        const float MaxBobHeight = 0.1524f;
        const float MinBobSpeed = 5.08f;

        public PlayerMotor motor;
        public LocalPlayerInput input;

        [Header("Field of view")]
        [Tooltip("Horizontal FOV at 4:3, like cg_fov. Wider screens see more at the sides (Hor+).")]
        [Range(60f, 130f)]
        public float fov = 90f;

        [Header("View bob (ET cvar defaults, per m/s)")]
        [Tooltip("Master scale for all bob and tilt. 0 turns it off.")]
        [Range(0f, 2f)]
        public float bobScale = 1f;
        [Tooltip("Degrees of pitch per m/s of forward speed (cg_runpitch).")]
        public float pitchPerSpeed = 0.0787f;
        [Tooltip("Degrees of roll per m/s of sideways speed (cg_runroll).")]
        public float rollPerSpeed = 0.1969f;
        [Tooltip("Bob height per m/s (cg_bobup).")]
        public float bobUp = 0.005f;
        [Tooltip("Bob pitch in degrees per m/s (cg_bobpitch).")]
        public float bobPitchPerSpeed = 0.0787f;
        [Tooltip("Bob roll in degrees per m/s (cg_bobroll).")]
        public float bobRollPerSpeed = 0.0787f;

        [Header("Smoothing")]
        public bool smoothStanceChanges = true;
        public bool smoothSteps = true;
        public bool landingDip = true;

        Camera cam;

        float lastViewHeight = float.NaN;
        float duckChange;
        double duckTime = -1e9;
        float stepChange;
        double stepTime = -1e9;
        float landChange;
        double landTime = -1e9;

        int bobCycleBit;
        float bobFracSin;
        int lastValidBobCycleBit;
        float lastValidBobFracSin;

        static double NowMs => Time.timeAsDouble * 1000.0;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (motor == null) motor = GetComponentInParent<PlayerMotor>();
            if (input == null) input = GetComponentInParent<LocalPlayerInput>();
        }

        void OnEnable()
        {
            if (motor != null) motor.MoveEvent += OnMoveEvent;
        }

        void OnDisable()
        {
            if (motor != null) motor.MoveEvent -= OnMoveEvent;
        }

        void OnMoveEvent(PmEvent e)
        {
            double now = NowMs;
            switch (e.type)
            {
                case PmEventType.StepUp:
                {
                    // Stepping again before the last step finished smoothing: carry the remainder.
                    double delta = now - stepTime;
                    float oldStep = delta < StepTime ? stepChange * (float)(StepTime - delta) / StepTime : 0f;
                    stepChange = Mathf.Min(oldStep + e.value, MaxStepChange);
                    stepTime = now;
                    break;
                }
                case PmEventType.FallShort:
                    landChange = HardLandingDip;
                    landTime = now;
                    break;
                case PmEventType.FallDamage:
                    landChange = e.param <= 15 ? LightDamageDip : HeavyDamageDip;
                    landTime = now;
                    break;
            }
        }

        void LateUpdate()
        {
            if (motor == null || input == null)
                return;

            MoveConfig cfg = motor.movement;
            PlayerState s = motor.State;
            PlayerState prev = motor.PreviousState;
            float alpha = motor.InterpolationAlpha;
            double now = NowMs;
            float frameMs = Time.deltaTime * 1000f;

            // Stance change smoothing (ET compares successive player states).
            if (float.IsNaN(lastViewHeight))
                lastViewHeight = s.viewHeight;
            if (s.viewHeight != lastViewHeight)
            {
                duckChange = s.viewHeight - lastViewHeight;
                duckTime = now;
                lastViewHeight = s.viewHeight;
            }

            // Latest mouse look, with the simulation's corrections (pitch clamp, prone limits).
            float pitch = Mathf.Clamp(input.Pitch + s.deltaAngles.x, -PlayerMove.PitchLimit, PlayerMove.PitchLimit);
            if (s.IsProne)
                pitch = Mathf.Clamp(pitch, -cfg.pronePitchLimit, cfg.pronePitchLimit);
            float yaw = input.Yaw + s.deltaAngles.y + motor.RenderYawOffset;
            if (s.pmType == PmType.Dead)
            {
                pitch = -15f;
                yaw = s.viewAngles.y;
            }

            // Offsets below follow Quake conventions: + pitch looks down, + roll tilts right.
            float addPitch = 0f;
            float addRoll = s.pmType == PmType.Dead ? 40f : 0f;

            Quaternion look = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 vel = s.velocity;

            // Tilt with velocity.
            addPitch += Vector3.Dot(vel, look * Vector3.forward) * pitchPerSpeed * bobScale;
            addRoll -= Vector3.Dot(vel, look * Vector3.left) * rollPerSpeed * bobScale;

            // View bob, driven by the simulation's bob cycle.
            if (bobFracSin > 0f && s.bobCycle == 0)
            {
                lastValidBobCycleBit = bobCycleBit;
                lastValidBobFracSin = bobFracSin;
            }
            bobCycleBit = (s.bobCycle & 128) >> 7;
            bobFracSin = Mathf.Abs(Mathf.Sin((s.bobCycle & 127) / 127f * Mathf.PI));
            float xyspeed = new Vector2(vel.x, vel.z).magnitude;

            float frac = bobFracSin;
            int cycle = bobCycleBit;
            if (bobFracSin == 0f && lastValidBobFracSin > 0f)
            {
                // Ease back to centre over 200 ms when we stop.
                lastValidBobFracSin = Mathf.Max(0f, lastValidBobFracSin - 0.005f * frameMs);
                frac = lastValidBobFracSin;
                cycle = lastValidBobCycleBit;
            }

            float speed = Mathf.Max(xyspeed, MinBobSpeed);
            float crouchMul = s.IsDucked ? 3f : 1f;
            addPitch += frac * bobPitchPerSpeed * speed * crouchMul * bobScale;
            float roll = frac * bobRollPerSpeed * speed * crouchMul * bobScale;
            addRoll += (cycle & 1) != 0 ? -roll : roll;

            // Eye position.
            // The motor's interpolated origin, which also keeps the view on a moving platform as drawn.
            Vector3 origin = motor.RenderOrigin;
            float height = s.viewHeight;

            if (smoothStanceChanges)
            {
                float duration = s.IsProne ? ProneTime : DuckTime;
                double dt = now - duckTime;
                if (dt < duration)
                    height -= duckChange * (float)(duration - dt) / duration;
            }

            height += Mathf.Min(bobFracSin * xyspeed * bobUp, MaxBobHeight) * bobScale;

            if (landingDip)
            {
                double dt = now - landTime;
                if (dt < LandDeflectTime)
                    height += landChange * (float)(dt / LandDeflectTime);
                else if (dt < LandDeflectTime + LandReturnTime)
                    height += landChange * (1f - (float)((dt - LandDeflectTime) / LandReturnTime));
            }

            if (smoothSteps)
            {
                double dt = now - stepTime;
                if (dt < StepTime)
                    height -= stepChange * (float)(StepTime - dt) / StepTime;
            }

            origin.y += height;

            // Lean: roll the head and shift it sideways.
            float lean = Mathf.LerpUnclamped(prev.leanf, s.leanf, alpha);
            Quaternion rotation;
            if (lean != 0f)
            {
                addRoll += motor.Simulation.LeanRoll(lean);
                rotation = Quaternion.Euler(pitch + addPitch, yaw, -addRoll);
                origin += rotation * Vector3.right * lean;
            }
            else
            {
                rotation = Quaternion.Euler(pitch + addPitch, yaw, -addRoll);
            }

            transform.SetPositionAndRotation(origin, rotation);

            // Hor+: fov is horizontal at 4:3; Unity wants vertical.
            float halfH = fov * 0.5f * Mathf.Deg2Rad;
            cam.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(halfH) * 0.75f) * Mathf.Rad2Deg;
        }
    }
}
