using UnityEngine;

namespace ShooterMashup.Player
{
    /// <summary>Something players can ride that knows its own velocity (kinematic bodies don't report one).</summary>
    public interface IMovingPlatform
    {
        /// <summary>Velocity (m/s) of the platform's surface at a world point.</summary>
        Vector3 PointVelocity(Vector3 point);
    }

    /// <summary>
    /// A scripted platform for testing: moves back and forth between its start and an offset,
    /// pausing at each end (an elevator, a sliding platform), or turns on the spot (a carousel).
    /// A kinematic, interpolated rigidbody, moved in FixedUpdate like a vehicle, so players ride
    /// it the same way.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MovingPlatform : MonoBehaviour, IMovingPlatform
    {
        public enum Motion { BackAndForth, Turn }

        public Motion motion = Motion.BackAndForth;
        [Tooltip("BackAndForth: where the far end is, relative to the start (m, world axes).")]
        public Vector3 travel = new Vector3(0f, 6f, 0f);
        [Tooltip("BackAndForth: speed while moving (m/s).")]
        public float speed = 2f;
        [Tooltip("BackAndForth: how long it waits at each end (s).")]
        public float pause = 2f;
        [Tooltip("Turn: degrees per second (positive turns right).")]
        public float degreesPerSecond = 30f;

        Rigidbody body;
        Vector3 start;
        Quaternion startRotation;
        Vector3 velocity;
        Vector3 angularVelocity;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            start = transform.position;
            startRotation = transform.rotation;
        }

        void FixedUpdate()
        {
            float t = Time.fixedTime, dt = Time.fixedDeltaTime;
            if (motion == Motion.Turn)
            {
                angularVelocity = Vector3.up * (degreesPerSecond * Mathf.Deg2Rad);
                body.MoveRotation(Quaternion.Euler(0f, degreesPerSecond * t, 0f) * startRotation);
                return;
            }
            Vector3 next = start + travel * Phase(t);
            velocity = (next - start - travel * Phase(t - dt)) / dt;
            body.MovePosition(next);
        }

        /// <summary>0 at the start, 1 at the far end, with pauses at both.</summary>
        float Phase(float t)
        {
            float moving = travel.magnitude / Mathf.Max(speed, 0.01f);
            float cycle = 2f * (moving + pause);
            float c = Mathf.Repeat(t, cycle);
            if (c < pause) return 0f;
            if (c < pause + moving) return (c - pause) / moving;
            if (c < 2f * pause + moving) return 1f;
            return 1f - (c - 2f * pause - moving) / moving;
        }

        public Vector3 PointVelocity(Vector3 point) =>
            velocity + Vector3.Cross(angularVelocity, point - body.position);
    }
}
