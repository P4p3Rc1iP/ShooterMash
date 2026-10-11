using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShooterMashup.Building.Tests
{
    /// <summary>
    /// Structural integrity, collapse, undo and snapping, against a fake world whose ground is
    /// a list of boxes (by default a big slab with its top at y = 0).
    /// </summary>
    public class BuildSystemTests
    {
        sealed class FakeWorld : IBuildWorld
        {
            public readonly List<Bounds> ground = new List<Bounds>();
            public readonly List<Bounds> actors = new List<Bounds>();

            public bool IsGrounded(Obb[] boxes, float tolerance)
            {
                foreach (Obb box in boxes)
                {
                    Bounds b = box.Bounds;
                    b.Expand(tolerance * 2f);
                    if (ground.Any(g => g.Intersects(b)))
                        return true;
                }
                return false;
            }

            public bool IsBlocked(Obb[] boxes) =>
                boxes.Any(box => actors.Any(a => Obb.Overlap(box, new Obb(a.center, a.size), 0f)));
        }

        static readonly int Floor2 = Type("Floor 2.5x2.5");
        static readonly int Wall2 = Type("Wall 2.5x2.5");
        static readonly int Pillar2 = Type("Pillar 2.5 m");
        const float PostTop = PieceCatalog.StoreyHeight;
        const float BeamY = PostTop - 0.2f; // a beam flush with the top of a post
        static readonly int Beam2 = Type("Beam 2.5 m");
        const float G = PieceCatalog.Grid; // big pieces are G across
        const float H = G / 2f;
        static float BeamX(int k) => H + G * k; // centre of the k-th beam out from a post at the origin

        BuildConfig cfg;
        FakeWorld world;
        BuildSystem sys;
        double time;

        static int Type(string name)
        {
            int i = Array.FindIndex(PieceCatalog.All, d => d.name == name);
            Assert.GreaterOrEqual(i, 0, name);
            return i;
        }

        [SetUp]
        public void SetUp()
        {
            cfg = new BuildConfig();
            world = new FakeWorld();
            world.ground.Add(new Bounds(new Vector3(0, -1, 0), new Vector3(1000, 2, 1000)));
            sys = new BuildSystem(cfg, world);
            time = 100;
        }

        // ------------------------------------------------------------------ harness

        BuildResult TryPlace(int type, BuildMaterial m, Vector3 pos, float yaw = 0f, int owner = 0) =>
            sys.Execute(BuildCmd.Place(owner, type, m, pos, Quaternion.Euler(0, yaw, 0), time));

        int Place(int type, BuildMaterial m, Vector3 pos, float yaw = 0f, int owner = 0)
        {
            BuildResult r = TryPlace(type, m, pos, yaw, owner);
            Assert.IsTrue(r.Ok, $"placing {PieceCatalog.All[type].name} at {pos}: {r.failure}");
            return r.added[0];
        }

        BuildResult Remove(int id, int owner = 0) => sys.Execute(BuildCmd.Remove(owner, id, time));
        BuildResult Undo(int owner = 0) => sys.Execute(BuildCmd.Undo(owner, time));
        BuildResult Redo(int owner = 0) => sys.Execute(BuildCmd.Redo(owner, time));

        float Support(int id) => sys.Get(id).support;

        /// <summary>A storey-high post at the origin, and beams along +x from its top, flush with it. Returns the beam ids.</summary>
        List<int> PostWithBeams(BuildMaterial m, int count, out int post)
        {
            post = Place(Pillar2, m, Vector3.zero);
            var beams = new List<int>();
            for (int k = 0; k < count; k++)
                beams.Add(Place(Beam2, m, new Vector3(BeamX(k), BeamY, 0)));
            return beams;
        }

        // ------------------------------------------------------------------ support

        [Test]
        public void FloorOnGround_IsGroundedWithFullSupport()
        {
            int id = Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            Assert.IsTrue(sys.Get(id).grounded);
            Assert.AreEqual(100f, Support(id));
        }

        [Test]
        public void Wood_StacksEightPostsHigh()
        {
            for (int n = 0; n < 8; n++)
                Place(Pillar2, BuildMaterial.Wood, new Vector3(0, PostTop * n, 0));
            Assert.AreEqual(BuildFailure.Unsupported, TryPlace(Pillar2, BuildMaterial.Wood, new Vector3(0, PostTop * 8, 0)).failure);
        }

        [Test]
        public void Wood_ReachesFourBeamsOutFromAPost()
        {
            List<int> beams = PostWithBeams(BuildMaterial.Wood, 4, out _);
            Assert.AreEqual(80f, Support(beams[0]), 0.01f);
            Assert.AreEqual(60f, Support(beams[1]), 0.01f);
            Assert.AreEqual(40f, Support(beams[2]), 0.01f);
            Assert.AreEqual(20f, Support(beams[3]), 0.01f);
            Assert.AreEqual(BuildFailure.Unsupported, TryPlace(Beam2, BuildMaterial.Wood, new Vector3(BeamX(4), BeamY, 0)).failure);
        }

        [Test]
        public void Stone_CannotBuildSideways()
        {
            Place(Pillar2, BuildMaterial.Stone, Vector3.zero);
            Assert.AreEqual(BuildFailure.Unsupported, TryPlace(Beam2, BuildMaterial.Stone, new Vector3(BeamX(0), BeamY, 0)).failure);
        }

        [Test]
        public void Iron_ReachesTwelveBeamsOut()
        {
            PostWithBeams(BuildMaterial.Iron, 12, out _);
            Assert.AreEqual(BuildFailure.Unsupported, TryPlace(Beam2, BuildMaterial.Iron, new Vector3(BeamX(12), BeamY, 0)).failure);
        }

        [Test]
        public void StonePost_HoldsWoodBeamsLikeAWoodPost()
        {
            Place(Pillar2, BuildMaterial.Stone, Vector3.zero);
            int beam = Place(Beam2, BuildMaterial.Wood, new Vector3(BeamX(0), BeamY, 0));
            Assert.AreEqual(80f, Support(beam), 0.01f); // capped at wood's 100 first
        }

        [Test]
        public void Bridge_IsSupportedFromBothEnds()
        {
            Place(Pillar2, BuildMaterial.Wood, Vector3.zero);
            int far = Place(Pillar2, BuildMaterial.Wood, new Vector3(6 * G, 0, 0));
            var beams = new List<int>();
            for (int k = 0; k < 4; k++)
                beams.Add(Place(Beam2, BuildMaterial.Wood, new Vector3(BeamX(k), BeamY, 0)));
            // The fifth beam alone would fail; build the last one off the far post first.
            int last = Place(Beam2, BuildMaterial.Wood, new Vector3(BeamX(5), BeamY, 0));
            int middle = Place(Beam2, BuildMaterial.Wood, new Vector3(BeamX(4), BeamY, 0));
            Assert.AreEqual(60f, Support(middle), 0.01f);

            BuildResult r = Remove(far);
            CollectionAssert.AreEquivalent(new[] { middle, last }, r.collapsed);
        }

        [Test]
        public void AddingAPost_RaisesSupportOfWhatItHolds()
        {
            List<int> beams = PostWithBeams(BuildMaterial.Wood, 4, out _);
            Place(Pillar2, BuildMaterial.Wood, new Vector3(4 * G, 0, 0)); // under the end of the last beam
            Assert.AreEqual(80f, Support(beams[3]), 0.01f);
            Assert.AreEqual(60f, Support(beams[2]), 0.01f);
        }

        [Test]
        public void IntegrityOff_NothingBreaks()
        {
            cfg.structuralIntegrity = false;
            PostWithBeams(BuildMaterial.Stone, 6, out int post);
            Assert.AreEqual(0, Remove(post).collapsed.Count);
        }

        // ------------------------------------------------------------------ collapse

        [Test]
        public void RemovingThePost_CollapsesTheBeams_AsPreviewed()
        {
            List<int> beams = PostWithBeams(BuildMaterial.Wood, 3, out int post);
            var preview = new List<int>();
            sys.PreviewRemove(post, preview);
            CollectionAssert.AreEquivalent(beams, preview);

            BuildResult r = Remove(post);
            CollectionAssert.AreEqual(new[] { post }, r.removed);
            CollectionAssert.AreEquivalent(beams, r.collapsed);
            Assert.AreEqual(0, sys.Pieces.Count);
        }

        [Test]
        public void DiggingOutTheGround_CollapsesWhatStoodOnIt()
        {
            world.ground.Clear();
            var block = new Bounds(new Vector3(0, -0.5f, 0), new Vector3(2, 1, 2));
            world.ground.Add(block);
            var other = new Bounds(new Vector3(10, -0.5f, 0), new Vector3(2, 1, 2));
            world.ground.Add(other);
            List<int> beams = PostWithBeams(BuildMaterial.Wood, 2, out int post);
            int elsewhere = Place(Floor2, BuildMaterial.Wood, new Vector3(10, 0, 0));

            world.ground.Remove(block);
            BuildResult r = sys.TerrainChanged(block);
            CollectionAssert.AreEquivalent(beams.Append(post), r.collapsed);
            Assert.IsNotNull(sys.Get(elsewhere));
        }

        [Test]
        public void PlacementIsBlockedByPlayers()
        {
            world.actors.Add(new Bounds(new Vector3(0, 0.9f, 0), new Vector3(0.9f, 1.8f, 0.9f)));
            Assert.AreEqual(BuildFailure.Blocked, TryPlace(Floor2, BuildMaterial.Wood, Vector3.zero).failure);
            Assert.IsTrue(TryPlace(Floor2, BuildMaterial.Wood, new Vector3(3, 0, 0)).Ok);
        }

        // ------------------------------------------------------------------ undo

        [Test]
        public void UndoRemoval_RestoresEverythingThatFell_AndRedoRemovesItAgain()
        {
            List<int> beams = PostWithBeams(BuildMaterial.Wood, 3, out int post);
            Remove(post);
            Assert.AreEqual(0, sys.Pieces.Count);

            BuildResult u = Undo();
            Assert.IsTrue(u.Ok, u.failure.ToString());
            Assert.AreEqual(4, sys.Pieces.Count);
            Assert.AreEqual(40f, Support(beams[2]), 0.01f);

            BuildResult r = Redo();
            Assert.IsTrue(r.Ok, r.failure.ToString());
            Assert.AreEqual(0, sys.Pieces.Count);
        }

        [Test]
        public void UndoPlacement_RemovesIt_AndRedoPutsItBack()
        {
            int id = Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            Assert.IsTrue(Undo().Ok);
            Assert.IsNull(sys.Get(id));
            Assert.IsTrue(Redo().Ok);
            Assert.IsNotNull(sys.Get(id));
        }

        [Test]
        public void Undo_GoesBackOneStepAtATime()
        {
            int post = Place(Pillar2, BuildMaterial.Wood, Vector3.zero);
            int beam = Place(Beam2, BuildMaterial.Wood, new Vector3(BeamX(0), BeamY, 0));
            Assert.IsTrue(Undo().Ok);
            Assert.IsNull(sys.Get(beam));
            Assert.IsNotNull(sys.Get(post));
            Assert.IsTrue(Undo().Ok);
            Assert.IsNull(sys.Get(post));
            Assert.AreEqual(BuildFailure.NothingToUndo, Undo().failure);
        }

        [Test]
        public void Undo_ExpiresAfterTheWindow()
        {
            Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            time += cfg.undoWindow + 1;
            Assert.AreEqual(BuildFailure.UndoExpired, Undo().failure);
        }

        [Test]
        public void Undo_IsRefusedOnceSomeoneElseBuiltThere()
        {
            Place(Pillar2, BuildMaterial.Wood, Vector3.zero, owner: 0);
            Place(Beam2, BuildMaterial.Wood, new Vector3(BeamX(0), BeamY, 0), owner: 1);
            Assert.AreEqual(BuildFailure.StructureChanged, Undo(0).failure);
            Assert.IsTrue(Undo(1).Ok); // their own is fine
        }

        [Test]
        public void Undo_IgnoresOtherPlayersBuildingFarAway()
        {
            Place(Floor2, BuildMaterial.Wood, Vector3.zero, owner: 0);
            Place(Floor2, BuildMaterial.Wood, new Vector3(50, 0, 0), owner: 1);
            Assert.IsTrue(Undo(0).Ok);
        }

        [Test]
        public void NewAction_ClearsRedo()
        {
            Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            Undo();
            Place(Floor2, BuildMaterial.Wood, new Vector3(5, 0, 0));
            Assert.AreEqual(BuildFailure.NothingToRedo, Redo().failure);
        }

        // ------------------------------------------------------------------ snapping

        [Test]
        public void Wall_SnapsOntoTheEdgeOfAFloor_StandingOnIt()
        {
            int floor = Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            SnapResult s = Snap(Wall2, new Vector3(H - 0.05f, 0.2f, 0.1f), Vector3.up, 90f);
            Assert.IsTrue(s.snapped);
            Assert.AreEqual(floor, s.targetId);
            // Not hanging below the floor, though that lines up just as many snap points.
            AssertNear(new Vector3(H, 0.2f, 0), s.position);
            Assert.IsTrue(sys.CheckPlace(Wall2, BuildMaterial.Wood, s.position, s.rotation).Valid);
        }

        [Test]
        public void Floor_AimedNearTheEdgeOfAFloor_GoesNextToIt()
        {
            Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            // Standing on it, looking at the floor just inside its +x edge.
            AssertNear(new Vector3(G, 0, 0), Snap(Floor2, new Vector3(H - 0.2f, 0.2f, 0.1f), Vector3.up).position);
            // Or at the ground just beyond that edge.
            AssertNear(new Vector3(G, 0, 0), Snap(Floor2, new Vector3(H + 0.4f, 0f, -0.2f), Vector3.up).position);
        }

        [Test]
        public void Floor_AimedNearACorner_LinesUpWithAnEdge()
        {
            Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            SnapResult s = Snap(Floor2, new Vector3(H - 0.15f, 0.2f, H - 0.25f), Vector3.up);
            Assert.IsTrue(s.snapped);
            // Beside it on one side or the other, sharing a whole edge; never overlapping or half offset.
            Assert.IsTrue(Near(s.position, new Vector3(G, 0, 0)) || Near(s.position, new Vector3(0, 0, G)), s.position.ToString());
        }

        [Test]
        public void Snap_NeverPutsAPieceInsideAnother()
        {
            // A floor already sits next to the first one: aiming near their shared edge must not stack.
            Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            Place(Floor2, BuildMaterial.Wood, new Vector3(G, 0, 0));
            SnapResult s = Snap(Floor2, new Vector3(H + 0.05f, 0.2f, H - 0.1f), Vector3.up);
            Assert.IsTrue(s.snapped);
            Assert.IsFalse(Near(s.position, Vector3.zero) || Near(s.position, new Vector3(G, 0, 0)), s.position.ToString());
        }

        [Test]
        public void SmallPieces_TileAlongABigOne()
        {
            // 1 + 1 + 0.5 floors along a big floor's edge end exactly at its corner.
            Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            int f1 = Type("Floor 1x1"), f05 = Type("Floor 0.5x0.5");
            Place(f1, BuildMaterial.Wood, new Vector3(H + 0.5f, 0, -H + 0.5f));
            Place(f1, BuildMaterial.Wood, new Vector3(H + 0.5f, 0, -H + 1.5f));
            int last = Place(f05, BuildMaterial.Wood, new Vector3(H + 0.25f, 0, -H + 2.25f));
            Piece p = sys.Get(last);
            Assert.AreEqual(H, p.bounds.max.z, 1e-4f);
        }

        [Test]
        public void ForcedSnapPoint_AttachesWhereYouAim()
        {
            Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            // Force the new floor's (-H, ., -H) corner onto the existing floor's (H, ., H) corner.
            SnapResult s = Snap(Floor2, new Vector3(H - 0.1f, 0.2f, H - 0.05f), Vector3.up, held: Held(Floor2, new Vector3(-H, 0.2f, -H)));
            Assert.IsTrue(s.snapped);
            Assert.AreEqual(Held(Floor2, new Vector3(-H, 0.2f, -H)), s.heldIndex);
            AssertNear(new Vector3(G, 0, G), s.position); // diagonal, as asked for
        }

        [Test]
        public void ForcedSnapPoint_OnABeam()
        {
            Place(Pillar2, BuildMaterial.Wood, Vector3.zero);
            // Force the beam's right end onto the post top.
            SnapResult s = Snap(Beam2, new Vector3(0.05f, PostTop, 0), Vector3.up, held: Held(Beam2, new Vector3(H, 0.2f, 0)));
            Assert.IsTrue(s.snapped);
            Assert.AreEqual(Held(Beam2, new Vector3(H, 0.2f, 0)), s.heldIndex);
            AssertNear(new Vector3(-H, BeamY, 0), s.position);
        }

        [Test]
        public void Snap_KeepsTheWorldRotation()
        {
            Place(Floor2, BuildMaterial.Wood, Vector3.zero, yaw: 30f);
            Vector3 aim = Quaternion.Euler(0, 30, 0) * new Vector3(H + 0.3f, 0.2f, 0.1f); // just past its local +x edge
            SnapResult s = Snap(Floor2, aim, Vector3.up, yaw: 0f);
            Assert.IsTrue(s.snapped);
            Assert.That(Quaternion.Angle(s.rotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void Snap_WithTheTargetsRotation_LinesUpWithIt()
        {
            int floor = Place(Floor2, BuildMaterial.Wood, Vector3.zero, yaw: 30f);
            Quaternion r = Quaternion.Euler(0, 30, 0);
            Vector3 aim = r * new Vector3(H + 0.3f, 0.2f, 0.1f);
            SnapResult s = Snap(Floor2, aim, Vector3.up, yaw: 30f); // as after copying it
            Assert.IsTrue(s.snapped);
            Assert.AreEqual(floor, s.targetId);
            AssertNear(r * new Vector3(G, 0, 0), s.position);
            Assert.IsTrue(TryPlace(Floor2, BuildMaterial.Wood, s.position, 30f).Ok);
        }

        [Test]
        public void NothingInReach_DoesNotSnap()
        {
            Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            Assert.IsFalse(Snap(Floor2, new Vector3(H + 3f, 0f, 0f), Vector3.up).snapped);
        }

        [Test]
        public void LooserOverlapLimit_LetsPiecesGoInsideEachOther()
        {
            Place(Floor2, BuildMaterial.Wood, Vector3.zero);
            // The new floor's (0.25, ., H) point onto the existing floor's (H, ., 0.25) point:
            // the new floor then covers about a third of the old one.
            int held = Held(Floor2, new Vector3(0.25f, 0.2f, H));
            var aim = new Vector3(H - 0.05f, 0.2f, 0.3f);
            var overlapping = new Vector3(H - 0.25f, 0, 0.25f - H);
            AssertNear(overlapping, Snap(Floor2, aim, Vector3.up, held: held, overlap: 0.95f).position);
            Assert.IsFalse(Near(overlapping, Snap(Floor2, aim, Vector3.up, held: held).position));
            // Even a loose limit refuses an exact duplicate.
            SnapResult duplicate = Snap(Floor2, aim, Vector3.up, held: Held(Floor2, new Vector3(H, 0.2f, 0.25f)), overlap: 0.95f);
            Assert.IsFalse(duplicate.snapped && Near(Vector3.zero, duplicate.position));
        }

        [Test]
        public void AllSnapPoints_AreOnTheHalfMetreGrid()
        {
            // Any two snap points of a piece are a whole number of grid steps apart on every axis,
            // so snapping can never take a structure off the grid.
            foreach (PieceDef def in PieceCatalog.All)
            foreach (Vector3 a in def.snapPoints)
            foreach (Vector3 b in def.snapPoints)
                for (int axis = 0; axis < 3; axis++)
                {
                    float steps = (a[axis] - b[axis]) / PieceCatalog.Step;
                    Assert.AreEqual(Mathf.Round(steps), steps, 1e-4f, $"{def.name}: {a} vs {b}");
                }
        }

        static int Held(int type, Vector3 local)
        {
            int i = Array.FindIndex(PieceCatalog.All[type].snapPoints, p => (p - local).sqrMagnitude < 1e-6f);
            Assert.GreaterOrEqual(i, 0, $"{PieceCatalog.All[type].name} has no snap point at {local}");
            return i;
        }

        SnapResult Snap(int type, Vector3 aim, Vector3 normal, float yaw = 0f, int held = -1, float overlap = BuildSnapping.DefaultOverlapLimit) =>
            BuildSnapping.Snap(sys, PieceCatalog.All[type], aim, normal, Quaternion.Euler(0, yaw, 0), held, 1f, overlap);

        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;
        static void AssertNear(Vector3 expected, Vector3 actual) => Assert.IsTrue(Near(expected, actual), $"expected {expected}, got {actual}");

        // ------------------------------------------------------------------ determinism

        [Test]
        public void SameCommands_GiveTheSameWorld()
        {
            string Run()
            {
                SetUp();
                PostWithBeams(BuildMaterial.Wood, 4, out int post);
                Place(Pillar2, BuildMaterial.Stone, new Vector3(8, 0, 0));
                Place(Wall2, BuildMaterial.Wood, new Vector3(3, 0, 3), 45f);
                Remove(post);
                Undo();
                return string.Join("|", sys.Pieces.Values.OrderBy(p => p.Id)
                    .Select(p => $"{p.Id}:{p.record.type}:{p.record.position}:{p.support:R}:{p.grounded}"));
            }

            Assert.AreEqual(Run(), Run());
        }
    }
}
