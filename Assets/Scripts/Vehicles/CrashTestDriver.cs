using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// A crash test: once a player stands on the vehicle, it drives straight ahead at a set speed
    /// into whatever is in front of it (a crash barrier), to see riders thrown off. When it has
    /// stopped and nobody is on it any more, it's put back where it started, ready for another go.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class CrashTestDriver : MonoBehaviour
    {
        [Tooltip("Speed it drives at (m/s); it holds this until it hits something.")]
        public float speed = 25f;
        [Tooltip("Seconds after the crash before it's put back (once nobody is on it).")]
        public float resetDelay = 3f;

        enum Phase { Waiting, Running, Crashed }

        Vehicle vehicle;
        Phase phase;
        Vector3 startPosition;
        Quaternion startRotation;
        Vector3 heading;
        float fastest;
        float crashedAt;

        /// <summary>The speed it hit at (m/s), for the last run.</summary>
        public float ImpactSpeed { get; private set; }

        void Awake() => vehicle = GetComponent<Vehicle>();

        void Start()
        {
            startPosition = transform.position;
            startRotation = transform.rotation;
            heading = transform.TransformDirection(vehicle.Forward);
            heading.y = 0f;
            heading.Normalize();
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
                        fastest = 0f;
                    }
                    break;

                case Phase.Running:
                    // Hold the speed and keep straight on the start heading.
                    Vector3 forward = transform.TransformDirection(vehicle.Forward);
                    forward.y = 0f;
                    float steer = Mathf.Clamp(Vector3.SignedAngle(forward, heading, Vector3.up) / 10f, -1f, 1f);
                    vehicle.SetControls(Mathf.Clamp((speed - current) * 0.5f, 0f, 1f), steer, false);
                    fastest = Mathf.Max(fastest, current);
                    if (fastest > 5f && current < fastest * 0.5f)
                    {
                        phase = Phase.Crashed;      // lost half its speed at once: it hit something
                        ImpactSpeed = fastest;
                        crashedAt = Time.time;
                    }
                    break;

                case Phase.Crashed:
                    vehicle.SetControls(0f, 0f, false);
                    if (!rider && Time.time > crashedAt + resetDelay)
                        PutBack();
                    break;
            }
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
