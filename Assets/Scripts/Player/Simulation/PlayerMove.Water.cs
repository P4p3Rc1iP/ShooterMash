using UnityEngine;

namespace ShooterMashup.Player
{
    // Swimming and jumping out of water. Disable with MoveConfig.enableSwimming.
    public sealed partial class PlayerMove
    {
        void WaterMove()
        {
            if (CheckWaterJump())
            {
                WaterJumpMove();
                return;
            }

            Friction();

            float scale = CmdScale();
            Vector3 wishvel;
            if (scale == 0f)
            {
                wishvel = new Vector3(0f, -60f * Inch, 0f); // sink towards the bottom
            }
            else
            {
                wishvel = scale * forward * cmd.forwardMove + scale * right * cmd.rightMove;
                wishvel.y += scale * cmd.upMove;
            }

            Vector3 wishdir = wishvel;
            float wishspeed = Normalize(ref wishdir);

            bool slime = (ps.waterType & Contents.Slime) != 0;
            float swimMax = BaseSpeed * (slime ? cfg.slimeSwimScale : cfg.waterSwimScale);
            if (wishspeed > swimMax)
                wishspeed = swimMax;

            Accelerate(wishdir, wishspeed, slime ? cfg.slimeAccelerate : cfg.waterAccelerate);

            // Make sure we can go up slopes easily under water.
            if (groundPlane && Vector3.Dot(ps.velocity, groundTrace.normal) < 0f)
            {
                float vel = ps.velocity.magnitude;
                ps.velocity = ClipVelocity(ps.velocity, groundTrace.normal);
                Normalize(ref ps.velocity);
                ps.velocity *= vel;
            }

            SlideMove(false);
        }

        /// <summary>If we're at the surface facing a ledge we can climb, pop out of the water.</summary>
        bool CheckWaterJump()
        {
            if (ps.pmTime != 0)
                return false;
            if (ps.waterLevel != 2)
                return false;

            Vector3 flat = forward;
            flat.y = 0f;
            Normalize(ref flat);

            Vector3 spot = ps.origin + flat * (30f * Inch);
            spot.y += 4f * Inch;
            if ((world.PointContents(spot) & Contents.Solid) == 0)
                return false;

            spot.y += 16f * Inch;
            if (world.PointContents(spot) != Contents.None)
                return false;

            // Jump out of the water.
            ps.velocity = forward * (200f * Inch);
            ps.velocity.y = 350f * Inch;
            ps.pmFlags |= PmFlags.TimeWaterJump;
            ps.pmTime = 2000;
            return true;
        }

        /// <summary>Flying out of the water: no control, but we fall.</summary>
        void WaterJumpMove()
        {
            StepSlideMove(true);

            ps.velocity.y -= cfg.gravity * frametime;
            if (ps.velocity.y < 0f)
            {
                // Cancel as soon as we start falling again.
                ps.pmFlags &= ~PmFlags.AllTimes;
                ps.pmTime = 0;
            }
        }
    }
}
