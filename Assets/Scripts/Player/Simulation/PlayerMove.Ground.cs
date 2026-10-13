using UnityEngine;

namespace ShooterMashup.Player
{
    // Ground detection, landing / fall damage, stance (crouch), water level, footsteps.
    public sealed partial class PlayerMove
    {
        void GroundTrace()
        {
            Vector3 point = ps.origin;
            point.y -= 0.25f * Inch;

            TraceResult trace = TraceAllLegs(ps.origin, point, out float legsOffset, out bool legsTraced);
            if (legsTraced)
                ps.proneLegsOffset = legsOffset;
            groundTrace = trace;

            // Do something corrective if we start inside a solid.
            if (trace.allSolid)
            {
                if (!CorrectAllSolid(ref trace))
                    return;
            }

            // Nothing below us: free fall.
            if (trace.fraction == 1f)
            {
                GroundTraceMissed();
                return;
            }

            // Moving up and away from the ground (jump pads, slopes at speed).
            if (ps.velocity.y > 0f && Vector3.Dot(ps.velocity, trace.normal) > 10f * Inch && !ps.IsProne)
            {
                ps.onGround = false;
                groundPlane = false;
                walking = false;
                return;
            }

            // Too steep to stand on: we have a ground plane, but we slide.
            if (trace.normal.y < cfg.minWalkNormal)
            {
                ps.onGround = false;
                groundPlane = true;
                walking = false;
                return;
            }

            groundPlane = true;
            walking = true;

            // Hitting solid ground ends a water jump.
            if ((ps.pmFlags & PmFlags.TimeWaterJump) != 0)
            {
                ps.pmFlags &= ~(PmFlags.TimeWaterJump | PmFlags.TimeLand);
                ps.pmTime = 0;
            }

            if (!ps.onGround)
            {
                // Just landed.
                CrashLand();

                // Don't do the landing timer if we were just going down a slope.
                if (previousVelocity.y < -200f * Inch)
                {
                    ps.pmFlags |= PmFlags.TimeLand;
                    ps.pmTime = 250;
                }

                // Not in ET: if we landed without the move hitting the floor (it came within the
                // ground check's reach, e.g. a moving deck rose to meet us), take away the speed
                // into the ground now, as hitting it would have. Otherwise the walk code's "keep
                // your speed on slopes" turns the fall into a run along the floor: on a deck tilted
                // a degree or two, straight down becomes 6 m/s sideways.
                ps.velocity = ClipVelocity(ps.velocity, trace.normal);
            }

            ps.onGround = true;
        }

        void GroundTraceMissed()
        {
            ps.onGround = false;
            groundPlane = false;
            walking = false;
        }

        /// <summary>
        /// We're stuck in a solid. ET jitters the origin by 1 inch in each direction looking for free
        /// space (but never actually moves there, a long-standing bug). We do move, and fall back
        /// to proper depenetration, because players will get stuck in freshly built voxels.
        /// </summary>
        bool CorrectAllSolid(ref TraceResult trace)
        {
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            for (int k = -1; k <= 1; k++)
            {
                Vector3 point = ps.origin + new Vector3(i, j, k) * Inch;
                trace = TraceAll(point, point);
                if (!trace.allSolid)
                {
                    ps.origin = point;
                    return RetraceGroundAfterUnstick(ref trace);
                }
            }

            Vector3 origin = ps.origin;
            if (world.TryDepenetrate(ref origin, mins, maxs))
            {
                ps.origin = origin;
                return RetraceGroundAfterUnstick(ref trace);
            }

            ps.onGround = false;
            groundPlane = false;
            walking = false;
            return false;
        }

        bool RetraceGroundAfterUnstick(ref TraceResult trace)
        {
            Vector3 point = ps.origin;
            point.y -= 0.25f * Inch;
            trace = TraceAll(ps.origin, point);
            groundTrace = trace;
            return true;
        }

        /// <summary>Work out how hard we hit the ground; emit landing events and fall damage.</summary>
        void CrashLand()
        {
            // Calculate the exact velocity on landing.
            float dist = ps.origin.y - previousOrigin.y;
            float vel = previousVelocity.y;
            float acc = -cfg.gravity;

            float a = acc / 2f;
            float b = vel;
            float c = -dist;

            float den = b * b - 4f * a * c;
            if (den < 0f)
                return;
            float t = (-b - Mathf.Sqrt(den)) / (2f * a);
            float impact = vel + t * acc;

            // ET grades landings by impact speed squared. We express that as the height of an
            // equivalent fall from rest, which is easier to design with.
            float height = impact * impact / (2f * cfg.gravity);

            // Never take falling damage if completely underwater; reduce it in standing water.
            if (ps.waterLevel == 3)
                return;
            if (ps.waterLevel == 2)
                height *= 0.25f;
            if (ps.waterLevel == 1)
                height *= 0.5f;

            if (height < MinLandingHeight)
                return;

            int damage = 0;
            if (cfg.enableFallDamage && cfg.fallDamage != null)
            {
                foreach (FallDamageTier tier in cfg.fallDamage)
                {
                    if (height > tier.height)
                        damage = Mathf.Max(damage, tier.damage);
                }
            }

            if ((groundTrace.surfaceFlags & SurfaceFlags.NoDamage) == 0)
            {
                SurfaceType surface = groundTrace.surfaceType;
                if (damage > 0)
                    AddEvent(PmEventType.FallDamage, damage, surface);
                else if (height > cfg.hardLandingHeight)
                    AddEvent(PmEventType.FallShort, 0, surface);
                else
                    AddEvent(PmEventType.Footstep, 0, surface);
            }

            // A damaging fall stops you dead.
            if (damage > 0)
                ps.velocity = Vector3.zero;

            // Start the footstep cycle over.
            ps.bobCycle = 0;
        }

        // ET ignores landings softer than this (an impact of 100 qu/s).
        const float MinLandingHeight = 0.15875f;

        void SetWaterLevel()
        {
            ps.waterLevel = 0;
            ps.waterType = Contents.None;
            if (!cfg.enableSwimming)
                return;

            // Sample at the feet, the waist, and the eyes.
            Vector3 point = ps.origin;
            point.y += cfg.mins.y + 1f * Inch;
            Contents cont = world.PointContents(point) & Contents.AnyLiquid;
            if (cont == Contents.None)
                return;

            float sample2 = ps.viewHeight - cfg.mins.y;
            float sample1 = sample2 / 2f;

            ps.waterType = cont;
            ps.waterLevel = 1;
            point.y = ps.origin.y + cfg.mins.y + sample1;
            if ((world.PointContents(point) & Contents.AnyLiquid) != 0)
            {
                ps.waterLevel = 2;
                point.y = ps.origin.y + cfg.mins.y + sample2;
                if ((world.PointContents(point) & Contents.AnyLiquid) != 0)
                    ps.waterLevel = 3;
            }
        }

        /// <summary>Crouch / stand. Sets mins, maxs and viewHeight.</summary>
        void CheckDuck()
        {
            mins = cfg.mins;
            maxs = cfg.maxs;

            if (ps.pmType == PmType.Dead)
            {
                ps.viewHeight = cfg.deadViewHeight;
                return;
            }

            if (cmd.upMove < 0 && !ps.IsOnLadder)
            {
                ps.pmFlags |= PmFlags.Ducked;
            }
            else if (ps.IsDucked)
            {
                // Try to stand up.
                maxs.y = cfg.maxs.y;
                TraceResult trace = TraceAll(ps.origin, ps.origin);
                if (!trace.allSolid)
                    ps.pmFlags &= ~PmFlags.Ducked;
            }

            if (ps.IsDucked)
            {
                maxs.y = cfg.CrouchMaxY;
                ps.viewHeight = cfg.crouchViewHeight;
            }
            else
            {
                maxs.y = cfg.maxs.y;
                ps.viewHeight = cfg.standViewHeight;
            }
        }

        /// <summary>Advance the bob cycle and emit footstep events when it crosses a step.</summary>
        void Footsteps()
        {
            float xyspeed = new Vector2(ps.velocity.x, ps.velocity.z).magnitude;

            if (ps.waterLevel > 2 || !ps.onGround)
                return;

            if (cmd.forwardMove == 0 && cmd.rightMove == 0)
            {
                if (xyspeed < 5f * Inch)
                    ps.bobCycle = 0; // start at the beginning of the cycle again
                return;
            }

            float bobmove;
            bool footstep = false;
            if (ps.IsProne)
            {
                bobmove = 0.2f; // prone never plays footsteps
            }
            else if (ps.IsDucked)
            {
                bobmove = 0.5f; // crouched bobs faster, but silently
            }
            else if (!cmd.Has(CmdButtons.Walk))
            {
                bobmove = 0.4f;
                footstep = true;
            }
            else
            {
                bobmove = 0.3f; // walking is silent
            }

            int old = ps.bobCycle;
            ps.bobCycle = (int)(old + bobmove * msec) & 255;

            // Footstep whenever the cycle crosses a half-way point.
            if ((((old + 64) ^ (ps.bobCycle + 64)) & 128) != 0)
            {
                if (ps.waterLevel == 0)
                {
                    if (footstep && (groundTrace.surfaceFlags & SurfaceFlags.NoFootsteps) == 0)
                        AddEvent(PmEventType.Footstep, 0, groundTrace.surfaceType);
                }
                else if (ps.waterLevel == 1)
                {
                    AddEvent(PmEventType.FootSplash);
                }
                else if (ps.waterLevel == 2)
                {
                    AddEvent(PmEventType.Swim);
                }
            }
        }

        void WaterEvents()
        {
            if (previousWaterLevel == 0 && ps.waterLevel > 0)
                AddEvent(PmEventType.WaterTouch);
            if (previousWaterLevel > 0 && ps.waterLevel == 0)
                AddEvent(PmEventType.WaterLeave);
            if (previousWaterLevel != 3 && ps.waterLevel == 3)
                AddEvent(PmEventType.WaterUnder);
            if (previousWaterLevel == 3 && ps.waterLevel != 3)
                AddEvent(PmEventType.WaterClear);
        }

        // ------------------------------------------------------------------ traces

        /// <summary>Trace the body and, when prone, the legs too, returning whichever hits first.</summary>
        TraceResult TraceAll(Vector3 start, Vector3 end) => TraceAllLegs(start, end, out _, out _);

        TraceResult TraceAllLegs(Vector3 start, Vector3 end, out float legsOffset, out bool legsTraced)
        {
            legsOffset = 0f;
            legsTraced = false;
            TraceResult trace = world.Trace(start, end, mins, maxs);

            if (ps.IsProne)
            {
                legsTraced = true;
                TraceResult legs = TraceLegs(start, end, trace, true, ps.viewAngles.y, out legsOffset);
                if (legs.fraction < trace.fraction || legs.startSolid || legs.allSolid)
                {
                    legs.endPos = start + (end - start) * legs.fraction;
                    trace = legs;
                }
            }

            return trace;
        }

        /// <summary>
        /// Trace the prone legs box, which trails the body by proneLegsDistance in the view's yaw
        /// direction. If the legs are blocked they try to step up, like the body does.
        /// </summary>
        TraceResult TraceLegs(Vector3 start, Vector3 end, TraceResult bodyTrace, bool hasBodyTrace, float yaw, out float legsOffset)
        {
            legsOffset = 0f;

            float rad = yaw * Mathf.Deg2Rad;
            Vector3 ofs = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * -cfg.proneLegsDistance;

            TraceResult trace = world.Trace(start + ofs, end + ofs, cfg.proneLegsMins, cfg.proneLegsMaxs);
            if (!hasBodyTrace || trace.fraction < bodyTrace.fraction || trace.allSolid)
            {
                // Legs clip sooner than the body: see if they can step up.
                ofs.y += cfg.stepSize;
                TraceResult step = world.Trace(start + ofs, end + ofs, cfg.proneLegsMins, cfg.proneLegsMaxs);
                if (!step.allSolid && !step.startSolid && step.fraction > trace.fraction)
                {
                    trace = step;
                    legsOffset = ofs.y;

                    Vector3 org = step.endPos;
                    Vector3 point = org;
                    point.y -= cfg.stepSize;
                    TraceResult down = world.Trace(org, point, cfg.proneLegsMins, cfg.proneLegsMaxs);
                    if (!down.allSolid)
                        legsOffset = ofs.y - (org.y - down.endPos.y);
                }
            }

            return trace;
        }
    }
}
