using System;
using System.Collections.Generic;
using System.Globalization;
using ShooterMashup.Player;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// The shape of a buildable piece: one or more boxes plus snap points, in piece space, and the
    /// materials it comes in. The pivot is the bottom centre, so a freely placed piece rests on
    /// whatever you aim at.
    /// </summary>
    public sealed class PieceDef
    {
        public readonly string name;
        public readonly Obb[] boxes;
        public readonly Vector3[] snapPoints;
        public BuildMaterial[] Materials { get; private set; } = Array.Empty<BuildMaterial>();
        /// <summary>How the player moves on it, e.g. a ladder (set on the piece's colliders via PmSurface).</summary>
        public SurfaceFlags Surface { get; private set; }

        public PieceDef(string name, Obb[] boxes, Vector3[] snapPoints)
        {
            this.name = name;
            this.boxes = boxes;
            this.snapPoints = snapPoints;
        }

        public bool ComesIn(BuildMaterial material) => Array.IndexOf(Materials, material) >= 0;

        internal PieceDef In(params BuildMaterial[] materials)
        {
            Materials = materials;
            return this;
        }

        internal PieceDef WithSurface(SurfaceFlags surface)
        {
            Surface = surface;
            return this;
        }
    }

    /// <summary>
    /// Greybox pieces on a 1 m grid: every shape in 3 m and 1 m, and storeys of 3 m (a floor on top
    /// leaves 2.8 m of headroom). Snap points sit every metre along a piece's edges, so whatever
    /// you build stays on the grid. Horizontal wooden pieces snap by their top surface, so floors
    /// end up flush with the tops of the walls, beams and posts they sit on. Stone slabs snap
    /// around both faces so they stack.
    /// </summary>
    public static class PieceCatalog
    {
        const float Slab = 0.2f;      // wooden floor and wall thickness
        const float Post = 0.25f;     // pillar cross-section
        const float Beam = 0.2f;      // beam cross-section
        const float StoneSlab = 0.5f; // stone slab thickness

        /// <summary>The grid all pieces and snap points line up on.</summary>
        public const float Step = 1f;
        /// <summary>The big pieces' size, and the height of one storey.</summary>
        public const float Grid = 3f;
        public const float StoreyHeight = Grid;

        static readonly float[] Sizes = { Grid, Step };

        public static readonly PieceDef[] All = Build();

        public static PieceDef Get(int type) => type >= 0 && type < All.Length ? All[type] : null;

        /// <summary>Whether this piece can be built in this material.</summary>
        public static bool Allows(int type, BuildMaterial material) => Get(type)?.ComesIn(material) ?? false;

        /// <summary>The piece types that come in a material, in catalog order.</summary>
        public static List<int> Pieces(BuildMaterial material)
        {
            var list = new List<int>();
            for (int t = 0; t < All.Length; t++)
                if (All[t].ComesIn(material))
                    list.Add(t);
            return list;
        }

        static PieceDef[] Build()
        {
            const BuildMaterial wood = BuildMaterial.Wood, stone = BuildMaterial.Stone, iron = BuildMaterial.Iron;
            var all = new List<PieceDef>();
            // Wood: everything. Stone: slabs, pillars and stairs. Iron: pillars and beams.
            foreach (float s in Sizes) all.Add(Floor($"Floor {N(s)}x{N(s)}", s).In(wood));
            foreach (float s in Sizes) all.Add(Cuboid($"Slab {N(s)}x{N(s)}", s, StoneSlab, s).In(stone));
            foreach (float s in Sizes) all.Add(Wall($"Wall {N(s)}x{N(s)}", s, s).In(wood));
            foreach (float s in Sizes) all.Add(Pillar($"Pillar {N(s)} m", s).In(wood, stone, iron));
            foreach (float s in Sizes) all.Add(HorizontalBeam($"Beam {N(s)} m", s).In(wood, iron));
            foreach (float s in Sizes) all.Add(Roof45($"Roof 45° {N(s)}x{N(s)}", s).In(wood));
            foreach (float s in Sizes) all.Add(Ladder($"Ladder {N(s)} m", s).In(wood).WithSurface(SurfaceFlags.Ladder));
            // 27° stairs, twice as long as they rise (1 m up in 2 m, 3 m up in 6 m), in each width.
            foreach (float rise in Sizes)
            foreach (float width in Sizes)
                all.Add(Stairs($"Stairs {N(rise)} m up, {N(width)} m wide", width, 2f * rise, rise).In(wood, stone));
            return all.ToArray();
        }

        static string N(float metres) => metres.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>A solid box (x by y by z) with snap points around its bottom and top faces.</summary>
        static PieceDef Cuboid(string name, float x, float y, float z)
        {
            var snaps = new List<Vector3>(Rectangle(new Vector3(-x / 2, 0, -z / 2), Vector3.right * x, Vector3.forward * z));
            snaps.AddRange(Rectangle(new Vector3(-x / 2, y, -z / 2), Vector3.right * x, Vector3.forward * z));
            return new PieceDef(name, new[] { new Obb(new Vector3(0, y / 2, 0), new Vector3(x, y, z)) }, snaps.ToArray());
        }

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
            // The slab's underside runs through both edges, so it rests on the wall, beam or floor
            // it snaps to instead of cutting into it, and two slopes meet cleanly at a ridge.
            float h = size / 2f;
            Quaternion tilt = Quaternion.Euler(-45f, 0f, 0f);
            Vector3 normal = tilt * Vector3.up;
            float length = size * Mathf.Sqrt(2f);
            var slab = new Obb(new Vector3(0, h, 0) + normal * (Slab / 2), new Vector3(size, Slab, length), tilt);
            var snaps = new List<Vector3>(Line(new Vector3(-h, 0, -h), new Vector3(h, 0, -h)));
            snaps.AddRange(Line(new Vector3(-h, size, h), new Vector3(h, size, h)));
            return new PieceDef(name, new[] { slab }, snaps.ToArray());
        }

        /// <summary>
        /// A 1 m wide ladder: a backing board with rails and rungs in front (+z). Its snap points sit
        /// 0.1 m behind the board, on the line of the wall, beam or post it leans on, so snapped to
        /// one it stands against its face. Climbable through SurfaceFlags.Ladder.
        /// </summary>
        static PieceDef Ladder(string name, float height)
        {
            const float width = 1f, board = 0.04f, rail = 0.08f, depth = 0.1f, behind = 0.1f;
            const float rungSpacing = 0.3f;
            var boxes = new List<Obb>
            {
                new Obb(new Vector3(0, height / 2, board / 2), new Vector3(width, height, board)),
                new Obb(new Vector3(-(width - rail) / 2, height / 2, board + depth / 2), new Vector3(rail, height, depth)),
                new Obb(new Vector3((width - rail) / 2, height / 2, board + depth / 2), new Vector3(rail, height, depth)),
            };
            for (float y = rungSpacing / 2; y < height; y += rungSpacing)
                boxes.Add(new Obb(new Vector3(0, y, board + depth / 2), new Vector3(width - 2 * rail, 0.04f, 0.04f)));
            Vector3[] snaps = Rectangle(new Vector3(-width / 2, 0, -behind), Vector3.right * width, Vector3.up * height);
            return new PieceDef(name, boxes.ToArray(), snaps);
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
