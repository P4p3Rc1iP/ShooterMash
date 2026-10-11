using UnityEngine;

namespace ShooterMashup.Player
{
    // Ladder climbing. Ladders are any surface with SurfaceFlags.Ladder (see PmSurface).
    // Disable with MoveConfig.enableLadders.
    public sealed partial class PlayerMove
    {
        const float LadderTraceDistance = 48f * Inch;

        void CheckLadderMove()
        {
            if (ps.pmTime != 0)
                return;

            if (!cfg.enableLadders)
            {
                ps.pmFlags &= ~PmFlags.Ladder;
                return;
            }

            float tracedist = walking ? 1f * Inch : LadderTraceDistance;

            ladder = false;
            ladderForward = false;
            ps.pmFlags &= ~PmFlags.Ladder;

            if (ps.pmType == PmType.Dead)
            {
                // Bodies fall off ladders.
                ps.onGround = false;
                groundPlane = false;
                walking = false;
                return;
            }

            if (ps.IsProne)
                return;

            Vector3 flat = forward;
            flat.y = 0f;
            Normalize(ref flat);

            Vector3 spot = ps.origin + flat * tracedist;
            TraceResult trace = world.Trace(ps.origin, spot, mins, maxs);
            if (trace.fraction < 1f && (trace.surfaceFlags & SurfaceFlags.Ladder) != 0)
            {
                ladder = true;
                ladderNormal = trace.normal;
            }

            if (ladder && !walking && trace.fraction * tracedist > 1f * Inch)
            {
                // We're near a ladder but not touching it. Trace towards it with the feet lifted;
                // if it's there, pull the player onto it.
                ladder = false;
                Vector3 m = mins;
                m.y = -1f * Inch;
                spot = ps.origin - ladderNormal * tracedist;
                trace = world.Trace(ps.origin, spot, m, maxs);
                if (trace.fraction < 1f && (trace.surfaceFlags & SurfaceFlags.Ladder) != 0)
                {
                    ladderForward = true;
                    ladder = true;
                    ps.pmFlags |= PmFlags.Ladder;
                }
            }
            else if (ladder)
            {
                ps.pmFlags |= PmFlags.Ladder;
            }

            // Standing at the bottom of a ladder, only climb when pushing forwards.
            if (ladder && walking && cmd.forwardMove <= 0)
                ladder = false;
        }

        void LadderMove()
        {
            if (ladderForward)
            {
                // Move towards the ladder.
                Vector3 pull = ladderNormal * (-200f * Inch);
                ps.velocity.x = pull.x;
                ps.velocity.z = pull.z;
            }

            // Looking up or straight ahead climbs, looking well down descends;
            // backpedalling reverses that.
            float upscale = Mathf.Clamp((forward.y + 0.5f) * 2.5f, -1f, 1f);

            float scale = CmdScale();
            Vector3 wishvel = Vector3.zero;

            if (cmd.forwardMove != 0)
                wishvel.y = 0.9f * upscale * scale * cmd.forwardMove;

            if (cmd.rightMove != 0)
            {
                // Strafe along the ladder, so we can step off it.
                Vector3 ladderRight = LadderRight(ladderNormal);
                Vector3 flat = forward;
                flat.y = 0f;
                if (Vector3.Dot(ladderNormal, flat) < 0f)
                    ladderRight = -ladderRight;
                wishvel += 0.5f * scale * cmd.rightMove * ladderRight;
            }

            // Strafe friction.
            Friction();

            if (Mathf.Abs(ps.velocity.x) < 1f * Inch)
                ps.velocity.x = 0f;
            if (Mathf.Abs(ps.velocity.z) < 1f * Inch)
                ps.velocity.z = 0f;

            Vector3 wishdir = wishvel;
            float wishspeed = Normalize(ref wishdir);
            Accelerate(wishdir, wishspeed, cfg.accelerate);

            // Not climbing: bleed off any vertical speed.
            if (wishvel.y == 0f)
            {
                if (ps.velocity.y > 0f)
                    ps.velocity.y = Mathf.Max(0f, ps.velocity.y - cfg.gravity * frametime);
                else
                    ps.velocity.y = Mathf.Min(0f, ps.velocity.y + cfg.gravity * frametime);
            }

            StepSlideMove(false); // no gravity while on the ladder

            ps.movementDir = 0; // always point legs forward
        }

        static Vector3 LadderRight(Vector3 normal)
        {
            Vector3 flat = new Vector3(normal.x, 0f, normal.z);
            if (flat.sqrMagnitude < 1e-6f)
                return Vector3.right;
            return Quaternion.LookRotation(normal, Vector3.up) * Vector3.right;
        }
    }
}
