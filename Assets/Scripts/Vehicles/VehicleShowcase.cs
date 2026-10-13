using System;
using System.Collections.Generic;
using ShooterMashup.Building;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// A demo buggy, built the way a player would: on a lift, from ordinary pieces, then the lift
    /// is taken away and it drops onto its wheels. Failed placements are logged.
    /// </summary>
    public class VehicleShowcase : MonoBehaviour
    {
        /// <summary>Owner id for demo pieces, so they never mix with a player's undo history.</summary>
        public const int Owner = -3;

        [Tooltip("Where the buggy is built (the lift's base), facing +z rotated by yaw.")]
        public Vector3 buggyPosition = new Vector3(64f, 0f, 14f);
        public float buggyYaw;

        public struct Placement
        {
            public string name;
            public BuildMaterial material;
            public Vector3 position;   // on the lift's top, which becomes the buggy's origin
            public float yaw;
        }

        void Start()
        {
            var structures = FindAnyObjectByType<StructureManager>();
            if (structures != null)
                Build(structures, buggyPosition, buggyYaw, BuggyPlan());
        }

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
            Debug.Log($"Vehicle showcase: buggy of {plan.Count - failed} pieces" + (failed > 0 ? $", {failed} failed" : ""));
            return v;
        }

        /// <summary>
        /// A small buggy: two 3 m rails a metre apart with a floor between them, a seat, a roll bar,
        /// a front bumper, and a wheel at each end of each rail (3 m wheelbase, 2.1 m track).
        /// </summary>
        public static List<Placement> BuggyPlan()
        {
            const BuildMaterial wood = BuildMaterial.Wood;
            float beamMid = PieceCatalog.BeamSize / 2f;   // a beam's centre line, above its bottom
            var plan = new List<Placement>();
            void Place(string name, Vector3 position, float yaw = 0f) =>
                plan.Add(new Placement { name = name, material = wood, position = position, yaw = yaw });

            Place("Beam 3 m", new Vector3(-0.5f, 0, 0), 90);               // left rail, along z
            Place("Floor 1x1", Vector3.zero);                              // between the rails
            Place("Beam 3 m", new Vector3(0.5f, 0, 0), 90);                // right rail
            Place("Driver seat", new Vector3(0, 0.2f, -0.05f));            // on the floor
            Place("Beam 1 m", new Vector3(0, 0, 1.5f));                    // front bumper, across the rail ends
            foreach (float x in new[] { -0.5f, 0.5f })                     // roll bar: posts on the rails...
                Place("Pillar 1 m", new Vector3(x, 2 * beamMid, -0.9f));
            Place("Beam 1 m", new Vector3(0, 2 * beamMid + 1f - PieceCatalog.BeamSize, -0.9f)); // ...and a bar across
            foreach (float z in new[] { -1.5f, 1.5f })                     // wheels at the rails' ends
            {
                Place("Wheel", new Vector3(0.5f, beamMid, z));             // right side, axle along +x
                Place("Wheel", new Vector3(-0.5f, beamMid, z), 180);       // left side
            }
            return plan;
        }
    }
}
