using System;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>An oriented box. Pieces are made of these, in local space in their definition.</summary>
    [Serializable]
    public struct Obb
    {
        public Vector3 center;
        public Quaternion rotation;
        public Vector3 halfExtents;

        public Obb(Vector3 center, Vector3 size, Quaternion rotation)
        {
            this.center = center;
            this.rotation = rotation;
            halfExtents = size * 0.5f;
        }

        public Obb(Vector3 center, Vector3 size) : this(center, size, Quaternion.identity) { }

        public Vector3 Size => halfExtents * 2f;

        /// <summary>This box moved from piece space into the world.</summary>
        public Obb Transformed(Vector3 position, Quaternion rot) => new Obb
        {
            center = position + rot * center,
            rotation = rot * rotation,
            halfExtents = halfExtents,
        };

        public Bounds Bounds
        {
            get
            {
                Vector3 x = rotation * Vector3.right;
                Vector3 y = rotation * Vector3.up;
                Vector3 z = rotation * Vector3.forward;
                Vector3 extents = Abs(x) * halfExtents.x + Abs(y) * halfExtents.y + Abs(z) * halfExtents.z;
                return new Bounds(center, extents * 2f);
            }
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>
        /// Separating-axis test. Boxes closer than <paramref name="tolerance"/> count as overlapping,
        /// so pieces that are snapped face to face are seen as touching.
        /// </summary>
        public static bool Overlap(in Obb a, in Obb b, float tolerance)
        {
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
    }
}
