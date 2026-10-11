using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ShooterMashup.Building.Tests
{
    /// <summary>The PhysX-backed world: what counts as ground. Built far from the origin and removed afterwards.</summary>
    public class UnityBuildWorldTests
    {
        static readonly Vector3 Base = new Vector3(-500f, 0f, 500f);
        readonly List<GameObject> objects = new List<GameObject>();
        UnityBuildWorld world;

        [SetUp]
        public void SetUp() => world = new UnityBuildWorld(Physics.DefaultRaycastLayers);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in objects)
                Object.DestroyImmediate(go);
            objects.Clear();
            Physics.SyncTransforms();
        }

        GameObject Box(Vector3 center, Vector3 size)
        {
            var go = new GameObject("TestBox");
            go.transform.position = Base + center;
            go.AddComponent<BoxCollider>().size = size;
            objects.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        static Obb[] Floor(Vector3 at) => new[] { new Obb(at + new Vector3(0, 0.1f, 0), new Vector3(2, 0.2f, 2)) };

        [Test]
        public void Resting_OnACollider_IsGrounded()
        {
            Box(new Vector3(0, -0.5f, 0), new Vector3(4, 1, 4));
            Assert.IsTrue(world.IsGrounded(Floor(Base), 0.05f));
            Assert.IsFalse(world.IsGrounded(Floor(Base + new Vector3(0, 0.5f, 0)), 0.05f));
        }

        [Test]
        public void OtherPieces_AreNotGround()
        {
            Box(new Vector3(0, -0.5f, 0), new Vector3(4, 1, 4)).AddComponent<BuildPieceTag>();
            Assert.IsFalse(world.IsGrounded(Floor(Base), 0.05f));
        }

        [Test]
        public void DisabledCollider_StopsBeingGroundStraightAway()
        {
            GameObject dirt = Box(new Vector3(0, -0.5f, 0), new Vector3(4, 1, 4));
            dirt.GetComponent<Collider>().enabled = false;
            Assert.IsFalse(world.IsGrounded(Floor(Base), 0.05f));
        }
    }
}
