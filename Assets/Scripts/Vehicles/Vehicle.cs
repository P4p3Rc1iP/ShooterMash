using System.Collections.Generic;
using ShooterMashup.Building;
using ShooterMashup.Player;
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
            public float driveFactor;     // how much of the engines' push it passes on (0 if it rolls sideways)
            public float steerFactor;     // 1 for the frontmost wheels, 0 for the rear half
            public Transform tyre;
            public float compression;     // 0 full droop .. 1 full bump
            public float compressionSpeed;  // how fast it compresses (m/s)
            public float steerAngle;
            public float spin;            // degrees, for the tyre
            public float spinSign;        // +1 when the axle points right of the rolling direction
            public bool grounded;
            public RaycastHit hit;        // this step's suspension ray
            public bool sliding;          // the tyre is sliding: it grips less, the faster it slides
            public float gripLeft = 1f;   // the fraction of its grip it has left (1 unless sliding)
            public bool spinning, locked, slick; // why it slides, and on what
            public float slip;            // how fast the tyre slides over the ground (m/s)
            public float treadSpeed;      // how fast the tread turns (m/s): the ground speed plus any wheelspin
            public float load, capacity, force; // this step (N)
            public float weightShare;     // the fraction of the vehicle's weight it carries standing still
            public float fifthWheelShare; // the fraction of a load on the fifth wheel it carries
            public Wheel partner;         // the wheel across the axle from it, for the anti-roll bar
            public float fromPivot, lateral; // ahead of the turning pivot, and right of the middle (m)
            public float barStiffness;    // roll stiffness added by anti-roll bars by its axle
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
        float wheelbase = 3f;             // front to rear wheels (m), for the steering assist
        float frontalArea = 4f;           // across times height (m²), for air drag
        float throttle, steerInput, steer;
        float steerScale = 1f;            // the understeer limiter's cut
        float lastSlipAngle, slipRate;    // for stability control: last step's slip angle, and how fast it changes (smoothed, degrees/s)
        bool handbrake;
        float power, pull;               // the engines' (W, N)
        bool hasKingpin, hasFifthWheel;
        Vector3 kingpinPoint, fifthWheelPoint; // local
        float kingpinShare;              // the fraction of a trailer's weight on its kingpin
        ConfigurableJoint joint;         // to the tractor, while coupled as a trailer
        readonly List<(Vector3 at, float stiffness)> bars = new List<(Vector3, float)>();   // anti-roll bars (local)
        readonly List<(Vector3 at, WingSpec spec)> wings = new List<(Vector3, WingSpec)>();  // wings (local, where they push)

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
        /// <summary>Driven by something other than a seated player (VehicleAutopilot), so it doesn't park itself.</summary>
        public bool Autopilot { get; set; }
        public int WheelCount => wheels.Count;
        public int WheelsOnGround { get; private set; }
        public float Speed => body.linearVelocity.magnitude;
        public float ForwardSpeed => Vector3.Dot(body.linearVelocity, transform.TransformDirection(forward));
        public float Mass => body.mass;
        /// <summary>How fast it can drive forwards (m/s): its fastest engine's top speed.</summary>
        public float TopSpeed { get; private set; } = 26f;
        /// <summary>The engines' power (W) and their pull at the wheels in low gears (N).</summary>
        public float Power => power;
        public float Pull => pull;
        /// <summary>Wheels whose tyres are sliding this step (for tuning and effects).</summary>
        public int WheelsSliding { get; private set; }
        /// <summary>Parked (holding still) or braking this step; a trailer follows its tractor's.</summary>
        public bool Parked { get; private set; }
        public bool Braking { get; private set; }
        /// <summary>The vehicle towing this one (it's a coupled trailer), or null.</summary>
        public Vehicle TowedBy { get; private set; }
        /// <summary>The trailer this one is towing, or null.</summary>
        public Vehicle Towing { get; private set; }
        /// <summary>The engine's revs and (for show) gear: for the rev counter and the sound.</summary>
        public EngineRevs Revs { get; } = new EngineRevs();
        /// <summary>Someone or something is driving it (the engine runs).</summary>
        public bool Controlled => Driver != null || Autopilot;
        /// <summary>The throttle as given: -1..1.</summary>
        public float Throttle => throttle;
        /// <summary>The driver's assists (set by VehicleDriver), or null: the standard ones.</summary>
        public DrivingAssists Assists { get; set; }
        static readonly DrivingAssists StandardAssists = DrivingAssists.For(DrivingAssists.Level.Standard);
        /// <summary>The assists in use now.</summary>
        public DrivingAssists ActiveAssists => Assists ?? StandardAssists;
        /// <summary>Stability control pushed the car back towards where it's going this step.</summary>
        public bool StabilityActive { get; private set; }
        /// <summary>Traction control held a driven wheel back this step.</summary>
        public bool TractionControlActive { get; private set; }
        /// <summary>The engine runs: someone sits in it, or the autopilot is actually driving it.</summary>
        public bool EngineRunning => power > 0f && (Driver != null || Autopilot && (throttle != 0f || Mathf.Abs(ForwardSpeed) > 1f));
        /// <summary>How far the body leans sideways (degrees, positive: to the right).</summary>
        public float RollAngle { get; private set; }
        /// <summary>The angle between where it points and where it goes (degrees, positive: sliding to the right).</summary>
        public float SlipAngle { get; private set; }
        /// <summary>The front wheels' steering angle (degrees, positive right) and the most they could turn now.</summary>
        public float SteerAngle { get; private set; }
        public float SteerLock { get; private set; }
        /// <summary>The name of its gearbox part, or null: the engine's own gears.</summary>
        public string GearboxName { get; private set; }
        /// <summary>The name of its most powerful engine piece (for its sound), or null.</summary>
        public string EngineName { get; private set; }
        /// <summary>Where the engines' exhausts come out, in the vehicle's space (for backfires).</summary>
        public IReadOnlyList<Vector3> Exhausts => exhausts;
        readonly List<Vector3> exhausts = new List<Vector3>();

        /// <summary>What a wheel is doing, for effects and the HUD.</summary>
        public struct WheelState
        {
            public bool grounded, driven, sliding;
            public bool spinning;      // sliding because it's driven harder than it grips
            public bool locked;        // braked or parked, skidding
            public bool slick;         // on ice
            public float slip;         // how fast the tyre slides over the ground (m/s)
            public Vector3 contact, normal;
            public Collider ground;
            public Vector3 localAnchor; // in the vehicle's space
            public float width;
            public float load, capacity, force; // N: on the spring, what the tyre could give, what it gives
            public float compression;  // 0 full droop .. 1 full bump (on the bump stop)
        }

        /// <summary>The wheels' states this physics step.</summary>
        public WheelState GetWheel(int i)
        {
            Wheel w = wheels[i];
            return new WheelState
            {
                grounded = w.grounded, driven = w.spec.driven, sliding = w.sliding, spinning = w.spinning,
                locked = w.locked, slick = w.slick, slip = w.slip, contact = w.hit.point, normal = w.hit.normal,
                ground = w.grounded ? w.hit.collider : null, localAnchor = w.anchor, width = w.spec.width,
                load = w.grounded ? w.load : 0f, capacity = w.grounded ? w.capacity : 0f, force = w.grounded ? w.force : 0f,
                compression = w.compression,
            };
        }

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
            body.linearDamping = 0f;   // air drag is worked out from the frontal area
            body.angularDamping = 0.05f;   // no more: it would damp spins

            world = new VehicleBuildWorld(RestsOnLift, b => b.Transformed(transform.position, transform.rotation), look.World.Actors);
            system = new BuildSystem(VehicleBuildWorld.Config(), world);
            views = new PieceViews(system, transform, look);
            views.Rebuilt += Rebuild;
            All.Add(this);
            gameObject.AddComponent<VehicleAudio>();     // before the effects: they listen for its backfires
            gameObject.AddComponent<VehicleEffects>();
        }

        void OnDestroy()
        {
            All.Remove(this);
            Uncouple();
            if (Towing != null)
                Towing.Uncouple();
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
                Vector3 droop = w.anchor - Vector3.up * (w.spec.travel * tuning.rideHeight);
                b.Encapsulate(droop - Vector3.up * w.spec.radius);
                b.Encapsulate(droop + Vector3.up * w.spec.radius);
            }
            return b;
        }

        // ------------------------------------------------------------------ lift

        internal void AttachTo(Lift lift)
        {
            Lift = lift;
            Uncouple();
            if (Towing != null)
                Towing.Uncouple();
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

        static PhysicsMaterial slipperyBody;

        /// <summary>After pieces changed: mass, centre of mass, inertia, wheels and seat.</summary>
        void Rebuild()
        {
            // Assist: the body has little friction, so scraping the ground or a rock skids instead of
            // braking hard (the tyres do the gripping) and bottoming out doesn't stop it dead. Not none:
            // a wreck on its roof should scrape to a stop, not slide like on ice.
            if (tuning.slipperyBody)
            {
                if (slipperyBody == null)
                    slipperyBody = new PhysicsMaterial("Vehicle body")
                    {
                        dynamicFriction = 0.3f,
                        staticFriction = 0.3f,
                        bounciness = 0f,
                        frictionCombine = PhysicsMaterialCombine.Minimum,
                        bounceCombine = PhysicsMaterialCombine.Minimum,
                    };
                foreach (Collider c in GetComponentsInChildren<Collider>())
                    c.sharedMaterial = slipperyBody;
            }

            wheels.Clear();
            SeatId = -1;
            forward = Vector3.forward;
            power = pull = 0f;
            float topSpeed = 0f;
            EngineSpec loudest = null;
            EngineName = null;
            exhausts.Clear();
            hasKingpin = hasFifthWheel = false;
            GearboxSpec gearbox = null;
            GearboxName = null;
            bars.Clear();
            wings.Clear();
            float mass = 0f;
            Vector3 moment = Vector3.zero;
            var ids = new List<int>(system.Pieces.Keys);
            ids.Sort();
            foreach (int id in ids)
            {
                Piece p = system.Get(id);
                foreach (Obb box in p.boxes)
                {
                    float m = PieceMass(p, box);
                    mass += m;
                    moment += box.center * m;
                }
                if (p.def.Engine != null)
                {
                    power += p.def.Engine.power;
                    pull += p.def.Engine.pull;
                    topSpeed = Mathf.Max(topSpeed, p.def.Engine.topSpeed);
                    if (loudest == null || p.def.Engine.power > loudest.power)
                        loudest = p.def.Engine;
                    if (loudest == p.def.Engine)
                        EngineName = p.def.name;
                    // The exhaust: the top of the engine block's back end.
                    Obb block = p.boxes[0];
                    Vector3 back = p.record.rotation * Vector3.back;
                    exhausts.Add(block.center + back * (block.Bounds.extents.z * 0.9f) + Vector3.up * block.Bounds.extents.y);
                }
                if (p.def.Gearbox != null && gearbox == null)
                {
                    gearbox = p.def.Gearbox;                       // the first one counts
                    GearboxName = p.def.name;
                }
                if (p.def.AntiRollBar != null)
                    bars.Add((p.record.position, p.def.AntiRollBar.stiffness));
                if (p.def.Wing != null)
                    wings.Add((p.record.position + p.record.rotation * Vector3.up * p.boxes[p.boxes.Length - 1].center.y, p.def.Wing));
                if (p.def.Coupling != null)
                {
                    Vector3 point = p.record.position + p.record.rotation * p.def.Coupling.point;
                    if (p.def.Coupling.kingpin)
                    {
                        hasKingpin = true;
                        kingpinPoint = point;
                    }
                    else
                    {
                        hasFifthWheel = true;
                        fifthWheelPoint = point;
                    }
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
            // Without an engine (but with driven wheels) it still drives, as vehicles did before
            // engines: at `acceleration` until 12 m/s, then on that power, up to 26 m/s.
            if (power <= 0f && wheels.Exists(w => w.spec.driven))
            {
                pull = tuning.acceleration * body.mass;
                power = pull * 12f;
                topSpeed = 26f;
            }
            TopSpeed = topSpeed > 0f ? topSpeed : 26f;
            // The gears: the engine's own, or a gearbox part's, which can make it faster or slower.
            int ownGears = loudest != null ? loudest.gears : 5;
            Revs.Configure(loudest?.idle ?? 850f, loudest?.redline ?? 6500f, loudest?.cylinders ?? 4, ownGears, TopSpeed,
                gearbox?.gears ?? ownGears, gearbox?.length ?? 1f, gearbox?.shiftTime ?? 0.3f);
            TopSpeed = Revs.TopSpeed;
            body.centerOfMass = centerOfMass;
            body.inertiaTensor = Inertia(ids, centerOfMass, body.mass);
            body.inertiaTensorRotation = Quaternion.identity;

            SetupWheels();
        }

        float Density(BuildMaterial m) => m switch
        {
            BuildMaterial.Stone => tuning.stoneDensity,
            BuildMaterial.Iron => tuning.ironDensity,
            BuildMaterial.Scrap => tuning.scrapDensity,
            _ => tuning.woodDensity,
        };

        /// <summary>A box of a piece: its volume times its material's density, or for a vehicle part (engine, ballast...) its own mass.</summary>
        float PieceMass(Piece p, Obb box)
        {
            if (p.def.OwnMass > 0f)
                return p.def.OwnMass / p.boxes.Length;
            return box.Volume * Density(p.record.material);
        }

        /// <summary>Inertia about the centre of mass, from the pieces' boxes (each as a solid box).</summary>
        Vector3 Inertia(List<int> ids, Vector3 com, float totalMass)
        {
            Vector3 inertia = Vector3.zero;
            foreach (int id in ids)
            {
                Piece p = system.Get(id);
                foreach (Obb box in p.boxes)
                {
                    float m = PieceMass(p, box);
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
            WeightShares();
            // Anti-roll bars: each wheel pairs with the one across the same axle.
            Vector3 across = Vector3.Cross(Vector3.up, forward);
            foreach (Wheel w in wheels)
            {
                w.partner = null;
                float best = 0.5f;
                foreach (Wheel o in wheels)
                {
                    if (o == w || Mathf.Sign(Vector3.Dot(o.anchor - centerOfMass, across)) == Mathf.Sign(Vector3.Dot(w.anchor - centerOfMass, across)))
                        continue;
                    float along = Mathf.Abs(Vector3.Dot(o.anchor - w.anchor, forward));
                    if (along < best)
                    {
                        best = along;
                        w.partner = o;
                    }
                }
            }
            // Anti-roll bars stiffen the axle they're built by (within 1.5 m along), so where they go
            // sets the balance: at the front the car pushes wide, at the back it turns in.
            foreach (Wheel w in wheels)
            {
                w.barStiffness = 0f;
                float along = Vector3.Dot(w.anchor, forward);
                foreach ((Vector3 at, float stiffness) in bars)
                    if (Mathf.Abs(Vector3.Dot(at, forward) - along) < 1.5f)
                        w.barStiffness += stiffness;
            }
            Bounds extent = LocalBounds();
            frontalArea = Mathf.Max(extent.size.x * extent.size.y, 0.5f);
            // The front half of the wheels steer, the frontmost the most: works whatever the layout.
            float mid = (minAlong + maxAlong) / 2f;
            foreach (Wheel w in wheels)
            {
                float along = Vector3.Dot(w.anchor - centerOfMass, forward);
                w.steerFactor = maxAlong - mid > 0.25f ? Mathf.Clamp01((along - mid) / (maxAlong - mid)) : 0f;
            }
            // Steering geometry: the vehicle turns about a point between its rear (non-steering)
            // wheels, and every wheel points along its own circle round the turn's centre: the
            // inner front wheel turns more than the outer (Ackermann), and on a long truck the
            // wheels behind the pivot steer a little the other way, so no axle is dragged sideways.
            float pivot = 0f;
            int rearWheels = 0;
            foreach (Wheel w in wheels)
                if (w.steerFactor <= 0f)
                {
                    pivot += Vector3.Dot(w.anchor - centerOfMass, forward);
                    rearWheels++;
                }
            pivot = rearWheels > 0 ? pivot / rearWheels : mid;
            foreach (Wheel w in wheels)
            {
                w.fromPivot = Vector3.Dot(w.anchor - centerOfMass, forward) - pivot;
                w.lateral = Vector3.Dot(w.anchor - centerOfMass, across);
            }
            wheelbase = wheels.Count > 1 ? Mathf.Max(maxAlong - pivot, 0.5f) : 3f;
        }

        /// <summary>
        /// Assist: how much of the weight each wheel carries standing still, so each spring can be
        /// sized for its own load and a vehicle loaded at one end still sits level (a truck with
        /// its load at the back would otherwise squash its rear springs onto the bump stops). A
        /// trailer's kingpin carries a share too (its tractor's springs take that), and a tractor
        /// works out how a load on its fifth wheel spreads over its wheels.
        /// </summary>
        void WeightShares()
        {
            float[] shares = Shares(centerOfMass, hasKingpin, kingpinPoint);
            for (int i = 0; i < wheels.Count; i++)
                wheels[i].weightShare = shares[i];
            kingpinShare = hasKingpin && shares.Length > wheels.Count ? shares[wheels.Count] : 0f;
            if (hasFifthWheel)
            {
                float[] fifth = Shares(fifthWheelPoint, false, Vector3.zero);
                for (int i = 0; i < wheels.Count; i++)
                    wheels[i].fifthWheelShare = fifth[i];
            }
        }

        /// <summary>
        /// How a load at `about` spreads over the wheels (and an extra support point): shares that
        /// vary linearly across and along the vehicle, sum to 1 and balance about `about`.
        /// </summary>
        float[] Shares(Vector3 about, bool withExtra, Vector3 extra)
        {
            int n = wheels.Count + (withExtra ? 1 : 0);
            var points = new Vector3[n];
            for (int i = 0; i < wheels.Count; i++)
                points[i] = wheels[i].anchor - about;
            if (withExtra)
                points[n - 1] = extra - about;
            var shares = new float[n];
            if (n == 0)
                return shares;
            float sx = 0f, sz = 0f, sxx = 0f, szz = 0f, sxz = 0f;
            foreach (Vector3 d in points)
            {
                sx += d.x; sz += d.z; sxx += d.x * d.x; szz += d.z * d.z; sxz += d.x * d.z;
            }
            // Shares (1 + a x + b z) / n with no moment about the point.
            float a = 0f, b = 0f;
            float det = sxx * szz - sxz * sxz;
            if (Mathf.Abs(det) > 1e-4f)
            {
                a = (-sx * szz + sz * sxz) / det;
                b = (-sz * sxx + sx * sxz) / det;
            }
            else if (szz > 1e-4f)
                b = -sz / szz;
            else if (sxx > 1e-4f)
                a = -sx / sxx;
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                shares[i] = Mathf.Max(0.1f, 1f + a * points[i].x + b * points[i].z);
                total += shares[i];
            }
            for (int i = 0; i < n; i++)
                shares[i] /= total;
            return shares;
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

        /// <summary>
        /// One physics step of suspension, tyres, engines, brakes and assists.
        ///
        /// Tyres: each wheel works out the force that would stop its contact patch sliding
        /// sideways (and the drive or brake force asked of it) and gets as much as its grip allows:
        /// grip times load. Once a tyre is asked for more it slides, and a sliding tyre grips less
        /// (slideGrip) until the slide dies down. That's what lets the back step out under power
        /// (more so with rear-wheel drive), a car spin, and a drift be held. The engines' force
        /// goes to the driven wheels only, so rear-, front- and all-wheel drive feel different.
        /// </summary>
        public void Simulate(float dt)
        {
            VehicleTuning t = tuning;
            DrivingAssists a = Assists ?? StandardAssists;
            body.AddForce(Vector3.down * t.extraGravity, ForceMode.Acceleration);
            // Air drag, by the vehicle's frontal area: what holds fast light cars back, hardly a heavy truck.
            Vector3 airVelocity = body.linearVelocity;
            body.AddForce(-airVelocity * (0.5f * 1.2f * t.dragCoefficient * frontalArea * airVelocity.magnitude));
            // Wings push down where they're built, with the square of the speed through the air
            // along the vehicle, and drag a little.
            if (wings.Count > 0)
            {
                float along = Vector3.Dot(airVelocity, transform.TransformDirection(forward));
                float squared = along * along / (30f * 30f);
                foreach ((Vector3 at, WingSpec spec) in wings)
                    body.AddForceAtPosition(-transform.up * (spec.downforce * squared) - airVelocity.normalized * (spec.drag * squared),
                        transform.TransformPoint(at));
            }
            UpdateCoupling();

            int n = wheels.Count;
            WheelsOnGround = 0;
            WheelsSliding = 0;
            TractionControlActive = false;
            if (n == 0)
                return;

            bool controlled = Driver != null || Autopilot;
            float throttleNow = controlled ? throttle : 0f;
            steer = Mathf.MoveTowards(steer, controlled ? steerInput : 0f, t.steerRate * dt);

            Vector3 up = transform.up;
            Vector3 fwdWorld = transform.TransformDirection(forward);
            float speed = Vector3.Dot(body.linearVelocity, fwdWorld);

            // Brakes. A trailer brakes and parks with whatever is towing it.
            if (TowedBy != null)
            {
                Parked = TowedBy.Parked;
                Braking = TowedBy.Braking;
            }
            else
            {
                Braking = throttleNow != 0f && Mathf.Abs(speed) > 0.5f && Mathf.Sign(throttleNow) != Mathf.Sign(speed);
                Parked = !controlled || (throttleNow == 0f && Mathf.Abs(speed) < t.holdSpeed);
            }

            float share = body.mass / n;   // per wheel, until each wheel's own is known below
            // A coupled trailer rests partly on the fifth wheel: the springs there carry that too.
            float towLoad = Towing != null ? Towing.body.mass * Towing.kingpinShare : 0f;
            float gravity = -Physics.gravity.y + t.extraGravity;
            // Assist: at speed, full lock asks for about what the tyres can give (steerGripMargin
            // times it), so steering stays near the limit instead of far past it. Sliding, the lock
            // grows by the slip angle, so you can always steer into a slide (countersteer assist).
            float cornering = t.grip * gravity * a.steerGripMargin;
            float gripLock = Mathf.Atan(wheelbase * cornering / Mathf.Max(speed * speed, 1f)) * Mathf.Rad2Deg;
            float maxSteer = Mathf.Min(t.maxSteer, gripLock);
            Vector3 flatVelocity = Vector3.ProjectOnPlane(body.linearVelocity, up);
            if (flatVelocity.sqrMagnitude > 4f)
            {
                float slipAngle = Vector3.Angle(Mathf.Sign(speed) * fwdWorld, flatVelocity);
                maxSteer = Mathf.Min(maxSteer + slipAngle, t.maxSteer);  // the grip limit, measured from where it's going
            }
            SteerLock = maxSteer;
            // Understeer limiter (assist): while the steered tyres slide sideways and the rear ones
            // grip (pushing wide, not a drift: there the lock is needed to countersteer), ease the
            // lock off until they grip again; let it back as they do.
            bool frontSliding = false, rearSliding = false;
            foreach (Wheel w in wheels)
            {
                if (w.steerFactor > 0f && w.sliding && !w.spinning && !w.locked) frontSliding = true;
                if (w.steerFactor <= 0f && w.sliding) rearSliding = true;
            }
            frontSliding &= !rearSliding;
            steerScale = a.understeerLimiter
                ? Mathf.MoveTowards(steerScale, frontSliding ? 0.5f : 1f, (frontSliding ? 1.5f : 0.8f) * dt)
                : 1f;
            maxSteer *= steerScale;
            SteerAngle = steer * maxSteer;
            Vector3 com = body.worldCenterOfMass;
            Vector3 normalSum = Vector3.zero;

            // Suspension rays first: the engines' force is shared by the driven wheels on the ground,
            // each by how hard it presses on it (the rear pulling away, the outside in a corner).
            int drivenOnGround = 0;
            float drivenLoad = 0f;
            foreach (Wheel w in wheels)
            {
                Vector3 anchor = transform.TransformPoint(w.anchor);
                float top = w.spec.travel * (1f - t.rideHeight);       // full bump above the anchor
                w.grounded = CastWheel(anchor + up * top, -up, w.spec.travel + w.spec.radius, out w.hit);
                if (w.grounded && w.spec.driven)
                {
                    drivenOnGround++;
                    drivenLoad += w.load;                // last step's: how hard each presses on the ground
                }
            }
            float drive = 0f;
            if (!Parked && !Braking && throttleNow != 0f && drivenOnGround > 0)
            {
                // Forwards through the gears (see EngineRevs); in reverse with a low gear up to reverseSpeed.
                float v = Mathf.Abs(speed);
                float force = throttleNow > 0f ? Revs.DriveForce(speed, power, pull)
                    : Mathf.Min(pull, power / Mathf.Max(v, 0.1f)) * Mathf.Clamp01((1f - v / t.reverseSpeed) / 0.08f);
                drive = throttleNow * force;                // shared below by the driven wheels on the ground
            }

            foreach (Wheel w in wheels)
            {
                float travel = w.spec.travel;
                float length = travel + w.spec.radius;
                // Each wheel along its own circle round the turn's centre (see SetupWheels).
                float curvature = Mathf.Tan(steer * maxSteer * Mathf.Deg2Rad) / wheelbase;
                w.steerAngle = Mathf.Clamp(Mathf.Atan2(w.fromPivot * curvature, 1f - w.lateral * curvature) * Mathf.Rad2Deg, -60f, 60f);
                if (!w.grounded)
                {
                    w.sliding = w.spinning = w.locked = false;
                    w.gripLeft = 1f;
                    w.slip = 0f;
                    w.compression = Mathf.MoveTowards(w.compression, 0f, dt * 4f);
                    w.compressionSpeed = 0f;
                    // In the air a driven wheel spins up on the throttle, and otherwise slowly runs down.
                    float free = w.spec.driven && controlled && throttleNow != 0f && !Parked ? Mathf.Sign(throttleNow) * TopSpeed
                        : Parked ? 0f : w.treadSpeed * 0.98f;
                    w.treadSpeed = Mathf.MoveTowards(w.treadSpeed, free, 25f * dt);
                    w.spin += w.spinSign * w.treadSpeed / w.spec.radius * Mathf.Rad2Deg * dt;
                    continue;
                }
                RaycastHit hit = w.hit;
                share = body.mass * w.weightShare + towLoad * w.fifthWheelShare;
                WheelsOnGround++;
                normalSum += hit.normal;

                // Spring and damper along the suspension, sized so the wheel carries its own share of
                // the weight at ride height.
                float k = share * gravity / (travel * t.rideHeight);
                float c = 2f * t.damping * Mathf.Sqrt(k * share);
                float raw = (length - hit.distance) / travel;
                float compression = Mathf.Clamp01(raw);
                float velocity = (compression - w.compression) * travel / dt;
                w.compression = compression;
                w.compressionSpeed = velocity;
                float load = Mathf.Max(0f, k * compression * travel + c * velocity);
                // Roll stiffness: the springs resist the difference in compression across an axle
                // rollStiffness (plus any anti-roll bars by this axle) times as much as they resist both
                // compressing. The dampers follow the roll springs, damping leaning rollDamping times as
                // much (softer roll springs with the full damping would ease back upright slowly
                // instead of springing back).
                // Progressive: the further it already leans (the bigger the difference across the
                // axle), the stiffer, so a soft vehicle leans readily at first but firms up well
                // before its outside springs reach their bump stops.
                float lean = w.partner != null ? compression - w.partner.compression : 0f;
                float roll = t.rollStiffness + w.barStiffness + t.rollProgression * lean * lean;
                if (w.partner != null)
                    load = Mathf.Max(0f, load + (roll - 1f) * k * travel * lean / 2f
                        + (Mathf.Sqrt(roll) * t.rollDamping - 1f) * c * (velocity - w.partner.compressionSpeed) / 2f);
                if (raw > 1f)
                    load += k * (raw - 1f) * travel * 4f;              // bump stop
                body.AddForceAtPosition(up * load, hit.point);

                // The tyre's rolling and sideways directions on the ground, and how it moves over it.
                Vector3 rollWorld = transform.TransformDirection(Quaternion.AngleAxis(w.steerAngle, Vector3.up) * w.roll);
                Vector3 rollOnGround = Vector3.ProjectOnPlane(rollWorld, hit.normal).normalized;
                Vector3 side = Vector3.Cross(hit.normal, rollOnGround);
                Vector3 pointVelocity = body.GetPointVelocity(hit.point);
                float vRoll = Vector3.Dot(pointVelocity, rollOnGround);
                float vSide = Vector3.Dot(pointVelocity, side);

                // What the tyre can give: grip times load (much less on ice), less again once it slides.
                float capacity = t.grip * w.spec.grip * load * (IsSlick(hit.collider) ? t.slickGrip : 1f);

                // What's asked of it: stop sliding sideways, and drive, brake or hold.
                float lateral = -vSide * share * t.lateralStiffness / dt;
                float longitudinal;
                bool rear = w.steerFactor <= 0f;
                if (Parked || (handbrake && controlled && rear))
                    longitudinal = -vRoll * share / dt;                         // locked (the handbrake locks the rear)
                else if (Braking)
                    longitudinal = -Mathf.Sign(vRoll) * Mathf.Min(t.braking * share, Mathf.Abs(vRoll) * share / dt,
                        a.abs ? capacity * 0.95f : float.MaxValue);                // ABS: never more than the tyre grips
                else
                {
                    // Assist (traction control): a driven wheel is never pushed more than a bit past its grip.
                    float push = w.spec.driven && drivenOnGround > 0
                        ? drive * w.driveFactor * (drivenLoad > 1f ? w.load / drivenLoad : 1f / drivenOnGround) : 0f;
                    float most = capacity * w.gripLeft * (1f + a.tractionSlack);
                    // Leave part of the grip the corner needs (all of it at most: in a slide some
                    // power still gets through, so a drift can be held on the throttle).
                    float corner = Mathf.Min(Mathf.Abs(lateral), most) * a.tractionCornering;
                    most = Mathf.Sqrt(Mathf.Max(0f, most * most - corner * corner));
                    if (a.tractionSlack >= 0f && Mathf.Abs(push) > most)
                    {
                        push = Mathf.Clamp(push, -most, most);
                        TractionControlActive = true;
                    }
                    // Rolling resistance (a fraction of the load), and engine braking off the throttle.
                    float rolling = Mathf.Min(t.rollingResistance * load, Mathf.Abs(vRoll) * share / dt);
                    longitudinal = push - Mathf.Sign(vRoll) * rolling
                        - (w.spec.driven && throttleNow == 0f ? vRoll * t.engineBraking * share : 0f);
                }

                var demand = new Vector2(lateral, longitudinal);
                float asked = demand.magnitude;
                bool slow = Mathf.Abs(vRoll) < t.slideMinSpeed && Mathf.Abs(vSide) < t.slideMinSpeed;
                // How fast the tread would slide over the ground: sideways, spinning (driven
                // harder than it grips) or skidding (locked).
                bool lockedNow = Parked || Braking && !a.abs || (handbrake && controlled && rear);
                float slipNow = Mathf.Max(Mathf.Abs(vSide), lockedNow ? Mathf.Abs(vRoll)
                    : Mathf.Max(0f, Mathf.Abs(longitudinal) / Mathf.Max(capacity, 1f) - 1f) * 10f);
                // At the limit it grips; once it slides it lets go, the faster it slides the more (down
                // to slideGrip), and it gets its grip back the same way as the slide dies down, so a
                // slide can be caught without the grip snapping back. Parking and crawling stay sure-footed.
                float letGo = slow ? 0f : Mathf.Clamp01((slipNow - t.slideStartSlip) / Mathf.Max(t.slideFullSlip - t.slideStartSlip, 0.01f));
                float available = capacity * Mathf.Lerp(1f, t.slideGrip, letGo);
                w.sliding = !slow && asked > available && slipNow > t.slideStartSlip;
                w.gripLeft = Mathf.Lerp(1f, t.slideGrip, letGo);
                Vector2 force = demand;
                bool locked = lockedNow;
                float spinUp = 0f;   // how much faster than the ground the tread turns (wheelspin)
                if (asked > available)
                {
                    // Too much: the tyre slides, and its force points along the slip. Sideways that's
                    // how fast it slides sideways; lengthways, a locked wheel skids at the ground
                    // speed, and a spinning one spins up more the harder it's driven. So a wheel
                    // spinning under power has little sideways grip left: the back steps out.
                    float slipAlong = locked ? Mathf.Abs(vRoll)
                        : Mathf.Abs(longitudinal) / Mathf.Max(available, 1f) * t.wheelspinSlip;
                    if (!locked && w.spec.driven && drive != 0f)
                        spinUp = slipAlong;
                    var slip = new Vector2(Mathf.Abs(vSide), slipAlong);
                    if (slip.sqrMagnitude < 1e-6f)
                        force = demand * (available / asked);
                    else
                    {
                        slip.Normalize();
                        force = new Vector2(Mathf.Sign(lateral) * slip.x, Mathf.Sign(longitudinal) * slip.y) * available;
                        // Never more than was asked for in either direction.
                        force.x = Mathf.Clamp(force.x, -Mathf.Abs(lateral), Mathf.Abs(lateral));
                        force.y = Mathf.Clamp(force.y, -Mathf.Abs(longitudinal), Mathf.Abs(longitudinal));
                    }
                }
                if (w.sliding)
                    WheelsSliding++;

                // Assist: grip acts partway up towards the centre of mass, so corners roll the body less.
                Vector3 at = hit.point + up * (Vector3.Dot(com - hit.point, up) * t.gripForceHeight);
                body.AddForceAtPosition(side * force.x + rollOnGround * force.y, at);
                w.load = load;
                w.capacity = capacity;
                w.force = force.magnitude;

                // What it's doing, for the effects, the HUD and the revs. The tread turns with the
                // ground, faster when it spins up under power, not at all when it's locked and skidding.
                w.slick = capacity > 0f && IsSlick(hit.collider);
                w.spinning = w.sliding && spinUp > 0f;
                w.locked = w.sliding && locked && Mathf.Abs(vRoll) > 0.5f;
                w.treadSpeed = w.locked ? 0f : vRoll + Mathf.Sign(drive) * spinUp;
                w.slip = w.sliding ? new Vector2(vSide, w.treadSpeed - vRoll).magnitude : 0f;
                w.spin += w.spinSign * w.treadSpeed / w.spec.radius * Mathf.Rad2Deg * dt;
            }

            // The revs follow the driven wheels' treads (wheelspin flares them), in the air they run free.
            float tread = 0f;
            int driven = 0;
            foreach (Wheel w in wheels)
                if (w.spec.driven)
                {
                    tread += w.treadSpeed;
                    driven++;
                }
            bool pushing = !Parked && !Braking && throttleNow != 0f;
            Revs.Update(dt, speed, driven > 0 ? tread / driven : speed, throttleNow, pushing, drivenOnGround == 0,
                EngineRunning && driven > 0);

            // Assists: level the body a little to the ground while wheels touch it (most of the roll
            // is left to the springs and anti-roll bars), catch it near the tipping point (rollover
            // assist), and turn it upright in the air.
            Vector3 angular = body.angularVelocity;
            Vector3 tumble = angular - up * Vector3.Dot(angular, up);
            RollAngle = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, fwdWorld), up, fwdWorld);
            SlipAngle = flatVelocity.sqrMagnitude > 4f ? Vector3.SignedAngle(Mathf.Sign(speed) * fwdWorld, flatVelocity, up) : 0f;
            // Stability control: once the car slides further than stabilityAngle from where it's going,
            // turn it back towards its path (real ones brake single wheels; this is the arcade version).
            // It also resists the slide growing (either way, but never a slide being caught), which
            // takes the swing out of a caught slide: otherwise the yaw that catching it builds up
            // carries the car into a slide the other way.
            StabilityActive = false;
            // Smoothed over about 0.15 s: over bumps the slip angle jitters from step to step, and
            // damping that would kick the car about.
            slipRate = Mathf.Lerp(slipRate, (SlipAngle - lastSlipAngle) / dt, Mathf.Clamp01(dt / 0.15f));
            lastSlipAngle = SlipAngle;
            // Both get stronger with speed: drifting round a bend at 50 km/h is fun, at 120 it's a crash.
            // Only with most wheels on the ground: in the air or over big bumps the slip angle swings
            // about without the tyres doing it, and turning the body then lands it rotating.
            if (WheelsOnGround * 4 >= n * 3 && Mathf.Abs(speed) > 5f)
            {
                float yaw = 0f;
                float bySpeed = Mathf.Clamp(Mathf.Abs(speed) / 20f, 0.5f, 1.5f);
                float allowed = a.stabilityAngle / bySpeed;
                if (a.stability > 0f && Mathf.Abs(SlipAngle) > allowed)
                    yaw += Mathf.Sign(SlipAngle) * (Mathf.Abs(SlipAngle) - allowed) * a.stability * bySpeed;
                if (a.stabilityDamping > 0f && Mathf.Abs(SlipAngle) > 2f)
                    yaw += Mathf.Clamp(slipRate, -30f, 30f) * Mathf.Deg2Rad * a.stabilityDamping * bySpeed;   // as fast as a slide being caught swings
                if (yaw != 0f)
                {
                    body.AddTorque(up * yaw, ForceMode.Acceleration);
                    StabilityActive = true;
                }
            }
            if (WheelsOnGround > 0)
            {
                Vector3 ground = normalSum.normalized;
                Vector3 level = Vector3.Cross(up, ground);
                float lean = Vector3.Angle(up, ground);
                float catchUp = lean > a.rolloverAssistAngle ? (lean - a.rolloverAssistAngle) * a.rolloverAssist : 0f;
                body.AddTorque(level * t.uprightStrength + level.normalized * catchUp - tumble * t.uprightDamping, ForceMode.Acceleration);
                body.AddForce(-up * Mathf.Min(t.downforce * speed * speed, t.maxDownforce), ForceMode.Acceleration);
            }
            else
            {
                body.AddTorque(Vector3.Cross(up, Vector3.up) * t.airUpright, ForceMode.Acceleration);
            }
        }

        // ------------------------------------------------------------------ trailers

        /// <summary>
        /// A trailer (a vehicle with a kingpin) couples to a fifth wheel that comes within reach,
        /// with a joint that lets it turn freely, pitch over crests and roll a little. It
        /// uncouples when either is put on a lift or goes away.
        /// </summary>
        void UpdateCoupling()
        {
            if (!hasKingpin)
                return;
            if (joint != null && (TowedBy == null || TowedBy.Lift != null || Lift != null))
                Uncouple();
            if (joint != null || Lift != null)
                return;
            Vector3 pin = transform.TransformPoint(kingpinPoint);
            foreach (Vehicle v in All)
            {
                if (v == this || !v.hasFifthWheel || v.Lift != null || v.Towing != null)
                    continue;
                if ((v.transform.TransformPoint(v.fifthWheelPoint) - pin).sqrMagnitude < tuning.couplingReach * tuning.couplingReach)
                {
                    Couple(v);
                    return;
                }
            }
        }

        /// <summary>
        /// Put this trailer on a tractor's fifth wheel, lined up behind it, and couple. Returns
        /// false if either is missing its half of the coupling.
        /// </summary>
        public bool CoupleTo(Vehicle tractor)
        {
            if (!hasKingpin || !tractor.hasFifthWheel || tractor == this)
                return false;
            Uncouple();
            Quaternion rotation = tractor.transform.rotation;
            Vector3 position = tractor.transform.TransformPoint(tractor.fifthWheelPoint) - rotation * kingpinPoint;
            SetPose(position, rotation);
            body.linearVelocity = tractor.body.linearVelocity;
            body.angularVelocity = Vector3.zero;
            Couple(tractor);
            return true;
        }

        void Couple(Vehicle tractor)
        {
            joint = gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = tractor.body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = kingpinPoint;
            joint.connectedAnchor = tractor.fifthWheelPoint;
            joint.axis = Vector3.right;            // angular x: pitch
            joint.secondaryAxis = Vector3.up;      // angular y: turning; angular z: roll
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -25f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 25f };
            joint.angularZLimit = new SoftJointLimit { limit = 10f };
            joint.enableCollision = false;
            body.solverIterations = tractor.body.solverIterations = 16;
            TowedBy = tractor;
            tractor.Towing = this;
        }

        void Uncouple()
        {
            if (joint != null)
                Destroy(joint);
            joint = null;
            if (TowedBy != null)
                TowedBy.Towing = null;
            TowedBy = null;
        }

        static bool IsSlick(Collider c) =>
            c.TryGetComponent(out PmSurface surface) && (surface.flags & SurfaceFlags.Slick) != 0;

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
                Vector3 center = w.anchor + Vector3.up * (w.spec.travel * (w.compression - tuning.rideHeight));
                w.tyre.SetPositionAndRotation(transform.TransformPoint(center),
                    transform.rotation * Quaternion.AngleAxis(w.steerAngle, Vector3.up) * w.rotation * Quaternion.AngleAxis(w.spin, Vector3.right));
            }
        }
    }
}
