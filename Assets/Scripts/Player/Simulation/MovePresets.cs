using UnityEngine;

namespace ShooterMashup.Player
{
    public enum MovePreset
    {
        /// <summary>ET's values converted exactly from inches. Feels identical to ET.</summary>
        EtExact,
        /// <summary>The same, rounded to friendly metric numbers (1-2% different).</summary>
        Rounded,
    }

    /// <summary>
    /// Writes a set of dimensional values (sizes, speeds, gravity, heights) into a MoveConfig.
    /// Unitless values (scales, acceleration, friction, timings, stamina) and feature toggles
    /// are the same in both presets and are left alone.
    /// </summary>
    public static class MovePresets
    {
        public static void Apply(MoveConfig c, MovePreset preset)
        {
            if (preset == MovePreset.Rounded)
                ApplyRounded(c);
            else
                ApplyEtExact(c);
        }

        static void ApplyEtExact(MoveConfig c)
        {
            c.mins = new Vector3(-0.4572f, -0.6096f, -0.4572f);
            c.maxs = new Vector3(0.4572f, 1.2192f, 0.4572f);         // 1.83 m tall
            c.standViewHeight = 1.016f;                              // eyes at 1.63 m
            c.crouchViewHeight = 0.4064f;
            c.proneViewHeight = -0.2032f;
            c.deadViewHeight = -0.4064f;
            c.proneLegsDistance = 0.8128f;
            c.proneLegsMins = new Vector3(-0.3429f, -0.6096f, -0.3429f);
            c.proneLegsMaxs = new Vector3(0.3429f, -0.36576f, 0.3429f);
            c.stepSize = 0.4572f;

            c.speed = 8.128f;                                        // run 6.50, sprint 8.94 m/s
            c.gravity = 20.32f;
            c.jumpVelocity = 6.858f;                                 // 1.157 m jump
            c.stopSpeed = 2.54f;

            c.leanMax = 0.7112f;
            c.leanMins = new Vector3(-0.2032f, -0.1778f, -0.2032f);
            c.leanMaxs = new Vector3(0.2032f, 0.1016f, 0.2032f);

            c.hardLandingHeight = 1.11f;
            c.fallDamage = new[]
            {
                new FallDamageTier { height = 6.15f, damage = 10 },
                new FallDamageTier { height = 7.62f, damage = 15 },
                new FallDamageTier { height = 9.21f, damage = 25 },
                new FallDamageTier { height = 10.64f, damage = 50 },
                new FallDamageTier { height = 12.23f, damage = 500 },
            };
        }

        static void ApplyRounded(MoveConfig c)
        {
            c.mins = new Vector3(-0.45f, -0.6f, -0.45f);
            c.maxs = new Vector3(0.45f, 1.2f, 0.45f);                // 1.8 m tall
            c.standViewHeight = 1.0f;                                // eyes at 1.6 m
            c.crouchViewHeight = 0.4f;                               // crouched 1.2 m tall
            c.proneViewHeight = -0.2f;                               // prone 1.0 m tall
            c.deadViewHeight = -0.4f;
            c.proneLegsDistance = 0.8f;
            c.proneLegsMins = new Vector3(-0.35f, -0.6f, -0.35f);
            c.proneLegsMaxs = new Vector3(0.35f, -0.35f, 0.35f);
            c.stepSize = 0.45f;

            c.speed = 8f;                                            // run 6.4, sprint 8.8 m/s
            c.gravity = 20f;
            c.jumpVelocity = 6.8f;                                   // 1.156 m jump
            c.stopSpeed = 2.5f;

            c.leanMax = 0.7f;
            c.leanMins = new Vector3(-0.2f, -0.18f, -0.2f);
            c.leanMaxs = new Vector3(0.2f, 0.1f, 0.2f);

            c.hardLandingHeight = 1.1f;
            c.fallDamage = new[]
            {
                new FallDamageTier { height = 6f, damage = 10 },
                new FallDamageTier { height = 7.5f, damage = 15 },
                new FallDamageTier { height = 9f, damage = 25 },
                new FallDamageTier { height = 10.5f, damage = 50 },
                new FallDamageTier { height = 12f, damage = 500 },
            };
        }
    }
}
