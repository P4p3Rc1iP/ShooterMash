using System;
using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>
    /// Lets a player ride whatever moving thing they stand on: a vehicle, a lift, a scripted
    /// platform (anything with a Rigidbody). Each frame, before the movement ticks, the player is
    /// moved and turned along with the platform since the last frame, and the view turns with it.
    /// Stepping or jumping off adds the platform's velocity to the player's, so a jump on a moving
    /// truck lands back on it; landing on one takes it off again.
    ///
    /// The carry follows the platform's physics pose, which is what its colliders (and so the
    /// player's ground checks) use. Platforms are drawn interpolated between physics steps, so
    /// RenderOffset says how far to shift the drawn player to stay on the drawn platform.
    ///
    /// This sits beside the deterministic movement simulation rather than in it: the platform's
    /// motion is an input the simulation doesn't produce (vehicles run on PhysX). Over the network
    /// the platform's pose is known to both sides, so the same carry can be applied.
    /// </summary>
    public sealed class PlatformRider
    {
        Rigidbody body;
        Vector3 lastPosition;
        Quaternion lastRotation;
        readonly Func<Transform, Vector3, Vector3> velocityOf;

        /// <param name="velocityOf">
        /// Where a platform's velocity at a point comes from. By default: an IMovingPlatform on it,
        /// else its (non-kinematic) rigidbody.
        /// </param>
        public PlatformRider(Func<Transform, Vector3, Vector3> velocityOf = null)
        {
            this.velocityOf = velocityOf ?? DefaultVelocity;
        }

        /// <summary>What the player is riding, or null.</summary>
        public Transform Platform => body != null ? body.transform : null;

        /// <summary>Shift the drawn player by this to match the platform's interpolated drawing.</summary>
        public Vector3 RenderOffset => body != null ? body.transform.position - body.position : Vector3.zero;

        public void Clear() => body = null;

        /// <summary>
        /// Move the player (and the previous state, so interpolation doesn't smear it) with the
        /// platform since last time. The hull (mins/maxs) is for not ending up inside walls.
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
            }
        }

        /// <summary>
        /// After the movement ticks: what is the player standing on now? Hands velocity over when
        /// they step off a platform or land on one.
        /// </summary>
        public void UpdateGround(ref PlayerState state, object groundObject)
        {
            Rigidbody now = state.onGround ? BodyOf(groundObject) : null;
            if (body == null)
                body = null; // destroyed
            if (now == body)
                return;

            if (body != null)
                state.velocity += velocityOf(body.transform, state.origin);   // off: keep the platform's momentum
            if (now != null)
            {
                Vector3 v = velocityOf(now.transform, state.origin);         // on: move relative to it from now on
                state.velocity -= new Vector3(v.x, 0f, v.z);
                lastPosition = now.position;
                lastRotation = now.rotation;
            }
            body = now;
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
