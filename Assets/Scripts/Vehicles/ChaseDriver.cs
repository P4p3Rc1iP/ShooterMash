using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// Drives a vehicle in a car chase (see ChaseGroup) while nobody sits in its seat. The quarry
    /// heads for points a few hundred metres ahead, picking the one furthest from the pursuers and
    /// slowing for sharp turns. A pursuer aims at where the quarry will be when it gets there, each
    /// one a few metres to a side of its own (so they come alongside rather than queue up behind),
    /// flat out while far and matching the quarry's speed once close: bumping is part of it. Stuck
    /// against something, it backs off; on its side or roof, it's put back on its wheels.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class ChaseDriver : MonoBehaviour
    {
        public ChaseGroup group;
        [Tooltip("The one being chased (else a pursuer).")]
        public bool quarry;
        [Tooltip("A pursuer's place beside the quarry (m to its right; negative: left) once it's close.")]
        public float slot;

        public Vehicle Vehicle { get; private set; }

        Vector3 waypoint;
        bool hasWaypoint;
        float lastSlip;
        float stuckFor, reverseUntil, upsideDownFor;

        void Awake() => Vehicle = GetComponent<Vehicle>();

        void Start()
        {
            if (group != null)
                group.Join(this);
        }

        void OnDestroy()
        {
            if (group != null)
                group.Leave(this);
        }

        void FixedUpdate()
        {
            if (Vehicle == null || Vehicle.Driver != null || Vehicle.Lift != null || group == null)
                return;                                     // a player drives it (or it's on a lift)
            bool active = group.Active;
            Vehicle.Autopilot = active;                     // off: it parks
            if (!active)
            {
                hasWaypoint = false;
                return;
            }
            if (Recover())
                return;

            Vector3 position = transform.position;
            Vector3 forward = Flat(transform.TransformDirection(Vehicle.Forward)).normalized;
            float speed = Vehicle.ForwardSpeed;
            Vector3 target;
            float targetSpeed;
            if (quarry)
                Flee(position, forward, out target, out targetSpeed);
            else if (!Chase(position, out target, out targetSpeed))
                return;

            // Steer at the target (along with a slide that's swinging, as the autopilot does).
            float angle = Vector3.SignedAngle(forward, Flat(target - position), Vector3.up);
            float slipRate = (Vehicle.SlipAngle - lastSlip) / Time.fixedDeltaTime;
            lastSlip = Vehicle.SlipAngle;
            float steering = Mathf.Clamp((angle + Mathf.Clamp(slipRate, -60f, 60f) * 0.15f) / 25f, -1f, 1f);
            // Slow for sharp turns.
            targetSpeed = Mathf.Min(targetSpeed, Mathf.Lerp(targetSpeed, 9f, Mathf.Abs(angle) / 90f));
            float throttle = Mathf.Clamp((targetSpeed - speed) * 0.3f, -1f, 1f);

            // Stuck (against something, or pointing the wrong way at a crawl): back off for a moment.
            stuckFor = Mathf.Abs(speed) < 1.5f && throttle > 0.3f ? stuckFor + Time.fixedDeltaTime : 0f;
            if (stuckFor > 2.5f)
            {
                reverseUntil = Time.time + 2f;
                stuckFor = 0f;
            }
            if (Time.time < reverseUntil)
            {
                throttle = -1f;
                steering = -Mathf.Sign(angle);
            }
            Vehicle.SetControls(throttle, steering, false);
        }

        /// <summary>The quarry: on to a point a few hundred metres ahead, away from the pursuers.</summary>
        void Flee(Vector3 position, Vector3 forward, out Vector3 target, out float targetSpeed)
        {
            if (!hasWaypoint || Flat(waypoint - position).magnitude < 40f)
            {
                Vector3 away = Flat(position - group.PursuersCentre());
                float best = float.MinValue;
                for (int k = 0; k < 12; k++)
                {
                    float yaw = Random.Range(-75f, 75f);
                    Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * forward;
                    Vector3 candidate = position + dir * Random.Range(200f, 450f);
                    float score = (away.sqrMagnitude > 1f ? Vector3.Dot(dir, away.normalized) : 0f) + Random.value * 0.5f;
                    if (!group.Allowed(candidate))
                        score -= 10f;
                    if (score > best)
                    {
                        best = score;
                        waypoint = candidate;
                    }
                }
                if (!group.Allowed(waypoint))
                    waypoint = new Vector3(group.area.center.x, 0f, group.area.center.y);   // boxed in: head for the middle
                hasWaypoint = true;
            }
            target = waypoint;
            targetSpeed = Mathf.Min(group.quarrySpeed, Vehicle.TopSpeed);
        }

        /// <summary>A pursuer: at where the quarry will be, to its own side when close.</summary>
        bool Chase(Vector3 position, out Vector3 target, out float targetSpeed)
        {
            Vehicle prey = group.Quarry;
            target = position;
            targetSpeed = 0f;
            if (prey == null)
            {
                Vehicle.SetControls(0f, 0f, false);
                return false;
            }
            Vector3 preyPosition = prey.transform.position;
            Vector3 preyVelocity = Flat(prey.Body.linearVelocity);
            float distance = Flat(preyPosition - position).magnitude;
            float closing = Mathf.Max(5f, Vehicle.Speed);
            float lead = Mathf.Clamp(distance / closing, 0f, 2f);
            Vector3 preyForward = Flat(prey.transform.TransformDirection(prey.Forward)).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, preyForward);
            float beside = Mathf.Clamp01(1f - (distance - 15f) / 30f);   // move over to its slot within ~30 m
            target = preyPosition + preyVelocity * lead + side * (slot * beside);
            // Flat out while far; close in at a little more than its speed.
            float preySpeed = preyVelocity.magnitude;
            targetSpeed = distance > 40f ? group.pursuerSpeed : preySpeed + Mathf.Clamp((distance - 6f) * 0.5f, -3f, 8f);
            targetSpeed = Mathf.Min(targetSpeed, group.pursuerSpeed, Vehicle.TopSpeed);
            return true;
        }

        /// <summary>On its side or roof for a while: put it back on its wheels where it is. Returns true while it's down.</summary>
        bool Recover()
        {
            if (transform.up.y > 0.3f)
            {
                upsideDownFor = 0f;
                return false;
            }
            upsideDownFor += Time.fixedDeltaTime;
            Vehicle.SetControls(0f, 0f, false);
            if (upsideDownFor > 3f)
            {
                Vector3 f = Flat(transform.TransformDirection(Vehicle.Forward));
                Quaternion upright = Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward)
                                     * Quaternion.Inverse(Quaternion.LookRotation(Vehicle.Forward));
                Vehicle.SetPose(transform.position + Vector3.up * 2.5f, upright);
                Vehicle.Body.linearVelocity = Vector3.zero;
                Vehicle.Body.angularVelocity = Vector3.zero;
                upsideDownFor = 0f;
            }
            return true;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void OnDisable()
        {
            if (Vehicle != null && Vehicle.Driver == null)
            {
                Vehicle.Autopilot = false;
                Vehicle.SetControls(0f, 0f, false);
            }
        }
    }
}
