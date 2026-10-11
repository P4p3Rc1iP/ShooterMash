using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// Per-player undo and redo of their own build actions. Undoing a removal restores the piece
    /// and everything that collapsed with it. An action can only be undone within
    /// BuildConfig.undoWindow, and not once anyone else (another player, or the terrain) has
    /// changed the structure near it since.
    /// </summary>
    public sealed partial class BuildSystem
    {
        const int MaxLog = 1024;

        sealed class UndoEntry
        {
            public bool placed;          // false: a removal
            public int owner;
            public double time;
            public int revision;
            public int targetId;
            public List<PieceRecord> pieces; // placed: the piece. Removed: the target, then whatever collapsed.
            public Bounds bounds;
        }

        sealed class History
        {
            public readonly List<UndoEntry> undo = new List<UndoEntry>();
            public readonly List<UndoEntry> redo = new List<UndoEntry>();
        }

        struct LogEntry
        {
            public int revision;
            public int owner;
            public Bounds bounds;
        }

        readonly Dictionary<int, History> histories = new Dictionary<int, History>();
        readonly List<LogEntry> log = new List<LogEntry>();
        readonly List<int> undoScratch = new List<int>();
        int forgottenUpTo; // log entries up to this revision have been dropped

        public bool CanUndo(int owner) => cfg.undo && histories.TryGetValue(owner, out History h) && h.undo.Count > 0;
        public bool CanRedo(int owner) => cfg.undo && histories.TryGetValue(owner, out History h) && h.redo.Count > 0;

        void Log(int owner, Bounds bounds)
        {
            log.Add(new LogEntry { revision = revision, owner = owner, bounds = bounds });
            if (log.Count > MaxLog)
            {
                forgottenUpTo = log[0].revision;
                log.RemoveAt(0);
            }
        }

        void RecordAction(int owner, bool placed, double time, int targetId, List<PieceRecord> records)
        {
            if (!cfg.undo)
                return;
            History h = HistoryOf(owner);
            h.redo.Clear();
            h.undo.Add(new UndoEntry
            {
                placed = placed,
                owner = owner,
                time = time,
                revision = revision,
                targetId = targetId,
                pieces = records,
                bounds = UnionBounds(records),
            });
            if (h.undo.Count > cfg.undoDepth)
                h.undo.RemoveAt(0);
        }

        History HistoryOf(int owner)
        {
            if (!histories.TryGetValue(owner, out History h))
                histories[owner] = h = new History();
            return h;
        }

        /// <summary>Has anyone but the entry's owner changed things near it since?</summary>
        bool ChangedByOthers(UndoEntry e)
        {
            if (e.revision < forgottenUpTo)
                return true;
            Bounds region = e.bounds;
            region.Expand(1f);
            foreach (LogEntry c in log)
                if (c.revision > e.revision && c.owner != e.owner && c.bounds.Intersects(region))
                    return true;
            return false;
        }

        BuildResult Undo(in BuildCmd cmd)
        {
            if (!cfg.undo)
                return BuildResult.Fail(BuildFailure.Disabled);
            History h = HistoryOf(cmd.owner);
            if (h.undo.Count == 0)
                return BuildResult.Fail(BuildFailure.NothingToUndo);

            UndoEntry e = h.undo[h.undo.Count - 1];
            if (cmd.time - e.time > cfg.undoWindow)
            {
                h.undo.Clear(); // everything below it is older still
                return BuildResult.Fail(BuildFailure.UndoExpired);
            }
            if (ChangedByOthers(e))
                return BuildResult.Fail(BuildFailure.StructureChanged);

            var result = new BuildResult();
            if (e.placed)
            {
                int id = e.pieces[0].id;
                if (!pieces.ContainsKey(id))
                    return BuildResult.Fail(BuildFailure.StructureChanged);
                // Pieces built on it since (and no longer undoable) would fall: refuse instead.
                PreviewRemove(id, undoScratch);
                if (undoScratch.Count > 0)
                    return BuildResult.Fail(BuildFailure.StructureChanged);

                BeginChange();
                RemoveWithCollapse(id, result, null);
            }
            else
            {
                var restored = new List<Piece>(e.pieces.Count);
                foreach (PieceRecord record in e.pieces)
                {
                    if (pieces.ContainsKey(record.id))
                        return BuildResult.Fail(BuildFailure.StructureChanged);
                    Piece p = Create(record);
                    if (world.IsBlocked(p.boxes))
                        return BuildResult.Fail(BuildFailure.Blocked);
                    restored.Add(p);
                }

                int rev = BeginChange();
                var seeds = new List<int>(restored.Count);
                foreach (Piece p in restored)
                {
                    p.grounded = world.IsGrounded(p.boxes, cfg.groundTolerance);
                    FindLinks(p);
                    Insert(p, rev);
                    seeds.Add(p.Id);
                    result.added.Add(p.Id);
                }
                Collapse(seeds, result, null);
            }

            e.revision = revision;
            e.time = cmd.time;
            h.undo.RemoveAt(h.undo.Count - 1);
            h.redo.Add(e);
            Log(cmd.owner, e.bounds);
            return result;
        }

        BuildResult Redo(in BuildCmd cmd)
        {
            if (!cfg.undo)
                return BuildResult.Fail(BuildFailure.Disabled);
            History h = HistoryOf(cmd.owner);
            if (h.redo.Count == 0)
                return BuildResult.Fail(BuildFailure.NothingToRedo);

            UndoEntry e = h.redo[h.redo.Count - 1];
            if (cmd.time - e.time > cfg.undoWindow)
            {
                h.redo.Clear();
                return BuildResult.Fail(BuildFailure.UndoExpired);
            }
            if (ChangedByOthers(e))
                return BuildResult.Fail(BuildFailure.StructureChanged);

            var result = new BuildResult();
            if (e.placed)
            {
                Piece p = Create(e.pieces[0]);
                if (pieces.ContainsKey(p.Id))
                    return BuildResult.Fail(BuildFailure.StructureChanged);
                PlacementCheck check = Check(p);
                if (!check.Valid)
                    return BuildResult.Fail(check.failure);

                p.support = check.support;
                Insert(p, BeginChange());
                result.added.Add(p.Id);
                Collapse(new List<int> { p.Id }, result, null);
            }
            else
            {
                if (!pieces.ContainsKey(e.targetId))
                    return BuildResult.Fail(BuildFailure.StructureChanged);
                BeginChange();
                var records = new List<PieceRecord>();
                RemoveWithCollapse(e.targetId, result, records);
                e.pieces = records;
                e.bounds = UnionBounds(records);
            }

            e.revision = revision;
            e.time = cmd.time;
            h.redo.RemoveAt(h.redo.Count - 1);
            h.undo.Add(e);
            Log(cmd.owner, e.bounds);
            return result;
        }
    }
}
