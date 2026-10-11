using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// The shape of a buildable piece: one or more boxes plus snap points, in piece space.
    /// The pivot is the bottom centre, so a freely placed piece rests on whatever you aim at.
    /// Any shape can be built in any material.
    /// </summary>
    public sealed class PieceDef
    {
        public readonly string name;
        public readonly Obb[] boxes;
        public readonly Vector3[] snapPoints;

        public PieceDef(string name, Obb[] boxes, Vector3[] snapPoints)
        {
            this.name = name;
            this.boxes = boxes;
            this.snapPoints = snapPoints;
        }
    }

    /// <summary>
    /// Greybox pieces, sized like Valheim's. Horizontal pieces snap by their top surface, so
    /// floors end up flush with the tops of the walls, beams and posts they sit on.
    /// </summary>
    public static class PieceCatalog
    {
        const float Slab = 0.2f;   // floor and wall thickness
        const float Post = 0.25f;  // pillar cross-section
        const float Beam = 0.2f;   // beam cross-section

        /// <summary>Wall and pillar height for one storey. A floor on top leaves 2.3 m of headroom.</summary>
        public const float StoreyHeight = 2.5f;

        public static readonly PieceDef[] All =
        {
            Floor("Floor 2x2", 2f),
            Floor("Floor 1x1", 1f),
            // Storeys are 2.5 m: tall walls, or 1 m + 1 m + 0.5 m pieces stacked.
            Wall("Wall 2x2.5", 2f, StoreyHeight),
            Wall("Wall 1x1", 1f, 1f),
            Wall("Wall 1x0.5", 1f, 0.5f),
            Pillar("Pillar 2.5 m", StoreyHeight),
            Pillar("Pillar 1 m", 1f),
            Pillar("Pillar 0.5 m", 0.5f),
            HorizontalBeam("Beam 2 m", 2f),
            HorizontalBeam("Beam 1 m", 1f),
            Roof45("Roof 45° 2x2"),
            Stairs("Stairs"),
        };

        public static PieceDef Get(int type) => type >= 0 && type < All.Length ? All[type] : null;

        static PieceDef Floor(string name, float size)
        {
            float h = size / 2f;
            var snaps = new List<Vector3>();
            for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && z == 0) continue;
                if (size < 2f && (x == 0 || z == 0)) continue; // 1x1: corners only
                snaps.Add(new Vector3(x * h, Slab, z * h));
            }
            return new PieceDef(name, new[] { new Obb(new Vector3(0, Slab / 2, 0), new Vector3(size, Slab, size)) }, snaps.ToArray());
        }

        static PieceDef Wall(string name, float width, float height)
        {
            float w = width / 2f;
            var snaps = new List<Vector3>
            {
                new Vector3(-w, 0, 0), new Vector3(w, 0, 0),
                new Vector3(-w, height, 0), new Vector3(w, height, 0),
            };
            if (width >= 2f)
            {
                // Midpoints, so 1x1 pieces can attach halfway along.
                snaps.Add(new Vector3(0, 0, 0));
                snaps.Add(new Vector3(0, height, 0));
                snaps.Add(new Vector3(-w, height / 2, 0));
                snaps.Add(new Vector3(w, height / 2, 0));
            }
            return new PieceDef(name, new[] { new Obb(new Vector3(0, height / 2, 0), new Vector3(width, height, Slab)) }, snaps.ToArray());
        }

        static PieceDef Pillar(string name, float height)
        {
            var snaps = new List<Vector3> { Vector3.zero, new Vector3(0, height, 0) };
            if (height > 1f)
                snaps.Add(new Vector3(0, height / 2, 0));
            return new PieceDef(name, new[] { new Obb(new Vector3(0, height / 2, 0), new Vector3(Post, height, Post)) }, snaps.ToArray());
        }

        static PieceDef HorizontalBeam(string name, float length)
        {
            float l = length / 2f;
            var snaps = new List<Vector3> { new Vector3(-l, Beam, 0), new Vector3(l, Beam, 0) };
            if (length >= 2f)
                snaps.Add(new Vector3(0, Beam, 0));
            return new PieceDef(name, new[] { new Obb(new Vector3(0, Beam / 2, 0), new Vector3(length, Beam, Beam)) }, snaps.ToArray());
        }

        static PieceDef Roof45(string name)
        {
            // Rises 2 m over a 2 m run, from the low edge at z = -1 to the high edge at z = +1.
            // The slab's top surface runs through both edges.
            Quaternion tilt = Quaternion.Euler(-45f, 0f, 0f);
            Vector3 normal = tilt * Vector3.up;
            float length = 2f * Mathf.Sqrt(2f);
            var slab = new Obb(new Vector3(0, 1f, 0) - normal * (Slab / 2), new Vector3(2f, Slab, length), tilt);
            Vector3[] snaps =
            {
                new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(0, 0, -1),
                new Vector3(-1, 2, 1), new Vector3(1, 2, 1), new Vector3(0, 2, 1),
            };
            return new PieceDef(name, new[] { slab }, snaps);
        }

        static PieceDef Stairs(string name)
        {
            // 1 m wide, rising one storey (2.5 m) over a 2 m floor tile in ten 25 cm steps
            // of 20 cm tread. Steep, but players step up to 52 cm.
            const int steps = 10;
            const float run = 2f, rise = StoreyHeight / steps, tread = run / steps;
            var boxes = new Obb[steps];
            for (int k = 0; k < steps; k++)
            {
                float h = (k + 1) * rise;
                boxes[k] = new Obb(new Vector3(0, h / 2, -run / 2 + (k + 0.5f) * tread), new Vector3(1f, h, tread));
            }
            Vector3[] snaps =
            {
                new Vector3(-0.5f, 0, -1), new Vector3(0.5f, 0, -1), new Vector3(0, 0, -1),
                new Vector3(-0.5f, StoreyHeight, 1), new Vector3(0.5f, StoreyHeight, 1), new Vector3(0, StoreyHeight, 1),
            };
            return new PieceDef(name, boxes, snaps);
        }
    }
}
