using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// Drives a stunt once a player stands on the vehicle: at a set speed, straight ahead or along
    /// a path (a crash into a barrier, a kicker ramp that rolls it over, a loop). Steering is
    /// worked out in the vehicle's own frame, so it keeps working on its side or upside down.
    /// The run ends when it crashes (loses over 5 m/s within 0.1 s), when it has gone `runLength`
    /// metres, or when it gets stuck; then it brakes, and once it has stopped and nobody is on it
    /// any more, it's put back where it started, ready for another go.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class StuntDriver : MonoBehaviour
    {
        [Tooltip("Speed it drives at (m/s); it holds this until the run ends.")]
        public float speed = 25f;
        [Tooltip("World points to follow, in order (empty: straight ahead).")]
        public Vector3[] path = new Vector3[0];
        [Tooltip("The run ends after this many metres (0: only when it crashes or gets stuck).")]
        public float runLength;
        [Tooltip("Seconds after the run before it's put back (once nobody is on it).")]
        public float resetDelay = 3f;
        [Tooltip("Keep the self-levelling assists (level to the ground, upright in the air). Off lets it roll over.")]
        public bool uprightAssists = true;
        [Tooltip("Spring sag at rest, as a fraction of travel (0: keep the vehicle's own). Lower is stiffer: 0.2 takes about 5 times the weight before bottoming out, for loops.")]
        [Range(0f, 0.9f)] public float rideHeight;

        /// <summary>
        /// Losing more speed than this (m/s) within CrashWindow seconds is a crash. Brakes and
        /// loops never do; a knock against a bump is over within a step and loses less.
        /// </summary>
        const float CrashSpeedLoss = 5f, CrashWindow = 0.1f;

        enum Phase { Waiting, Running, Done }

        Vehicle vehicle;
        Phase phase;
        Vector3 startPosition;
        Quaternion startRotation;
        Vector3 heading;
        Vector3 lastPosition;
        float fastest, travelled, doneAt, slowSince;
        readonly Queue<(float time, float speed)> recent = new Queue<(float, float)>();
        int index;

        /// <summary>The fastest it went in the last run (m/s), and whether that run ended in a crash.</summary>
        public float TopSpeed => fastest;
        public bool Crashed { get; private set; }

        void Awake() => vehicle = GetComponent<Vehicle>();

        void Start()
        {
            startPosition = transform.position;
            startRotation = transform.rotation;
            heading = transform.TransformDirection(vehicle.Forward);
            heading.y = 0f;
            heading.Normalize();
            if (rideHeight > 0f)
                vehicle.tuning.rideHeight = rideHeight;
            if (!uprightAssists)
            {
                vehicle.tuning.uprightStrength = 0f;
                vehicle.tuning.airUpright = 0f;
            }
        }

        void FixedUpdate()
        {
            bool active = vehicle.Driver == null && vehicle.Lift == null;
            vehicle.Autopilot = active;
            if (!active)
                return;
            bool rider = Riders.On(transform);
            float current = vehicle.ForwardSpeed;
            switch (phase)
            {
                case Phase.Waiting:
                    vehicle.SetControls(0f, 0f, false);
                    if (rider)
                    {
                        phase = Phase.Running;
                        fastest = travelled = 0f;
                        index = 0;
                        Crashed = false;
                        slowSince = Time.time;
                        lastPosition = transform.position;
                        recent.Clear();
                    }
                    break;

                case Phase.Running:
                    vehicle.SetControls(Mathf.Clamp((speed - current) * 0.5f, 0f, 1f), Steering(), false);
                    travelled += Vector3.Distance(transform.position, lastPosition);
                    lastPosition = transform.position;
                    fastest = Mathf.Max(fastest, current);
                    if (current > 2f)
                        slowSince = Time.time;
                    recent.Enqueue((Time.time, current));
                    while (recent.Peek().time < Time.time - CrashWindow)
                        recent.Dequeue();
                    Crashed = recent.Peek().speed - current > CrashSpeedLoss;
                    bool finished = runLength > 0f && travelled >= runLength;
                    bool stuck = Time.time > slowSince + 3f;              // on its roof, or wedged
                    if (Crashed || finished || stuck)
                    {
                        phase = Phase.Done;
                        doneAt = Time.time;
                    }
                    break;

                case Phase.Done:
                    // Brake to a stop (reverse throttle brakes while it still rolls forwards).
                    vehicle.SetControls(current > 0.5f ? -1f : 0f, 0f, false);
                    if (!rider && Mathf.Abs(current) < 0.5f && Time.time > doneAt + resetDelay)
                        PutBack();
                    break;
            }
        }

        /// <summary>
        /// Steer at a point ahead: on the path, or straight on along the start heading. The angle
        /// is taken in the vehicle's own frame, so it works on a loop's ceiling too.
        /// </summary>
        float Steering()
        {
            Vector3 target;
            if (path != null && path.Length >= 2)
            {
                // Move along the path to the nearest point a little way ahead.
                for (int k = 0; k < 8 && index + 1 < path.Length; k++)
                {
                    if ((path[index + 1] - transform.position).sqrMagnitude < (path[index] - transform.position).sqrMagnitude)
                        index++;
                    else
                        break;
                }
                target = path[Mathf.Min(index + 3, path.Length - 1)];
            }
            else
            {
                Vector3 along = Vector3.Project(transform.position - startPosition, heading);
                target = startPosition + along + heading * 15f;
            }
            Vector3 local = transform.InverseTransformPoint(target);
            Vector3 forward = vehicle.Forward;
            float angle = Vector3.SignedAngle(forward, new Vector3(local.x, 0f, local.z), Vector3.up);
            return Mathf.Clamp(angle / 25f, -1f, 1f);
        }

        void PutBack()
        {
            vehicle.Body.linearVelocity = Vector3.zero;
            vehicle.Body.angularVelocity = Vector3.zero;
            vehicle.SetPose(startPosition, startRotation);
            phase = Phase.Waiting;
        }

        void OnDisable()
        {
            if (vehicle != null)
                vehicle.Autopilot = false;
        }
    }
}
