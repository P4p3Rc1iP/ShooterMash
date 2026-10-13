using ShooterMashup.Player;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// Drives a vehicle while nobody sits in its seat: a moving platform to practise riding on.
    /// Without a path it drives in a steady circle. With one (a closed loop of world points, e.g. a
    /// track's centre line) it laps it: steering at a point ahead on the line, and planning its
    /// speed from the bends ahead (as fast as the line's curvature allows, braking in time for
    /// each bend). It can stop at the start of the path each lap so passengers can get on, and/or
    /// only drive while a player is riding it. A player who sits down takes over; when they get
    /// out, it carries on.
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
        [Tooltip("Top speed it aims for (m/s); the vehicle's own top speed caps it too.")]
        public float maxSpeed = 22f;
        [Tooltip("It slows for bends so their sideways acceleration stays under this (m/s²).")]
        public float cornerAcceleration = 7f;
        [Tooltip("Never slower than this in a bend (m/s).")]
        public float cornerSpeed = 8f;
        [Tooltip("How hard it plans to brake for bends and stops (m/s²).")]
        public float planBraking = 6f;
        [Tooltip("Seconds it waits at the start of the path each lap (0: doesn't stop).")]
        public float stopSeconds = 6f;
        [Tooltip("Only drive while a player is standing on it (or jumping above it).")]
        public bool waitForRider;

        Vehicle vehicle;
        int index;
        float waitUntil;
        float[] along;      // distance along the path from its start to each point
        float[] allowed;    // the fastest speed the line's curvature allows at each point
        float lapLength;
        PlayerMotor[] players = new PlayerMotor[0];
        float nextScan;

        /// <summary>Is a player riding it right now?</summary>
        public bool HasRider { get; private set; }
        /// <summary>The speed it's aiming for and the throttle it gave, last physics step (for tuning).</summary>
        public float TargetSpeed { get; private set; }
        public float Throttle { get; private set; }

        void Awake() => vehicle = GetComponent<Vehicle>();

        void Start() => waitUntil = Time.time + stopSeconds;

        void FixedUpdate()
        {
            bool active = vehicle.Driver == null && vehicle.Lift == null;
            vehicle.Autopilot = active;
            if (!active)
                return;
            HasRider = Riding();
            if (path == null || path.Length < 3)
            {
                float target = waitForRider && !HasRider ? 0f : speed;
                float throttle = Mathf.Clamp((target - vehicle.ForwardSpeed) * 0.5f, -0.5f, 1f);
                vehicle.SetControls(throttle, steer, false);
                return;
            }
            FollowPath();
        }

        bool Riding()
        {
            if (!waitForRider)
                return false;
            if (Time.time >= nextScan)
            {
                players = FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);
                nextScan = Time.time + 1f;
            }
            foreach (PlayerMotor p in players)
                if (p != null && p.enabled && p.Platform == transform)
                    return true;
            return false;
        }

        void FollowPath()
        {
            Prepare();
            Vector3 position = vehicle.transform.position;
            Vector3 forward = vehicle.transform.TransformDirection(vehicle.Forward);
            forward.y = 0f;
            float currentSpeed = vehicle.ForwardSpeed;

            // Move along the line to the nearest point a little way ahead of us.
            int best = index;
            float bestDistance = Flat(path[index] - position).sqrMagnitude;
            for (int k = 1; k < 16; k++)
            {
                int i = (index + k) % path.Length;
                float d = Flat(path[i] - position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            if (best < index && stopSeconds > 0f)
                waitUntil = Time.time + stopSeconds;    // a new lap: stop at the start
            index = best;

            // Steer at a point ahead on the line, further ahead the faster we go.
            Vector3 target = PointAhead(6f + Mathf.Abs(currentSpeed) * 0.5f);
            float angle = Vector3.SignedAngle(forward, Flat(target - position), Vector3.up);
            float steering = Mathf.Clamp(angle / 25f, -1f, 1f);

            float targetSpeed = PlannedSpeed();
            if (stopSeconds > 0f)
                targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(2f, Mathf.Sqrt(2f * planBraking * (lapLength - along[index]))));
            bool waiting = Time.time < waitUntil || (waitForRider && !HasRider);
            if (waiting)
                targetSpeed = 0f;
            float throttle = Mathf.Clamp((targetSpeed - currentSpeed) * 0.4f, -1f, 1f);
            if (waiting && Mathf.Abs(currentSpeed) < 1f)
                throttle = 0f;
            TargetSpeed = targetSpeed;
            Throttle = throttle;
            vehicle.SetControls(throttle, steering, false);
        }

        /// <summary>
        /// The fastest we can go now and still slow down in time for every bend ahead: the lowest
        /// over the points ahead of sqrt(allowed² + 2 · braking · distance to it).
        /// </summary>
        float PlannedSpeed()
        {
            float top = Mathf.Min(maxSpeed, vehicle.TopSpeed);
            float horizon = top * top / (2f * planBraking) + 20f;
            float result = top;
            float distance = 0f;
            for (int k = 0; k < path.Length && distance < horizon; k++)
            {
                int i = (index + k) % path.Length;
                if (k > 0)
                    distance += Vector3.Distance(path[(i - 1 + path.Length) % path.Length], path[i]);
                float reachable = Mathf.Sqrt(allowed[i] * allowed[i] + 2f * planBraking * distance);
                result = Mathf.Min(result, reachable);
            }
            return result;
        }

        /// <summary>Distances along the path and the speed its curvature allows at each point (once).</summary>
        void Prepare()
        {
            if (along != null && along.Length == path.Length)
                return;
            int n = path.Length;
            along = new float[n];
            allowed = new float[n];
            for (int i = 1; i < n; i++)
                along[i] = along[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            lapLength = along[n - 1] + Vector3.Distance(path[n - 1], path[0]);
            for (int i = 0; i < n; i++)
            {
                // Curvature from the turn between the segments on either side of the point.
                Vector3 a = path[(i - 1 + n) % n], b = path[i], c = path[(i + 1) % n];
                float turn = Vector3.Angle(Flat(b - a), Flat(c - b)) * Mathf.Deg2Rad;
                float length = (Flat(b - a).magnitude + Flat(c - b).magnitude) / 2f;
                float curvature = length > 1e-3f ? turn / length : 0f;
                allowed[i] = curvature > 1e-4f
                    ? Mathf.Max(cornerSpeed, Mathf.Sqrt(cornerAcceleration / curvature))
                    : float.MaxValue;
            }
            // Smooth over neighbours: a bend's sample points can be uneven.
            var smoothed = new float[n];
            for (int i = 0; i < n; i++)
                smoothed[i] = Mathf.Min(allowed[i], Mathf.Min(allowed[(i - 1 + n) % n], allowed[(i + 1) % n]));
            allowed = smoothed;
        }

        /// <summary>The point `distance` metres further along the line from the current one.</summary>
        Vector3 PointAhead(float distance)
        {
            Vector3 from = path[index];
            for (int k = 1; k <= path.Length; k++)
            {
                Vector3 to = path[(index + k) % path.Length];
                float step = Vector3.Distance(from, to);
                if (step >= distance && step > 1e-4f)
                    return Vector3.Lerp(from, to, distance / step);
                distance -= step;
                from = to;
            }
            return from;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDisable()
        {
            if (vehicle != null)
                vehicle.Autopilot = false;
        }
    }
}
