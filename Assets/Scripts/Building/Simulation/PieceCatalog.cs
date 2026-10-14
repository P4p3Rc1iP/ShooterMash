using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        /// <summary>A wheel with suspension (vehicles only), or null.</summary>
        public WheelSpec Wheel { get; private set; }
        /// <summary>A seat you can drive from (vehicles only), or null.</summary>
        public SeatSpec Seat { get; private set; }
        /// <summary>An engine (vehicles only), or null.</summary>
        public EngineSpec Engine { get; private set; }
        /// <summary>A trailer coupling half (vehicles only), or null.</summary>
        public CouplingSpec Coupling { get; private set; }
        /// <summary>Only buildable on a vehicle: the world refuses it.</summary>
        public bool VehicleOnly => Wheel != null || Seat != null || Engine != null || Coupling != null;

        public PieceDef(string name, Obb[] boxes, Vector3[] snapPoints)
        {
            this.name = name;
            this.boxes = boxes;
            this.snapPoints = snapPoints;
        }

        public bool ComesIn(BuildMaterial material) => Array.IndexOf(Materials, material) >= 0;

        internal PieceDef WithWheel(WheelSpec wheel)
        {
            Wheel = wheel;
            return this;
        }

        internal PieceDef WithSeat(SeatSpec seat)
        {
            Seat = seat;
            return this;
        }

        internal PieceDef WithEngine(EngineSpec engine)
        {
            Engine = engine;
            return this;
        }

        internal PieceDef WithCoupling(CouplingSpec coupling)
        {
            Coupling = coupling;
            return this;
        }

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
    /// A wheel with built-in suspension, in piece space. It's drawn but has no collider: the
    /// vehicle casts a ray for it and handles springs and grip in code (Trailmakers style).
    /// </summary>
    public sealed class WheelSpec
    {
        public readonly Vector3 center;   // where the wheel sits at its normal ride height
        public readonly float radius;
        public readonly float width;      // along the axle, which is the piece's x axis
        public readonly float mass;       // kg
        public readonly float travel;     // suspension travel, full droop to full bump (m)
        public readonly bool driven;      // the engines turn it; a free wheel only rolls
        public readonly float grip;       // tyre grip, relative to a standard tyre

        public WheelSpec(Vector3 center, float radius, float width, float mass, float travel, bool driven, float grip)
        {
            this.center = center;
            this.radius = radius;
            this.width = width;
            this.mass = mass;
            this.travel = travel;
            this.driven = driven;
            this.grip = grip;
        }
    }

    /// <summary>
    /// An engine. Engines on a vehicle add up and drive its driven wheels: at low speed with up to
    /// `pull` (the most its gearing puts through the wheels), higher up with its power (force =
    /// power / speed), up to its top speed, where a limiter cuts in.
    /// </summary>
    public sealed class EngineSpec
    {
        public readonly float power;      // W
        public readonly float pull;       // N, the most force at the wheels (low gears)
        public readonly float topSpeed;   // m/s
        public readonly float mass;       // kg (the piece's own mass, whatever its material)

        public EngineSpec(float powerKw, float pullKn, float topSpeed, float mass)
        {
            power = powerKw * 1000f;
            pull = pullKn * 1000f;
            this.topSpeed = topSpeed;
            this.mass = mass;
        }
    }

    /// <summary>
    /// Half of a trailer coupling: a fifth wheel on the towing vehicle, or a kingpin under the
    /// trailer's front. When a kingpin comes within reach of a fifth wheel they couple.
    /// </summary>
    public sealed class CouplingSpec
    {
        public readonly bool kingpin;     // true: on the trailer; false: the fifth wheel on the tractor
        public readonly Vector3 point;    // where they join, in piece space

        public CouplingSpec(bool kingpin, Vector3 point)
        {
            this.kingpin = kingpin;
            this.point = point;
        }
    }

    /// <summary>A seat, in piece space. It faces +z, which becomes the vehicle's forward.</summary>
    public sealed class SeatSpec
    {
        public readonly Vector3 eye;      // the driver's eye, for the first-person camera

        public SeatSpec(Vector3 eye) => this.eye = eye;
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
        // Framing is a bit thicker than a wall, and each kind a touch thinner than the last where
        // they meet, so their faces never coincide (no z-fighting): horizontal beams, then
        // pillars, then diagonal beams.
        /// <summary>Horizontal beam cross-section.</summary>
        public const float BeamSize = 0.25f;
        const float Beam = BeamSize;
        const float Post = 0.23f;     // pillar cross-section
        const float Brace = 0.21f;    // diagonal beam cross-section
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
            const BuildMaterial wood = BuildMaterial.Wood, stone = BuildMaterial.Stone, iron = BuildMaterial.Iron, scrap = BuildMaterial.Scrap;
            var all = new List<PieceDef>();
            // Wood: everything. Stone: slabs, pillars and stairs. Iron: pillars and beams. Scrap
            // (sheet metal, light: vehicle bodies, shacks): floors, walls, pillars and beams.
            foreach (float s in Sizes) all.Add(Floor($"Floor {N(s)}x{N(s)}", s).In(wood, scrap));
            foreach (float s in Sizes) all.Add(Cuboid($"Slab {N(s)}x{N(s)}", s, StoneSlab, s).In(stone));
            foreach (float s in Sizes) all.Add(Wall($"Wall {N(s)}x{N(s)}", s, s).In(wood, scrap));
            foreach (float s in Sizes) all.Add(Pillar($"Pillar {N(s)} m", s).In(wood, stone, iron, scrap));
            foreach (float s in Sizes) all.Add(HorizontalBeam($"Beam {N(s)} m", s).In(wood, iron, scrap));
            // Diagonal beams for bracing, named by how high they rise: 45° (rise over the same run)
            // and 27° (rise over twice the run, like the stairs).
            foreach (float s in Sizes) all.Add(DiagonalBeam($"Diagonal beam 45° {N(s)} m", s, s).In(wood, iron));
            foreach (float s in Sizes) all.Add(DiagonalBeam($"Diagonal beam 27° {N(s)} m", 2f * s, s).In(wood, iron));
            // Roofs in three pitches, rising 1:1 (45°), 1:2 (27°, like the stairs) and 2:1 (63°), so
            // every edge lands on the grid. Each set: plain slopes, hip and valley corners, a 1 m
            // ridge cap for odd widths, gable walls under a slope and a gable cap under the ridge cap.
            // Pieces are named by plan size (width x run), gables by width x height. The steep set
            // has a 1 m run only (3 m would rise 6 m), in 3 m and 1 m widths.
            foreach ((string pitch, float slope, float[] runs) in new[]
            {
                ("45°", 1f, Sizes), ("27°", 0.5f, Sizes), ("63°", 2f, new[] { Step }),
            })
            {
                foreach (float run in runs)
                foreach (float width in runs.Length == 1 ? Sizes : new[] { run })
                    all.Add(Roof($"Roof {pitch} {N(width)}x{N(run)}", width, run, slope * run).In(wood));
                foreach (float s in runs) all.Add(RoofOuterCorner($"Roof {pitch} outer corner {N(s)}x{N(s)}", s, slope * s).In(wood));
                foreach (float s in runs) all.Add(RoofInnerCorner($"Roof {pitch} inner corner {N(s)}x{N(s)}", s, slope * s).In(wood));
                all.Add(RoofCap($"Roof cap {pitch} {N(Step)}x{N(Step)}", Step, slope * Step / 2f).In(wood));
                foreach (float s in runs) all.Add(GableWall($"Gable wall {pitch} {N(s)}x{N(slope * s)}", s, slope * s).In(wood));
                all.Add(GableCap($"Gable cap {pitch} {N(Step)}x{N(slope * Step / 2f)}", Step, slope * Step / 2f).In(wood));
            }
            foreach (float s in Sizes) all.Add(Ladder($"Ladder {N(s)} m", s).In(wood).WithSurface(SurfaceFlags.Ladder));
            // 27° stairs, twice as long as they rise (1 m up in 2 m, 3 m up in 6 m), in each width.
            foreach (float rise in Sizes)
            foreach (float width in Sizes)
                all.Add(Stairs($"Stairs {N(rise)} m up, {N(width)} m wide", width, 2f * rise, rise).In(wood, stone));
            // Vehicle parts, only buildable on vehicles, in any material (their mass is their own).
            // Wheels in four sizes, each driven (the engines turn it) or free (it only rolls), so a
            // build can be rear-, front- or all-wheel drive: 0.8 m road wheels, 1 m wheels, 1.5 m
            // off-road wheels and 2 m truck wheels, with more suspension travel as they grow.
            foreach ((string name, float radius, float width, float mass, float travel, float grip) in new[]
            {
                ("Small wheel", 0.4f, 0.3f, 25f, 0.25f, 1.05f),
                ("Wheel", 0.5f, 0.4f, 40f, 0.35f, 1f),
                ("Big wheel", 0.75f, 0.55f, 90f, 0.6f, 1f),
                ("Huge wheel", 1f, 0.7f, 180f, 0.6f, 1f),
            })
            {
                all.Add(WheelPiece(name, radius, width, mass, travel, true, grip).In(wood, iron, scrap));
                all.Add(WheelPiece($"{name} (free)", radius, width, mass, travel, false, grip).In(wood, iron, scrap));
            }
            // Engines (size across x up y along z; power kW, pull at the wheels kN, top speed m/s, mass kg).
            all.Add(EnginePiece("Engine 4-cyl", new Vector3(0.7f, 0.5f, 0.6f), new EngineSpec(130f, 7f, 45f, 130f)).In(wood, iron, scrap));
            all.Add(EnginePiece("Engine V6", new Vector3(0.8f, 0.55f, 0.8f), new EngineSpec(200f, 10f, 52f, 180f)).In(wood, iron, scrap));
            all.Add(EnginePiece("Engine V8", new Vector3(0.8f, 0.6f, 1f), new EngineSpec(320f, 14f, 60f, 250f)).In(wood, iron, scrap));
            all.Add(EnginePiece("Engine diesel", new Vector3(1f, 1f, 1.4f), new EngineSpec(420f, 75f, 30f, 900f)).In(wood, iron, scrap));
            all.Add(DriverSeat("Driver seat").In(wood, iron, scrap));
            all.Add(CouplingPiece("Fifth wheel", false).In(wood, iron, scrap));
            all.Add(CouplingPiece("Kingpin", true).In(wood, iron, scrap));
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

        /// <summary>A beam along x; its snap points run along its centre line.</summary>
        static PieceDef HorizontalBeam(string name, float length)
        {
            float l = length / 2f;
            return new PieceDef(name, new[] { new Obb(new Vector3(0, Beam / 2, 0), new Vector3(length, Beam, Beam)) },
                Line(new Vector3(-l, Beam / 2, 0), new Vector3(l, Beam / 2, 0)));
        }

        /// <summary>
        /// A beam rising `rise` over a horizontal `run` along z, from its low end at -z to its high
        /// end at +z. Like the roof, it snaps at both ends, which sit on its centre line; the pivot
        /// is under its middle at the height of the low end.
        /// </summary>
        static PieceDef DiagonalBeam(string name, float run, float rise)
        {
            float r = run / 2f;
            Vector3 low = new Vector3(0, 0, -r), high = new Vector3(0, rise, r);
            Quaternion tilt = Quaternion.LookRotation(high - low);
            var box = new Obb((low + high) / 2f, new Vector3(Brace, Brace, (high - low).magnitude), tilt);
            return new PieceDef(name, new[] { box }, new[] { low, high });
        }


        /// <summary>
        /// A plain roof `width` wide (x), rising `rise` over a run of `run`, from the low edge at -z
        /// to the high edge at +z. The slab's centre plane runs through both edges, like the centre
        /// lines of beams and diagonal beams, so a roof, a diagonal and a beam snapped to the same
        /// point all cross there. On a wall top it sits half its thickness into the wall.
        /// </summary>
        static PieceDef Roof(string name, float width, float run, float rise)
        {
            float w = width / 2f, r = run / 2f;
            var snaps = new List<Vector3>(Line(new Vector3(-w, 0, -r), new Vector3(w, 0, -r)));
            snaps.AddRange(Line(new Vector3(-w, rise, r), new Vector3(w, rise, r)));
            return new PieceDef(name, new[] { RoofSlab(width, run, rise, 0f, Vector3.zero) }, snaps.ToArray());
        }

        /// <summary>
        /// The slab of a roof `width` wide rising `rise` over `run`, towards `yaw` (0 = +z),
        /// optionally cut to the triangle given by `cut` (in the slab's own axes: x across, z up the
        /// slope). Its centre plane runs through the low and high edges.
        /// </summary>
        static Obb RoofSlab(float width, float run, float rise, float yaw, Vector3 cut)
        {
            float pitch = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            Quaternion tilt = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-pitch, 0f, 0f);
            float length = Mathf.Sqrt(run * run + rise * rise);
            return Obb.Wedge(new Vector3(0, rise / 2f, 0), new Vector3(width, Slab, length), tilt, cut);
        }

        /// <summary>
        /// A hip corner for a 90° outer corner, `size` square: eaves along -z and -x, rising `rise`
        /// to a peak at the (+x, +z) corner. Two triangular halves of roof slab, one rising towards
        /// +z and one towards +x, meeting along the hip (the diagonal). Its sloped sides match
        /// plain roofs beside it (rising +z along +x, rising +x along +z).
        /// </summary>
        static PieceDef RoofOuterCorner(string name, float size, float rise)
        {
            float h = size / 2f;
            Obb[] halves =
            {
                RoofSlab(size, size, rise, 0f, new Vector3(-1, 0, 1)),   // plan triangle (-h,-h) (h,-h) (h,h)
                RoofSlab(size, size, rise, 90f, new Vector3(1, 0, 1)),   // plan triangle (-h,-h) (-h,h) (h,h)
            };
            var snaps = new List<Vector3>(Line(new Vector3(-h, 0, -h), new Vector3(h, 0, -h)));
            snaps.AddRange(Line(new Vector3(-h, 0, -h), new Vector3(-h, 0, h)).Skip(1));
            snaps.Add(new Vector3(h, rise, h));
            return new PieceDef(name, halves, snaps.ToArray());
        }

        /// <summary>
        /// A valley corner for a 90° inner corner, `size` square: low only at the (-x, -z) corner,
        /// rising `rise` to the high edges along +x and +z. Two triangular halves meeting along the
        /// valley (the diagonal); plain roofs rising +z continue it along -x, and roofs rising +x along -z.
        /// </summary>
        static PieceDef RoofInnerCorner(string name, float size, float rise)
        {
            float h = size / 2f;
            Obb[] halves =
            {
                RoofSlab(size, size, rise, 0f, new Vector3(1, 0, -1)),   // plan triangle (-h,-h) (-h,h) (h,h)
                RoofSlab(size, size, rise, 90f, new Vector3(-1, 0, -1)), // plan triangle (-h,-h) (h,-h) (h,h)
            };
            var snaps = new List<Vector3> { new Vector3(-h, 0, -h) };
            snaps.AddRange(Line(new Vector3(h, rise, -h), new Vector3(h, rise, h)));
            snaps.AddRange(Line(new Vector3(h, rise, h), new Vector3(-h, rise, h)).Skip(1));
            return new PieceDef(name, halves, snaps.ToArray());
        }

        /// <summary>
        /// A ridge cap for roofs of an odd width, `size` square: rises `rise` from both edges (along
        /// z) to a ridge along x, so it fits between two plain roofs whose high edges are `size`
        /// apart. Each half reaches a little past the ridge so their top faces meet: all the way at
        /// 45° and flatter; on steeper pitches only as far as stays hidden inside the other half.
        /// </summary>
        static PieceDef RoofCap(string name, float size, float rise)
        {
            float half = size / 2f;
            float tan = rise / half;
            float past = Slab / 2f * Mathf.Min(tan, 1f / tan);
            float pitch = Mathf.Atan(tan) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(half * half + rise * rise) + past;
            var boxes = new Obb[2];
            for (int side = 0; side < 2; side++)
            {
                bool front = side == 0;   // the -z half rises towards +z, the +z half towards -z
                Quaternion tilt = Quaternion.Euler(front ? -pitch : pitch, 0f, 0f);
                Vector3 upSlope = tilt * (front ? Vector3.forward : Vector3.back);
                Vector3 mid = new Vector3(0, rise / 2f, front ? -half / 2f : half / 2f) + upSlope * (past / 2f);
                boxes[side] = new Obb(mid, new Vector3(size, Slab, length), tilt);
            }
            var snaps = new List<Vector3>(Line(new Vector3(-half, 0, -half), new Vector3(half, 0, -half)));
            snaps.AddRange(Line(new Vector3(-half, 0, half), new Vector3(half, 0, half)));
            return new PieceDef(name, boxes, snaps.ToArray());
        }

        /// <summary>
        /// A right-triangle wall that fills the gable under one roof slope: `run` along the bottom
        /// (x), rising `rise` at +x, like a wall cut along its diagonal (rotate it 180° for the other
        /// slope). Snap points along all three edges, on the grid.
        /// </summary>
        static PieceDef GableWall(string name, float run, float rise)
        {
            float h = run / 2f;
            var shape = Obb.Wedge(new Vector3(0, rise / 2f, 0), new Vector3(run, rise, Slab), Quaternion.identity, new Vector3(-1, 1, 0));
            var snaps = new List<Vector3>(Line(new Vector3(-h, 0, 0), new Vector3(h, 0, 0)));
            for (float y = Step; y < rise - 1e-4f; y += Step)       // up the tall side, every metre
                snaps.Add(new Vector3(h, y, 0));
            snaps.Add(new Vector3(h, rise, 0));
            int steps = Mathf.RoundToInt(run / Step);
            for (int k = 1; k < steps; k++)                         // the slope, between its ends
                snaps.Add(new Vector3(-h + k * Step, rise * k / steps, 0));
            return new PieceDef(name, new[] { shape }, snaps.ToArray());
        }

        /// <summary>
        /// The small gable under a ridge cap: an isosceles triangle `size` wide and `rise` tall, made
        /// of two right-triangle halves. Snaps by its bottom edge, e.g. onto a wall between two gable walls.
        /// </summary>
        static PieceDef GableCap(string name, float size, float rise)
        {
            float q = size / 4f;
            var halfSize = new Vector3(size / 2f, rise, Slab);
            Obb[] halves =
            {
                Obb.Wedge(new Vector3(-q, rise / 2f, 0), halfSize, Quaternion.identity, new Vector3(-1, 1, 0)), // high at the middle
                Obb.Wedge(new Vector3(q, rise / 2f, 0), halfSize, Quaternion.identity, new Vector3(1, 1, 0)),
            };
            return new PieceDef(name, halves, Line(new Vector3(-size / 2f, 0, 0), new Vector3(size / 2f, 0, 0)));
        }

        /// <summary>
        /// A 1 m wide ladder: two rails and rungs, open so you can see through it. It stands in front
        /// (+z) of its snap points, which sit 0.1 m behind it on the line of the wall, beam or post it
        /// leans on, so snapped to one it stands against its face. Climbable through SurfaceFlags.Ladder.
        /// </summary>
        static PieceDef Ladder(string name, float height)
        {
            const float width = 1f, rail = 0.08f, depth = 0.1f, behind = 0.1f;
            const float rungSpacing = 0.3f;
            var boxes = new List<Obb>
            {
                new Obb(new Vector3(-(width - rail) / 2, height / 2, depth / 2), new Vector3(rail, height, depth)),
                new Obb(new Vector3((width - rail) / 2, height / 2, depth / 2), new Vector3(rail, height, depth)),
            };
            for (float y = rungSpacing / 2; y < height; y += rungSpacing)
                boxes.Add(new Obb(new Vector3(0, y, depth / 2), new Vector3(width - 2 * rail, 0.04f, 0.04f)));
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

        /// <summary>
        /// A wheel on a short mount, with its axle along x. It snaps by two points: on a beam's
        /// centre line, so the mount sits against the side of a beam running along z; and half a
        /// beam further in, so it sits flush on the end of a beam running along x (an axle). The
        /// wheel is on the +x side; turn it 180° for the other side of a vehicle. (Vehicle parts
        /// are off the 1 m grid.) Each wheel brings its own suspension travel and tyre grip, and
        /// is driven by the vehicle's engines or free.
        /// </summary>
        static PieceDef WheelPiece(string name, float radius, float width, float mass, float travel, bool driven, float grip)
        {
            const float mount = 0.2f, gap = 0.03f;
            float inner = Beam / 2f;
            var mountBox = new Obb(new Vector3(inner + mount / 2f, 0, 0), new Vector3(mount, mount, mount));
            var wheel = new WheelSpec(new Vector3(inner + mount + gap + width / 2f, 0, 0), radius, width, mass, travel, driven, grip);
            return new PieceDef(name, new[] { mountBox }, new[] { Vector3.zero, new Vector3(inner, 0, 0) }).WithWheel(wheel);
        }

        /// <summary>An engine block `size` big, snapping by the middle of its bottom (it sits on a floor).</summary>
        static PieceDef EnginePiece(string name, Vector3 size, EngineSpec engine) =>
            new PieceDef(name, new[] { new Obb(new Vector3(0, size.y / 2f, 0), size) }, new[] { Vector3.zero }).WithEngine(engine);

        /// <summary>
        /// A coupling plate, 1.2 m square and 0.15 m thick. A fifth wheel sits on top of a deck
        /// (it snaps by the middle of its bottom) and couples at the middle of its top; a kingpin
        /// hangs under a trailer's deck (it snaps by the middle of its top) and couples at the
        /// middle of its bottom.
        /// </summary>
        static PieceDef CouplingPiece(string name, bool kingpin)
        {
            const float size = 1.2f, thick = 0.15f;
            float bottom = kingpin ? -thick : 0f;
            var plate = new Obb(new Vector3(0, bottom + thick / 2f, 0), new Vector3(size, thick, size));
            Vector3 point = new Vector3(0, kingpin ? -thick : thick, 0);
            return new PieceDef(name, new[] { plate }, new[] { Vector3.zero }).WithCoupling(new CouplingSpec(kingpin, point));
        }

        /// <summary>
        /// A placeholder driver's seat facing +z. It snaps by a point half a beam below its
        /// bottom, so snapped to a beam's centre line it sits on top of the beam.
        /// </summary>
        static PieceDef DriverSeat(string name)
        {
            const float width = 0.6f, height = 0.4f, depth = 0.6f, back = 0.1f, backHeight = 0.6f;
            Obb[] boxes =
            {
                new Obb(new Vector3(0, height / 2f, 0), new Vector3(width, height, depth)),
                new Obb(new Vector3(0, height + backHeight / 2f, -depth / 2f + back / 2f), new Vector3(width, backHeight, back)),
            };
            return new PieceDef(name, boxes, new[] { new Vector3(0, -Beam / 2f, 0) })
                .WithSeat(new SeatSpec(new Vector3(0, height + 0.75f, -0.05f)));
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
