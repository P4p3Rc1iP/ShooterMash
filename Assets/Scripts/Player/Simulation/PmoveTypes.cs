using System;
using UnityEngine;

namespace ShooterMashup.Player
{
    // Conventions for everything in the movement simulation:
    //  * Distances are in metres, speeds in m/s. (ET works in Quake units, 1 qu = 1 inch;
    //    every ET value has been converted exactly.)
    //  * Axes are Unity's: +Y up, +Z forward, +X right.
    //  * Angles are Unity's: pitch is positive looking down, yaw is positive turning right.
    //  * Times are integer milliseconds, like ET, so the simulation is deterministic per tick.

    [Flags]
    public enum CmdButtons : ushort
    {
        None = 0,
        Sprint = 1 << 0,
        Walk = 1 << 1,
        Prone = 1 << 2,
        LeanLeft = 1 << 3,
        LeanRight = 1 << 4,
    }

    public enum DoubleTap : byte { None, Forward, Back, Left, Right }

    /// <summary>
    /// One tick of player intent. Holds everything the simulation reads and nothing else,
    /// so it can be sent to a server verbatim and replayed for prediction.
    /// </summary>
    [Serializable]
    public struct PlayerCmd
    {
        public int serverTime;      // ms timestamp this command simulates up to
        public sbyte forwardMove;   // -127..127 (64 = walk)
        public sbyte rightMove;     // -127..127
        public sbyte upMove;        // + jump, - crouch
        public float pitch;         // raw accumulated view angles in degrees, before PlayerState.deltaAngles
        public float yaw;
        public CmdButtons buttons;
        public DoubleTap doubleTap;

        public readonly bool Has(CmdButtons b) => (buttons & b) != 0;
    }

    [Flags]
    public enum PmFlags : ushort
    {
        None = 0,
        Ducked = 1 << 0,
        JumpHeld = 1 << 1,
        Ladder = 1 << 2,
        BackwardsJump = 1 << 3,
        BackwardsRun = 1 << 4,
        TimeLand = 1 << 5,       // pmTime is time before the landing effect ends
        TimeKnockback = 1 << 6,  // pmTime is time with air-accelerate-only control
        TimeWaterJump = 1 << 7,  // pmTime is time of the water jump
        AllTimes = TimeLand | TimeKnockback | TimeWaterJump,
    }

    [Flags]
    public enum EntityFlags : byte
    {
        None = 0,
        Prone = 1 << 0,
        ProneMoving = 1 << 1,
    }

    public enum PmType : byte { Normal, Noclip, Dead, Freeze }

    [Flags]
    public enum Contents : byte
    {
        None = 0,
        Solid = 1 << 0,
        Water = 1 << 1,
        Slime = 1 << 2,
        Lava = 1 << 3,
        AnyLiquid = Water | Slime | Lava,
    }

    [Flags]
    public enum SurfaceFlags : byte
    {
        None = 0,
        Ladder = 1 << 0,
        Slick = 1 << 1,
        NoDamage = 1 << 2,     // landing on it never hurts
        NoFootsteps = 1 << 3,
    }

    /// <summary>Footstep material, passed along with footstep and landing events.</summary>
    public enum SurfaceType : byte { Default, Metal, Wood, Dirt, Gravel, Snow, Sand, Flesh }

    /// <summary>Complete movement state of one player. Small, copyable and deterministic.</summary>
    [Serializable]
    public struct PlayerState
    {
        public int commandTime;
        public PmType pmType;
        public PmFlags pmFlags;
        public EntityFlags eFlags;
        public int pmTime;

        public Vector3 origin;      // m, the feet are at origin + mins.y (0.61 m below)
        public Vector3 velocity;    // m/s
        public Vector2 viewAngles;  // x = pitch, y = yaw
        public Vector2 deltaAngles; // added to the raw cmd angles; lets the sim clamp or set the view
        public float viewHeight;    // eye height above origin

        public bool onGround;
        public sbyte movementDir;   // legs yaw relative to view, for animation (+ = moving to the right)
        public int bobCycle;        // 0..255, drives view bob and footsteps
        public float leanf;         // current lean offset in m, + = right
        public int waterLevel;      // 0 = dry, 1 = feet, 2 = waist, 3 = submerged
        public Contents waterType;

        public float stamina;       // 0..modifiers.staminaMax (1 = a full ET bar)
        public int jumpTime;        // time of the last jump
        public int proneTime;       // + time we went prone, - time we left prone
        public float proneLegsOffset;

        public MoveModifiers modifiers; // set by gameplay systems (food, encumbrance, injuries...)

        public readonly bool IsProne => (eFlags & EntityFlags.Prone) != 0;
        public readonly bool IsDucked => (pmFlags & PmFlags.Ducked) != 0;
        public readonly bool IsOnLadder => (pmFlags & PmFlags.Ladder) != 0;
        public readonly Vector3 EyePosition => origin + new Vector3(0f, viewHeight, 0f);
        public readonly float StaminaFraction => modifiers.staminaMax > 0f ? stamina / modifiers.staminaMax : 0f;

        public static PlayerState Spawn(Vector3 origin, MoveConfig cfg, int time)
        {
            return new PlayerState
            {
                commandTime = time,
                pmType = PmType.Normal,
                origin = origin,
                viewHeight = cfg.standViewHeight,
                stamina = 1f,
                jumpTime = time - cfg.jumpCooldownMs,
                proneTime = -(time - cfg.proneDelayMs - 1),
                modifiers = MoveModifiers.Default,
            };
        }
    }

    /// <summary>
    /// Multipliers other gameplay systems use to affect movement without touching movement code.
    /// They're part of PlayerState, so they're simulated (and predicted) like everything else;
    /// the authority (server) decides their values, e.g. from food, load carried or injuries.
    /// </summary>
    [Serializable]
    public struct MoveModifiers
    {
        [Tooltip("Multiplies all movement speeds.")]
        public float speed;
        [Tooltip("Size of the stamina bar. 1 = ET's bar (4 s of sprint).")]
        public float staminaMax;
        [Tooltip("Multiplies stamina spent sprinting and jumping.")]
        public float staminaCost;
        [Tooltip("Multiplies stamina regeneration.")]
        public float staminaRegen;

        public static MoveModifiers Default => new MoveModifiers
        {
            speed = 1f,
            staminaMax = 1f,
            staminaCost = 1f,
            staminaRegen = 1f,
        };
    }

    public enum PmEventType : byte
    {
        Jump,
        Footstep,       // param: unused
        FootSplash,
        Swim,
        FallShort,      // a landing hard enough to dip the view
        FallDamage,     // param: damage to apply
        StepUp,         // value: height stepped up (m)
        WaterTouch,
        WaterLeave,
        WaterUnder,
        WaterClear,
    }

    public struct PmEvent
    {
        public PmEventType type;
        public int param;
        public float value;
        public SurfaceType surface;

        public override readonly string ToString() => $"{type}({param}, {value:0.###}, {surface})";
    }

    public struct TraceResult
    {
        public bool allSolid;    // the whole move was inside a solid
        public bool startSolid;  // the start position was inside a solid
        public float fraction;   // 0..1 of the move completed
        public Vector3 endPos;
        public Vector3 normal;
        public SurfaceFlags surfaceFlags;
        public SurfaceType surfaceType;
        public object hitObject;
    }

    /// <summary>
    /// Collision queries the movement code needs. All positions are in metres.
    /// Implement this on top of the voxel world to run movement against it directly.
    /// </summary>
    public interface IPmoveWorld
    {
        /// <summary>Sweep an axis-aligned box from start to end.</summary>
        TraceResult Trace(Vector3 start, Vector3 end, Vector3 mins, Vector3 maxs);

        Contents PointContents(Vector3 point);

        /// <summary>Push a box that is stuck inside geometry out of it. Returns false if it can't.</summary>
        bool TryDepenetrate(ref Vector3 origin, Vector3 mins, Vector3 maxs);
    }
}
