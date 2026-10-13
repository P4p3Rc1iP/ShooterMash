using UnityEngine;

namespace ShooterMashup.Player
{
    public enum MovePreset
    {
        /// <summary>ET's values converted exactly from inches. Feels identical to ET.</summary>
        EtExact,
        /// <summary>
        /// Our own values: ET rounded to friendly metric numbers (1-2% different), plus changes for
        /// building: a narrower hull (0.7 m, fits between pillars 1 m apart), a lower prone hull that
        /// fits under a floor on 1 m pillars, and 50 cm steps.
        /// Stamina is more forgiving than ET's.
        /// </summary>
        ShooterMash,
    }

    /// <summary>
    /// Writes a set of values into a MoveConfig: dimensional ones (sizes, speeds, gravity,
    /// heights) and the stamina drain and regen rates. Other unitless values (scales,
    /// acceleration, friction, timings) and feature toggles are the same in both presets and
    /// are left alone.
    /// </summary>
    public static class MovePresets
    {
        public static void Apply(MoveConfig c, MovePreset preset)
        {
            if (preset == MovePreset.ShooterMash)
                ApplyShooterMash(c);
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
            c.proneHeight = 1.016f;
            c.deadViewHeight = -0.4064f;
            c.proneLegsDistance = 0.8128f;
            c.proneLegsMins = new Vector3(-0.3429f, -0.6096f, -0.3429f);
            c.proneLegsMaxs = new Vector3(0.3429f, -0.36576f, 0.3429f);
            c.stepSize = 0.4572f;

            c.speed = 8.128f;                                        // run 6.50, sprint 8.94 m/s
            c.gravity = 20.32f;
            c.jumpVelocity = 6.858f;                                 // 1.157 m jump
            c.stopSpeed = 2.54f;

            c.staminaDrainPerSecond = 0.25f;                         // a full bar lasts 4 s of sprint
            c.staminaRegenPerSecond = 0.025f;

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

        static void ApplyShooterMash(MoveConfig c)
        {
            c.mins = new Vector3(-0.35f, -0.6f, -0.35f);     // 0.7 m wide: fits between pillars 1 m apart (0.77 m gap)
            c.maxs = new Vector3(0.35f, 1.2f, 0.35f);      // 1.8 m tall
            c.standViewHeight = 1.0f;                                // eyes at 1.6 m
            c.crouchViewHeight = 0.4f;                               // crouched 1.2 m tall
            c.proneViewHeight = -0.2f;                               // prone eyes at 0.4 m
            c.proneHeight = 0.6f;                                    // fits under a floor on 1 m pillars (0.8 m)
            c.deadViewHeight = -0.4f;
            c.proneLegsDistance = 0.8f;
            c.proneLegsMins = new Vector3(-0.35f, -0.6f, -0.35f);
            c.proneLegsMaxs = new Vector3(0.35f, -0.35f, 0.35f);
            c.stepSize = 0.52f;                                      // 50 cm steps, with a little margin

            c.speed = 8f;                                            // run 6.4, sprint 8.8 m/s
            c.gravity = 20f;
            c.jumpVelocity = 6.8f;                                   // 1.156 m jump
            c.stopSpeed = 2.5f;

            c.staminaDrainPerSecond = 0.1f;                          // more forgiving: 10 s of sprint
            c.staminaRegenPerSecond = 0.05f;                         // and twice ET's regen

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
