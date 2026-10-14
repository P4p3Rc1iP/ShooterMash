using System;
using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>
    /// Lets a player ride whatever moving thing they stand on: a vehicle, a lift, a scripted
    /// platform (anything with a Rigidbody). Each frame, before the movement ticks, the player is
    /// moved and turned along with the platform since the last frame; the view and the player's
    /// velocity turn with it, so walking on a turning truck feels like walking on solid ground.
    ///
    /// Jumping (or stepping off an edge) keeps you in the platform's frame while you're above it,
    /// so a jump on a turning truck lands where you'd expect instead of drifting off the side. Once
    /// you're past its footprint you let go of it and keep its momentum. Landing on a platform from
    /// elsewhere sticks (see StickyLandings).
    ///
    /// Riders have inertia for hard jolts only (see JoltThreshold): the part of the platform's
    /// acceleration above it isn't passed on, so a truck slamming into a wall or dropping away
    /// over a crest throws you (forwards, or up off the deck). Gentler acceleration is carried
    /// as if your feet never slip, as before.
    ///
    /// The carry follows the platform's physics pose, which is what its colliders (and so the
    /// player's ground checks) use. Platforms are drawn interpolated between physics steps, so
    /// ToDrawn and DrawnYawOffset move the drawn player and view onto the drawn platform.
    ///
    /// This sits beside the deterministic movement simulation rather than in it: the platform's
    /// motion is an input the simulation doesn't produce (vehicles run on PhysX). Over the network
    /// the platform's pose is known to both sides, so the same carry can be applied.
    /// </summary>
    public sealed class PlatformRider
    {
        /// <summary>How far past a platform's edge (m) you can be in the air and still move with it.</summary>
        const float FootprintMargin = 0.4f;

        Rigidbody body;
        Vector3 lastPosition;
        Quaternion lastRotation;
        Vector3 lastVelocity;       // the platform's velocity at the player, at the last physics step
        double lastSampleTime;
        readonly Func<Transform, Vector3, Vector3> velocityOf;
        readonly Func<double> physicsTime;

        /// <param name="velocityOf">
        /// Where a platform's velocity at a point comes from. By default: an IMovingPlatform on it,
        /// else its (non-kinematic) rigidbody.
        /// </param>
        /// <param name="physicsTime">The physics clock (default: Time.fixedTimeAsDouble), for measuring jolts.</param>
        public PlatformRider(Func<Transform, Vector3, Vector3> velocityOf = null, Func<double> physicsTime = null)
        {
            this.velocityOf = velocityOf ?? DefaultVelocity;
            this.physicsTime = physicsTime ?? (() => Time.fixedTimeAsDouble);
        }

        /// <summary>
        /// Landing on a moving platform (from the ground, or another platform) sticks: you stand
        /// still on it. Off: physically right, so you slide if your speed didn't match it.
        /// </summary>
        public bool StickyLandings { get; set; } = true;

        /// <summary>
        /// Platform accelerations up to this (m/s²) carry the player along; anything above it is
        /// left out, so the player keeps going (a crash) or floats off (a crest). 0: no jolts.
        /// Pushing up into the player is never left out: the floor takes that.
        /// </summary>
        public float JoltThreshold { get; set; } = 20f;

        /// <summary>
        /// A sideways jolt also knocks you off your feet: you're lifted this fraction of the speed
        /// it threw you at (up to TripMaxLift m/s). The movement code's ground friction is made for
        /// crisp control and would otherwise stop an 18 m/s slide within a metre and a half.
        /// </summary>
        public float TripLift { get; set; } = 0.3f;
        public const float TripMaxLift = 5f;

        /// <summary>What the player is riding (standing on, or jumping above), or null.</summary>
        public Transform Platform => body != null ? body.transform : null;

        /// <summary>
        /// Where to draw a point that rides along: moved from the platform's physics pose to its
        /// interpolated (drawn) pose, so the player doesn't jitter against the platform between
        /// physics steps.
        /// </summary>
        public Vector3 ToDrawn(Vector3 point)
        {
            if (body == null)
                return point;
            Transform t = body.transform;
            return t.position + t.rotation * (Quaternion.Inverse(body.rotation) * (point - body.position));
        }

        /// <summary>How far the drawn platform is turned from its physics pose (degrees), for the view.</summary>
        public float DrawnYawOffset => body != null ? Yaw(body.transform.rotation * Quaternion.Inverse(body.rotation)) : 0f;

        public void Clear() => body = null;

        /// <summary>
        /// Move the player (and the previous state, so interpolation doesn't smear it) with the
        /// platform since last time, turning their view and velocity with it. In the air, let go
        /// once they're no longer above it. The hull (mins/maxs) is for not ending up inside walls.
        /// </summary>
        public void Carry(ref PlayerState state, ref PlayerState previous, IPmoveWorld world, Vector3 mins, Vector3 maxs)
        {
            if (body == null)
            {
                body = null;
                return;
            }
            Quaternion turn = body.rotation * Quaternion.Inverse(lastRotation);
            Vector3 target = body.position + turn * (state.origin - lastPosition);
            float yaw = Yaw(turn);
            lastPosition = body.position;
            lastRotation = body.rotation;

            Vector3 move = target - state.origin;
            if (move.sqrMagnitude > 1e-10f)
            {
                // Carried into a wall (a truck through a doorway): get pushed out, or stay put.
                Vector3 destination = target;
                if (world.Trace(destination, destination, mins, maxs).startSolid && !world.TryDepenetrate(ref destination, mins, maxs))
                    destination = state.origin;
                Vector3 applied = destination - state.origin;
                state.origin += applied;
                previous.origin += applied;
            }
            if (yaw != 0f)
            {
                state.deltaAngles.y += yaw;
                state.viewAngles.y += yaw;
                previous.viewAngles.y += yaw;
                Quaternion spin = Quaternion.Euler(0f, yaw, 0f);
                state.velocity = spin * state.velocity;   // velocity is relative to the platform: it turns too
            }
            Jolt(ref state);

            if (!state.onGround && !Above(state.origin))
                LetGo(ref state);
        }

        /// <summary>
        /// After the movement ticks: what is the player standing on now? Hands velocity over when
        /// they get off a platform or onto another one. In the air nothing changes: Carry decides.
        /// </summary>
        public void UpdateGround(ref PlayerState state, object groundObject)
        {
            if (body == null)
                body = null; // destroyed
            if (!state.onGround)
                return;      // jumping or falling: still in the frame of what we left, while above it

            Rigidbody now = BodyOf(groundObject);
            if (now == body)
                return;      // still on it, or landed back on it: nothing to hand over

            if (body != null)
                LetGo(ref state);
            if (now != null)
            {
                // Onto a platform from elsewhere: move relative to it from now on.
                if (StickyLandings)
                {
                    state.velocity = new Vector3(0f, state.velocity.y, 0f);
                }
                else
                {
                    Vector3 v = velocityOf(now.transform, state.origin);
                    state.velocity -= new Vector3(v.x, 0f, v.z);
                }
                body = now;
                lastPosition = now.position;
                lastRotation = now.rotation;
                lastVelocity = velocityOf(now.transform, state.origin);
                lastSampleTime = physicsTime();
            }
        }

        /// <summary>
        /// Once per physics step: the part of the platform's change in velocity (at the player)
        /// above the jolt threshold isn't passed on, so it comes off the player's velocity
        /// relative to the platform. Physics time only advances in play mode.
        /// </summary>
        void Jolt(ref PlayerState state)
        {
            double time = physicsTime();
            if (time <= lastSampleTime)
                return;     // no physics step since the last frame
            Vector3 velocity = velocityOf(body.transform, state.origin);
            float dt = (float)(time - lastSampleTime);
            Vector3 change = velocity - lastVelocity;
            lastVelocity = velocity;
            lastSampleTime = time;
            if (JoltThreshold <= 0f)
                return;
            if (change.y > 0f)
                change.y = 0f;      // pushed up: the floor carries that
            float acceleration = change.magnitude / dt;
            if (acceleration <= JoltThreshold)
                return;
            Vector3 thrown = -change * (1f - JoltThreshold / acceleration);
            state.velocity += thrown;
            float lift = Mathf.Min(TripLift * new Vector2(thrown.x, thrown.z).magnitude, TripMaxLift);
            if (state.velocity.y < lift)
                state.velocity.y = lift;
        }

        /// <summary>Leave the platform's frame, keeping its momentum.</summary>
        void LetGo(ref PlayerState state)
        {
            state.velocity += velocityOf(body.transform, state.origin);
            body = null;
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>Is the point above the platform: does a short cast straight down (with a margin) hit it?</summary>
        bool Above(Vector3 point)
        {
            int n = Physics.SphereCastNonAlloc(point + Vector3.up * FootprintMargin, FootprintMargin, Vector3.down, Hits,
                30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Hits[i].collider.attachedRigidbody == body)
                    return true;
            return false;
        }

        /// <summary>A collider's rigidbody, if it has one: static geometry doesn't move.</summary>
        static Rigidbody BodyOf(object groundObject)
        {
            if (!(groundObject is Collider c) || c == null)
                return null;
            Rigidbody b = c.attachedRigidbody;
            return b != null ? b : null;
        }

        static Vector3 DefaultVelocity(Transform t, Vector3 point)
        {
            if (t.TryGetComponent(out IMovingPlatform scripted))
                return scripted.PointVelocity(point);
            return t.TryGetComponent(out Rigidbody rb) && !rb.isKinematic ? rb.GetPointVelocity(point) : Vector3.zero;
        }

        /// <summary>How far a rotation turns things about the vertical (degrees, positive right).</summary>
        static float Yaw(Quaternion q)
        {
            Vector3 f = q * Vector3.forward;
            f.y = 0f;
            return f.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
    }
}
