using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// Drives a vehicle in a steady circle while nobody sits in its seat: a moving platform to
    /// practise riding on (jump on the back of the truck). A player who sits down takes over;
    /// when they get out, it carries on.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class VehicleAutopilot : MonoBehaviour
    {
        [Tooltip("Speed to hold (m/s).")]
        public float speed = 5f;
        [Tooltip("Steering, -1 (full left) to 1 (full right).")]
        [Range(-1f, 1f)] public float steer = 0.8f;

        Vehicle vehicle;

        void Awake() => vehicle = GetComponent<Vehicle>();

        void FixedUpdate()
        {
            bool active = vehicle.Driver == null && vehicle.Lift == null;
            vehicle.Autopilot = active;
            if (!active)
                return;
            float throttle = Mathf.Clamp((speed - vehicle.ForwardSpeed) * 0.5f, -0.5f, 1f);
            vehicle.SetControls(throttle, steer, false);
        }

        void OnDisable()
        {
            if (vehicle != null)
                vehicle.Autopilot = false;
        }
    }
}
