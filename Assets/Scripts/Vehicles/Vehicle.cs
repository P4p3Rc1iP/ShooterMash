using System.Collections.Generic;
using ShooterMashup.Building;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// A vehicle: one rigid body made of build pieces, with its own build simulation in its local
    /// space (built on only while it's on a lift). Its pieces' colliders make up the body; mass and
    /// centre of mass come from the pieces. Wheels are raycast suspension done in code (no joints),
    /// with assists so that whatever a player builds drives reasonably (see VehicleTuning).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Vehicle : BuildHost
    {
        public VehicleTuning tuning = new VehicleTuning();

        public static readonly List<Vehicle> All = new List<Vehicle>();

        /// <summary>A wheel piece at run time.</summary>
        sealed class Wheel
        {
            public int pieceId;
            public WheelSpec spec;
            public Vector3 anchor;        // local: the wheel centre at ride height
            public Quaternion rotation;   // local: the piece's rotation (axle along its x)
            public Vector3 roll;          // local: the way it rolls, along the vehicle's forward
            public float driveFactor;     // how much of the throttle it turns into push (0 if it rolls sideways)
            public float steerFactor;     // 1 for the frontmost wheels, 0 for the rear half
            public Transform tyre;
            public float compression;     // 0 full droop .. 1 full bump
            public float steerAngle;
            public float spin;            // degrees, for the tyre
            public float spinSign;        // +1 when the axle points right of the rolling direction
            public bool grounded;
        }

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        StructureManager look;
        BuildSystem system;
        VehicleBuildWorld world;
        PieceViews views;
        Rigidbody body;
        int anchorId = -1;
        readonly List<Wheel> wheels = new List<Wheel>();
        Vector3 forward = Vector3.forward;
        Vector3 centerOfMass;
        float throttle, steerInput, steer;
        bool handbrake;

        public override BuildSystem System => system;
        public override PieceViews Views => views;
        public override BuildFailure CanBuild => Lift != null ? BuildFailure.None : BuildFailure.NotOnLift;

        /// <summary>The lift holding the vehicle (frozen in place), or null when it's free.</summary>
        public Lift Lift { get; private set; }
        public Rigidbody Body => body;
        /// <summary>Driving direction in local space: where the seat faces.</summary>
        public Vector3 Forward => forward;
        /// <summary>The seat piece's id, or -1.</summary>
        public int SeatId { get; private set; } = -1;
        /// <summary>Who's driving, or null (the vehicle then holds itself still).</summary>
        public VehicleDriver Driver { get; set; }
        public int WheelCount => wheels.Count;
        public int WheelsOnGround { get; private set; }
        public float Speed => body.linearVelocity.magnitude;
        public float ForwardSpeed => Vector3.Dot(body.linearVelocity, transform.TransformDirection(forward));
        public float Mass => body.mass;

        /// <summary>A new, empty vehicle at a frame (a lift's top), frozen until released.</summary>
        public static Vehicle Create(StructureManager look, Transform at)
        {
            var go = new GameObject("Vehicle");
            go.transform.SetPositionAndRotation(at.position, at.rotation);
            go.AddComponent<Rigidbody>();
            var v = go.AddComponent<Vehicle>();
            v.Init(look);
            return v;
        }

        void Init(StructureManager structures)
        {
            look = structures;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.5f;

            world = new VehicleBuildWorld(RestsOnLift, b => b.Transformed(transform.position, transform.rotation), look.World.Actors);
            system = new BuildSystem(VehicleBuildWorld.Config(), world);
            views = new PieceViews(system, transform, look);
            views.Rebuilt += Rebuild;
            All.Add(this);
        }

        void OnDestroy()
        {
            All.Remove(this);
            views?.Dispose();
        }

        // ------------------------------------------------------------------ building

        public override BuildResult Execute(in BuildCmd cmd)
        {
            BuildResult r = base.Execute(cmd);
            if (r.Ok)
                AfterChange();
            return r;
        }

        void AfterChange()
        {
            if (system.Pieces.Count == 0)
            {
                // Nothing left of it.
                if (Driver != null) Driver.Exit();
                if (Lift != null) Lift.Forget(this);
                Destroy(gameObject);
                return;
            }
            if (system.Get(anchorId) == null)
            {
                // The first piece, or the anchor was removed: anchor the biggest connected part,
                // and anything not connected to it falls off.
                anchorId = PickAnchor();
                world.Anchor = system.Get(anchorId).boxes;
                system.RecomputeAll();
            }
            if (Lift != null)
                Lift.Fit();
        }

        /// <summary>The lowest-id piece of the biggest connected group.</summary>
        int PickAnchor()
        {
            int best = -1, bestSize = 0;
            var seen = new HashSet<int>();
            var ids = new List<int>(system.Pieces.Keys);
            ids.Sort();
            foreach (int start in ids)
            {
                if (seen.Contains(start))
                    continue;
                int size = 0;
                var stack = new Stack<int>();
                stack.Push(start);
                seen.Add(start);
                while (stack.Count > 0)
                {
                    size++;
                    foreach (Link link in system.Get(stack.Pop()).Links)
                        if (seen.Add(link.other))
                            stack.Push(link.other);
                }
                if (size > bestSize)
                {
                    bestSize = size;
                    best = start;
                }
            }
            return best;
        }

        bool RestsOnLift(Obb[] boxes, float tolerance)
        {
            if (Lift == null)
                return false;
            foreach (Obb b in boxes)
                if (Lift.Supports(b.Transformed(transform.position, transform.rotation), tolerance))
                    return true;
            return false;
        }

        /// <summary>Bounds of the vehicle in its local space, including the wheels hanging at full droop.</summary>
        public Bounds LocalBounds()
        {
            var b = new Bounds();
            bool any = false;
            foreach (Piece p in system.Pieces.Values)
            {
                if (!any) b = p.bounds;
                else b.Encapsulate(p.bounds);
                any = true;
            }
            foreach (Wheel w in wheels)
            {
                Vector3 droop = w.anchor - Vector3.up * (tuning.travel * tuning.rideHeight);
                b.Encapsulate(droop - Vector3.up * w.spec.radius);
                b.Encapsulate(droop + Vector3.up * w.spec.radius);
            }
            return b;
        }

        // ------------------------------------------------------------------ lift

        internal void AttachTo(Lift lift)
        {
            Lift = lift;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            foreach (Wheel w in wheels)
            {
                w.compression = 0f;
                w.grounded = false;
            }
        }

        internal void Detach()
        {
            Lift = null;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }

        internal void SetPose(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
        }

        // ------------------------------------------------------------------ parts and mass

        /// <summary>After pieces changed: mass, centre of mass, inertia, wheels and seat.</summary>
        void Rebuild()
        {
            wheels.Clear();
            SeatId = -1;
            forward = Vector3.forward;
            float mass = 0f;
            Vector3 moment = Vector3.zero;
            var ids = new List<int>(system.Pieces.Keys);
            ids.Sort();
            foreach (int id in ids)
            {
                Piece p = system.Get(id);
                float density = Density(p.record.material);
                foreach (Obb box in p.boxes)
                {
                    float m = box.Volume * density;
                    mass += m;
                    moment += box.center * m;
                }
                if (p.def.Wheel != null)
                {
                    WheelSpec spec = p.def.Wheel;
                    var w = new Wheel
                    {
                        pieceId = id,
                        spec = spec,
                        anchor = p.record.position + p.record.rotation * spec.center,
                        rotation = p.record.rotation,
                        tyre = views.ViewOf(id)?.transform.Find(StructureManager.WheelVisualName),
                    };
                    wheels.Add(w);
                    mass += spec.mass;
                    moment += w.anchor * spec.mass;
                }
                if (p.def.Seat != null && SeatId < 0)
                {
                    SeatId = id;
                    Vector3 f = p.record.rotation * Vector3.forward;
                    f.y = 0f;
                    if (f.sqrMagnitude > 1e-4f)
                        forward = f.normalized;
                }
            }

            centerOfMass = mass > 0f ? moment / mass : Vector3.zero;
            if (wheels.Count > 0)
            {
                // Assist: keep the centre of mass low, so tall builds (a shed on a truck) don't tip over.
                float wheelHeight = 0f;
                foreach (Wheel w in wheels) wheelHeight += w.anchor.y;
                wheelHeight /= wheels.Count;
                centerOfMass.y = Mathf.Min(centerOfMass.y, wheelHeight + tuning.maxCenterOfMassAboveWheels);
            }

            body.mass = Mathf.Max(mass, 10f);
            body.centerOfMass = centerOfMass;
            body.inertiaTensor = Inertia(ids, centerOfMass, body.mass);
            body.inertiaTensorRotation = Quaternion.identity;

            SetupWheels();
        }

        float Density(BuildMaterial m) => m switch
        {
            BuildMaterial.Stone => tuning.stoneDensity,
            BuildMaterial.Iron => tuning.ironDensity,
            _ => tuning.woodDensity,
        };

        /// <summary>Inertia about the centre of mass, from the pieces' boxes (each as a solid box).</summary>
        Vector3 Inertia(List<int> ids, Vector3 com, float totalMass)
        {
            Vector3 inertia = Vector3.zero;
            foreach (int id in ids)
            {
                Piece p = system.Get(id);
                float density = Density(p.record.material);
                foreach (Obb box in p.boxes)
                {
                    float m = box.Volume * density;
                    Vector3 s = box.Bounds.size;
                    Vector3 d = box.center - com;
                    inertia += m * new Vector3(
                        (s.y * s.y + s.z * s.z) / 12f + d.y * d.y + d.z * d.z,
                        (s.x * s.x + s.z * s.z) / 12f + d.x * d.x + d.z * d.z,
                        (s.x * s.x + s.y * s.y) / 12f + d.x * d.x + d.y * d.y);
                }
            }
            foreach (Wheel w in wheels)
            {
                Vector3 d = w.anchor - com;
                inertia += w.spec.mass * new Vector3(d.y * d.y + d.z * d.z, d.x * d.x + d.z * d.z, d.x * d.x + d.y * d.y);
            }
            // Never less than a 1 m cube of the same mass, so tiny builds don't spin wildly.
            float floor = totalMass / 6f;
            return Vector3.Max(inertia, new Vector3(floor, floor, floor));
        }

        /// <summary>Which way each wheel rolls, how much it drives, and how much it steers.</summary>
        void SetupWheels()
        {
            float minAlong = float.MaxValue, maxAlong = float.MinValue;
            foreach (Wheel w in wheels)
            {
                Vector3 axle = w.rotation * Vector3.right;
                Vector3 roll = Vector3.Cross(axle, Vector3.up);
                if (Vector3.Dot(roll, forward) < 0f) roll = -roll;
                w.roll = roll.sqrMagnitude > 1e-6f ? roll.normalized : forward;
                w.driveFactor = Mathf.Abs(Vector3.Dot(w.roll, forward));
                w.spinSign = Vector3.Dot(axle, Vector3.Cross(Vector3.up, w.roll)) >= 0f ? 1f : -1f;
                float along = Vector3.Dot(w.anchor - centerOfMass, forward);
                minAlong = Mathf.Min(minAlong, along);
                maxAlong = Mathf.Max(maxAlong, along);
            }
            // The front half of the wheels steer, the frontmost the most: works whatever the layout.
            float mid = (minAlong + maxAlong) / 2f;
            foreach (Wheel w in wheels)
            {
                float along = Vector3.Dot(w.anchor - centerOfMass, forward);
                w.steerFactor = maxAlong - mid > 0.25f ? Mathf.Clamp01((along - mid) / (maxAlong - mid)) : 0f;
            }
        }

        // ------------------------------------------------------------------ driving

        /// <summary>Throttle -1..1 (negative brakes, then reverses), steer -1 (left) .. 1 (right).</summary>
        public void SetControls(float throttleInput, float steerAxis, bool handbrakeOn)
        {
            throttle = Mathf.Clamp(throttleInput, -1f, 1f);
            steerInput = Mathf.Clamp(steerAxis, -1f, 1f);
            handbrake = handbrakeOn;
        }

        void FixedUpdate()
        {
            if (body == null || body.isKinematic)
                return;
            Simulate(Time.fixedDeltaTime);
        }

        /// <summary>One physics step of suspension, grip, drive and assists.</summary>
        public void Simulate(float dt)
        {
            VehicleTuning t = tuning;
            body.AddForce(Vector3.down * t.extraGravity, ForceMode.Acceleration);

            int n = wheels.Count;
            WheelsOnGround = 0;
            if (n == 0)
                return;

            bool driven = Driver != null;
            float throttleNow = driven ? throttle : 0f;
            bool braking = handbrake || !driven;
            steer = Mathf.MoveTowards(steer, driven ? steerInput : 0f, t.steerRate * dt);

            Vector3 up = transform.up;
            Vector3 fwdWorld = transform.TransformDirection(forward);
            float speed = Vector3.Dot(body.linearVelocity, fwdWorld);
            float share = body.mass / n;
            float gravity = -Physics.gravity.y + t.extraGravity;
            float k = share * gravity / (t.travel * t.rideHeight);
            float c = 2f * t.damping * Mathf.Sqrt(k * share);
            float maxSteer = Mathf.Lerp(t.maxSteer, t.steerAtTopSpeed, Mathf.Abs(speed) / t.topSpeed);
            Vector3 com = body.worldCenterOfMass;
            Vector3 normalSum = Vector3.zero;

            foreach (Wheel w in wheels)
            {
                Vector3 anchor = transform.TransformPoint(w.anchor);
                float top = t.travel * (1f - t.rideHeight);           // full bump above the anchor
                Vector3 origin = anchor + up * top;
                float length = t.travel + w.spec.radius;
                w.steerAngle = w.steerFactor * steer * maxSteer;

                if (!CastWheel(origin, -up, length, out RaycastHit hit))
                {
                    w.grounded = false;
                    w.compression = Mathf.MoveTowards(w.compression, 0f, dt * 4f);
                    continue;
                }
                w.grounded = true;
                WheelsOnGround++;
                normalSum += hit.normal;

                // Spring and damper along the suspension.
                float raw = (length - hit.distance) / t.travel;
                float compression = Mathf.Clamp01(raw);
                float velocity = (compression - w.compression) * t.travel / dt;
                w.compression = compression;
                float load = Mathf.Max(0f, k * compression * t.travel + c * velocity);
                if (raw > 1f)
                    load += k * (raw - 1f) * t.travel * 4f;            // bump stop
                body.AddForceAtPosition(up * load, hit.point);

                // Grip: the tyre's rolling and sideways directions on the ground.
                Vector3 rollWorld = transform.TransformDirection(Quaternion.AngleAxis(w.steerAngle, Vector3.up) * w.roll);
                Vector3 rollOnGround = Vector3.ProjectOnPlane(rollWorld, hit.normal).normalized;
                Vector3 side = Vector3.Cross(hit.normal, rollOnGround);
                Vector3 v = body.GetPointVelocity(hit.point);
                float vRoll = Vector3.Dot(v, rollOnGround);
                float vSide = Vector3.Dot(v, side);

                float lateral = -vSide * share * t.lateralStiffness / dt;
                float longitudinal;
                if (braking)
                {
                    longitudinal = -vRoll * share / dt;                 // locked: stop it rolling
                    if (handbrake && w.steerFactor <= 0f)
                        lateral *= t.handbrakeGrip;                     // rear end slides
                }
                else if (throttleNow != 0f && Mathf.Abs(speed) > 0.5f && Mathf.Sign(throttleNow) != Mathf.Sign(speed))
                {
                    longitudinal = -Mathf.Sign(vRoll) * Mathf.Min(t.braking * share, Mathf.Abs(vRoll) * share / dt);
                }
                else if (throttleNow == 0f && Mathf.Abs(speed) < t.holdSpeed)
                {
                    longitudinal = -vRoll * share / dt;                 // hold still on slopes
                }
                else if (throttleNow == 0f)
                {
                    longitudinal = -vRoll * t.rollingResistance * share;   // coasting
                }
                else
                {
                    // Full push until near top speed, then it fades out.
                    float limit = throttleNow > 0f ? t.topSpeed : t.reverseSpeed;
                    float fraction = Mathf.Abs(speed) / limit;
                    float taper = Mathf.Clamp01(1f - fraction * fraction);
                    longitudinal = throttleNow * t.acceleration * share * taper * w.driveFactor;
                }

                // Friction circle: the tyre can't push harder than its load allows.
                var force = new Vector2(lateral, longitudinal);
                float maxForce = t.grip * load;
                if (force.magnitude > maxForce)
                    force *= maxForce / force.magnitude;

                // Assist: grip acts partway up towards the centre of mass, so corners roll the body less.
                Vector3 at = hit.point + up * (Vector3.Dot(com - hit.point, up) * t.gripForceHeight);
                body.AddForceAtPosition(side * force.x + rollOnGround * force.y, at);

                w.spin += w.spinSign * vRoll / w.spec.radius * Mathf.Rad2Deg * dt;
            }

            // Assists: level the body to the ground while wheels touch it, upright in the air.
            Vector3 angular = body.angularVelocity;
            Vector3 tumble = angular - up * Vector3.Dot(angular, up);
            if (WheelsOnGround > 0)
            {
                Vector3 ground = normalSum.normalized;
                body.AddTorque(Vector3.Cross(up, ground) * t.uprightStrength - tumble * t.uprightDamping, ForceMode.Acceleration);
                body.AddForce(-up * (t.downforce * speed * speed), ForceMode.Acceleration);
            }
            else
            {
                body.AddTorque(Vector3.Cross(up, Vector3.up) * t.airUpright, ForceMode.Acceleration);
            }
        }

        /// <summary>The nearest hit along the wheel's ray that isn't the vehicle itself.</summary>
        bool CastWheel(Vector3 origin, Vector3 direction, float length, out RaycastHit best)
        {
            best = default;
            int count = Physics.RaycastNonAlloc(origin, direction, Hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.attachedRigidbody == body || Hits[i].distance >= nearest)
                    continue;
                nearest = Hits[i].distance;
                best = Hits[i];
            }
            return nearest < float.MaxValue;
        }

        void LateUpdate()
        {
            views?.Tick();
            // Tyres follow the suspension, steer and spin.
            foreach (Wheel w in wheels)
            {
                if (w.tyre == null)
                    continue;
                Vector3 center = w.anchor + Vector3.up * (tuning.travel * (w.compression - tuning.rideHeight));
                w.tyre.SetPositionAndRotation(transform.TransformPoint(center),
                    transform.rotation * Quaternion.AngleAxis(w.steerAngle, Vector3.up) * w.rotation * Quaternion.AngleAxis(w.spin, Vector3.right));
            }
        }
    }
}
