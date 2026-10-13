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
    ///
    /// Support loss is per 2.5 m of piece, so with 3 m pieces: a wood beam out sideways loses
    /// 20 * 3 / 2.5 = 24, a wood post stacked loses 12.5 * 1.2 = 15, an iron beam 138.6.
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

        static readonly int Floor3 = Type("Floor 3x3");
        static readonly int Wall3 = Type("Wall 3x3");
        static readonly int Pillar3 = Type("Pillar 3 m");
        static readonly int Beam3 = Type("Beam 3 m");
        static readonly int Beam1 = Type("Beam 1 m");
        static readonly int Slab3 = Type("Slab 3x3");
        static readonly int Slab1 = Type("Slab 1x1");
        const float G = PieceCatalog.Grid; // big pieces are G across
        const float H = G / 2f;
        const float PostTop = PieceCatalog.StoreyHeight;
        const float BeamY = PostTop - PieceCatalog.BeamSize; // a beam flush with the top of a post
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

        /// <summary>A storey-high post at the origin, and 3 m beams along +x from its top, flush with it. Returns the beam ids.</summary>
        List<int> PostWithBeams(BuildMaterial m, int count, out int post)
        {
            post = Place(Pillar3, m, Vector3.zero);
            var beams = new List<int>();
            for (int k = 0; k < count; k++)
                beams.Add(Place(Beam3, m, new Vector3(BeamX(k), BeamY, 0)));
            return beams;
        }

        // ------------------------------------------------------------------ support

        [Test]
        public void FloorOnGround_IsGroundedWithFullSupport()
        {
            int id = Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            Assert.IsTrue(sys.Get(id).grounded);
            Assert.AreEqual(100f, Support(id));
        }

        [Test]
        public void Wood_StacksPostsAbout20MetresHigh()
        {
            // 3 m posts lose 15 each: 100, 85, 70, 55, 40, 25, and a 7th would be left with 10.
            int top = 0;
            for (int n = 0; n < 6; n++)
                top = Place(Pillar3, BuildMaterial.Wood, new Vector3(0, PostTop * n, 0));
            Assert.AreEqual(25f, Support(top), 0.01f);
            Assert.AreEqual(10f, sys.CheckPlace(Pillar3, BuildMaterial.Wood, new Vector3(0, PostTop * 6, 0), Quaternion.identity).support, 0.01f);
        }

        [Test]
        public void Wood_ReachesThreeBeamsOutFromAPost()
        {
            List<int> beams = PostWithBeams(BuildMaterial.Wood, 3, out _);
            Assert.AreEqual(76f, Support(beams[0]), 0.01f);
            Assert.AreEqual(52f, Support(beams[1]), 0.01f);
            Assert.AreEqual(28f, Support(beams[2]), 0.01f);
            Assert.AreEqual(BuildFailure.Unsupported, TryPlace(Beam3, BuildMaterial.Wood, new Vector3(BeamX(3), BeamY, 0)).failure);
        }

        [Test]
        public void SmallBeams_ReachAsFarAsBigOnes()
        {
            // Loss is per metre: 1 m beams lose 8 each, so they reach 11 m (the 12th would be left
            // with 4), against 9 m for three 3 m beams.
            Place(Pillar3, BuildMaterial.Wood, Vector3.zero);
            for (int k = 0; k < 11; k++)
                Place(Beam1, BuildMaterial.Wood, new Vector3(0.5f + k, BeamY, 0));
            Assert.AreEqual(BuildFailure.Unsupported, TryPlace(Beam1, BuildMaterial.Wood, new Vector3(11.5f, BeamY, 0)).failure);
        }

        [Test]
        public void Stone_CannotBuildSideways()
        {
            // A 1x1 stone slab sticking out sideways from the top of a stone pillar.
            Place(Pillar3, BuildMaterial.Stone, Vector3.zero);
            Assert.AreEqual(BuildFailure.Unsupported,
                TryPlace(Slab1, BuildMaterial.Stone, new Vector3(0.125f + 0.5f, PostTop - 0.5f, 0)).failure);
        }

        [Test]
        public void Iron_ReachesTenBeamsOut()
        {
            PostWithBeams(BuildMaterial.Iron, 10, out _);
            Assert.AreEqual(BuildFailure.Unsupported, TryPlace(Beam3, BuildMaterial.Iron, new Vector3(BeamX(10), BeamY, 0)).failure);
        }

        [Test]
        public void StonePost_HoldsWoodBeamsLikeAWoodPost()
        {
            Place(Pillar3, BuildMaterial.Stone, Vector3.zero);
            int beam = Place(Beam3, BuildMaterial.Wood, new Vector3(BeamX(0), BeamY, 0));
            Assert.AreEqual(76f, Support(beam), 0.01f); // capped at wood's 100 first
        }

        [Test]
        public void Bridge_IsSupportedFromBothEnds()
        {
            Place(Pillar3, BuildMaterial.Wood, Vector3.zero);
            int far = Place(Pillar3, BuildMaterial.Wood, new Vector3(6 * G, 0, 0));
            for (int k = 0; k < 3; k++)
                Place(Beam3, BuildMaterial.Wood, new Vector3(BeamX(k), BeamY, 0));
            // A fourth beam off the near post would fail; build the rest off the far post.
            int last = Place(Beam3, BuildMaterial.Wood, new Vector3(BeamX(5), BeamY, 0));
            int next = Place(Beam3, BuildMaterial.Wood, new Vector3(BeamX(4), BeamY, 0));
            int middle = Place(Beam3, BuildMaterial.Wood, new Vector3(BeamX(3), BeamY, 0));
            Assert.AreEqual(28f, Support(middle), 0.01f);

            BuildResult r = Remove(far);
            CollectionAssert.AreEquivalent(new[] { middle, next, last }, r.collapsed);
        }

        [Test]
        public void AddingAPost_RaisesSupportOfWhatItHolds()
        {
            List<int> beams = PostWithBeams(BuildMaterial.Wood, 3, out _);
            Place(Pillar3, BuildMaterial.Wood, new Vector3(3 * G, 0, 0)); // under the end of the last beam
            Assert.AreEqual(76f, Support(beams[2]), 0.01f);
            Assert.AreEqual(52f, Support(beams[1]), 0.01f);
        }

        [Test]
        public void OffsetStackedSlab_CountsAsStacked()
        {
            // A slab on another, offset by a metre: it rests on it, so the loss is vertical
            // (12.5% per 2.5 m over its 0.5 m height), not sideways.
            Place(Slab3, BuildMaterial.Stone, Vector3.zero);
            int upper = Place(Slab3, BuildMaterial.Stone, new Vector3(1f, 0.5f, 0));
            Assert.AreEqual(1000f - 125f * 0.2f, Support(upper), 0.01f);
        }

        [Test]
        public void Pieces_OnlyComeInTheirMaterials()
        {
            Assert.AreEqual(BuildFailure.WrongMaterial, TryPlace(Wall3, BuildMaterial.Stone, Vector3.zero).failure);
            Assert.AreEqual(BuildFailure.WrongMaterial, TryPlace(Floor3, BuildMaterial.Iron, Vector3.zero).failure);
            Assert.AreEqual(BuildFailure.WrongMaterial, TryPlace(Slab3, BuildMaterial.Wood, Vector3.zero).failure);
            Assert.AreEqual(BuildFailure.WrongMaterial,
                sys.CheckPlace(Beam3, BuildMaterial.Stone, Vector3.zero, Quaternion.identity).failure);
            Assert.IsTrue(TryPlace(Pillar3, BuildMaterial.Iron, Vector3.zero).Ok);
            Assert.IsTrue(TryPlace(Slab3, BuildMaterial.Stone, new Vector3(5, 0, 0)).Ok);
        }

        [Test]
        public void IntegrityOff_NothingBreaks()
        {
            cfg.structuralIntegrity = false;
            PostWithBeams(BuildMaterial.Wood, 6, out int post); // wood only reaches 3 out
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
            var other = new Bounds(new Vector3(12, -0.5f, 0), new Vector3(2, 1, 2));
            world.ground.Add(other);
            List<int> beams = PostWithBeams(BuildMaterial.Wood, 2, out int post);
            int elsewhere = Place(Floor3, BuildMaterial.Wood, new Vector3(12, 0, 0));

            world.ground.Remove(block);
            BuildResult r = sys.TerrainChanged(block);
            CollectionAssert.AreEquivalent(beams.Append(post), r.collapsed);
            Assert.IsNotNull(sys.Get(elsewhere));
        }

        [Test]
        public void PlacementIsBlockedByPlayers()
        {
            world.actors.Add(new Bounds(new Vector3(0, 0.9f, 0), new Vector3(0.9f, 1.8f, 0.9f)));
            Assert.AreEqual(BuildFailure.Blocked, TryPlace(Floor3, BuildMaterial.Wood, Vector3.zero).failure);
            Assert.IsTrue(TryPlace(Floor3, BuildMaterial.Wood, new Vector3(3, 0, 0)).Ok);
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
            Assert.AreEqual(28f, Support(beams[2]), 0.01f);

            BuildResult r = Redo();
            Assert.IsTrue(r.Ok, r.failure.ToString());
            Assert.AreEqual(0, sys.Pieces.Count);
        }

        [Test]
        public void UndoPlacement_RemovesIt_AndRedoPutsItBack()
        {
            int id = Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            Assert.IsTrue(Undo().Ok);
            Assert.IsNull(sys.Get(id));
            Assert.IsTrue(Redo().Ok);
            Assert.IsNotNull(sys.Get(id));
        }

        [Test]
        public void Undo_GoesBackOneStepAtATime()
        {
            int post = Place(Pillar3, BuildMaterial.Wood, Vector3.zero);
            int beam = Place(Beam3, BuildMaterial.Wood, new Vector3(BeamX(0), BeamY, 0));
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
            Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            time += cfg.undoWindow + 1;
            Assert.AreEqual(BuildFailure.UndoExpired, Undo().failure);
        }

        [Test]
        public void Undo_IsRefusedOnceSomeoneElseBuiltThere()
        {
            Place(Pillar3, BuildMaterial.Wood, Vector3.zero, owner: 0);
            Place(Beam3, BuildMaterial.Wood, new Vector3(BeamX(0), BeamY, 0), owner: 1);
            Assert.AreEqual(BuildFailure.StructureChanged, Undo(0).failure);
            Assert.IsTrue(Undo(1).Ok); // their own is fine
        }

        [Test]
        public void Undo_IgnoresOtherPlayersBuildingFarAway()
        {
            Place(Floor3, BuildMaterial.Wood, Vector3.zero, owner: 0);
            Place(Floor3, BuildMaterial.Wood, new Vector3(50, 0, 0), owner: 1);
            Assert.IsTrue(Undo(0).Ok);
        }

        [Test]
        public void NewAction_ClearsRedo()
        {
            Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            Undo();
            Place(Floor3, BuildMaterial.Wood, new Vector3(5, 0, 0));
            Assert.AreEqual(BuildFailure.NothingToRedo, Redo().failure);
        }

        // ------------------------------------------------------------------ snapping

        [Test]
        public void Wall_SnapsOntoTheEdgeOfAFloor_StandingOnIt()
        {
            int floor = Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            SnapResult s = Snap(Wall3, new Vector3(H - 0.05f, 0.2f, 0.1f), Vector3.up, 90f);
            Assert.IsTrue(s.snapped);
            Assert.AreEqual(floor, s.targetId);
            // Not hanging below the floor, though that lines up just as many snap points.
            AssertNear(new Vector3(H, 0.2f, 0), s.position);
            Assert.IsTrue(sys.CheckPlace(Wall3, BuildMaterial.Wood, s.position, s.rotation).Valid);
        }

        [Test]
        public void Floor_AimedNearTheEdgeOfAFloor_GoesNextToIt()
        {
            Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            // Standing on it, looking at the floor just inside its +x edge.
            AssertNear(new Vector3(G, 0, 0), Snap(Floor3, new Vector3(H - 0.2f, 0.2f, 0.1f), Vector3.up).position);
            // Or at the ground just beyond that edge.
            AssertNear(new Vector3(G, 0, 0), Snap(Floor3, new Vector3(H + 0.4f, 0f, -0.2f), Vector3.up).position);
        }

        [Test]
        public void Floor_AimedNearACorner_LinesUpWithAnEdge()
        {
            Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            SnapResult s = Snap(Floor3, new Vector3(H - 0.15f, 0.2f, H - 0.25f), Vector3.up);
            Assert.IsTrue(s.snapped);
            // Beside it on one side or the other, sharing a whole edge; never overlapping or half offset.
            Assert.IsTrue(Near(s.position, new Vector3(G, 0, 0)) || Near(s.position, new Vector3(0, 0, G)), s.position.ToString());
        }

        [Test]
        public void Snap_NeverPutsAPieceInsideAnother()
        {
            // A floor already sits next to the first one: aiming near their shared edge must not stack.
            Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            Place(Floor3, BuildMaterial.Wood, new Vector3(G, 0, 0));
            SnapResult s = Snap(Floor3, new Vector3(H + 0.05f, 0.2f, H - 0.1f), Vector3.up);
            Assert.IsTrue(s.snapped);
            Assert.IsFalse(Near(s.position, Vector3.zero) || Near(s.position, new Vector3(G, 0, 0)), s.position.ToString());
        }

        [Test]
        public void SmallPieces_TileAlongABigOne()
        {
            // Three 1x1 floors along a big floor's edge end exactly at its corner.
            Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            int f1 = Type("Floor 1x1");
            int last = 0;
            for (int k = 0; k < 3; k++)
                last = Place(f1, BuildMaterial.Wood, new Vector3(H + 0.5f, 0, -H + 0.5f + k));
            Assert.AreEqual(H, sys.Get(last).bounds.max.z, 1e-4f);
        }

        [Test]
        public void ForcedSnapPoint_AttachesWhereYouAim()
        {
            Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            // Force the new floor's (-H, ., -H) corner onto the existing floor's (H, ., H) corner.
            int held = Held(Floor3, new Vector3(-H, 0.2f, -H));
            SnapResult s = Snap(Floor3, new Vector3(H - 0.1f, 0.2f, H - 0.05f), Vector3.up, held: held);
            Assert.IsTrue(s.snapped);
            Assert.AreEqual(held, s.heldIndex);
            AssertNear(new Vector3(G, 0, G), s.position); // diagonal, as asked for
        }

        [Test]
        public void ForcedSnapPoint_OnABeam()
        {
            Place(Pillar3, BuildMaterial.Wood, Vector3.zero);
            // Force the beam's right end onto the post top: beams snap by their centre line, so the
            // beam sits centred on the top of the post.
            int held = Held(Beam3, new Vector3(H, PieceCatalog.BeamSize / 2f, 0));
            SnapResult s = Snap(Beam3, new Vector3(0.05f, PostTop, 0), Vector3.up, held: held);
            Assert.IsTrue(s.snapped);
            Assert.AreEqual(held, s.heldIndex);
            AssertNear(new Vector3(-H, PostTop - PieceCatalog.BeamSize / 2f, 0), s.position);
        }

        [TestCase("Diagonal beam 45° 3 m", 3f, 3f)]
        [TestCase("Diagonal beam 45° 1 m", 1f, 1f)]
        [TestCase("Diagonal beam 27° 3 m", 6f, 3f)]
        [TestCase("Diagonal beam 27° 1 m", 2f, 1f)]
        public void DiagonalBeams_SnapAtTheEndsOfTheirCentreLine(string name, float run, float rise)
        {
            PieceDef beam = PieceCatalog.All[Type(name)];
            Obb box = beam.boxes[0];
            CollectionAssert.AreEquivalent(new[] { BuildMaterial.Wood, BuildMaterial.Iron }, beam.Materials);
            Assert.AreEqual(2, beam.snapPoints.Length);
            AssertNear(new Vector3(0, 0, -run / 2), beam.snapPoints[0]);
            AssertNear(new Vector3(0, rise, run / 2), beam.snapPoints[1]);
            // Both ends are on the box's axis, at its two end faces.
            foreach (Vector3 p in beam.snapPoints)
            {
                Vector3 local = Quaternion.Inverse(box.rotation) * (p - box.center);
                AssertNear(new Vector3(0, 0, Mathf.Sign(local.z) * box.halfExtents.z), local);
            }
        }

        [Test]
        public void DiagonalBrace_SnapsOntoAPostTopAndHoldsUp()
        {
            // A 45° 3 m diagonal from the foot of one post to the top of another 3 m away.
            Place(Pillar3, BuildMaterial.Wood, Vector3.zero);
            Place(Pillar3, BuildMaterial.Wood, new Vector3(0, 0, G));
            int diag = Type("Diagonal beam 45° 3 m");
            SnapResult s = Snap(diag, new Vector3(0.05f, PostTop - 0.05f, G), Vector3.up, held: 1); // high end onto the far post top
            Assert.IsTrue(s.snapped);
            AssertNear(new Vector3(0, 0, H), s.position);
            Assert.IsTrue(TryPlace(diag, BuildMaterial.Wood, s.position).Ok);
        }

        [Test]
        public void Snap_KeepsTheWorldRotation()
        {
            Place(Floor3, BuildMaterial.Wood, Vector3.zero, yaw: 30f);
            Vector3 aim = Quaternion.Euler(0, 30, 0) * new Vector3(H + 0.3f, 0.2f, 0.1f); // just past its local +x edge
            SnapResult s = Snap(Floor3, aim, Vector3.up, yaw: 0f);
            Assert.IsTrue(s.snapped);
            Assert.That(Quaternion.Angle(s.rotation, Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void Snap_WithTheTargetsRotation_LinesUpWithIt()
        {
            int floor = Place(Floor3, BuildMaterial.Wood, Vector3.zero, yaw: 30f);
            Quaternion r = Quaternion.Euler(0, 30, 0);
            Vector3 aim = r * new Vector3(H + 0.3f, 0.2f, 0.1f);
            SnapResult s = Snap(Floor3, aim, Vector3.up, yaw: 30f); // as after copying it
            Assert.IsTrue(s.snapped);
            Assert.AreEqual(floor, s.targetId);
            AssertNear(r * new Vector3(G, 0, 0), s.position);
            Assert.IsTrue(TryPlace(Floor3, BuildMaterial.Wood, s.position, 30f).Ok);
        }

        [Test]
        public void NothingInReach_DoesNotSnap()
        {
            Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            Assert.IsFalse(Snap(Floor3, new Vector3(H + 3f, 0f, 0f), Vector3.up).snapped);
        }

        [Test]
        public void LooserOverlapLimit_LetsPiecesGoInsideEachOther()
        {
            Place(Floor3, BuildMaterial.Wood, Vector3.zero);
            // The new floor's (0.5, ., H) point onto the existing floor's (H, ., 0.5) point:
            // the new floor then covers four ninths of the old one.
            int held = Held(Floor3, new Vector3(0.5f, 0.2f, H));
            var aim = new Vector3(H - 0.05f, 0.2f, 0.55f);
            var overlapping = new Vector3(H - 0.5f, 0, 0.5f - H);
            AssertNear(overlapping, Snap(Floor3, aim, Vector3.up, held: held, overlap: 0.95f).position);
            Assert.IsFalse(Near(overlapping, Snap(Floor3, aim, Vector3.up, held: held).position));
            // Even a loose limit refuses an exact duplicate.
            SnapResult duplicate = Snap(Floor3, aim, Vector3.up, held: Held(Floor3, new Vector3(H, 0.2f, 0.5f)), overlap: 0.95f);
            Assert.IsFalse(duplicate.snapped && Near(Vector3.zero, duplicate.position));
        }

        // A stone slab goes on the face you aim at: on its top it stacks neatly; on its side, or
        // the ground just past it, it goes beside at the same height.
        [TestCase("Slab 3x3")]
        [TestCase("Slab 1x1")]
        public void StoneSlabs_GoOnTheFaceYouAimAt(string name)
        {
            int slab = Type(name);
            float half = PieceCatalog.All[slab].boxes[0].halfExtents.x;
            Place(slab, BuildMaterial.Stone, Vector3.zero);
            var beside = new Vector3(2 * half, 0, 0);
            AssertNear(new Vector3(0, 0.5f, 0), Snap(slab, new Vector3(half - 0.1f, 0.5f, 0.05f), Vector3.up).position);
            AssertNear(beside, Snap(slab, new Vector3(half, 0.25f, 0.05f), Vector3.right).position);
            AssertNear(beside, Snap(slab, new Vector3(half + 0.15f, 0f, 0.05f), Vector3.up).position);
        }

        [Test]
        public void StoneSlab_StacksOffsetWhenYouPickASnapPoint()
        {
            Place(Slab3, BuildMaterial.Stone, Vector3.zero);
            // Force the new slab's bottom (-H, ., -H) corner onto a point of the top face a metre in.
            int held = Held(Slab3, new Vector3(-H, 0, -H));
            SnapResult s = Snap(Slab3, new Vector3(-0.45f, 0.5f, -H + 0.1f), Vector3.up, held: held);
            Assert.IsTrue(s.snapped);
            AssertNear(new Vector3(1f, 0.5f, 0), s.position);
            Assert.IsTrue(TryPlace(Slab3, BuildMaterial.Stone, s.position).Ok);
        }

        [TestCase("Roof 45° 3x3")]
        [TestCase("Roof 45° 1x1")]
        public void Roof_LinesUpWithDiagonalBeams(string name)
        {
            // The slab's centre plane runs through every snap point, like the centre line of a
            // diagonal beam, so a roof and a 45° diagonal of the same size placed the same way lie
            // in one plane, and both cross a beam snapped to their ends at its centre line.
            PieceDef roof = PieceCatalog.All[Type(name)];
            Obb slab = roof.boxes[0];
            foreach (Vector3 p in roof.snapPoints)
            {
                Vector3 local = Quaternion.Inverse(slab.rotation) * (p - slab.center);
                Assert.AreEqual(0f, local.y, 1e-4f, $"{name}: {p}");
            }
            string size = name.Substring(name.LastIndexOf('x') + 1);
            Obb diagonal = PieceCatalog.All[Type($"Diagonal beam 45° {size} m")].boxes[0];
            Vector3 offset = Quaternion.Inverse(slab.rotation) * (diagonal.center - slab.center);
            Assert.AreEqual(0f, offset.y, 1e-4f, "diagonal's centre line in the roof's centre plane");
        }

        static bool OnPiece(PieceDef def, Vector3 p) => def.boxes.Any(b => b.Contains(p));

        [Test]
        public void Wedge_OverlapsOnlyOnItsKeptSide()
        {
            // The 3 m gable wall: a 3 x 3 m wall cut along its diagonal, high at +x.
            Obb gable = PieceCatalog.All[Type("Gable wall 45° 3x3")].boxes[0];
            Assert.IsTrue(gable.IsWedge);
            var small = new Vector3(0.2f, 0.2f, 0.2f);
            Assert.IsTrue(Obb.Overlap(gable, new Obb(new Vector3(1f, 0.5f, 0), small), 0f), "low corner, kept");
            Assert.IsFalse(Obb.Overlap(gable, new Obb(new Vector3(-1f, 2.5f, 0), small), 0f), "high corner, cut away");
            // Only the cut plane separates this one: inside the whole box, just above the diagonal.
            Assert.IsFalse(Obb.Overlap(gable, new Obb(new Vector3(-0.2f, 1.8f, 0), small), 0f), "just above the cut");
            Assert.IsTrue(Obb.Overlap(gable, new Obb(new Vector3(0.2f, 1.6f, 0), small), 0f), "across the cut");
            Assert.AreEqual(4.5f * 0.2f, gable.Volume, 1e-4f);
        }

        [TestCase("Roof 45° outer corner 3x3", true, 1f)]
        [TestCase("Roof 45° outer corner 1x1", true, 1f)]
        [TestCase("Roof 45° inner corner 3x3", false, 1f)]
        [TestCase("Roof 45° inner corner 1x1", false, 1f)]
        [TestCase("Roof 27° outer corner 3x3", true, 0.5f)]
        [TestCase("Roof 27° outer corner 1x1", true, 0.5f)]
        [TestCase("Roof 27° inner corner 3x3", false, 0.5f)]
        [TestCase("Roof 27° inner corner 1x1", false, 0.5f)]
        [TestCase("Roof 63° outer corner 1x1", true, 2f)]
        [TestCase("Roof 63° inner corner 1x1", false, 2f)]
        public void RoofCorners_FollowBothSlopes(string name, bool outer, float slope)
        {
            // Over the tile, the roof's centre plane is the lower of the two slopes (rising +z and
            // rising +x) for a hip, and the higher one for a valley. Sampled away from the diagonal.
            PieceDef corner = PieceCatalog.All[Type(name)];
            float h = corner.snapPoints.Max(p => p.x);
            for (float x = -0.8f * h; x <= 0.8f * h + 1e-4f; x += 0.4f * h)
            for (float z = -0.8f * h; z <= 0.8f * h + 1e-4f; z += 0.4f * h)
            {
                if (slope * Mathf.Abs(x - z) < 0.3f)
                    continue;
                float low = slope * (Mathf.Min(x, z) + h), high = slope * (Mathf.Max(x, z) + h);
                Assert.IsTrue(OnPiece(corner, new Vector3(x, outer ? low : high, z)), $"{name}: roof at ({x}, {z})");
                Assert.IsFalse(OnPiece(corner, new Vector3(x, outer ? high : low, z)), $"{name}: no roof at the other slope ({x}, {z})");
            }
        }

        [Test]
        public void FourOuterCorners_MakeAPyramidRoof()
        {
            // A 6 x 6 m room of 3 m walls, roofed by four hip corners rising to a peak in the middle.
            foreach (float x in new[] { H, G + H })
            {
                Place(Wall3, BuildMaterial.Wood, new Vector3(x, 0, 0));
                Place(Wall3, BuildMaterial.Wood, new Vector3(x, 0, 2 * G));
            }
            foreach (float z in new[] { H, G + H })
            {
                Place(Wall3, BuildMaterial.Wood, new Vector3(0, 0, z), 90);
                Place(Wall3, BuildMaterial.Wood, new Vector3(2 * G, 0, z), 90);
            }
            int corner = Type("Roof 45° outer corner 3x3");
            var ids = new[]
            {
                Place(corner, BuildMaterial.Wood, new Vector3(H, G, H)),
                Place(corner, BuildMaterial.Wood, new Vector3(G + H, G, H), -90),
                Place(corner, BuildMaterial.Wood, new Vector3(G + H, G, G + H), 180),
                Place(corner, BuildMaterial.Wood, new Vector3(H, G, G + H), 90),
            };
            foreach (int id in ids)
            {
                Assert.Greater(Support(id), 0f);
                // Just inside its own tile, below the peak at (G, 2G, G).
                PieceRecord r = sys.Get(id).record;
                Vector3 inward = new Vector3(r.position.x - G, 0, r.position.z - G).normalized * 0.1f;
                Vector3 nearPeak = new Vector3(G, 2 * G - 0.15f, G) + inward;
                Assert.IsTrue(OnPiece(PieceCatalog.All[corner], Quaternion.Inverse(r.rotation) * (nearPeak - r.position)), "reaches the peak");
            }
        }

        [Test]
        public void OuterCorner_SnapsOntoTheCornerOfTwoWalls()
        {
            Place(Wall3, BuildMaterial.Wood, new Vector3(H, 0, 0));
            Place(Wall3, BuildMaterial.Wood, new Vector3(0, 0, H), 90);
            SnapResult s = Snap(Type("Roof 45° outer corner 3x3"), new Vector3(0.15f, G, 0.1f), Vector3.up);
            Assert.IsTrue(s.snapped);
            AssertNear(new Vector3(H, G, H), s.position);
        }

        [Test]
        public void GableWall_SnapsOntoAWallTop()
        {
            Place(Wall3, BuildMaterial.Wood, Vector3.zero);
            int gable = Type("Gable wall 45° 3x3");
            SnapResult s = Snap(gable, new Vector3(0.3f, G, 0.05f), Vector3.up);
            Assert.IsTrue(s.snapped);
            AssertNear(new Vector3(0, G, 0), s.position);
            Assert.IsTrue(TryPlace(gable, BuildMaterial.Wood, s.position).Ok);
        }

        [Test]
        public void RoofCap_BridgesTwoRoofsAMetreApart()
        {
            // An odd-width roof: 1 m slopes from both sides leave a 1 m gap at the top for the cap.
            int roof = Type("Roof 45° 1x1"), cap = Type("Roof cap 45° 1x1");
            Place(roof, BuildMaterial.Wood, new Vector3(0, 0, 0.5f));
            Place(roof, BuildMaterial.Wood, new Vector3(0, 0, 2.5f), 180);
            SnapResult s = Snap(cap, new Vector3(0.1f, 1f, 1.05f), Vector3.up);
            Assert.IsTrue(s.snapped);
            AssertNear(new Vector3(0, 1f, 1.5f), s.position);
            int id = Place(cap, BuildMaterial.Wood, s.position);
            Assert.Greater(Support(id), 0f);
            // The ridge is half a metre up, and the two halves meet in a clean top edge.
            PieceDef def = PieceCatalog.All[cap];
            Assert.IsTrue(OnPiece(def, new Vector3(0, 0.5f + 0.13f, 0)));
            Assert.IsFalse(OnPiece(def, new Vector3(0, 0.5f + 0.16f, 0)));
        }

        [TestCase("Roof 45° 3x3", 45f)]
        [TestCase("Roof 45° 1x1", 45f)]
        [TestCase("Roof 27° 3x3", 26.57f)]
        [TestCase("Roof 27° 1x1", 26.57f)]
        [TestCase("Roof 63° 3x1", 63.43f)]
        [TestCase("Roof 63° 1x1", 63.43f)]
        public void Roofs_HaveTheirPitch(string name, float degrees)
        {
            PieceDef roof = PieceCatalog.All[Type(name)];
            float rise = roof.snapPoints.Max(p => p.y);
            float run = roof.snapPoints.Max(p => p.z) - roof.snapPoints.Min(p => p.z);
            Assert.AreEqual(degrees, Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0.01f);
        }

        [TestCase("45°", 0.5f)]
        [TestCase("27°", 0.25f)]
        [TestCase("63°", 1f)]
        public void RoofCap_RidgeIsCleanAndTheGableCapFillsUnderIt(string pitch, float rise)
        {
            // The ridge cap's halves meet at the ridge without poking out above either top face.
            PieceDef cap = PieceCatalog.All[Type($"Roof cap {pitch} 1x1")];
            float slope = rise / 0.5f;
            float top = 0.1f * Mathf.Sqrt(1f + slope * slope);  // half a slab, measured vertically
            Assert.IsTrue(OnPiece(cap, new Vector3(0, rise, 0)), "solid at the ridge");
            for (float z = -0.45f; z <= 0.45f; z += 0.05f)
            for (float x = -0.4f; x <= 0.4f; x += 0.4f)
                Assert.IsFalse(OnPiece(cap, new Vector3(x, rise + top - slope * Mathf.Abs(z) + 0.01f, z)), $"pokes out at z {z}");

            // The gable cap is the triangle under it: up to the cap's centre plane, like the gable walls.
            string riseName = rise.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            PieceDef gable = PieceCatalog.All[Type($"Gable cap {pitch} 1x{riseName}")];
            for (float x = -0.4f; x <= 0.4f; x += 0.1f)
            {
                float y = rise - slope * Mathf.Abs(x);
                Assert.IsTrue(OnPiece(gable, new Vector3(x, y - 0.03f, 0)), $"inside at {x}");
                Assert.IsFalse(OnPiece(gable, new Vector3(x, y + 0.03f, 0)), $"outside at {x}");
            }
        }

        [Test]
        public void Showcase_UsesEveryPieceInEveryMaterialAndBuilds()
        {
            List<BuildShowcase.Placement> plan = BuildShowcase.Plan(BuildShowcase.DefaultFarm, BuildShowcase.DefaultBarn, BuildShowcase.DefaultTower);
            foreach (BuildShowcase.Placement p in plan)
            {
                BuildResult r = BuildShowcase.Execute(sys, p, time);
                Assert.IsTrue(r.Ok, $"{p}: {r.failure}");
            }
            var used = new HashSet<(string, BuildMaterial)>(plan.Select(p => (p.name, p.material)));
            var missing = PieceCatalog.All.Where(d => !d.VehicleOnly).SelectMany(d => d.Materials.Select(m => (d.name, m))).Where(x => !used.Contains(x)).ToList();
            Assert.IsEmpty(missing, "not in the showcase: " + string.Join(", ", missing));
        }

        [Test]
        public void Ladders_AreWoodAndClimbable()
        {
            foreach (string name in new[] { "Ladder 3 m", "Ladder 1 m" })
            {
                PieceDef ladder = PieceCatalog.All[Type(name)];
                CollectionAssert.AreEqual(new[] { BuildMaterial.Wood }, ladder.Materials, name);
                Assert.AreEqual(ShooterMashup.Player.SurfaceFlags.Ladder, ladder.Surface, name);
            }
        }

        [Test]
        public void AllSnapPoints_AreOnTheGrid()
        {
            // Any two snap points of a piece are a whole number of metres apart across (and half
            // metres in height: stone slabs are 0.5 m thick), so snapping can never take a
            // structure off the grid.
            foreach (PieceDef def in PieceCatalog.All)
            foreach (Vector3 a in def.snapPoints)
            foreach (Vector3 b in def.snapPoints)
                for (int axis = 0; axis < 3; axis++)
                {
                    float step = axis == 1 ? 0.5f : PieceCatalog.Step;
                    float steps = (a[axis] - b[axis]) / step;
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
                PostWithBeams(BuildMaterial.Wood, 3, out int post);
                Place(Pillar3, BuildMaterial.Stone, new Vector3(9, 0, 0));
                Place(Wall3, BuildMaterial.Wood, new Vector3(3, 0, 3), 45f);
                Remove(post);
                Undo();
                return string.Join("|", sys.Pieces.Values.OrderBy(p => p.Id)
                    .Select(p => $"{p.Id}:{p.record.type}:{p.record.position}:{p.support:R}:{p.grounded}"));
            }

            Assert.AreEqual(Run(), Run());
        }
    }
}
