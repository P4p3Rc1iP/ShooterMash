using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>A placed piece and its derived state (geometry, connections, support).</summary>
    public sealed class Piece
    {
        public PieceRecord record;
        public PieceDef def;
        public Obb[] boxes;      // world space
        public Bounds bounds;
        public bool grounded;
        public float support;
        public int revision;     // world revision when it was placed
        internal readonly List<Link> links = new List<Link>();

        public int Id => record.id;
        public IReadOnlyList<Link> Links => links;
    }

    /// <summary>
    /// A connection between two touching pieces. Verticality is 1 when a piece sits straight
    /// above (or hangs straight below) the contact, 0 when it sticks out sideways.
    /// </summary>
    public readonly struct Link
    {
        public readonly int other;
        public readonly float verticality;      // of this piece
        public readonly float otherVerticality; // of the other piece

        public Link(int other, float verticality, float otherVerticality)
        {
            this.other = other;
            this.verticality = verticality;
            this.otherVerticality = otherVerticality;
        }
    }

    /// <summary>
    /// All placed pieces and the rules for changing them. Deterministic and free of GameObjects,
    /// like PlayerMove: the server runs Execute on BuildCmds, and views listen to Changed.
    /// Split into partial files: this one (state, place, remove, terrain), Integrity and Undo.
    /// </summary>
    public sealed partial class BuildSystem
    {
        const float GridCell = 4f;

        readonly BuildConfig cfg;
        readonly IBuildWorld world;
        readonly Dictionary<int, Piece> pieces = new Dictionary<int, Piece>();
        readonly SpatialHash grid = new SpatialHash(GridCell);
        readonly List<int> query = new List<int>();
        int nextId = 1;
        int revision;

        /// <summary>Raised after anything changed: pieces added, removed or collapsed, or support values.</summary>
        public event Action<BuildResult> Changed;

        public BuildSystem(BuildConfig cfg, IBuildWorld world)
        {
            this.cfg = cfg;
            this.world = world;
        }

        public BuildConfig Config => cfg;
        public IReadOnlyDictionary<int, Piece> Pieces => pieces;
        public int Revision => revision;

        public Piece Get(int id) => pieces.TryGetValue(id, out Piece p) ? p : null;
        public MaterialStats StatsOf(Piece p) => cfg.Stats(p.record.material);

        public BuildResult Execute(in BuildCmd cmd)
        {
            BuildResult result = cmd.action switch
            {
                BuildAction.Place => Place(cmd),
                BuildAction.Remove => Remove(cmd),
                BuildAction.Undo => Undo(cmd),
                BuildAction.Redo => Redo(cmd),
                _ => BuildResult.Fail(BuildFailure.UnknownPiece),
            };
            if (result.Changed)
                Changed?.Invoke(result);
            return result;
        }

        /// <summary>Would this placement be accepted? Used for the ghost preview.</summary>
        public PlacementCheck CheckPlace(int type, BuildMaterial material, Vector3 position, Quaternion rotation)
        {
            PieceDef def = PieceCatalog.Get(type);
            if (def == null)
                return new PlacementCheck { failure = BuildFailure.UnknownPiece };
            Piece p = Create(new PieceRecord { id = 0, type = type, material = material, position = position, rotation = rotation });
            return Check(p);
        }

        /// <summary>Pieces whose bounds overlap the region, sorted by id.</summary>
        public void QueryPieces(Bounds region, List<int> results)
        {
            grid.Query(region, results);
            results.RemoveAll(id => !pieces[id].bounds.Intersects(region));
        }

        /// <summary>
        /// The terrain changed inside this region (dug out, filled in). Re-checks which pieces
        /// rest on it and collapses whatever lost its support.
        /// </summary>
        public BuildResult TerrainChanged(Bounds region)
        {
            region.Expand(cfg.groundTolerance * 2f);
            grid.Query(region, query);
            var seeds = new List<int>();
            foreach (int id in query)
            {
                Piece p = pieces[id];
                if (!p.bounds.Intersects(region))
                    continue;
                bool grounded = world.IsGrounded(p.boxes, cfg.groundTolerance);
                if (grounded != p.grounded)
                {
                    p.grounded = grounded;
                    seeds.Add(id);
                }
            }

            var result = new BuildResult();
            if (seeds.Count == 0)
                return result;

            BeginChange();
            Collapse(seeds, result, null);
            Log(TerrainOwner, region);
            Changed?.Invoke(result);
            return result;
        }

        /// <summary>Recompute everything, e.g. after material values were edited.</summary>
        public BuildResult RecomputeAll()
        {
            var result = new BuildResult();
            foreach (Piece p in pieces.Values)
                p.grounded = world.IsGrounded(p.boxes, cfg.groundTolerance);
            var seeds = new List<int>(pieces.Keys);
            seeds.Sort();
            BeginChange();
            Collapse(seeds, result, null);
            Changed?.Invoke(result);
            return result;
        }

        // ------------------------------------------------------------------ commands

        BuildResult Place(in BuildCmd cmd)
        {
            if (PieceCatalog.Get(cmd.pieceType) == null)
                return BuildResult.Fail(BuildFailure.UnknownPiece);

            Piece p = Create(new PieceRecord
            {
                id = nextId,
                type = cmd.pieceType,
                material = cmd.material,
                position = cmd.position,
                rotation = cmd.rotation,
                owner = cmd.owner,
            });
            PlacementCheck check = Check(p);
            if (!check.Valid)
                return BuildResult.Fail(check.failure);

            nextId++;
            p.support = check.support;
            int rev = BeginChange();
            Insert(p, rev);

            // A new piece can only add support, so this raises its neighbours and breaks nothing.
            var result = new BuildResult();
            result.added.Add(p.Id);
            Collapse(new List<int> { p.Id }, result, null);

            Log(cmd.owner, p.bounds);
            RecordAction(cmd.owner, true, cmd.time, p.Id, new List<PieceRecord> { p.record });
            return result;
        }

        BuildResult Remove(in BuildCmd cmd)
        {
            if (!pieces.ContainsKey(cmd.targetId))
                return BuildResult.Fail(BuildFailure.NotFound);

            BeginChange();
            var result = new BuildResult();
            var records = new List<PieceRecord>();
            RemoveWithCollapse(cmd.targetId, result, records);

            Log(cmd.owner, UnionBounds(records));
            RecordAction(cmd.owner, false, cmd.time, cmd.targetId, records);
            return result;
        }

        // ------------------------------------------------------------------ helpers

        const int TerrainOwner = -1;

        int BeginChange() => ++revision;

        Piece Create(PieceRecord record)
        {
            PieceDef def = PieceCatalog.Get(record.type);
            var p = new Piece { record = record, def = def, boxes = new Obb[def.boxes.Length] };
            for (int i = 0; i < def.boxes.Length; i++)
            {
                p.boxes[i] = def.boxes[i].Transformed(record.position, record.rotation);
                Bounds b = p.boxes[i].Bounds;
                if (i == 0) p.bounds = b;
                else p.bounds.Encapsulate(b);
            }
            return p;
        }

        /// <summary>Fills in grounded, links and support for a piece that isn't in the world yet.</summary>
        PlacementCheck Check(Piece p)
        {
            if (world.IsBlocked(p.boxes))
                return new PlacementCheck { failure = BuildFailure.Blocked };

            p.grounded = world.IsGrounded(p.boxes, cfg.groundTolerance);
            FindLinks(p);
            float support = Evaluate(p);
            var check = new PlacementCheck { support = support, grounded = p.grounded };
            if (cfg.structuralIntegrity && support < StatsOf(p).minSupport)
                check.failure = BuildFailure.Unsupported;
            return check;
        }

        void FindLinks(Piece p)
        {
            p.links.Clear();
            Bounds region = p.bounds;
            region.Expand(cfg.connectTolerance * 2f);
            grid.Query(region, query);
            foreach (int id in query)
            {
                if (id == p.Id)
                    continue;
                Piece n = pieces[id];
                if (!n.bounds.Intersects(region) || !Touching(p, n))
                    continue;
                Verticality(p, n, out float vp, out float vn);
                p.links.Add(new Link(id, vp, vn));
            }
        }

        bool Touching(Piece a, Piece b)
        {
            foreach (Obb x in a.boxes)
            foreach (Obb y in b.boxes)
                if (Obb.Overlap(x, y, cfg.connectTolerance))
                    return true;
            return false;
        }

        /// <summary>
        /// How vertical each piece is relative to where they touch. The contact is approximated by
        /// the middle of the overlap of their (slightly grown) bounding boxes.
        /// </summary>
        void Verticality(Piece a, Piece b, out float va, out float vb)
        {
            Bounds ba = a.bounds, bb = b.bounds;
            ba.Expand(cfg.connectTolerance * 2f);
            bb.Expand(cfg.connectTolerance * 2f);
            Vector3 contact = (Vector3.Max(ba.min, bb.min) + Vector3.Min(ba.max, bb.max)) * 0.5f;
            va = Vertical(a.bounds.center - contact);
            vb = Vertical(b.bounds.center - contact);
        }

        static float Vertical(Vector3 d) => d.sqrMagnitude < 1e-6f ? 1f : Mathf.Abs(d.normalized.y);

        void Insert(Piece p, int rev)
        {
            p.revision = rev;
            pieces[p.Id] = p;
            grid.Insert(p.Id, p.bounds);
            foreach (Link link in p.links)
                pieces[link.other].links.Add(new Link(p.Id, link.otherVerticality, link.verticality));
        }

        void Detach(int id)
        {
            Piece p = pieces[id];
            foreach (Link link in p.links)
                if (pieces.TryGetValue(link.other, out Piece n))
                    n.links.RemoveAll(l => l.other == id);
            grid.Remove(id, p.bounds);
            pieces.Remove(id);
        }

        void RemoveWithCollapse(int id, BuildResult result, List<PieceRecord> records)
        {
            Piece p = pieces[id];
            var neighbours = new List<int>(p.links.Count);
            foreach (Link link in p.links)
                neighbours.Add(link.other);

            records?.Add(p.record);
            Detach(id);
            result.removed.Add(id);
            Collapse(neighbours, result, records);
        }

        /// <summary>Re-solves support around the seeds and removes every piece that broke.</summary>
        void Collapse(List<int> seeds, BuildResult result, List<PieceRecord> records)
        {
            var broken = new List<int>();
            Solve(seeds, -1, true, broken);
            foreach (int id in broken)
            {
                records?.Add(pieces[id].record);
                Detach(id);
                result.collapsed.Add(id);
            }
        }

        Bounds UnionBounds(List<PieceRecord> records)
        {
            var bounds = new Bounds();
            for (int i = 0; i < records.Count; i++)
            {
                Bounds b = Create(records[i]).bounds;
                if (i == 0) bounds = b;
                else bounds.Encapsulate(b);
            }
            return bounds;
        }
    }
}
