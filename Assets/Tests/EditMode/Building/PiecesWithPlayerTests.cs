using System.Collections.Generic;
using NUnit.Framework;
using ShooterMashup.Player;
using UnityEngine;

namespace ShooterMashup.Building.Tests
{
    /// <summary>
    /// Pieces against the real player controller: can you walk up every stairs variant, and
    /// stand in a room one storey tall. Built from colliders far from the origin and removed afterwards.
    /// </summary>
    public class PiecesWithPlayerTests
    {
        static readonly Vector3 Base = new Vector3(-900f, 0f, -900f);
        const int Tick = 8;
        readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in objects)
                Object.DestroyImmediate(go);
            objects.Clear();
            Physics.SyncTransforms();
        }

        void Box(Vector3 center, Vector3 size)
        {
            var go = new GameObject("TestBox");
            go.transform.position = Base + center;
            go.AddComponent<BoxCollider>().size = size;
            objects.Add(go);
        }

        static IEnumerable<TestCaseData> AllStairs()
        {
            foreach (PieceDef def in PieceCatalog.All)
                if (def.name.StartsWith("Stairs"))
                    foreach (MovePreset preset in new[] { MovePreset.ShooterMash, MovePreset.EtExact })
                        yield return new TestCaseData(def.name, preset).SetName($"Stairs_AreWalkable({def.name}, {preset})");
        }

        [TestCaseSource(nameof(AllStairs))]
        public void Stairs_AreWalkable(string name, MovePreset preset)
        {
            PieceDef stairs = System.Array.Find(PieceCatalog.All, d => d.name == name);
            Bounds b = stairs.boxes[0].Bounds;
            foreach (Obb o in stairs.boxes) b.Encapsulate(o.Bounds);
            float rise = b.max.y, run = b.size.z;

            // Ground, the stairs starting 3 m ahead, and a landing at the top.
            Box(new Vector3(0, -0.4f, 0), new Vector3(50, 0.8f, 50));
            var at = new Vector3(0, 0, 3f + run / 2);
            foreach (Obb o in stairs.boxes) Box(at + o.center, o.Size);
            Box(at + new Vector3(0, rise - 0.1f, run / 2 + 1.25f), new Vector3(2.5f, 0.2f, 2.5f));
            Physics.SyncTransforms();

            var cfg = new MoveConfig();
            MovePresets.Apply(cfg, preset);
            var pmove = new PlayerMove(cfg, new UnityPmoveWorld(Physics.DefaultRaycastLayers));
            PlayerState st = PlayerState.Spawn(Base + new Vector3(0, -cfg.mins.y, 0), cfg, 1000);
            float highest = 0f;
            for (int t = 1000; t < 1000 + 3000; t += Tick)
            {
                pmove.Move(ref st, new PlayerCmd { serverTime = t + Tick, forwardMove = 127 });
                highest = Mathf.Max(highest, st.origin.y - Base.y + cfg.mins.y);
            }
            Assert.AreEqual(rise, highest, 0.05f, "feet should reach the top");
        }

        [Test]
        public void OneStoreyRoom_HasRoomToStand()
        {
            // A floor on top of storey-high walls: its underside is a slab below the wall tops.
            float ceiling = PieceCatalog.StoreyHeight - 0.2f;
            Box(new Vector3(0, -0.4f, 0), new Vector3(50, 0.8f, 50));
            Box(new Vector3(0, ceiling + 0.1f, 10f), new Vector3(5f, 0.2f, 10f)); // z 5..15
            Physics.SyncTransforms();

            var cfg = new MoveConfig();
            MovePresets.Apply(cfg, MovePreset.ShooterMash);
            var pmove = new PlayerMove(cfg, new UnityPmoveWorld(Physics.DefaultRaycastLayers));
            PlayerState st = PlayerState.Spawn(Base + new Vector3(0, -cfg.mins.y, 0), cfg, 1000);
            for (int t = 1000; t < 1000 + 1600; t += Tick)
                pmove.Move(ref st, new PlayerCmd { serverTime = t + Tick, forwardMove = 127 });
            Assert.Greater(st.origin.z - Base.z, 8f, "should walk in standing up");
            Assert.Greater(ceiling - cfg.maxs.y + cfg.mins.y, 0.4f, "with clear headroom");
        }
    }
}
