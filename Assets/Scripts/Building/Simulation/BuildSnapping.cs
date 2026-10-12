using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Building
{
    public struct SnapResult
    {
        public bool snapped;
        public Vector3 position;
        public Quaternion rotation;
        public int heldIndex;   // which of the held piece's snap points attached
        public int targetId;    // the piece it attached to
        public Vector3 point;   // where, in the world
    }

    /// <summary>
    /// Snapping. You aim at a snap point on a placed piece: the one closest to the aim point
    /// (within the radius) is where the held piece attaches. Which of the held piece's own snap
    /// points goes there is either forced (Q/E in game) or picked automatically:
    /// <list type="number">
    /// <item>never a placement that sinks into an existing piece (more than overlapLimit of
    ///   either one's volume; small overlaps like a floor flush on a wall top are fine; the
    ///   build tool allows much more when you pick the snap point yourself),</item>
    /// <item>with preferAimedFace: placements on the face you aim at (out in front of it) first,
    ///   Minecraft style, so aiming at a slab's top stacks on it and at its side goes beside,</item>
    /// <item>then the best fit: the most snap points lining up with the target, so pieces sit
    ///   neatly side by side rather than half offset,</item>
    /// <item>then the one closest to a point just off the aimed surface, so a wall put on a
    ///   floor's edge stands on it rather than hanging below.</item>
    /// </list>
    /// If every option at the nearest target point overlaps, the next nearest point is tried.
    /// The held piece keeps the rotation you gave it (in world space), whatever the orientation of
    /// the piece it snaps to. Copy a piece (Shift + right click in game) to match an angled one.
    /// </summary>
    public static class BuildSnapping
    {
        /// <summary>Max fraction of a piece's volume allowed inside another.</summary>
        public const float DefaultOverlapLimit = 0.2f;

        const float SamePoint = 0.02f;      // snap points closer than this coincide
        const float SurfaceOffset = 0.5f;   // how far off the aimed surface the tie-break point is
        const float InFrontMargin = 0.05f;  // a piece's centre must be this far out in front of the aimed face
        const int Samples = 4;              // per box axis, for overlap estimates

        struct TargetPoint
        {
            public int pieceId;
            public Vector3 point;
            public float distance;
        }

        static readonly List<int> Nearby = new List<int>();
        static readonly List<TargetPoint> Targets = new List<TargetPoint>();
        static readonly List<Piece> Neighbours = new List<Piece>();

        public static SnapResult Snap(BuildSystem system, PieceDef def, Vector3 aim, Vector3 surfaceNormal,
            Quaternion userRotation, int heldIndex, float radius, float overlapLimit = DefaultOverlapLimit,
            bool preferAimedFace = false)
        {
            var none = new SnapResult { heldIndex = -1, targetId = -1 };
            if (def.snapPoints.Length == 0)
                return none;

            float pieceReach = 0f;
            foreach (Vector3 s in def.snapPoints)
                pieceReach = Mathf.Max(pieceReach, s.magnitude);
            foreach (Obb b in def.boxes)
                pieceReach = Mathf.Max(pieceReach, b.center.magnitude + b.halfExtents.magnitude);

            // Pieces that could be snapped to or collided with.
            system.QueryPieces(new Bounds(aim, Vector3.one * 2f * (radius + 2f * pieceReach)), Nearby);
            Neighbours.Clear();
            Targets.Clear();
            foreach (int id in Nearby)
            {
                Piece p = system.Get(id);
                Neighbours.Add(p);
                foreach (Vector3 s in p.def.snapPoints)
                {
                    Vector3 w = p.record.position + p.record.rotation * s;
                    float d = Vector3.Distance(aim, w);
                    if (d <= radius)
                        Targets.Add(new TargetPoint { pieceId = id, point = w, distance = d });
                }
            }
            if (Targets.Count == 0)
                return none;
            Targets.Sort((a, b) => a.distance != b.distance ? a.distance.CompareTo(b.distance) : a.pieceId.CompareTo(b.pieceId));

            Vector3 reference = aim + surfaceNormal * SurfaceOffset;
            int first = heldIndex >= 0 ? Mathf.Min(heldIndex, def.snapPoints.Length - 1) : 0;
            int last = heldIndex >= 0 ? first : def.snapPoints.Length - 1;
            var boxes = new Obb[def.boxes.Length];

            // Nearest target point first; points at the same spot (shared by neighbouring pieces) together.
            for (int start = 0; start < Targets.Count;)
            {
                int end = start + 1;
                while (end < Targets.Count && Vector3.Distance(Targets[end].point, Targets[start].point) < SamePoint)
                    end++;

                SnapResult best = none;
                int bestFace = -1, bestFit = -1;
                float bestDistance = float.MaxValue;
                for (int t = start; t < end; t++)
                {
                    Piece target = system.Get(Targets[t].pieceId);
                    Quaternion rotation = userRotation; // world orientation, whatever the target's
                    for (int h = first; h <= last; h++)
                    {
                        Vector3 position = Targets[t].point - rotation * def.snapPoints[h];
                        for (int i = 0; i < def.boxes.Length; i++)
                            boxes[i] = def.boxes[i].Transformed(position, rotation);
                        if (SinksIntoNeighbours(boxes, overlapLimit))
                            continue;

                        Vector3 center = BoxesCenter(boxes);
                        // Aimed-face rule: placements that sit on the face you aim at (out in front of
                        // it) beat any that don't, before the fit is even compared.
                        int face = preferAimedFace && Vector3.Dot(center - aim, surfaceNormal) > InFrontMargin ? 1 : 0;
                        int fit = Coinciding(def, position, rotation, target, !preferAimedFace);
                        float distance = Vector3.Distance(center, reference);
                        if (face < bestFace || (face == bestFace && (fit < bestFit || (fit == bestFit && distance >= bestDistance))))
                            continue;
                        bestFace = face;
                        bestFit = fit;
                        bestDistance = distance;
                        best = new SnapResult
                        {
                            snapped = true,
                            position = position,
                            rotation = rotation,
                            heldIndex = h,
                            targetId = target.Id,
                            point = Targets[t].point,
                        };
                    }
                }

                if (best.snapped)
                    return best;
                start = end;
            }
            return none;
        }

        /// <summary>How many of the held piece's snap points land on one of the target's.</summary>
        static int Coinciding(PieceDef def, Vector3 position, Quaternion rotation, Piece target, bool matchFaces)
        {
            int count = 0;
            for (int i = 0; i < def.snapPoints.Length; i++)
            {
                Vector3 w = position + rotation * def.snapPoints[i];
                for (int j = 0; j < target.def.snapPoints.Length; j++)
                {
                    // Without the aimed-face rule, only top on top or bottom on bottom counts: a slab
                    // stacked on another lines up a whole face of points, but that's not what aiming
                    // near its edge usually means. With it, the face decides on top vs beside, and
                    // then every point counts so a stack lines up neatly.
                    if (matchFaces && !SameFace(def.snapFaces[i], target.def.snapFaces[j]))
                        continue;
                    if ((target.record.position + target.record.rotation * target.def.snapPoints[j] - w).sqrMagnitude < SamePoint * SamePoint)
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        static bool SameFace(SnapFace a, SnapFace b) => a == SnapFace.Any || b == SnapFace.Any || a == b;

        static bool SinksIntoNeighbours(Obb[] boxes, float limit)
        {
            Bounds bounds = boxes[0].Bounds;
            for (int i = 1; i < boxes.Length; i++)
                bounds.Encapsulate(boxes[i].Bounds);

            foreach (Piece n in Neighbours)
            {
                if (!n.bounds.Intersects(bounds))
                    continue;
                if (FractionInside(boxes, n.boxes) > limit || FractionInside(n.boxes, boxes) > limit)
                    return true;
            }
            return false;
        }

        /// <summary>Roughly what fraction of <paramref name="a"/>'s volume lies inside <paramref name="b"/>, by sampling.</summary>
        static float FractionInside(Obb[] a, Obb[] b)
        {
            float total = 0f, inside = 0f;
            foreach (Obb box in a)
            {
                Vector3 size = box.Size;
                float volume = size.x * size.y * size.z;
                total += volume;
                float perSample = volume / (Samples * Samples * Samples);
                for (int x = 0; x < Samples; x++)
                for (int y = 0; y < Samples; y++)
                for (int z = 0; z < Samples; z++)
                {
                    var local = new Vector3(
                        ((x + 0.5f) / Samples - 0.5f) * size.x,
                        ((y + 0.5f) / Samples - 0.5f) * size.y,
                        ((z + 0.5f) / Samples - 0.5f) * size.z);
                    Vector3 p = box.center + box.rotation * local;
                    foreach (Obb other in b)
                    {
                        if (Contains(other, p))
                        {
                            inside += perSample;
                            break;
                        }
                    }
                }
            }
            return total > 0f ? inside / total : 0f;
        }

        static bool Contains(in Obb box, Vector3 p)
        {
            Vector3 local = Quaternion.Inverse(box.rotation) * (p - box.center);
            return Mathf.Abs(local.x) <= box.halfExtents.x && Mathf.Abs(local.y) <= box.halfExtents.y && Mathf.Abs(local.z) <= box.halfExtents.z;
        }

        static Vector3 BoxesCenter(Obb[] boxes)
        {
            Bounds b = boxes[0].Bounds;
            for (int i = 1; i < boxes.Length; i++)
                b.Encapsulate(boxes[i].Bounds);
            return b.center;
        }
    }
}
