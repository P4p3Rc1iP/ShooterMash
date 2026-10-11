using System;
using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>
    /// Every tunable of the movement simulation, in metres, seconds and degrees.
    /// Defaults are Wolfenstein: Enemy Territory's values (bg_pmove.c, g_client.c) converted
    /// exactly from Quake units (1 qu = 1 inch = 0.0254 m). Client and server must share these.
    /// </summary>
    [Serializable]
    public class MoveConfig
    {
        [Header("Hull (m, relative to origin)")]
        [Tooltip("ET: 18 x 24 / 48 qu. The origin is 0.61 m above the feet; the player is 1.83 m tall.")]
        public Vector3 mins = new Vector3(-0.4572f, -0.6096f, -0.4572f);
        public Vector3 maxs = new Vector3(0.4572f, 1.2192f, 0.4572f);
        public float standViewHeight = 1.016f;
        public float crouchViewHeight = 0.4064f;
        public float proneViewHeight = -0.2032f;
        public float deadViewHeight = -0.4064f;
        [Tooltip("Prone adds a second box for the legs, this far behind the body.")]
        public float proneLegsDistance = 0.8128f;
        public Vector3 proneLegsMins = new Vector3(-0.3429f, -0.6096f, -0.3429f);
        public Vector3 proneLegsMaxs = new Vector3(0.3429f, -0.36576f, 0.3429f);
        public float stepSize = 0.4572f;
        [Tooltip("Ground steeper than this (normal.y below it) can't be walked on. 0.7 = 45.6 degrees.")]
        public float minWalkNormal = 0.7f;

        [Header("Speeds (m/s)")]
        [Tooltip("Base speed. Running is this times runSpeedScale (6.5 m/s).")]
        public float speed = 8.128f;
        public float runSpeedScale = 0.8f;
        public float sprintSpeedScale = 1.1f;
        public float crouchSpeedScale = 0.25f;
        public float proneSpeedScale = 0.21f;
        [Tooltip("m/s²")]
        public float gravity = 20.32f;
        [Tooltip("Upward speed of a jump. 6.858 m/s with 20.32 m/s² gravity jumps 1.16 m.")]
        public float jumpVelocity = 6.858f;

        [Header("Acceleration (1/s) & friction")]
        public float accelerate = 10f;
        public float airAccelerate = 1f;
        public float waterAccelerate = 4f;
        public float slimeAccelerate = 2f;
        public float friction = 6f;
        [Tooltip("Below this speed (m/s), friction acts as if you were going this fast, so you stop crisply.")]
        public float stopSpeed = 2.54f;
        public float waterFriction = 1f;
        public float slimeFriction = 1f;
        public float ladderFriction = 14f;
        public float waterSwimScale = 0.5f;
        public float slimeSwimScale = 0.3f;
        [Tooltip("0 = ET behaviour (strafe jumping can build speed). Above 0, air strafing can't push " +
                 "horizontal speed past this (m/s), though speed from other sources is kept.")]
        public float maxAirSpeed = 0f;

        [Header("Jumping")]
        [Tooltip("ET blocks jumping again for this long after a jump.")]
        public int jumpCooldownMs = 850;
        [Tooltip("After getting up from prone you can't jump for this long.")]
        public int proneExitJumpDelayMs = 200;

        [Header("Stamina (1 = a full bar; MoveModifiers.staminaMax scales the bar)")]
        [Tooltip("ET: a full bar lasts 4 s of sprinting.")]
        public float staminaDrainPerSecond = 0.25f;
        [Tooltip("ET: 40 s to refill from empty at the slow rate.")]
        public float staminaRegenPerSecond = 0.025f;
        [Tooltip("Above this fraction of the bar, stamina regenerates at double rate.")]
        public float staminaFastRegenAbove = 0.25f;
        public float jumpStaminaCost = 0.125f;
        [Tooltip("Sprint stops giving extra speed below this much stamina.")]
        public float minStaminaToSprint = 0.0025f;

        [Header("Lean")]
        public float leanMax = 0.7112f;
        [Tooltip("Head roll at full lean, in degrees.")]
        public float leanMaxRoll = 14f;
        public float leanTimeToMs = 200f;
        public float leanTimeFromMs = 300f;
        public Vector3 leanMins = new Vector3(-0.2032f, -0.1778f, -0.2032f);
        public Vector3 leanMaxs = new Vector3(0.2032f, 0.1016f, 0.2032f);

        [Header("Prone")]
        [Tooltip("Minimum time between going prone and getting up, and vice versa.")]
        public int proneDelayMs = 750;
        public float pronePitchLimit = 40f;

        [Header("Falling (heights in m, for a fall from rest)")]
        [Tooltip("Landings from above this height make a heavier landing sound and dip the view.")]
        public float hardLandingHeight = 1.11f;
        [Tooltip("ET's fall damage. Equivalent heights; the real check is on impact speed, " +
                 "so jumping off something counts the extra height of the jump.")]
        public FallDamageTier[] fallDamage =
        {
            new FallDamageTier { height = 6.15f, damage = 10 },
            new FallDamageTier { height = 7.62f, damage = 15 },
            new FallDamageTier { height = 9.21f, damage = 25 },
            new FallDamageTier { height = 10.64f, damage = 50 },
            new FallDamageTier { height = 12.23f, damage = 500 },
        };

        [Header("Feature toggles")]
        public bool enableSprint = true;
        public bool enableProne = true;
        public bool enableLean = true;
        public bool enableLadders = true;
        public bool enableSwimming = true;
        public bool enableFallDamage = true;

        [Header("Simulation")]
        [Tooltip("Long commands are split into steps no longer than this (ET: 50 ms).")]
        public int maxStepMsec = 50;

        public float CrouchMaxY => maxs.y - (standViewHeight - crouchViewHeight);
        public float ProneMaxY => maxs.y - standViewHeight - proneViewHeight;
    }

    [Serializable]
    public struct FallDamageTier
    {
        [Tooltip("Falls from at least this high (m)...")]
        public float height;
        [Tooltip("...do this much damage.")]
        public int damage;
    }
}
