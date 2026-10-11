using UnityEngine;

namespace ShooterMashup.Player
{
    // Prone, leaning and sprint stamina. Each has an enable flag in MoveConfig.
    public sealed partial class PlayerMove
    {
        // ------------------------------------------------------------------ prone

        /// <summary>Go prone / get up. Returns true (and sets mins, maxs, viewHeight) while prone.</summary>
        bool CheckProne()
        {
            if (!cfg.enableProne)
            {
                ps.eFlags &= ~(EntityFlags.Prone | EntityFlags.ProneMoving);
                return false;
            }

            if (!ps.IsProne && !ps.IsOnLadder && ps.waterLevel <= 1)
            {
                // Crouch + double-tap forward, or the prone button.
                bool wants = (ps.IsDucked && cmd.doubleTap == DoubleTap.Forward) || cmd.Has(CmdButtons.Prone);

                // proneTime is negative when we last got up, so this is "time since getting up".
                if (wants && cmd.serverTime + ps.proneTime > cfg.proneDelayMs)
                {
                    SetHull(cfg.CrouchMaxY);

                    ps.eFlags |= EntityFlags.Prone;
                    TraceResult trace = TraceAll(ps.origin, ps.origin);
                    ps.eFlags &= ~EntityFlags.Prone;

                    if (!trace.startSolid && !trace.allSolid)
                    {
                        ps.pmFlags |= PmFlags.Ducked;
                        ps.eFlags |= EntityFlags.Prone;
                        ps.proneTime = cmd.serverTime;
                    }
                }
            }

            if (ps.IsProne)
            {
                bool wantsUp = cmd.doubleTap == DoubleTap.Back || cmd.upMove > 10 || cmd.Has(CmdButtons.Prone);
                if (ps.waterLevel > 1 || ps.pmType == PmType.Dead
                    || (wantsUp && cmd.serverTime - ps.proneTime > cfg.proneDelayMs))
                {
                    // See if there's room to get up into a crouch.
                    SetHull(cfg.CrouchMaxY);

                    ps.eFlags &= ~EntityFlags.Prone;
                    TraceResult trace = TraceAll(ps.origin, ps.origin);
                    ps.eFlags |= EntityFlags.Prone;

                    if (!trace.allSolid)
                    {
                        ps.pmFlags |= PmFlags.Ducked; // crouch for a bit
                        ps.eFlags &= ~(EntityFlags.Prone | EntityFlags.ProneMoving);
                        ps.proneTime = -cmd.serverTime;

                        // Don't jump for a bit.
                        ps.jumpTime = cmd.serverTime - cfg.jumpCooldownMs + cfg.proneExitJumpDelayMs;
                    }
                }
            }

            if (!ps.IsProne)
                return false;

            float spd = ps.velocity.magnitude;
            bool userInput = Mathf.Abs(cmd.forwardMove) + Mathf.Abs(cmd.rightMove) > 10;
            if (userInput && spd > 40f * Inch)
                ps.eFlags |= EntityFlags.ProneMoving;
            else if (!userInput && spd < 20f * Inch)
                ps.eFlags &= ~EntityFlags.ProneMoving;

            SetHull(cfg.ProneMaxY);
            ps.viewHeight = cfg.proneViewHeight;
            return true;
        }

        void SetHull(float maxY)
        {
            mins = cfg.mins;
            maxs = cfg.maxs;
            maxs.y = maxY;
        }

        /// <summary>
        /// Prone limits pitch, and won't let you turn if it would swing your legs into a wall.
        /// </summary>
        void ClampProneView(Vector2 old)
        {
            float pitchMax = cfg.pronePitchLimit;
            if (ps.viewAngles.x > pitchMax)
            {
                ps.viewAngles.x = pitchMax;
                ps.deltaAngles.x = pitchMax - cmd.pitch;
            }
            else if (ps.viewAngles.x < -pitchMax)
            {
                ps.viewAngles.x = -pitchMax;
                ps.deltaAngles.x = -pitchMax - cmd.pitch;
            }

            if (ps.viewAngles.y != old.y)
            {
                TraceResult trace = TraceLegs(ps.origin, ps.origin, default, false, ps.viewAngles.y, out float legsOffset);
                if (trace.allSolid)
                {
                    ps.viewAngles.y = old.y;
                    ps.deltaAngles.y = old.y - cmd.yaw;
                }
                else
                {
                    ps.proneLegsOffset = legsOffset;
                }
            }
        }

        // ------------------------------------------------------------------ lean

        void UpdateLean()
        {
            int leaning = 0; // -1 left, 1 right

            if (cfg.enableLean && cmd.forwardMove == 0 && cmd.upMove <= 0)
            {
                // Both pressed cancels out.
                if (cmd.Has(CmdButtons.LeanLeft)) leaning -= 1;
                if (cmd.Has(CmdButtons.LeanRight)) leaning += 1;
            }

            if (ps.IsProne)
                leaning = 0;

            float leanofs = ps.leanf;
            float max = cfg.leanMax;

            if (leaning == 0)
            {
                // Go back to centre.
                float step = msec / cfg.leanTimeFromMs * max;
                if (leanofs > 0f)
                    leanofs = Mathf.Max(0f, leanofs - step);
                else if (leanofs < 0f)
                    leanofs = Mathf.Min(0f, leanofs + step);
            }
            else
            {
                float step = msec / cfg.leanTimeToMs * max;
                if (leaning > 0)
                    leanofs = Mathf.Min(max, leanofs + step);
                else
                    leanofs = Mathf.Max(-max, leanofs - step);
            }

            ps.leanf = leanofs;

            if (leaning != 0)
            {
                // Don't lean the head through walls.
                Vector3 start = ps.origin;
                start.y += ps.viewHeight;
                Vector3 leanRight = Quaternion.Euler(ps.viewAngles.x, ps.viewAngles.y, -LeanRoll(leanofs)) * Vector3.right;
                Vector3 end = start + leanRight * leanofs;

                TraceResult trace = world.Trace(start, end, cfg.leanMins, cfg.leanMaxs);
                ps.leanf *= trace.fraction;
            }

            // No strafing while leaning.
            if (ps.leanf != 0f)
                cmd.rightMove = 0;
        }

        /// <summary>Head roll in degrees for a lean offset (ET: half a degree per inch).</summary>
        public float LeanRoll(float leanOffset) => cfg.leanMax > 0f ? leanOffset / cfg.leanMax * cfg.leanMaxRoll : 0f;

        // ------------------------------------------------------------------ sprint

        void Sprint()
        {
            MoveModifiers mod = ps.modifiers;
            float max = Mathf.Max(0f, mod.staminaMax);

            if (!cfg.enableSprint)
            {
                ps.stamina = max;
                return;
            }

            bool sprinting = cmd.Has(CmdButtons.Sprint)
                             && (cmd.forwardMove != 0 || cmd.rightMove != 0)
                             && !ps.IsDucked && !ps.IsProne;

            if (sprinting)
            {
                ps.stamina -= cfg.staminaDrainPerSecond * mod.staminaCost * frametime;
            }
            else
            {
                float regen = cfg.staminaRegenPerSecond * mod.staminaRegen * frametime;
                ps.stamina += regen;
                if (ps.stamina > cfg.staminaFastRegenAbove * max)
                    ps.stamina += regen; // faster at the top of the bar
            }

            ps.stamina = Mathf.Clamp(ps.stamina, 0f, max);
        }
    }
}
