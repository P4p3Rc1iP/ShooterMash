using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// The shape of a buildable piece: one or more boxes plus snap points, in piece space, the
    /// materials it comes in, and the kit it belongs to. The pivot is the bottom centre, so a
    /// freely placed piece rests on whatever you aim at.
    /// </summary>
    public sealed class PieceDef
    {
        public readonly string name;
        public readonly Obb[] boxes;
        public readonly Vector3[] snapPoints;
        /// <summary>Which face each snap point is on, for pieces that snap on both (stone slabs, blocks).</summary>
        public readonly SnapFace[] snapFaces;
        public BuildMaterial[] Materials { get; private set; } = Array.Empty<BuildMaterial>();
        public int Kit { get; private set; }

        public PieceDef(string name, Obb[] boxes, Vector3[] snapPoints, SnapFace[] snapFaces = null)
        {
            this.name = name;
            this.boxes = boxes;
            this.snapPoints = snapPoints;
            this.snapFaces = snapFaces ?? new SnapFace[snapPoints.Length];
        }

        public bool ComesIn(BuildMaterial material) => Array.IndexOf(Materials, material) >= 0;

        internal PieceDef In(params BuildMaterial[] materials)
        {
            Materials = materials;
            return this;
        }

        internal void SetKit(int kit) => Kit = kit;
    }

    /// <summary>
    /// Which face of a piece a snap point is on. Snapping only counts a top point lining up with
    /// a top point (or bottom with bottom) as a good fit, so slabs go beside each other rather
    /// than on top. Any lines up with anything.
    /// </summary>
    public enum SnapFace : byte
    {
        Any,
        Bottom,
        Top,
    }

    /// <summary>A set of pieces sharing a grid. The build tool shows one kit at a time.</summary>
    public sealed class PieceKit
    {
        public readonly string name;
        /// <summary>The grid all of the kit's pieces and snap points line up on.</summary>
        public readonly float step;
        /// <summary>The big pieces' size, and the height of one storey.</summary>
        public readonly float size;

        public PieceKit(string name, float step, float size)
        {
            this.name = name;
            this.step = step;
            this.size = size;
        }
    }

    /// <summary>
    /// Greybox pieces in two kits, to compare grid sizes:
    /// <list type="bullet">
    /// <item>kit 0: a 0.5 m grid, every shape in 2.5 m, 1 m and 0.5 m (storeys of 2.5 m);</item>
    /// <item>kit 1: a 1 m grid, every shape in 3 m and 1 m (storeys of 3 m).</item>
    /// </list>
    /// Snap points sit every grid step along a piece's edges, so whatever you build stays on the
    /// grid. Horizontal wooden pieces snap by their top surface, so floors end up flush with the
    /// tops of the walls, beams and posts they sit on. Stone slabs and blocks snap around both
    /// faces so they stack.
    /// </summary>
    public static class PieceCatalog
    {
        const float Slab = 0.2f;      // wooden floor and wall thickness
        const float Post = 0.25f;     // pillar cross-section
        const float Beam = 0.2f;      // beam cross-section
        const float StoneSlab = 0.5f; // stone slab thickness

        public static readonly PieceKit[] Kits =
        {
            new PieceKit("2.5 m", 0.5f, 2.5f),
            new PieceKit("3 m", 1f, 3f),
        };

        /// <summary>Kit 0's grid step, big piece size and storey height.</summary>
        public const float Step = 0.5f;
        public const float Grid = 2.5f;
        public const float StoreyHeight = Grid;

        public static readonly PieceDef[] All = Build();

        public static PieceDef Get(int type) => type >= 0 && type < All.Length ? All[type] : null;

        /// <summary>Whether this piece can be built in this material.</summary>
        public static bool Allows(int type, BuildMaterial material) => Get(type)?.ComesIn(material) ?? false;

        /// <summary>The piece types in a kit that come in a material, in catalog order.</summary>
        public static List<int> Pieces(int kit, BuildMaterial material)
        {
            var list = new List<int>();
            for (int t = 0; t < All.Length; t++)
                if (All[t].Kit == kit && All[t].ComesIn(material))
                    list.Add(t);
            return list;
        }

        static PieceDef[] Build()
        {
            var all = new List<PieceDef>();
            AddKit(all, 0, new[] { 2.5f, 1f, 0.5f }, true);
            AddKit(all, 1, new[] { 3f, 1f }, false); // a 0.5 m wide block doesn't fit a 1 m grid
            return all.ToArray();
        }

        static void AddKit(List<PieceDef> all, int kit, float[] sizes, bool stoneBlock)
        {
            const BuildMaterial wood = BuildMaterial.Wood, stone = BuildMaterial.Stone, iron = BuildMaterial.Iron;
            var shapes = new Shapes(Kits[kit].step);
            int first = all.Count;
            // Wood: everything. Stone: slabs, a block, pillars and stairs. Iron: pillars and beams.
            foreach (float s in sizes) all.Add(shapes.Floor($"Floor {N(s)}x{N(s)}", s).In(wood));
            foreach (float s in sizes) all.Add(shapes.Cuboid($"Slab {N(s)}x{N(s)}", s, StoneSlab, s).In(stone));
            if (stoneBlock) all.Add(shapes.Cuboid("Block 1x0.5x0.5", 1f, 0.5f, 0.5f).In(stone));
            foreach (float s in sizes) all.Add(shapes.Wall($"Wall {N(s)}x{N(s)}", s, s).In(wood));
            foreach (float s in sizes) all.Add(shapes.Pillar($"Pillar {N(s)} m", s).In(wood, stone, iron));
            foreach (float s in sizes) all.Add(shapes.HorizontalBeam($"Beam {N(s)} m", s).In(wood, iron));
            foreach (float s in sizes) all.Add(shapes.Roof45($"Roof 45° {N(s)}x{N(s)}", s).In(wood));
            // 27° stairs, twice as long as they rise (e.g. 2.5 m up in 5 m), in each width.
            foreach (float rise in sizes)
            foreach (float width in sizes)
                all.Add(shapes.Stairs($"Stairs {N(rise)} m up, {N(width)} m wide", width, 2f * rise, rise).In(wood, stone));
            for (int i = first; i < all.Count; i++)
                all[i].SetKit(kit);
        }

        static string N(float metres) => metres.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>The piece shapes, with snap points every grid step of their kit.</summary>
        sealed class Shapes
        {
            readonly float step;

            public Shapes(float step)
            {
                this.step = step;
            }

            /// <summary>A solid box (x by y by z) with snap points around its bottom and top faces.</summary>
            public PieceDef Cuboid(string name, float x, float y, float z)
            {
                Vector3[] bottom = Rectangle(new Vector3(-x / 2, 0, -z / 2), Vector3.right * x, Vector3.forward * z);
                Vector3[] top = Rectangle(new Vector3(-x / 2, y, -z / 2), Vector3.right * x, Vector3.forward * z);
                var snaps = new List<Vector3>(bottom);
                snaps.AddRange(top);
                var faces = new SnapFace[snaps.Count];
                for (int i = 0; i < faces.Length; i++)
                    faces[i] = i < bottom.Length ? SnapFace.Bottom : SnapFace.Top;
                return new PieceDef(name, new[] { new Obb(new Vector3(0, y / 2, 0), new Vector3(x, y, z)) }, snaps.ToArray(), faces);
            }

            public PieceDef Floor(string name, float size)
            {
                float h = size / 2f;
                Vector3[] snaps = Rectangle(new Vector3(-h, Slab, -h), Vector3.right * size, Vector3.forward * size);
                return new PieceDef(name, new[] { new Obb(new Vector3(0, Slab / 2, 0), new Vector3(size, Slab, size)) }, snaps);
            }

            public PieceDef Wall(string name, float width, float height)
            {
                float w = width / 2f;
                Vector3[] snaps = Rectangle(new Vector3(-w, 0, 0), Vector3.right * width, Vector3.up * height);
                return new PieceDef(name, new[] { new Obb(new Vector3(0, height / 2, 0), new Vector3(width, height, Slab)) }, snaps);
            }

            public PieceDef Pillar(string name, float height) =>
                new PieceDef(name, new[] { new Obb(new Vector3(0, height / 2, 0), new Vector3(Post, height, Post)) },
                    Line(Vector3.zero, new Vector3(0, height, 0)));

            public PieceDef HorizontalBeam(string name, float length)
            {
                float l = length / 2f;
                return new PieceDef(name, new[] { new Obb(new Vector3(0, Beam / 2, 0), new Vector3(length, Beam, Beam)) },
                    Line(new Vector3(-l, Beam, 0), new Vector3(l, Beam, 0)));
            }

            public PieceDef Roof45(string name, float size)
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
            public PieceDef Stairs(string name, float width, float run, float rise)
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
            Vector3[] Line(Vector3 a, Vector3 b)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / step));
                var points = new Vector3[n + 1];
                for (int i = 0; i <= n; i++)
                    points[i] = Vector3.Lerp(a, b, i / (float)n);
                return points;
            }

            /// <summary>Points every grid step around a rectangle, starting at a corner and going round.</summary>
            Vector3[] Rectangle(Vector3 corner, Vector3 u, Vector3 v)
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
}
