using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// Structural integrity, after Valheim's WearNTear but solved exactly instead of iteratively.
    ///
    /// Grounded pieces have their material's maxSupport. Support flows through connections:
    /// a piece fed by a neighbour with support s gets min(s, its max) - its max * loss, where the
    /// loss blends from horizontal to vertical by how vertical the connection is. A piece keeps the
    /// best support any path gives it and breaks below minSupport. Broken pieces pass nothing on.
    ///
    /// Support never grows along a path, so this is a shortest-path problem: a Dijkstra-style
    /// pass from the grounded pieces, highest support first. It only visits the structures
    /// connected to whatever changed, so removing a pillar collapses everything at once.
    /// </summary>
    public sealed partial class BuildSystem
    {
        readonly HashSet<int> component = new HashSet<int>();
        readonly List<int> componentList = new List<int>();
        readonly Stack<int> stack = new Stack<int>();
        readonly Dictionary<int, float> best = new Dictionary<int, float>();
        readonly HashSet<int> done = new HashSet<int>();
        readonly SortedSet<(float support, int id)> open = new SortedSet<(float support, int id)>(HighestFirst.Instance);
        readonly List<int> previewSeeds = new List<int>();

        /// <summary>The pieces that would collapse if this one were removed (not including itself).</summary>
        public void PreviewRemove(int id, List<int> wouldCollapse)
        {
            wouldCollapse.Clear();
            if (!pieces.TryGetValue(id, out Piece p))
                return;
            previewSeeds.Clear();
            foreach (Link link in p.links)
                previewSeeds.Add(link.other);
            Solve(previewSeeds, id, false, wouldCollapse);
        }

        /// <summary>Support lost over one connection: the material's loss rate, blended by direction, per lossLength metres of the piece.</summary>
        float Loss(in MaterialStats stats, float verticality, float length) =>
            stats.maxSupport * Mathf.Lerp(stats.horizontalLoss, stats.verticalLoss, verticality) * length / cfg.lossLength;

        /// <summary>Support a piece not yet in the world would get from its links.</summary>
        float Evaluate(Piece p)
        {
            MaterialStats stats = StatsOf(p);
            if (!cfg.structuralIntegrity || p.grounded)
                return stats.maxSupport;

            float result = float.NegativeInfinity;
            foreach (Link link in p.links)
            {
                Piece n = pieces[link.other];
                float s = Mathf.Min(stats.maxSupport, n.support) - Loss(stats, link.verticality, link.length);
                if (s > result)
                    result = s;
            }
            return result;
        }

        /// <summary>
        /// Solves support for every piece connected to the seeds, ignoring <paramref name="excluded"/>.
        /// Pieces that would break go into <paramref name="broken"/>. With commit, new support
        /// values are stored; broken pieces are left for the caller to remove.
        /// </summary>
        void Solve(List<int> seeds, int excluded, bool commit, List<int> broken)
        {
            broken.Clear();

            // Everything connected to the seeds.
            component.Clear();
            componentList.Clear();
            stack.Clear();
            foreach (int s in seeds)
                if (s != excluded && pieces.ContainsKey(s) && component.Add(s))
                    stack.Push(s);
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                componentList.Add(id);
                foreach (Link link in pieces[id].links)
                    if (link.other != excluded && component.Add(link.other))
                        stack.Push(link.other);
            }
            componentList.Sort();

            if (!cfg.structuralIntegrity)
            {
                if (commit)
                    foreach (int id in componentList)
                        pieces[id].support = StatsOf(pieces[id]).maxSupport;
                return;
            }

            best.Clear();
            done.Clear();
            open.Clear();
            foreach (int id in componentList)
            {
                Piece p = pieces[id];
                if (!p.grounded)
                    continue;
                float s = StatsOf(p).maxSupport;
                best[id] = s;
                open.Add((s, id));
            }

            while (open.Count > 0)
            {
                (float support, int id) top = open.Min;
                open.Remove(top);
                done.Add(top.id);

                Piece p = pieces[top.id];
                if (top.support < StatsOf(p).minSupport)
                {
                    broken.Add(top.id);
                    continue;
                }

                foreach (Link link in p.links)
                {
                    if (link.other == excluded || done.Contains(link.other))
                        continue;
                    MaterialStats ns = StatsOf(pieces[link.other]);
                    float candidate = Mathf.Min(ns.maxSupport, top.support) - Loss(ns, link.otherVerticality, link.otherLength);
                    if (best.TryGetValue(link.other, out float old))
                    {
                        if (candidate <= old)
                            continue;
                        open.Remove((old, link.other));
                    }
                    best[link.other] = candidate;
                    open.Add((candidate, link.other));
                }
            }

            // Not connected to the ground at all.
            foreach (int id in componentList)
                if (!done.Contains(id))
                    broken.Add(id);
            broken.Sort();

            if (commit)
                foreach (int id in componentList)
                    if (best.TryGetValue(id, out float s))
                        pieces[id].support = s;
        }

        sealed class HighestFirst : IComparer<(float support, int id)>
        {
            public static readonly HighestFirst Instance = new HighestFirst();

            public int Compare((float support, int id) a, (float support, int id) b)
            {
                int c = b.support.CompareTo(a.support);
                return c != 0 ? c : a.id.CompareTo(b.id);
            }
        }
    }
}
