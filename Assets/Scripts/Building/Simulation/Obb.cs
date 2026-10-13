using System;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// An oriented box, or a wedge: a box cut in half diagonally. Pieces are made of these, in
    /// local space in their definition.
    /// </summary>
    [Serializable]
    public struct Obb
    {
        public Vector3 center;
        public Quaternion rotation;
        public Vector3 halfExtents;
        /// <summary>
        /// Zero for a whole box. For a wedge, two components are ±1 and one is 0: the box is cut by
        /// the plane through two opposite edges along the zero axis, and the half where
        /// dot(cut, local / halfExtents) &lt;= 0 is kept. E.g. (-1, 1, 0) keeps the part below the
        /// diagonal from the (-x, -y) edge to the (+x, +y) edge.
        /// </summary>
        public Vector3 cut;

        public Obb(Vector3 center, Vector3 size, Quaternion rotation)
        {
            this.center = center;
            this.rotation = rotation;
            halfExtents = size * 0.5f;
            cut = Vector3.zero;
        }

        public Obb(Vector3 center, Vector3 size) : this(center, size, Quaternion.identity) { }

        /// <summary>A wedge: this box, keeping only the half given by <paramref name="cut"/> (see <see cref="cut"/>).</summary>
        public static Obb Wedge(Vector3 center, Vector3 size, Quaternion rotation, Vector3 cut) =>
            new Obb(center, size, rotation) { cut = cut };

        public Vector3 Size => halfExtents * 2f;
        public bool IsWedge => cut != Vector3.zero;
        public float Volume => Size.x * Size.y * Size.z * (IsWedge ? 0.5f : 1f);

        /// <summary>This box moved from piece space into the world.</summary>
        public Obb Transformed(Vector3 position, Quaternion rot) => new Obb
        {
            center = position + rot * center,
            rotation = rot * rotation,
            halfExtents = halfExtents,
            cut = cut,
        };

        public Bounds Bounds
        {
            get
            {
                if (IsWedge)
                {
                    Span<Vector3> v = stackalloc Vector3[8];
                    int n = Corners(v);
                    var b = new Bounds(v[0], Vector3.zero);
                    for (int i = 1; i < n; i++) b.Encapsulate(v[i]);
                    return b;
                }
                Vector3 x = rotation * Vector3.right;
                Vector3 y = rotation * Vector3.up;
                Vector3 z = rotation * Vector3.forward;
                Vector3 extents = Abs(x) * halfExtents.x + Abs(y) * halfExtents.y + Abs(z) * halfExtents.z;
                return new Bounds(center, extents * 2f);
            }
        }

        /// <summary>The cut plane's outward normal in local space (meaningful only for a wedge).</summary>
        public Vector3 LocalCutNormal =>
            new Vector3(cut.x / halfExtents.x, cut.y / halfExtents.y, cut.z / halfExtents.z).normalized;

        /// <summary>Whether a local-space point is on the kept side of the cut (always true for a box).</summary>
        public bool KeepsLocal(Vector3 local, float tolerance = 1e-4f) =>
            !IsWedge || cut.x * local.x / halfExtents.x + cut.y * local.y / halfExtents.y + cut.z * local.z / halfExtents.z <= tolerance;

        /// <summary>Whether a world point lies inside.</summary>
        public bool Contains(Vector3 p)
        {
            Vector3 local = Quaternion.Inverse(rotation) * (p - center);
            return Mathf.Abs(local.x) <= halfExtents.x && Mathf.Abs(local.y) <= halfExtents.y
                && Mathf.Abs(local.z) <= halfExtents.z && KeepsLocal(local);
        }

        /// <summary>Writes the corners (8 for a box, 6 for a wedge) in local space and returns how many.</summary>
        public int LocalCorners(Span<Vector3> corners)
        {
            int n = 0;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? -halfExtents.x : halfExtents.x,
                    (i & 2) == 0 ? -halfExtents.y : halfExtents.y,
                    (i & 4) == 0 ? -halfExtents.z : halfExtents.z);
                if (KeepsLocal(c))
                    corners[n++] = c;
            }
            return n;
        }

        /// <summary>Writes the corners in world space and returns how many.</summary>
        public int Corners(Span<Vector3> corners)
        {
            int n = LocalCorners(corners);
            for (int i = 0; i < n; i++)
                corners[i] = center + rotation * corners[i];
            return n;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>
        /// Separating-axis test. Boxes closer than <paramref name="tolerance"/> count as overlapping,
        /// so pieces that are snapped face to face are seen as touching.
        /// </summary>
        public static bool Overlap(in Obb a, in Obb b, float tolerance)
        {
            if (a.IsWedge || b.IsWedge)
                return OverlapConvex(a, b, tolerance);

            Vector3 ea = a.halfExtents + Vector3.one * (tolerance * 0.5f);
            Vector3 eb = b.halfExtents + Vector3.one * (tolerance * 0.5f);

            Span<Vector3> axA = stackalloc Vector3[3];
            Span<Vector3> axB = stackalloc Vector3[3];
            axA[0] = a.rotation * Vector3.right; axA[1] = a.rotation * Vector3.up; axA[2] = a.rotation * Vector3.forward;
            axB[0] = b.rotation * Vector3.right; axB[1] = b.rotation * Vector3.up; axB[2] = b.rotation * Vector3.forward;

            // R expresses b's axes in a's frame. The epsilon guards near-parallel edge pairs.
            Span<float> r = stackalloc float[9];
            Span<float> absR = stackalloc float[9];
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                r[i * 3 + j] = Vector3.Dot(axA[i], axB[j]);
                absR[i * 3 + j] = Mathf.Abs(r[i * 3 + j]) + 1e-5f;
            }

            Vector3 d = b.center - a.center;
            Span<float> t = stackalloc float[3];
            t[0] = Vector3.Dot(d, axA[0]); t[1] = Vector3.Dot(d, axA[1]); t[2] = Vector3.Dot(d, axA[2]);

            for (int i = 0; i < 3; i++)
            {
                float rb = eb.x * absR[i * 3] + eb.y * absR[i * 3 + 1] + eb.z * absR[i * 3 + 2];
                if (Mathf.Abs(t[i]) > ea[i] + rb)
                    return false;
            }

            for (int j = 0; j < 3; j++)
            {
                float ra = ea.x * absR[j] + ea.y * absR[3 + j] + ea.z * absR[6 + j];
                float proj = t[0] * r[j] + t[1] * r[3 + j] + t[2] * r[6 + j];
                if (Mathf.Abs(proj) > ra + eb[j])
                    return false;
            }

            for (int i = 0; i < 3; i++)
            {
                int i1 = (i + 1) % 3, i2 = (i + 2) % 3;
                for (int j = 0; j < 3; j++)
                {
                    int j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                    float ra = ea[i1] * absR[i2 * 3 + j] + ea[i2] * absR[i1 * 3 + j];
                    float rb = eb[j1] * absR[i * 3 + j2] + eb[j2] * absR[i * 3 + j1];
                    float proj = t[i2] * r[i1 * 3 + j] - t[i1] * r[i2 * 3 + j];
                    if (Mathf.Abs(proj) > ra + rb)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The general separating-axis test for two convex shapes (used when either is a wedge):
        /// every face normal of both, and every cross product of their edge directions.
        /// </summary>
        static bool OverlapConvex(in Obb a, in Obb b, float tolerance)
        {
            Span<Vector3> va = stackalloc Vector3[8];
            Span<Vector3> vb = stackalloc Vector3[8];
            int na = a.Corners(va), nb = b.Corners(vb);

            // Face normals and edge directions: the three box axes, plus for a wedge the cut plane's
            // normal and its diagonal edge.
            Span<Vector3> fa = stackalloc Vector3[4];
            Span<Vector3> fb = stackalloc Vector3[4];
            Span<Vector3> ea = stackalloc Vector3[4];
            Span<Vector3> eb = stackalloc Vector3[4];
            int ca = Directions(a, fa, ea), cb = Directions(b, fb, eb);

            for (int i = 0; i < ca; i++)
                if (Separated(fa[i], va, na, vb, nb, tolerance)) return false;
            for (int i = 0; i < cb; i++)
                if (Separated(fb[i], va, na, vb, nb, tolerance)) return false;
            for (int i = 0; i < ca; i++)
            for (int j = 0; j < cb; j++)
            {
                Vector3 axis = Vector3.Cross(ea[i], eb[j]);
                if (axis.sqrMagnitude < 1e-8f)
                    continue;
                if (Separated(axis.normalized, va, na, vb, nb, tolerance)) return false;
            }
            return true;
        }

        static int Directions(in Obb o, Span<Vector3> faces, Span<Vector3> edges)
        {
            faces[0] = edges[0] = o.rotation * Vector3.right;
            faces[1] = edges[1] = o.rotation * Vector3.up;
            faces[2] = edges[2] = o.rotation * Vector3.forward;
            if (!o.IsWedge)
                return 3;
            Vector3 n = o.LocalCutNormal;
            // The diagonal edge runs across the cut face, perpendicular to the axis the cut runs along.
            Vector3 along = o.cut.x == 0 ? Vector3.right : o.cut.y == 0 ? Vector3.up : Vector3.forward;
            faces[3] = o.rotation * n;
            edges[3] = o.rotation * Vector3.Cross(n, along).normalized;
            return 4;
        }

        static bool Separated(Vector3 axis, Span<Vector3> va, int na, Span<Vector3> vb, int nb, float tolerance)
        {
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            for (int i = 0; i < na; i++)
            {
                float p = Vector3.Dot(va[i], axis);
                minA = Mathf.Min(minA, p); maxA = Mathf.Max(maxA, p);
            }
            for (int i = 0; i < nb; i++)
            {
                float p = Vector3.Dot(vb[i], axis);
                minB = Mathf.Min(minB, p); maxB = Mathf.Max(maxB, p);
            }
            return maxA + tolerance < minB || maxB + tolerance < minA;
        }
    }
}
