using System;
using System.Collections.Generic;
using ShooterMashup.Building;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// Demo vehicles, built the way a player would: on a lift, from ordinary pieces (mostly scrap,
    /// the light sheet-metal material) plus vehicle parts, then the lift is taken away and they
    /// drop onto their wheels. Failed placements are logged.
    ///
    /// The designs, all 3 m wide (the build grid's scale):
    /// - Buggy: a light frame with a V6 behind the seat, rear-wheel drive. Fast, light, loose.
    /// - Saloon: a closed car on small road wheels, V6 at the front, front-wheel drive.
    /// - Van: a tall box on a V8, rear-wheel drive: heavy, stable unless provoked.
    /// - Rally truck: a flatbed on big wheels with two V8s and all-wheel drive.
    /// - Heavy truck: 8x8 on huge wheels with a diesel, carrying 15 t of iron beams.
    /// - Semi: a 6x4 tractor with a diesel, towing a trailer loaded with stone.
    /// - Truck and stunt truck: flatbeds for riding on (the stunt one has no cab).
    /// </summary>
    public class VehicleShowcase : MonoBehaviour
    {
        /// <summary>Owner id for demo pieces, so they never mix with a player's undo history.</summary>
        public const int Owner = -3;

        [Tooltip("Where the vehicle is built (the lift's base), facing +z rotated by yaw.")]
        public Vector3 buggyPosition = new Vector3(64f, 0f, 14f);
        public float buggyYaw;

        public enum Design { Buggy, Truck, RallyTruck, StuntTruck, Saloon, Van, HeavyTruck, Semi }
        [Tooltip("Which vehicle to build.")]
        public Design design = Design.Buggy;
        [Tooltip("Drive it in circles while nobody sits in it (to practise riding on it).")]
        public bool autopilot;
        [Tooltip("With the autopilot: a closed loop of world points to lap (empty: drive in a circle).")]
        public Vector3[] autopilotPath = new Vector3[0];
        [Tooltip("With the autopilot: top speed it aims for (m/s).")]
        public float autopilotMaxSpeed = 22f;
        [Tooltip("With the autopilot: seconds it waits at the start of its path each lap (0: doesn't stop).")]
        public float autopilotStopSeconds = 6f;
        [Tooltip("With the autopilot: only drive while a player is standing on it.")]
        public bool autopilotWaitForRider;
        [Tooltip("Instead of the autopilot: a stunt (crash, flip, loop), driven at this speed (m/s) once someone stands on it. 0: off.")]
        public float stuntSpeed;
        [Tooltip("With a stunt: world points to follow (empty: straight ahead).")]
        public Vector3[] stuntPath = new Vector3[0];
        [Tooltip("With a stunt: the run ends after this many metres (0: when it crashes or gets stuck).")]
        public float stuntRunLength;
        [Tooltip("With a stunt: keep the self-levelling assists (off lets it roll over).")]
        public bool stuntUprightAssists = true;
        [Tooltip("With a stunt: spring sag at rest as a fraction of travel (0: normal). Lower is stiffer.")]
        public float stuntRideHeight;

        public struct Placement
        {
            public string name;
            public BuildMaterial material;
            public Vector3 position;   // on the lift's top, which becomes the vehicle's origin
            public float yaw;
        }

        /// <summary>The semi's trailer is built this far behind the tractor (in its space) and then coupled.</summary>
        static readonly Vector3 TrailerBuildOffset = new Vector3(30f, 0f, 0f);

        void Start()
        {
            var structures = FindAnyObjectByType<StructureManager>();
            if (structures == null)
                return;
            Vehicle v = Build(structures, buggyPosition, buggyYaw, PlanFor(design));
            if (v == null)
                return;
            v.name = $"{design}";
            if (design == Design.Semi)
            {
                // The trailer is built beside the tractor (lifts can't overlap), then coupled.
                Vector3 at = buggyPosition + Quaternion.Euler(0f, buggyYaw, 0f) * TrailerBuildOffset;
                Vehicle trailer = Build(structures, at, buggyYaw, TrailerPlan());
                if (trailer != null)
                {
                    trailer.name = "Semi trailer";
                    trailer.CoupleTo(v);
                }
            }
            if (stuntSpeed > 0f)
            {
                var stunt = v.gameObject.AddComponent<StuntDriver>();
                stunt.speed = stuntSpeed;
                stunt.path = stuntPath;
                stunt.runLength = stuntRunLength;
                stunt.uprightAssists = stuntUprightAssists;
                stunt.rideHeight = stuntRideHeight;
            }
            else if (autopilot)
            {
                var pilot = v.gameObject.AddComponent<VehicleAutopilot>();
                pilot.path = autopilotPath;
                pilot.maxSpeed = autopilotMaxSpeed;
                pilot.stopSeconds = autopilotStopSeconds;
                pilot.waitForRider = autopilotWaitForRider;
            }
        }

        public static List<Placement> PlanFor(Design design) => design switch
        {
            Design.Truck => TruckPlan(),
            Design.RallyTruck => RallyTruckPlan(),
            Design.StuntTruck => StuntTruckPlan(),
            Design.Saloon => SaloonPlan(),
            Design.Van => VanPlan(),
            Design.HeavyTruck => HeavyTruckPlan(),
            Design.Semi => TractorPlan(),
            _ => BuggyPlan(),
        };

        /// <summary>Builds a vehicle on a temporary lift and drops it. Returns the vehicle (or null).</summary>
        public static Vehicle Build(StructureManager structures, Vector3 at, float yaw, List<Placement> plan)
        {
            Lift lift = Lift.Create(structures, "Demo lift");
            lift.Place(at, yaw);
            int failed = 0;
            foreach (Placement p in plan)
            {
                int type = Array.FindIndex(PieceCatalog.All, d => d.name == p.name);
                BuildResult r = type < 0
                    ? BuildResult.Fail(BuildFailure.UnknownPiece)
                    : lift.Execute(BuildCmd.Place(Owner, type, p.material, p.position, Quaternion.Euler(0, p.yaw, 0), Time.timeAsDouble));
                if (!r.Ok)
                {
                    failed++;
                    Debug.LogWarning($"Vehicle showcase: {p.material} {p.name} at {p.position} (yaw {p.yaw}) failed: {r.failure}");
                }
            }
            Vehicle v = lift.Vehicle;
            lift.Remove();
            Debug.Log($"Vehicle showcase: vehicle of {plan.Count - failed} pieces" + (failed > 0 ? $", {failed} failed" : "") +
                      (v != null ? $", {v.Mass:0} kg, {v.Power / 1000f:0} kW" : ""));
            return v;
        }

        // ------------------------------------------------------------------ plans

        /// <summary>Collects placements, in scrap unless said otherwise.</summary>
        sealed class Planner
        {
            public readonly List<Placement> list = new List<Placement>();
            public const float B = PieceCatalog.BeamSize;   // an axle beam's height
            public const float Deck = B + 0.2f;              // the top of floors laid on axle beams

            public void Place(string name, Vector3 position, float yaw = 0f, BuildMaterial material = BuildMaterial.Scrap) =>
                list.Add(new Placement { name = name, material = material, position = position, yaw = yaw });

            /// <summary>A 3 m axle beam across the vehicle at `z`, with a wheel flush on each end.</summary>
            public void Axle(float z, string wheel)
            {
                Place("Beam 3 m", new Vector3(0, 0, z));
                Place(wheel, new Vector3(1.5f - B / 2f, B / 2f, z));
                Place(wheel, new Vector3(-1.5f + B / 2f, B / 2f, z), 180);
            }

            /// <summary>3x3 floors on the axle beams, centred at these z.</summary>
            public void Deck3(params float[] zs)
            {
                foreach (float z in zs)
                    Place("Floor 3x3", new Vector3(0, B, z));
            }

            /// <summary>A row of three 1x1 floors across the vehicle at `z`.</summary>
            public void Deck1(float z)
            {
                foreach (float x in new[] { -1f, 0f, 1f })
                    Place("Floor 1x1", new Vector3(x, B, z));
            }

            /// <summary>1x1 walls along both sides (x = ±1.4) at these z, from height `y`.</summary>
            public void Sides(float y, params float[] zs)
            {
                foreach (float z in zs)
                foreach (float x in new[] { -1.4f, 1.4f })
                    Place("Wall 1x1", new Vector3(x, y, z), 90);
            }

            /// <summary>A row of three 1x1 walls across the vehicle at `z`, from height `y`.</summary>
            public void Across(float y, float z)
            {
                foreach (float x in new[] { -1f, 0f, 1f })
                    Place("Wall 1x1", new Vector3(x, y, z));
            }
        }

        /// <summary>
        /// A light buggy: two 3 m rails a metre apart with floors between them, a seat, a roll bar,
        /// a V6 behind it, a front bumper, and a wheel at each end of each rail (3 m wheelbase,
        /// 2.1 m track). Rear-wheel drive: about 580 kg and 200 kW.
        /// </summary>
        public static List<Placement> BuggyPlan()
        {
            var p = new Planner();
            float beamMid = Planner.B / 2f;   // a beam's centre line, above its bottom
            p.Place("Beam 3 m", new Vector3(-0.5f, 0, 0), 90);                 // left rail, along z
            p.Place("Floor 1x1", Vector3.zero);                                // between the rails
            p.Place("Beam 3 m", new Vector3(0.5f, 0, 0), 90);                  // right rail
            p.Place("Floor 1x1", new Vector3(0, 0, -1f));                      // the engine bay
            p.Place("Driver seat", new Vector3(0, 0.2f, -0.05f));              // on the floor
            p.Place("Engine V6", new Vector3(0, 0.2f, -1.05f));                 // behind the seat
            p.Place("Beam 1 m", new Vector3(0, 0, 1.5f));                      // front bumper, across the rail ends
            foreach (float x in new[] { -0.5f, 0.5f })                         // roll bar: posts on the rails...
                p.Place("Pillar 1 m", new Vector3(x, 2 * beamMid, -0.4f));
            p.Place("Beam 1 m", new Vector3(0, 2 * beamMid + 1f - Planner.B, -0.4f)); // ...and a bar across
            foreach ((float z, string wheel) in new[] { (1.5f, "Wheel (free)"), (-1.5f, "Wheel") })
            {
                p.Place(wheel, new Vector3(0.5f, beamMid, z));                 // right side, axle along +x
                p.Place(wheel, new Vector3(-0.5f, beamMid, z), 180);           // left side
            }
            return p.list;
        }

        /// <summary>
        /// A saloon, 5 m long on small road wheels (4 m wheelbase): a floor, a V6 in the nose,
        /// low sides, a cabin of posts with a roof. Front-wheel drive: about 1.6 t and 200 kW.
        /// </summary>
        public static List<Placement> SaloonPlan()
        {
            var p = new Planner();
            p.Axle(2f, "Small wheel");
            p.Deck1(2f);
            p.Deck3(0f);
            p.Deck1(-2f);
            p.Axle(-2f, "Small wheel (free)");
            p.Place("Engine V6", new Vector3(0, Planner.Deck, 2f));
            p.Place("Driver seat", new Vector3(-0.6f, Planner.Deck, 0.4f));
            p.Sides(Planner.Deck, -1f, 0f, 1f);
            foreach (float x in new[] { -1.385f, 1.385f })
            foreach (float z in new[] { -1f, 1f })
                p.Place("Pillar 1 m", new Vector3(x, Planner.Deck + 1f, z));
            p.Place("Floor 3x3", new Vector3(0, Planner.Deck + 2f, 0));        // the roof
            return p.list;
        }

        /// <summary>
        /// A van, 6 m long on 1 m wheels: a cab with the seat beside a V8, and a 3 m tall cargo box
        /// behind. Rear-wheel drive: about 3.7 t and 320 kW.
        /// </summary>
        public static List<Placement> VanPlan()
        {
            var p = new Planner();
            float d = Planner.Deck;
            p.Axle(2.5f, "Wheel (free)");
            p.Deck3(1.5f, -1.5f);
            p.Axle(-2.5f, "Wheel");
            p.Place("Driver seat", new Vector3(-0.6f, d, 1.6f));
            p.Place("Engine V8", new Vector3(0.6f, d, 2.4f));
            // The cargo box: sides, back, a bulkhead behind the cab, and a roof.
            p.Place("Wall 3x3", new Vector3(-1.4f, d, -1.5f), 90);
            p.Place("Wall 3x3", new Vector3(1.4f, d, -1.5f), 90);
            p.Place("Wall 3x3", new Vector3(0, d, -2.9f));
            p.Place("Wall 3x3", new Vector3(0, d, -0.1f));
            p.Place("Floor 3x3", new Vector3(0, d + 3f, -1.5f));
            // The cab: low sides and a roof against the bulkhead.
            p.Sides(d, 0.5f, 1.5f, 2.5f);
            p.Place("Floor 3x3", new Vector3(0, d + 2f, 1.5f));
            foreach (float x in new[] { -1.385f, 1.385f })
                p.Place("Pillar 1 m", new Vector3(x, d + 1f, 2.885f));
            return p.list;
        }

        /// <summary>
        /// A flatbed truck: two 3x3 floors on axle beams (6 m long, 3 m wide), a seat at the front
        /// behind a cab wall of 1x1 walls, low rails and a tailgate round the bed, and a wheel on
        /// each end of each axle (flush on the beam ends). Without the cab (no seat either: it
        /// drives towards +z), the bed is open at the front, for the stunt tests.
        /// </summary>
        public static List<Placement> TruckPlan(string wheel = "Wheel", bool cab = true, string rearWheel = null)
        {
            var p = new Planner();
            float d = Planner.Deck;
            p.Axle(-2.5f, rearWheel ?? wheel);
            p.Deck3(-1.5f, 1.5f);
            p.Axle(2.5f, wheel);
            if (cab)
            {
                p.Place("Driver seat", new Vector3(0, d, 2f));
                p.Across(d, 1.3f);                                             // cab wall behind the seat
            }
            foreach (float x in new[] { -1.375f, 1.375f })                     // low rails along the bed
                p.Place("Beam 3 m", new Vector3(x, d, -1.5f), 90);
            p.Place("Beam 3 m", new Vector3(0, d, -2.875f));                   // tailgate
            return p.list;
        }

        /// <summary>
        /// The rally truck: the flatbed on big wheels, all driven, with a V8 either side of the
        /// seat. About 1.9 t and 640 kW: a lot.
        /// </summary>
        public static List<Placement> RallyTruckPlan()
        {
            List<Placement> list = TruckPlan("Big wheel");
            var p = new Planner();
            p.Place("Engine V8", new Vector3(-0.85f, Planner.Deck, 2f));
            p.Place("Engine V8", new Vector3(0.85f, Planner.Deck, 2f));
            list.AddRange(p.list);
            return list;
        }

        /// <summary>The stunt truck: the flatbed on big wheels without a cab, a V8 at the back of the bed.</summary>
        public static List<Placement> StuntTruckPlan()
        {
            List<Placement> list = TruckPlan("Big wheel", cab: false);
            var p = new Planner();
            p.Place("Engine V8", new Vector3(0, Planner.Deck, -2.2f));
            list.AddRange(p.list);
            return list;
        }

        /// <summary>
        /// A heavy 8x8 truck, 12 m long on huge (2 m) wheels, all driven: a cab with a diesel behind
        /// it and a bed loaded with ten iron beams (about 15 t), held by scrap stakes. About 20 t
        /// and 420 kW: slow, sure-footed, unstoppable.
        /// </summary>
        public static List<Placement> HeavyTruckPlan()
        {
            var p = new Planner();
            float d = Planner.Deck;
            p.Axle(2f, "Huge wheel");               // first: it rests on the lift
            p.Deck3(1.5f, 4.5f, -1.5f, -4.5f);
            p.Axle(4.5f, "Huge wheel");
            p.Axle(-2f, "Huge wheel");
            p.Axle(-4.5f, "Huge wheel");
            // The cab.
            p.Place("Driver seat", new Vector3(0, d, 4.6f));
            p.Across(d, 5.9f);                                                 // dashboard
            p.Place("Wall 3x3", new Vector3(0, d, 3.1f));                      // back of the cab
            p.Sides(d, 4f, 5f);
            p.Place("Floor 3x3", new Vector3(0, d + 3f, 4.5f));                // cab roof
            p.Place("Engine diesel", new Vector3(0, d, 2.2f));
            // The load: iron beams across the bed, two layers, between scrap stakes.
            for (int k = 0; k < 6; k++)
                p.Place("Beam 3 m", new Vector3(0, d, -5.4f + 0.9f * k), 0, BuildMaterial.Iron);
            for (int k = 0; k < 4; k++)
                p.Place("Beam 3 m", new Vector3(0, d + Planner.B, -5.4f + 0.9f * k), 0, BuildMaterial.Iron);
            foreach (float x in new[] { -1.385f, 1.385f })
            foreach (float z in new[] { -5.5f, -3f, -0.5f })
                p.Place("Pillar 1 m", new Vector3(x, d, z));
            return p.list;
        }

        /// <summary>
        /// The semi's tractor, 9 m long on big wheels: a steering axle under the cab (free wheels)
        /// and two driven axles under a fifth wheel. A diesel beside the seat. About 3.5 t and
        /// 420 kW; 19 t with its trailer.
        /// </summary>
        public static List<Placement> TractorPlan()
        {
            var p = new Planner();
            float d = Planner.Deck;
            p.Axle(-1.75f, "Big wheel");            // first: it rests on the lift
            p.Deck3(-3f, 0f, 3f);
            p.Axle(3.5f, "Big wheel (free)");
            p.Axle(-3.75f, "Big wheel");
            p.Place("Driver seat", new Vector3(-0.6f, d, 3.2f));
            p.Place("Engine diesel", new Vector3(0.7f, d, 3.6f));
            p.Place("Wall 3x3", new Vector3(0, d, 1.9f));                      // back of the cab
            foreach (float x in new[] { -1f, 0f })                              // dashboard, beside the engine
                p.Place("Wall 1x1", new Vector3(x, d, 4.4f));
            p.Place("Floor 3x3", new Vector3(0, d + 3f, 3f));                  // cab roof
            p.Place("Fifth wheel", new Vector3(0, d, -2.75f));
            return p.list;
        }

        /// <summary>
        /// The semi's trailer, 12 m long: two axles of free big wheels at the back, a kingpin under
        /// the front, low sides, and a 3x3 stone slab and two 1x1s for a load (about 13 t). Its
        /// origin is over the axles (the lift is smaller than the trailer), 4 m behind its middle.
        /// </summary>
        public static List<Placement> TrailerPlan()
        {
            var p = new Planner();
            float d = Planner.Deck;
            p.Axle(1f, "Big wheel (free)");
            p.Deck3(2.5f, -0.5f, 5.5f, 8.5f);
            p.Axle(-1f, "Big wheel (free)");
            p.Place("Kingpin", new Vector3(0, Planner.B, 8.5f));
            p.Place("Slab 3x3", new Vector3(0, d, 2.5f), 0, BuildMaterial.Stone);
            p.Place("Slab 1x1", new Vector3(-0.6f, d, 5f), 0, BuildMaterial.Stone);
            p.Place("Slab 1x1", new Vector3(0.6f, d, 5f), 0, BuildMaterial.Stone);
            foreach (float x in new[] { -1.375f, 1.375f })                     // low rails along the sides
            foreach (float z in new[] { -0.5f, 2.5f, 5.5f, 8.5f })
                p.Place("Beam 3 m", new Vector3(x, d, z), 90);
            return p.list;
        }
    }
}
