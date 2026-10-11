using System.Collections.Generic;
using System.Globalization;
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
    /// Greybox pieces on a 0.5 m grid. Every piece comes in 2.5 m, 1 m and 0.5 m, and snap
    /// points sit every 0.5 m along their edges, so whatever you build stays on the grid.
    /// Horizontal pieces snap by their top surface, so floors end up flush with the tops of the
    /// walls, beams and posts they sit on.
    /// </summary>
    public static class PieceCatalog
    {
        const float Slab = 0.2f;   // floor and wall thickness
        const float Post = 0.25f;  // pillar cross-section
        const float Beam = 0.2f;   // beam cross-section

        /// <summary>The grid all pieces and snap points line up on.</summary>
        public const float Step = 0.5f;
        /// <summary>The big pieces' size, and the height of one storey. A floor on top leaves 2.3 m of headroom.</summary>
        public const float Grid = 2.5f;
        public const float StoreyHeight = Grid;

        static readonly float[] Sizes = { Grid, 1f, Step };

        public static readonly PieceDef[] All = Build();

        public static PieceDef Get(int type) => type >= 0 && type < All.Length ? All[type] : null;

        static PieceDef[] Build()
        {
            var all = new List<PieceDef>();
            foreach (float s in Sizes) all.Add(Floor($"Floor {N(s)}x{N(s)}", s));
            foreach (float s in Sizes) all.Add(Wall($"Wall {N(s)}x{N(s)}", s, s));
            foreach (float s in Sizes) all.Add(Pillar($"Pillar {N(s)} m", s));
            foreach (float s in Sizes) all.Add(HorizontalBeam($"Beam {N(s)} m", s));
            foreach (float s in Sizes) all.Add(Roof45($"Roof 45° {N(s)}x{N(s)}", s));
            // 27° stairs, 1 m wide, twice as long as they rise: 0.5 m up in 1 m, 1 m in 2 m, 2.5 m in 5 m.
            foreach (float s in Sizes) all.Add(Stairs($"Stairs {N(s)} m rise", 1f, 2f * s, s));
            return all.ToArray();
        }

        static string N(float metres) => metres.ToString("0.##", CultureInfo.InvariantCulture);

        static PieceDef Floor(string name, float size)
        {
            float h = size / 2f;
            Vector3[] snaps = Rectangle(new Vector3(-h, Slab, -h), Vector3.right * size, Vector3.forward * size);
            return new PieceDef(name, new[] { new Obb(new Vector3(0, Slab / 2, 0), new Vector3(size, Slab, size)) }, snaps);
        }

        static PieceDef Wall(string name, float width, float height)
        {
            float w = width / 2f;
            Vector3[] snaps = Rectangle(new Vector3(-w, 0, 0), Vector3.right * width, Vector3.up * height);
            return new PieceDef(name, new[] { new Obb(new Vector3(0, height / 2, 0), new Vector3(width, height, Slab)) }, snaps);
        }

        static PieceDef Pillar(string name, float height) =>
            new PieceDef(name, new[] { new Obb(new Vector3(0, height / 2, 0), new Vector3(Post, height, Post)) },
                Line(Vector3.zero, new Vector3(0, height, 0)));

        static PieceDef HorizontalBeam(string name, float length)
        {
            float l = length / 2f;
            return new PieceDef(name, new[] { new Obb(new Vector3(0, Beam / 2, 0), new Vector3(length, Beam, Beam)) },
                Line(new Vector3(-l, Beam, 0), new Vector3(l, Beam, 0)));
        }

        static PieceDef Roof45(string name, float size)
        {
            // Rises `size` over a run of `size`, from the low edge at -z to the high edge at +z.
            // The slab's top surface runs through both edges.
            float h = size / 2f;
            Quaternion tilt = Quaternion.Euler(-45f, 0f, 0f);
            Vector3 normal = tilt * Vector3.up;
            float length = size * Mathf.Sqrt(2f);
            var slab = new Obb(new Vector3(0, h, 0) - normal * (Slab / 2), new Vector3(size, Slab, length), tilt);
            var snaps = new List<Vector3>(Line(new Vector3(-h, 0, -h), new Vector3(h, 0, -h)));
            snaps.AddRange(Line(new Vector3(-h, size, h), new Vector3(h, size, h)));
            return new PieceDef(name, new[] { slab }, snaps.ToArray());
        }

        /// <summary>Solid 25 cm steps rising over the run, from the bottom edge at -z to the top edge at +z.</summary>
        static PieceDef Stairs(string name, float width, float run, float rise)
        {
            const float stepRise = 0.25f;
            int steps = Mathf.RoundToInt(rise / stepRise);
            float tread = run / steps;
            var boxes = new Obb[steps];
            for (int k = 0; k < steps; k++)
            {
                float top = (k + 1) * stepRise;
                boxes[k] = new Obb(new Vector3(0, top / 2, -run / 2 + (k + 0.5f) * tread), new Vector3(width, top, tread));
            }
            float w = width / 2f, r = run / 2f;
            var snaps = new List<Vector3>(Line(new Vector3(-w, 0, -r), new Vector3(w, 0, -r)));
            snaps.AddRange(Line(new Vector3(-w, rise, r), new Vector3(w, rise, r)));
            return new PieceDef(name, boxes, snaps.ToArray());
        }

        /// <summary>Points every grid step from a to b, both ends included.</summary>
        static Vector3[] Line(Vector3 a, Vector3 b)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / Step));
            var points = new Vector3[n + 1];
            for (int i = 0; i <= n; i++)
                points[i] = Vector3.Lerp(a, b, i / (float)n);
            return points;
        }

        /// <summary>Points every grid step around a rectangle, starting at a corner and going round.</summary>
        static Vector3[] Rectangle(Vector3 corner, Vector3 u, Vector3 v)
        {
            var points = new List<Vector3>();
            Vector3[] corners = { corner, corner + u, corner + u + v, corner + v };
            for (int e = 0; e < 4; e++)
            {
                Vector3[] edge = Line(corners[e], corners[(e + 1) % 4]);
                for (int i = 0; i < edge.Length - 1; i++) // the last point is the next edge's first
                    points.Add(edge[i]);
            }
            return points.ToArray();
        }
    }
}
