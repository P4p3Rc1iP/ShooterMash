using System;
using UnityEngine;

namespace ShooterMashup.Player
{
    // Collide-and-slide plus stair stepping (ET's bg_slidemove.c).
    public sealed partial class PlayerMove
    {
        const int MaxClipPlanes = 5;

        /// <summary>
        /// Move along velocity for the frame, sliding along anything hit.
        /// Returns true if the velocity was clipped in some way.
        /// </summary>
        bool SlideMove(bool gravity)
        {
            int numbumps = 4;
            int extrabumps = 0;

            Vector3 primalVelocity = ps.velocity;
            Vector3 endVelocity = Vector3.zero;

            if (gravity)
            {
                // Half the gravity now and half after, for accurate integration.
                endVelocity = ps.velocity;
                endVelocity.y -= cfg.gravity * frametime;
                ps.velocity.y = (ps.velocity.y + endVelocity.y) * 0.5f;
                primalVelocity.y = endVelocity.y;
                if (groundPlane)
                    ps.velocity = ClipVelocity(ps.velocity, groundTrace.normal);
            }

            float timeLeft = frametime;

            Span<Vector3> planes = stackalloc Vector3[MaxClipPlanes];
            int numplanes = 0;

            // Never turn against the ground plane...
            if (groundPlane)
                planes[numplanes++] = groundTrace.normal;

            // ...or against the original velocity.
            Vector3 vdir = ps.velocity;
            Normalize(ref vdir);
            planes[numplanes++] = vdir;

            int bumpcount;
            for (bumpcount = 0; bumpcount < numbumps; bumpcount++)
            {
                Vector3 end = ps.origin + ps.velocity * timeLeft;
                TraceResult trace = TraceAll(ps.origin, end);

                if (trace.allSolid)
                {
                    // Trapped in a solid. Don't build up falling damage, but allow sideways movement.
                    ps.velocity.y = 0f;
                    return true;
                }

                if (trace.fraction > 0f)
                    ps.origin = trace.endPos;

                if (trace.fraction == 1f)
                    break; // moved the entire distance

                timeLeft -= timeLeft * trace.fraction;

                if (numplanes >= MaxClipPlanes)
                {
                    // Shouldn't really happen.
                    ps.velocity = Vector3.zero;
                    return true;
                }

                // Hitting the same plane again: nudge velocity out along it, which fixes some
                // epsilon issues with non-axial planes. If that happens twice, nudge the origin.
                int i;
                for (i = 0; i < numplanes; i++)
                {
                    if (Vector3.Dot(trace.normal, planes[i]) > 0.99f)
                    {
                        if (extrabumps <= 0)
                        {
                            ps.velocity += trace.normal * Inch;
                            extrabumps++;
                            numbumps++;
                        }
                        else
                        {
                            TraceResult nudge = TraceAll(ps.origin, ps.origin + trace.normal * Inch);
                            ps.origin = nudge.endPos;
                        }
                        break;
                    }
                }
                if (i < numplanes)
                    continue;

                planes[numplanes++] = trace.normal;

                // Modify velocity so it parallels all of the clip planes.
                for (i = 0; i < numplanes; i++)
                {
                    float into = Vector3.Dot(ps.velocity, planes[i]);
                    if (into >= 0.1f * Inch)
                        continue; // move doesn't interact with the plane

                    // See how hard we're hitting things.
                    if (-into > impactSpeed)
                        impactSpeed = -into;

                    Vector3 clipVelocity = ClipVelocity(ps.velocity, planes[i]);
                    Vector3 endClipVelocity = ClipVelocity(endVelocity, planes[i]);

                    // See if there is a second plane that the new move enters.
                    for (int j = 0; j < numplanes; j++)
                    {
                        if (j == i)
                            continue;
                        if (Vector3.Dot(clipVelocity, planes[j]) >= 0.1f * Inch)
                            continue;

                        clipVelocity = ClipVelocity(clipVelocity, planes[j]);
                        endClipVelocity = ClipVelocity(endClipVelocity, planes[j]);

                        // If it goes back into the first plane, it's fine.
                        if (Vector3.Dot(clipVelocity, planes[i]) >= 0f)
                            continue;

                        // Slide the original velocity along the crease.
                        Vector3 dir = Vector3.Cross(planes[i], planes[j]);
                        Normalize(ref dir);
                        clipVelocity = dir * Vector3.Dot(dir, ps.velocity);
                        endClipVelocity = dir * Vector3.Dot(dir, endVelocity);

                        // A third plane: stop dead.
                        for (int k = 0; k < numplanes; k++)
                        {
                            if (k == i || k == j)
                                continue;
                            if (Vector3.Dot(clipVelocity, planes[k]) >= 0.1f * Inch)
                                continue;
                            ps.velocity = Vector3.zero;
                            return true;
                        }
                    }

                    // All interactions fixed: try another move.
                    ps.velocity = clipVelocity;
                    endVelocity = endClipVelocity;
                    break;
                }
            }

            if (gravity)
                ps.velocity = endVelocity;

            // Don't change velocity during a timer (knockback, landing, water jump).
            if (ps.pmTime != 0)
                ps.velocity = primalVelocity;

            return bumpcount != 0;
        }

        /// <summary>SlideMove, and if that got blocked, try again stepSize higher and step down.</summary>
        void StepSlideMove(bool gravity)
        {
            Vector3 startO = ps.origin;
            Vector3 startV = ps.velocity;

            if (!SlideMove(gravity))
                return; // got exactly where we wanted to go first try

            Vector3 down = startO;
            down.y -= cfg.stepSize;
            TraceResult trace = TraceAll(startO, down);

            // Never step up while still moving up, unless there's walkable ground under us.
            if (ps.velocity.y > 0f && (trace.fraction == 1f || trace.normal.y < 0.7f))
                return;

            Vector3 downO = ps.origin;
            Vector3 downV = ps.velocity;

            // Can we stand a step higher?
            Vector3 up = startO;
            up.y += cfg.stepSize;
            trace = TraceAll(up, up);
            if (trace.allSolid)
                return;

            // Try the slide from up there.
            ps.origin = up;
            ps.velocity = startV;
            SlideMove(gravity);

            // Push back down.
            down = ps.origin;
            down.y -= cfg.stepSize;

            if (ps.IsProne)
            {
                // Legs don't step, they just fuzz.
                TraceResult legs = TraceLegs(ps.origin, down, default, false, ps.viewAngles.y, out _);
                if (legs.allSolid)
                {
                    ps.origin = downO;
                    ps.velocity = downV;
                    return;
                }
            }

            trace = world.Trace(ps.origin, down, mins, maxs);
            if (!trace.allSolid)
                ps.origin = trace.endPos;
            if (trace.fraction < 1f)
                ps.velocity = ClipVelocity(ps.velocity, trace.normal);

            // Step event, used by the view to smooth the camera. (ET rounds it to 4/8/12/16 qu
            // for the network; we send the real height.)
            float delta = ps.origin.y - startO.y;
            if (delta > 2f * Inch)
                Events.Add(new PmEvent { type = PmEventType.StepUp, value = delta });
        }
    }
}
