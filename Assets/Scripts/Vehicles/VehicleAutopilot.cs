using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// Drives a vehicle while nobody sits in its seat: a moving platform to practise riding on.
    /// Without a path it drives in a steady circle. With one (a closed loop of world points, e.g.
    /// a race circuit's centre line) it laps it: steering at a point ahead on the line, slowing for
    /// corners, and stopping at the start of the path each lap so passengers can get on and off.
    /// A player who sits down takes over; when they get out, it carries on.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class VehicleAutopilot : MonoBehaviour
    {
        [Tooltip("Circling: speed to hold (m/s).")]
        public float speed = 5f;
        [Tooltip("Circling: steering, -1 (full left) to 1 (full right).")]
        [Range(-1f, 1f)] public float steer = 0.8f;

        [Header("Following a path")]
        [Tooltip("A closed loop of world points to lap. Empty: drive in a circle.")]
        public Vector3[] path = new Vector3[0];
        [Tooltip("Top speed on straights (m/s).")]
        public float maxSpeed = 22f;
        [Tooltip("How hard it brakes to stop at the start of the path (m/s²).")]
        public float stopDeceleration = 5f;
        [Tooltip("Speed in the tightest corners (m/s).")]
        public float cornerSpeed = 8f;
        [Tooltip("Seconds it waits at the start of the path each lap.")]
        public float stopSeconds = 6f;

        Vehicle vehicle;
        int index;
        float waitUntil;

        void Awake() => vehicle = GetComponent<Vehicle>();

        void Start() => waitUntil = Time.time + stopSeconds;

        void FixedUpdate()
        {
            bool active = vehicle.Driver == null && vehicle.Lift == null;
            vehicle.Autopilot = active;
            if (!active)
                return;
            if (path == null || path.Length < 3)
            {
                float throttle = Mathf.Clamp((speed - vehicle.ForwardSpeed) * 0.5f, -0.5f, 1f);
                vehicle.SetControls(throttle, steer, false);
                return;
            }
            FollowPath();
        }

        void FollowPath()
        {
            Vector3 position = vehicle.transform.position;
            Vector3 forward = vehicle.transform.TransformDirection(vehicle.Forward);
            forward.y = 0f;
            float currentSpeed = vehicle.ForwardSpeed;

            // Move along the line to the nearest point a little way ahead of us.
            int best = index;
            float bestDistance = Flat(path[index] - position).sqrMagnitude;
            for (int k = 1; k < 12; k++)
            {
                int i = (index + k) % path.Length;
                float d = Flat(path[i] - position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            if (best < index)
                waitUntil = Time.time + stopSeconds;    // a new lap: stop at the start
            index = best;

            // Steer at a point ahead on the line, further ahead the faster we go.
            Vector3 target = PointAhead(6f + Mathf.Abs(currentSpeed) * 0.5f);
            float angle = Vector3.SignedAngle(forward, Flat(target - position), Vector3.up);
            float steering = Mathf.Clamp(angle / 25f, -1f, 1f);

            // Slow for the bends coming up: how much the line turns over the next 25 m.
            Vector3 here = Flat(PointAhead(2f) - path[index]);
            Vector3 there = Flat(PointAhead(27f) - PointAhead(22f));
            float bend = Vector3.Angle(here, there);
            float targetSpeed = Mathf.Lerp(maxSpeed, cornerSpeed, Mathf.Clamp01(bend / 70f));

            // Brake in time to stop on the start of the path, then wait there.
            targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(2f, Mathf.Sqrt(2f * stopDeceleration * DistanceToEnd())));
            bool waiting = Time.time < waitUntil;
            if (waiting)
                targetSpeed = 0f;
            float throttle = Mathf.Clamp((targetSpeed - currentSpeed) * 0.4f, -1f, 1f);
            vehicle.SetControls(waiting && Mathf.Abs(currentSpeed) < 1f ? 0f : throttle, steering, false);
        }

        /// <summary>The point `distance` metres further along the line from the current one.</summary>
        Vector3 PointAhead(float distance)
        {
            Vector3 from = path[index];
            for (int k = 1; k <= path.Length; k++)
            {
                Vector3 to = path[(index + k) % path.Length];
                float step = Vector3.Distance(from, to);
                if (step >= distance)
                    return Vector3.Lerp(from, to, distance / step);
                distance -= step;
                from = to;
            }
            return from;
        }

        float[] along;      // distance along the path from its start to each point
        float lapLength;

        /// <summary>How far it is along the path from the current point back to the start.</summary>
        float DistanceToEnd()
        {
            if (along == null || along.Length != path.Length)
            {
                along = new float[path.Length];
                for (int i = 1; i < path.Length; i++)
                    along[i] = along[i - 1] + Vector3.Distance(path[i - 1], path[i]);
                lapLength = along[path.Length - 1] + Vector3.Distance(path[path.Length - 1], path[0]);
            }
            return lapLength - along[index];
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDisable()
        {
            if (vehicle != null)
                vehicle.Autopilot = false;
        }
    }
}
