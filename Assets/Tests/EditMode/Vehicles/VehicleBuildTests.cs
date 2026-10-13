using System;
using System.Linq;
using NUnit.Framework;
using ShooterMashup.Building;
using UnityEngine;

namespace ShooterMashup.Vehicles.Tests
{
    /// <summary>
    /// The vehicle's build rules, without GameObjects: pieces just have to be connected (to the
    /// anchor), the first one must rest on the lift (here: the plane y = 0), and vehicle parts
    /// only go on vehicles.
    /// </summary>
    public class VehicleBuildTests
    {
        BuildSystem sys;
        VehicleBuildWorld world;

        static int Type(string name)
        {
            int i = Array.FindIndex(PieceCatalog.All, d => d.name == name);
            Assert.GreaterOrEqual(i, 0, name);
            return i;
        }

        [SetUp]
        public void SetUp()
        {
            // The "lift": anything with its bottom at y = 0 rests on it.
            world = new VehicleBuildWorld((boxes, tol) => boxes.Any(b => Mathf.Abs(b.Bounds.min.y) <= tol), b => b, null);
            sys = new BuildSystem(VehicleBuildWorld.Config(), world);
        }

        BuildResult Place(string name, Vector3 position, float yaw = 0f)
        {
            BuildResult r = sys.Execute(BuildCmd.Place(0, Type(name), BuildMaterial.Wood, position, Quaternion.Euler(0, yaw, 0), 100));
            // What Vehicle does after its first piece: that piece becomes the anchor.
            if (r.Ok && world.Anchor == null)
                world.Anchor = sys.Get(r.added[0]).boxes;
            return r;
        }

        [Test]
        public void VehicleParts_AreRefusedInTheWorld()
        {
            var worldSys = new BuildSystem(new BuildConfig(), new VehicleBuildWorld((b, t) => true, b => b, null));
            foreach (string name in new[] { "Wheel", "Driver seat" })
            {
                Assert.IsTrue(PieceCatalog.All[Type(name)].VehicleOnly, name);
                Assert.AreEqual(BuildFailure.VehicleOnly,
                    worldSys.Execute(BuildCmd.Place(0, Type(name), BuildMaterial.Wood, Vector3.zero, Quaternion.identity, 0)).failure, name);
            }
            Assert.IsTrue(Place("Beam 3 m", Vector3.zero, 90).Ok);
            Assert.IsTrue(Place("Wheel", new Vector3(0, PieceCatalog.BeamSize / 2f, 1.5f)).Ok, "on a vehicle it's fine");
        }

        [Test]
        public void Pieces_JustNeedToBeConnected()
        {
            // A 9 m wooden beam sideways would collapse in the world; on a vehicle support never runs out.
            Assert.IsTrue(Place("Beam 3 m", Vector3.zero).Ok);
            for (int k = 1; k <= 5; k++)
                Assert.IsTrue(Place("Beam 3 m", new Vector3(3f * k, 0f, 0f)).Ok, $"beam {k}");
            // Not touching anything: refused.
            Assert.AreEqual(BuildFailure.Unsupported, Place("Beam 1 m", new Vector3(0, 2f, 5f)).failure);
        }

        [Test]
        public void RemovingAConnection_DropsWhatHungOnIt()
        {
            Place("Beam 3 m", Vector3.zero);                                 // anchor
            int middle = Place("Beam 3 m", new Vector3(3f, 0, 0)).added[0];
            int end = Place("Beam 3 m", new Vector3(6f, 0, 0)).added[0];
            BuildResult r = sys.Execute(BuildCmd.Remove(0, middle, 101));
            Assert.IsTrue(r.Ok);
            CollectionAssert.Contains(r.collapsed, end);
        }

        [Test]
        public void DemoTruck_Builds()
        {
            foreach (VehicleShowcase.Placement p in VehicleShowcase.TruckPlan())
            {
                BuildResult r = sys.Execute(BuildCmd.Place(VehicleShowcase.Owner, Type(p.name), p.material, p.position, Quaternion.Euler(0, p.yaw, 0), 100));
                Assert.IsTrue(r.Ok, $"{p.name} at {p.position}: {r.failure}");
                if (world.Anchor == null)
                    world.Anchor = sys.Get(r.added[0]).boxes;
            }
            Assert.AreEqual(4, sys.Pieces.Values.Count(p => p.def.Wheel != null));
        }

        [Test]
        public void DemoBuggy_Builds()
        {
            foreach (VehicleShowcase.Placement p in VehicleShowcase.BuggyPlan())
            {
                BuildResult r = sys.Execute(BuildCmd.Place(VehicleShowcase.Owner, Type(p.name), p.material, p.position, Quaternion.Euler(0, p.yaw, 0), 100));
                Assert.IsTrue(r.Ok, $"{p.name} at {p.position}: {r.failure}");
                if (world.Anchor == null)
                    world.Anchor = sys.Get(r.added[0]).boxes;
            }
            Assert.AreEqual(4, sys.Pieces.Values.Count(p => p.def.Wheel != null));
            Assert.AreEqual(1, sys.Pieces.Values.Count(p => p.def.Seat != null));
        }
    }
}
