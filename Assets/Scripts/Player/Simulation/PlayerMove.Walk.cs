using UnityEngine;

namespace ShooterMashup.Player
{
    // Ground movement, air movement, jumping, noclip and dead movement.
    public sealed partial class PlayerMove
    {
        void WalkMove()
        {
            if (ps.waterLevel > 2 && Vector3.Dot(forward, groundTrace.normal) > 0f)
            {
                // Begin swimming.
                WaterMove();
                return;
            }

            if (CheckJump())
            {
                // Jumped away.
                if (ps.waterLevel > 1)
                    WaterMove();
                else
                    AirMove();

                ps.stamina = Mathf.Max(0f, ps.stamina - cfg.jumpStaminaCost * ps.modifiers.staminaCost);
                ps.jumpTime = cmd.serverTime;
                return;
            }

            Friction();

            float fmove = cmd.forwardMove;
            float smove = cmd.rightMove;
            float scale = CmdScale();

            // Project the movement directions onto the ground plane.
            Vector3 f = forward;
            Vector3 r = right;
            f.y = 0f;
            r.y = 0f;
            f = ClipVelocity(f, groundTrace.normal);
            r = ClipVelocity(r, groundTrace.normal);
            Normalize(ref f);
            Normalize(ref r);

            // When going up or down slopes the wish velocity is not flat.
            Vector3 wishdir = f * fmove + r * smove;
            float wishspeed = Normalize(ref wishdir) * scale;

            // Stance speed caps.
            if (ps.IsProne)
                wishspeed = Mathf.Min(wishspeed, BaseSpeed * cfg.proneSpeedScale);
            else if (ps.IsDucked)
                wishspeed = Mathf.Min(wishspeed, BaseSpeed * cfg.crouchSpeedScale);

            // Wading or walking on the bottom is slower.
            if (ps.waterLevel > 0)
            {
                float swimScale = (ps.waterType & Contents.Slime) != 0 ? cfg.slimeSwimScale : cfg.waterSwimScale;
                float waterScale = 1f - (1f - swimScale) * (ps.waterLevel / 3f);
                wishspeed = Mathf.Min(wishspeed, BaseSpeed * waterScale);
            }

            // On slick surfaces, or after being hit, you only get air control.
            bool slippery = (groundTrace.surfaceFlags & SurfaceFlags.Slick) != 0
                            || (ps.pmFlags & PmFlags.TimeKnockback) != 0;

            Accelerate(wishdir, wishspeed, slippery ? cfg.airAccelerate : cfg.accelerate);

            if (slippery)
                ps.velocity.y -= cfg.gravity * frametime;

            float vel = ps.velocity.magnitude;

            // Slide along the ground plane.
            ps.velocity = ClipVelocity(ps.velocity, groundTrace.normal);

            // Don't do anything if standing still.
            if (ps.velocity.x == 0f && ps.velocity.z == 0f)
                return;

            // Don't lose speed going up or down a slope.
            Normalize(ref ps.velocity);
            ps.velocity *= vel;

            StepSlideMove(false);
            SetMovementDir();
        }

        void AirMove()
        {
            Friction();

            float fmove = cmd.forwardMove;
            float smove = cmd.rightMove;
            float scale = CmdScale();

            // Project moves down to the horizontal plane.
            Vector3 f = forward;
            Vector3 r = right;
            f.y = 0f;
            r.y = 0f;
            Normalize(ref f);
            Normalize(ref r);

            Vector3 wishdir = f * fmove + r * smove;
            wishdir.y = 0f;
            float wishspeed = Normalize(ref wishdir) * scale;

            float horizontalBefore = new Vector2(ps.velocity.x, ps.velocity.z).magnitude;

            // Not on the ground, so little effect on velocity.
            Accelerate(wishdir, wishspeed, cfg.airAccelerate);

            if (cfg.maxAirSpeed > 0f)
                CapAirSpeed(Mathf.Max(cfg.maxAirSpeed, horizontalBefore));

            // We may be on a ground plane that's too steep to stand on: slide along it.
            if (groundPlane)
                ps.velocity = ClipVelocity(ps.velocity, groundTrace.normal);

            StepSlideMove(true);
            SetMovementDir();
        }

        void CapAirSpeed(float limit)
        {
            Vector2 h = new Vector2(ps.velocity.x, ps.velocity.z);
            float speed = h.magnitude;
            if (speed <= limit)
                return;
            h *= limit / speed;
            ps.velocity.x = h.x;
            ps.velocity.z = h.y;
        }

        bool CheckJump()
        {
            if (ps.IsProne)
                return false;

            // ET's anti-bunnyhop delay.
            if (cmd.serverTime - ps.jumpTime < cfg.jumpCooldownMs)
                return false;

            if (cmd.upMove < 10)
                return false; // not holding jump

            // Must release jump before jumping again.
            if ((ps.pmFlags & PmFlags.JumpHeld) != 0)
            {
                cmd.upMove = 0; // so CmdScale doesn't lower running speed
                return false;
            }

            groundPlane = false;
            walking = false;
            ps.pmFlags |= PmFlags.JumpHeld;
            ps.onGround = false;
            ps.velocity.y = cfg.jumpVelocity;
            AddEvent(PmEventType.Jump);

            if (cmd.forwardMove >= 0)
                ps.pmFlags &= ~PmFlags.BackwardsJump;
            else
                ps.pmFlags |= PmFlags.BackwardsJump;

            return true;
        }

        void NoclipMove()
        {
            ps.viewHeight = cfg.standViewHeight;

            float speed = ps.velocity.magnitude;
            if (speed < 1f)
            {
                ps.velocity = Vector3.zero;
            }
            else
            {
                float control = speed < cfg.stopSpeed ? cfg.stopSpeed : speed;
                float drop = control * cfg.friction * 1.5f * frametime;
                float newspeed = Mathf.Max(0f, speed - drop) / speed;
                ps.velocity *= newspeed;
            }

            float scale = CmdScale();
            Vector3 wishdir = forward * (cmd.forwardMove * scale) + right * (cmd.rightMove * scale);
            wishdir.y += cmd.upMove * scale;
            float wishspeed = Normalize(ref wishdir);

            Accelerate(wishdir, wishspeed, cfg.accelerate);

            ps.origin += frametime * ps.velocity;
        }

        void DeadMove()
        {
            if (!walking)
                return;

            // Extra friction.
            float speed = ps.velocity.magnitude - 20f * Inch;
            if (speed <= 0f)
                ps.velocity = Vector3.zero;
            else
                ps.velocity = ps.velocity.normalized * speed;
        }
    }
}
