using System;
using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>
    /// Runs the movement simulation for a locally controlled player at a fixed tick rate and
    /// interpolates the transform between ticks.
    ///
    /// When networking arrives this becomes the client-side prediction loop: keep the cmds that
    /// haven't been acknowledged, and when an authoritative PlayerState arrives, replay them on it.
    /// The transform sits at the simulation origin, which is 0.61 m above the feet.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LocalPlayerInput))]
    public class PlayerMotor : MonoBehaviour
    {
        [Tooltip("ShooterMash (our values) or ET-exact. Switching overwrites the sizes, speeds, gravity " +
                 "and fall heights in Movement below. F4 toggles it in play mode.")]
        [SerializeField] MovePreset preset = MovePreset.ShooterMash;

        public MoveConfig movement = new MoveConfig();

        [Tooltip("Set by gameplay systems (food, encumbrance...). Editable in play mode for testing.")]
        public MoveModifiers modifiers = MoveModifiers.Default;

        [Tooltip("Simulation ticks per second. ET players typically ran at 125 (8 ms).")]
        [Range(20, 250)]
        public int tickRate = 125;

        [Tooltip("Layers the player collides with. The player itself must not have colliders on these layers.")]
        public LayerMask solidMask = Physics.DefaultRaycastLayers;

        [Tooltip("Smooth rendering between simulation ticks.")]
        public bool interpolate = true;
        [Tooltip("Ride moving things you stand on (vehicles, lifts, platforms): carried along, and keep their speed when you jump off.")]
        public bool ridePlatforms = true;
        [Tooltip("Landing on a moving platform from elsewhere sticks: you stand still on it. Off: physically right (you slide if your speed doesn't match).")]
        public bool stickyLandings = true;

        /// <summary>Raised for every movement event (footsteps, landings, jumps, steps...).</summary>
        public event Action<PmEvent> MoveEvent;

        public PlayerState State => state;
        /// <summary>State before the latest tick. Its origin already includes any step-up from that tick.</summary>
        public PlayerState PreviousState => previousState;
        public PlayerCmd LastCmd => lastCmd;
        public PlayerMove Simulation => pmove;
        /// <summary>What the player is riding, or null.</summary>
        public Transform Platform => rider.Platform;
        public float InterpolationAlpha { get; private set; } = 1f;
        public int TickMsec => Mathf.Max(1, Mathf.RoundToInt(1000f / tickRate));
        public MovePreset Preset => preset;
        public Vector3 SpawnPosition { get; set; }
        public float SpawnYaw { get; set; }

        /// <summary>Interpolated simulation origin.</summary>
        /// (On a moving platform it's moved to stay on the platform as drawn, which is interpolated too.)
        public Vector3 RenderOrigin => rider.ToDrawn(Vector3.LerpUnclamped(previousState.origin, state.origin, InterpolationAlpha));

        /// <summary>Added to the view's yaw on a turning platform, to match it as drawn (degrees).</summary>
        public float RenderYawOffset => rider.DrawnYawOffset;

        [SerializeField, HideInInspector] MovePreset appliedPreset = MovePreset.EtExact;
        PlayerState state;
        PlayerState previousState;
        PlayerCmd lastCmd;
        PlayerMove pmove;
        LocalPlayerInput input;
        int serverTime;
        double accumulatorMs;
        readonly PlatformRider rider = new PlatformRider();

        void Awake()
        {
            input = GetComponent<LocalPlayerInput>();
            // A component added from code (or an old scene) may not have had its preset written yet.
            if (preset != appliedPreset)
            {
                MovePresets.Apply(movement, preset);
                appliedPreset = preset;
            }
            pmove = new PlayerMove(movement, new UnityPmoveWorld(solidMask));
            SpawnPosition = transform.position;
            SpawnYaw = transform.eulerAngles.y;
            Respawn();
        }

        public void Respawn() => Teleport(SpawnPosition, SpawnYaw, true);

        /// <summary>Place the player's origin (not feet) at a position, facing a yaw.</summary>
        public void Teleport(Vector3 position, float yaw, bool resetState)
        {
            if (resetState)
            {
                state = PlayerState.Spawn(position, movement, serverTime);
                state.modifiers = modifiers;
                state.stamina = modifiers.staminaMax;
            }
            else
            {
                state.origin = position;
                state.velocity = Vector3.zero;
            }

            // Point the view where we want without touching the raw input angles, like ET's SetClientViewAngle.
            state.viewAngles = new Vector2(0f, yaw);
            state.deltaAngles = new Vector2(-input.Pitch, yaw - input.Yaw);
            previousState = state;
            rider.Clear();
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>Switch value sets, keeping the player's feet where they are.</summary>
        public void SetPreset(MovePreset newPreset)
        {
            float feet = movement.mins.y;
            MovePresets.Apply(movement, newPreset);
            preset = newPreset;
            appliedPreset = newPreset;

            Vector3 shift = new Vector3(0f, feet - movement.mins.y, 0f);
            state.origin += shift;
            previousState.origin += shift;
            SpawnPosition += shift;
        }

        void OnValidate()
        {
            // Picking a preset in the Inspector applies it.
            if (preset != appliedPreset)
                SetPreset(preset);
        }

        public void SetPmType(PmType type)
        {
            state.pmType = type;
        }

        void Update()
        {
            input.SampleFrame();

            // Ride whatever moving thing we stand on (moved since last frame).
            if (ridePlatforms)
                rider.Carry(ref state, ref previousState, pmove.World, movement.mins, CurrentMaxs());

            int msec = TickMsec;
            accumulatorMs += Time.deltaTime * 1000.0;

            int ticks = 0;
            while (accumulatorMs >= msec)
            {
                accumulatorMs -= msec;
                serverTime += msec;

                lastCmd = input.BuildCmd(serverTime);
                previousState = state;
                state.modifiers = modifiers;
                pmove.Move(ref state, lastCmd);

                var events = pmove.Events;
                for (int i = 0; i < events.Count; i++)
                {
                    // Don't interpolate across a step. PlayerView smooths it from the moment the
                    // event fires, so the step's height has to be applied in full right away.
                    // Otherwise the eye briefly drops below where it was.
                    if (events[i].type == PmEventType.StepUp)
                        previousState.origin.y += events[i].value;
                    MoveEvent?.Invoke(events[i]);
                }

                // Don't spiral after a long hitch.
                if (++ticks >= 25)
                {
                    accumulatorMs = 0;
                    break;
                }
            }

            if (ridePlatforms)
            {
                rider.StickyLandings = stickyLandings;
                rider.UpdateGround(ref state, pmove.GroundObject);
            }
            else
            {
                rider.Clear();
            }

            InterpolationAlpha = interpolate ? (float)(accumulatorMs / msec) : 1f;
            transform.SetPositionAndRotation(RenderOrigin, Quaternion.Euler(0f, state.viewAngles.y, 0f));
        }

        /// <summary>The top of the hull for the current stance.</summary>
        Vector3 CurrentMaxs()
        {
            Vector3 maxs = movement.maxs;
            if (state.IsProne) maxs.y = movement.ProneMaxY;
            else if (state.IsDucked) maxs.y = movement.CrouchMaxY;
            return maxs;
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
                return;
            Vector3 maxs = movement.maxs;
            if (state.IsProne) maxs.y = movement.ProneMaxY;
            else if (state.IsDucked) maxs.y = movement.CrouchMaxY;
            Gizmos.color = state.onGround ? Color.green : Color.yellow;
            Gizmos.DrawWireCube(state.origin + (movement.mins + maxs) * 0.5f, maxs - movement.mins);
        }
    }
}
