using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// Builds four example structures when play starts, through ordinary BuildCmds: they have
    /// real support, show the support colours and collapse like anything a player builds.
    /// Together they use every piece in every material it comes in (a test checks this), so
    /// placements that fail, which are logged, double as a check on the pieces.
    ///
    /// - Farmstead (45° roofs): an L-shaped house on a stone foundation, with hip, valley and
    ///   gable, doors and a window from 1x1 walls, a stone chimney and a fence; beside it an
    ///   L-shaped cottage roofed with 1 m pieces (ridge caps crossing at the corner, a small hip
    ///   with a gable cap at one end, a gable at the other).
    /// - Barn (27° roofs): a timber frame on stone footings with a raised floor, wood and iron
    ///   beams, diagonal bracing in the open bays, a porch and steps; beside it a 1 m cottage
    ///   with small hips at both ends.
    /// - Watchtower (63° roof): stone, iron and wood legs, stairs up to the first floor from all
    ///   four sides, knee braces, ladders, a railed lookout with a steep roof; beside it a 1 m cottage.
    /// - Scrap shack (south of the watchtower): Gorkamorka scrap pillars, walls and beams under a
    ///   flat scrap roof, a patchwork front with a doorway, and an awning over a workbench.
    /// </summary>
    [RequireComponent(typeof(StructureManager))]
    public class BuildShowcase : MonoBehaviour
    {
        /// <summary>Owner id for showcase pieces, so they never mix with a player's undo history.</summary>
        public const int Owner = -2;

        [Tooltip("Where the farmhouse's foundation starts (its -x, -z corner); its cottage is behind it.")]
        public Vector3 farmCorner = DefaultFarm;
        [Tooltip("Where the barn's frame starts (its -x, -z corner); its cottage is behind it.")]
        public Vector3 barnCorner = DefaultBarn;
        [Tooltip("Where the watchtower's legs start (its -x, -z corner).")]
        public Vector3 towerCorner = DefaultTower;

        public static readonly Vector3 DefaultFarm = new Vector3(44f, 0f, 22f);
        public static readonly Vector3 DefaultBarn = new Vector3(58f, 0f, 22f);
        public static readonly Vector3 DefaultTower = new Vector3(70f, 0f, 44f);
        /// <summary>The scrap shack stands this far from the watchtower's corner (south of it).</summary>
        public static readonly Vector3 ScrapShackOffset = new Vector3(0f, 0f, -14f);

        /// <summary>One piece of the plan.</summary>
        public struct Placement
        {
            public string name;
            public BuildMaterial material;
            public Vector3 position;
            public float yaw;
            public override string ToString() => $"{material} {name} at {position} (yaw {yaw})";
        }

        void Start()
        {
            BuildSystem system = GetComponent<StructureManager>().System;
            int placed = 0, failed = 0;
            foreach (Placement p in Plan(farmCorner, barnCorner, towerCorner))
            {
                BuildResult r = Execute(system, p, Time.timeAsDouble);
                if (r.Ok)
                {
                    placed++;
                    continue;
                }
                failed++;
                Debug.LogWarning($"Build showcase: {p} failed: {r.failure}");
            }
            if (failed > 0)
                Debug.LogWarning($"Build showcase: {placed} pieces placed, {failed} failed (see above).");
            else
                Debug.Log($"Build showcase: {placed} pieces placed.");
        }

        public static BuildResult Execute(BuildSystem system, Placement p, double time)
        {
            int type = Array.FindIndex(PieceCatalog.All, d => d.name == p.name);
            return type < 0
                ? BuildResult.Fail(BuildFailure.UnknownPiece)
                : system.Execute(BuildCmd.Place(Owner, type, p.material, p.position, Quaternion.Euler(0, p.yaw, 0), time));
        }

        /// <summary>Every piece of the three structures, in an order that keeps each one supported.</summary>
        public static List<Placement> Plan(Vector3 farm, Vector3 barn, Vector3 tower)
        {
            var plan = new Planner();
            plan.Farmstead(farm);
            plan.Barn(barn);
            plan.Watchtower(tower);
            plan.ScrapShack(tower + ScrapShackOffset);
            return plan.list;
        }

        sealed class Planner
        {
            public readonly List<Placement> list = new List<Placement>();

            const BuildMaterial Wood = BuildMaterial.Wood, Stone = BuildMaterial.Stone, Iron = BuildMaterial.Iron;
            const float T = PieceCatalog.Grid, H = T / 2f; // a 3 m tile, and half of one
            const float B = PieceCatalog.BeamSize;          // beams sit this far below a post top to be flush with it

            void Place(string name, BuildMaterial material, Vector3 position, float yaw = 0f) =>
                list.Add(new Placement { name = name, material = material, position = position, yaw = yaw });

            static string N(float metres) => metres.ToString("0.##", CultureInfo.InvariantCulture);

            /// <summary>
            /// A 3 x 3 grid of 1x1 walls with some left out (a doorway or a window), along x
            /// (yaw 0) or z (yaw 90) from `start`.
            /// </summary>
            void WallGrid(Vector3 start, float yaw, Func<int, int, bool> open)
            {
                Vector3 along = yaw == 0f ? Vector3.right : Vector3.forward;
                for (int col = 0; col < 3; col++)
                for (int row = 0; row < 3; row++)
                    if (!open(col, row))
                        Place("Wall 1x1", Wood, start + along * (0.5f + col) + Vector3.up * row, yaw);
            }

            static bool Doorway(int col, int row) => col == 1 && row < 2;
            static bool Window(int col, int row) => col == 1 && row == 1;

            /// <summary>
            /// The roof of the L shape used by the farmhouse and the barn, in 3 m pieces at height
            /// `y`: wing A along x (9 x 6 m) with a hipped east end, wing B along +z from its west
            /// end (6 x 6 m more) with a gable at the north end, and a valley where they meet.
            /// </summary>
            void LRoof(Vector3 o, string pitch, float slope, float y)
            {
                string roof = $"Roof {pitch} 3x3", outer = $"Roof {pitch} outer corner 3x3", inner = $"Roof {pitch} inner corner 3x3";
                Vector3 Tile(int i, int j) => o + new Vector3(H + T * i, y, H + T * j);
                Place(outer, Wood, Tile(0, 0));
                Place(roof, Wood, Tile(1, 0));
                Place(outer, Wood, Tile(2, 0), -90);
                Place(outer, Wood, Tile(2, 1), 180);
                Place(inner, Wood, Tile(1, 1), 180);
                for (int j = 1; j < 4; j++) Place(roof, Wood, Tile(0, j), 90);   // rising towards wing B's ridge
                for (int j = 2; j < 4; j++) Place(roof, Wood, Tile(1, j), -90);
                string gable = $"Gable wall {pitch} 3x{N(slope * T)}";
                Place(gable, Wood, o + new Vector3(H, y, 4 * T));
                Place(gable, Wood, o + new Vector3(T + H, y, 4 * T), 180);
            }

            /// <summary>
            /// A 6 x 6 m L-shaped cottage of storey-high walls, wings 3 m wide, roofed with 1 m
            /// pieces of a pitch rising `slope` per metre: 1 m slopes from both sides of each
            /// wing and ridge caps between them, crossing where the wings meet, a valley in the
            /// inside corner and the outside corner as a hip. Wing A's east end is a small hip
            /// (corners, a slope and a gable cap under the end of the ridge cap). Wing B's north
            /// end is a gable (gable walls, 1x1 walls and a gable cap) or, if `gableEnd` is false
            /// (27°, whose 0.5 m filler doesn't exist), another small hip.
            /// </summary>
            void Cottage(Vector3 o, string pitch, float slope, bool gableEnd)
            {
                foreach (float x in new[] { H, T + H }) Place("Wall 3x3", Wood, o + new Vector3(x, 0, 0));
                Place("Wall 3x3", Wood, o + new Vector3(2 * T, 0, H), 90);
                Place("Wall 3x3", Wood, o + new Vector3(T + H, 0, T));
                Place("Wall 3x3", Wood, o + new Vector3(T, 0, T + H), 90);
                Place("Wall 3x3", Wood, o + new Vector3(H, 0, 2 * T));
                foreach (float z in new[] { H, T + H }) Place("Wall 3x3", Wood, o + new Vector3(0, 0, z), 90);

                string roof = $"Roof {pitch} 1x1", outer = $"Roof {pitch} outer corner 1x1", inner = $"Roof {pitch} inner corner 1x1";
                string cap = $"Roof cap {pitch} 1x1", gableCap = $"Gable cap {pitch} 1x{N(slope / 2f)}";
                const float y = T;
                Vector3 Tile(int i, int j) => o + new Vector3(i + 0.5f, y, j + 0.5f);
                Vector3 Ridge(int i, int j) => Tile(i, j) + Vector3.up * slope;   // caps sit on the slopes' tops

                // Wing A (x 0..6, z 0..3): south slope, ridge caps, north slope.
                Place(outer, Wood, Tile(0, 0));
                for (int i = 1; i < 5; i++) Place(roof, Wood, Tile(i, 0));
                Place(outer, Wood, Tile(5, 0), -90);
                Place(cap, Wood, Ridge(1, 1));
                Place(cap, Wood, Ridge(1, 1), 90);                                  // the two ridges cross
                for (int i = 2; i < 5; i++) Place(cap, Wood, Ridge(i, 1));
                Place(roof, Wood, Tile(5, 1), -90);                                // the small hip at the east end
                Place(gableCap, Wood, o + new Vector3(5f, y + slope, 1.5f), 90);
                Place(inner, Wood, Tile(2, 2), 180);
                for (int i = 3; i < 5; i++) Place(roof, Wood, Tile(i, 2), 180);
                Place(outer, Wood, Tile(5, 2), 180);

                // Wing B (x 0..3, z 0..6): west slope, ridge caps, east slope.
                int end = gableEnd ? 6 : 5;
                for (int j = 1; j < end; j++) Place(roof, Wood, Tile(0, j), 90);
                for (int j = 2; j < end; j++) Place(cap, Wood, Ridge(1, j), 90);
                for (int j = 3; j < end; j++) Place(roof, Wood, Tile(2, j), -90);
                if (gableEnd)
                {
                    string gable = $"Gable wall {pitch} 1x{N(slope)}";
                    Place(gable, Wood, o + new Vector3(0.5f, y, 2 * T));
                    Place(gable, Wood, o + new Vector3(2.5f, y, 2 * T), 180);
                    for (int row = 0; row < Mathf.RoundToInt(slope); row++)
                        Place("Wall 1x1", Wood, o + new Vector3(1.5f, y + row, 2 * T));
                    Place(gableCap, Wood, o + new Vector3(1.5f, y + slope, 2 * T));
                }
                else
                {
                    Place(outer, Wood, Tile(0, 5), 90);
                    Place(roof, Wood, Tile(1, 5), 180);
                    Place(outer, Wood, Tile(2, 5), 180);
                    Place(gableCap, Wood, o + new Vector3(1.5f, y + slope, 5f));
                }
            }

            /// <summary>The farmhouse (45°) on a 1 m stone foundation, with its cottage behind it.</summary>
            public void Farmstead(Vector3 o)
            {
                const float floor = 1f, wallTop = floor + T;

                // Foundation: two layers of 3x3 stone slabs under the L.
                for (int layer = 0; layer < 2; layer++)
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 4; j++)
                    if (i < 2 || j < 2)
                        Place("Slab 3x3", Stone, o + new Vector3(H + T * i, 0.5f * layer, H + T * j));

                // Walls: a front door, a side door and a window made of 1x1 walls.
                Place("Wall 3x3", Wood, o + new Vector3(H, floor, 0));
                WallGrid(o + new Vector3(T, floor, 0), 0, Doorway);
                Place("Wall 3x3", Wood, o + new Vector3(2 * T + H, floor, 0));
                WallGrid(o + new Vector3(3 * T, floor, 0), 90, Window);
                Place("Wall 3x3", Wood, o + new Vector3(3 * T, floor, T + H), 90);
                Place("Wall 3x3", Wood, o + new Vector3(2 * T + H, floor, 2 * T));
                foreach (float z in new[] { 2 * T + H, 3 * T + H }) Place("Wall 3x3", Wood, o + new Vector3(2 * T, floor, z), 90);
                foreach (float x in new[] { H, T + H }) Place("Wall 3x3", Wood, o + new Vector3(x, floor, 4 * T));
                foreach (float z in new[] { H, T + H, 3 * T + H }) Place("Wall 3x3", Wood, o + new Vector3(0, floor, z), 90);
                WallGrid(o + new Vector3(0, floor, 2 * T), 90, Doorway);

                LRoof(o, "45°", 1f, wallTop);

                // Steps up to both doors, a stone chimney against wing B, and a fence with a gate.
                Place("Stairs 1 m up, 3 m wide", Stone, o + new Vector3(T + H, 0, -1f));
                Place("Stairs 1 m up, 1 m wide", Stone, o + new Vector3(-1f, 0, 2 * T + H), 90);
                for (int k = 0; k < 16; k++)
                    Place("Slab 1x1", Stone, o + new Vector3(2 * T + 0.5f, 0.5f * k, 3 * T + H));
                foreach (float x in new[] { -1f, 2f, 7f, 10f })
                    Place("Pillar 1 m", Wood, o + new Vector3(x, 0, -3f));
                foreach (float x in new[] { 0.5f, 8.5f })
                    Place("Beam 3 m", Wood, o + new Vector3(x, 1f - B, -3f));

                Cottage(o + new Vector3(0, 0, 16f), "45°", 1f, gableEnd: true);
            }

            /// <summary>The timber-frame barn (27°), with its cottage behind it.</summary>
            public void Barn(Vector3 o)
            {
                const float floor = 1f, top = floor + T;

                // The frame: posts at every 3 m round the L, on 1 m stone footings.
                var posts = new[]
                {
                    (0, 0), (1, 0), (2, 0), (3, 0), (3, 1), (3, 2), (2, 2), (2, 3), (2, 4), (1, 4), (0, 4), (0, 3), (0, 2), (0, 1),
                };
                foreach ((int i, int j) in posts) Place("Pillar 1 m", Stone, o + new Vector3(T * i, 0, T * j));
                foreach ((int i, int j) in posts) Place("Pillar 3 m", Wood, o + new Vector3(T * i, floor, T * j));
                // Beams flush with the post tops between each pair of neighbouring posts. (Iron would
                // need an iron or stone path to the ground; on wooden posts it would lose too much.)
                for (int k = 0; k < posts.Length; k++)
                {
                    (int i0, int j0) = posts[k];
                    (int i1, int j1) = posts[(k + 1) % posts.Length];
                    var mid = new Vector3(T * (i0 + i1) / 2f, top - B, T * (j0 + j1) / 2f);
                    Place("Beam 3 m", Wood, o + mid, i0 == i1 ? 90 : 0);
                }

                // A raised floor, and walls on it.
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 4; j++)
                    if (i < 2 || j < 2)
                        Place("Floor 3x3", Wood, o + new Vector3(H + T * i, floor - 0.2f, H + T * j));
                Place("Wall 3x3", Wood, o + new Vector3(H, floor, 0));                         // south, door in the middle
                Place("Wall 3x3", Wood, o + new Vector3(2 * T + H, floor, 0));
                Place("Wall 3x3", Wood, o + new Vector3(2 * T + H, floor, 2 * T));             // wing A north
                WallGrid(o + new Vector3(2 * T, floor, 2 * T), 90, Doorway);                    // wing B east, onto the porch
                WallGrid(o + new Vector3(0, floor, 4 * T), 0, Doorway);                         // north, up the ladder
                Place("Wall 3x3", Wood, o + new Vector3(T + H, floor, 4 * T));
                foreach (float z in new[] { 2 * T + H, 3 * T + H }) Place("Wall 3x3", Wood, o + new Vector3(0, floor, z), 90);

                // Diagonal bracing in the open bays: wood 27° on the west (from the floor), iron 27°
                // on the east (each across two bays), and iron 45° in wing B's open bay. The iron
                // ones start on the ground, which they need to hold up their weight.
                Place("Diagonal beam 27° 3 m", Wood, o + new Vector3(0, floor, T));
                Place("Diagonal beam 27° 3 m", Iron, o + new Vector3(3 * T, 0, T));
                Place("Diagonal beam 45° 3 m", Iron, o + new Vector3(2 * T, 0, 3 * T + H));

                LRoof(o, "27°", 0.5f, top);

                // Steps to the doors: wide wooden ones at the front, a ladder at the back.
                Place("Stairs 1 m up, 3 m wide", Wood, o + new Vector3(T + H, 0, -1f));
                Place("Ladder 1 m", Wood, o + new Vector3(1.5f, 0, 4 * T + 0.1f));

                // A porch over the side door: posts, 1 m beams, a small 27° gable roof and steps.
                const float porchTop = floor + 2f, z0 = 2 * T + 0.5f;  // above the 2 m door, z 6.5..8.5
                foreach (float z in new[] { z0, z0 + 2f }) Place("Pillar 3 m", Wood, o + new Vector3(2 * T + 2f, 0, z));
                foreach (float z in new[] { z0, z0 + 2f })
                foreach (float x in new[] { 2 * T + 0.5f, 2 * T + 1.5f })
                    Place("Beam 1 m", Wood, o + new Vector3(x, porchTop - B, z));
                foreach (float z in new[] { z0 + 0.5f, z0 + 1.5f }) Place("Beam 1 m", Wood, o + new Vector3(2 * T + 2f, porchTop - B, z), 90);
                foreach (float x in new[] { 2 * T + 0.5f, 2 * T + 1.5f })
                {
                    Place("Roof 27° 1x1", Wood, o + new Vector3(x, porchTop, z0 + 0.5f));
                    Place("Roof 27° 1x1", Wood, o + new Vector3(x, porchTop, z0 + 1.5f), 180);
                }
                Place("Gable wall 27° 1x0.5", Wood, o + new Vector3(2 * T + 2f, porchTop, z0 + 0.5f), -90);
                Place("Gable wall 27° 1x0.5", Wood, o + new Vector3(2 * T + 2f, porchTop, z0 + 1.5f), 90);
                Place("Stairs 1 m up, 1 m wide", Wood, o + new Vector3(2 * T + 1f, 0, z0 + 1f), -90);

                Cottage(o + new Vector3(0, 0, 16f), "27°", 0.5f, gableEnd: false);
            }

            /// <summary>
            /// The watchtower (63°): four legs three storeys high, floors at 3 and 6 m, a railed
            /// lookout at 9 m under a steep gable roof, with its cottage behind it.
            /// </summary>
            public void Watchtower(Vector3 o)
            {
                var corners = new[] { (0f, 0f), (T, 0f), (0f, T), (T, T) };
                // Stone, then iron up to the lookout, so the iron railing up there has an iron path
                // to the ground (on wood it would lose too much). Wooden posts hold the roof.
                BuildMaterial[] legs = { Stone, Iron, Iron, Wood };
                for (int level = 0; level < 4; level++)
                foreach ((float x, float z) in corners)
                    Place("Pillar 3 m", legs[level], o + new Vector3(x, T * level, z));

                // A ring of beams at the top of each storey (iron), and the roof's beams (wood).
                for (int level = 1; level <= 4; level++)
                {
                    float y = T * level - B;
                    BuildMaterial m = level < 4 ? Iron : Wood;
                    foreach (float z in new[] { 0f, T }) Place("Beam 3 m", m, o + new Vector3(H, y, z));
                    foreach (float x in new[] { 0f, T }) Place("Beam 3 m", m, o + new Vector3(x, y, H), 90);
                }

                // Floors at 3 and 6 m, and the lookout at 9 m from 1x1 floors.
                Place("Floor 3x3", Wood, o + new Vector3(H, T - 0.2f, H));
                Place("Floor 3x3", Wood, o + new Vector3(H, 2 * T - 0.2f, H));
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    Place("Floor 1x1", Wood, o + new Vector3(i + 0.5f, 3 * T - 0.2f, j + 0.5f));

                // Bracing: 45° diagonals across the ground storey's four sides and the third storey's
                // back and sides. The first storey stays open for the stairs, with knee braces.
                const string d45 = "Diagonal beam 45° 3 m";
                foreach (float y in new[] { 0f, 2 * T })
                {
                    Place(d45, Wood, o + new Vector3(0, y, H));          // left, rising to the back
                    Place(d45, Wood, o + new Vector3(T, y, H), 180);     // right, rising to the front
                    Place(d45, Wood, o + new Vector3(H, y, T), 90);      // back, rising to the right
                }
                Place(d45, Wood, o + new Vector3(H, 0, 0), -90);         // front, rising to the left
                float knee = 2 * T - B / 2f - 1f;                         // a knee brace's low end, 1 m under a beam's centre line
                Place("Diagonal beam 45° 1 m", Iron, o + new Vector3(0.5f, knee, 0), 90);   // front, from the left leg
                Place("Diagonal beam 27° 1 m", Iron, o + new Vector3(1f, knee, T), 90);     // back
                Place("Diagonal beam 27° 1 m", Wood, o + new Vector3(0, knee, 1f));         // left
                Place("Diagonal beam 45° 1 m", Wood, o + new Vector3(T, knee, 0.5f));       // right, both ends
                Place("Diagonal beam 45° 1 m", Wood, o + new Vector3(T, knee, T - 0.5f), 180);
                Place("Diagonal beam 45° 1 m", Wood, o + new Vector3(0.5f, knee + T, 0), 90); // third storey, front

                // Stairs to the first floor from all four sides, ladders on up (front right).
                Place("Stairs 3 m up, 1 m wide", Wood, o + new Vector3(H, 0, -T));
                Place("Stairs 3 m up, 3 m wide", Wood, o + new Vector3(-T, 0, H), 90);
                Place("Stairs 3 m up, 1 m wide", Stone, o + new Vector3(2 * T, 0, H), -90);
                Place("Stairs 3 m up, 3 m wide", Stone, o + new Vector3(H, 0, 2 * T), 180);
                for (int level = 1; level < 3; level++)
                    Place("Ladder 3 m", Wood, o + new Vector3(2.5f, T * level, -0.1f), 180);

                // The lookout's railing: iron posts and rails, open where the ladder arrives.
                const float deck = 3 * T;
                foreach (var (x, z) in new[] { (1f, 0f), (2f, 0f), (T, 1f), (T, 2f), (1f, T), (2f, T), (0f, 1f), (0f, 2f) })
                    Place("Pillar 1 m", Iron, o + new Vector3(x, deck, z));
                for (int k = 0; k < 3; k++)
                {
                    if (k < 2) Place("Beam 1 m", Iron, o + new Vector3(k + 0.5f, deck + 1f - B, 0));
                    Place("Beam 1 m", Iron, o + new Vector3(k + 0.5f, deck + 1f - B, T));
                    Place("Beam 1 m", Iron, o + new Vector3(0, deck + 1f - B, k + 0.5f), 90);
                    Place("Beam 1 m", Iron, o + new Vector3(T, deck + 1f - B, k + 0.5f), 90);
                }

                // The steep roof: 3 m wide slopes from both sides, ridge caps, and gables at both ends.
                const float eaves = 4 * T;
                Place("Roof 63° 3x1", Wood, o + new Vector3(0.5f, eaves, H), 90);
                Place("Roof 63° 3x1", Wood, o + new Vector3(T - 0.5f, eaves, H), -90);
                for (int j = 0; j < 3; j++)
                    Place("Roof cap 63° 1x1", Wood, o + new Vector3(H, eaves + 2f, j + 0.5f), 90);
                foreach (float z in new[] { 0f, T })
                {
                    Place("Gable wall 63° 1x2", Wood, o + new Vector3(0.5f, eaves, z));
                    Place("Gable wall 63° 1x2", Wood, o + new Vector3(T - 0.5f, eaves, z), 180);
                    Place("Wall 1x1", Wood, o + new Vector3(H, eaves, z));
                    Place("Wall 1x1", Wood, o + new Vector3(H, eaves + 1f, z));
                    Place("Gable cap 63° 1x1", Wood, o + new Vector3(H, eaves + 2f, z));
                }

                Cottage(o + new Vector3(-4f, 0, 10f), "63°", 2f, gableEnd: true);
            }

            /// <summary>
            /// A 6 x 6 m scrap shack from `o` (its -x, -z corner): 3 m pillars at the corners and the
            /// middle of the back, 3x3 walls on the back and sides, a patchwork front of 1x1 walls
            /// with a doorway, beams round the top, a flat roof of 3x3 floors, and out front an
            /// awning (a 1 m pillar and a 1 m beam) over a 1x1 workbench top.
            /// </summary>
            public void ScrapShack(Vector3 o)
            {
                const BuildMaterial Scrap = BuildMaterial.Scrap;
                foreach (Vector3 p in new[] { new Vector3(0, 0, 0), new Vector3(2 * T, 0, 0), new Vector3(0, 0, 2 * T),
                             new Vector3(2 * T, 0, 2 * T), new Vector3(T, 0, 2 * T) })
                    Place("Pillar 3 m", Scrap, o + p);
                for (int k = 0; k < 2; k++)
                {
                    Place("Wall 3x3", Scrap, o + new Vector3(H + T * k, 0, 2 * T));             // back
                    Place("Wall 3x3", Scrap, o + new Vector3(0, 0, H + T * k), 90);              // west side
                    Place("Wall 3x3", Scrap, o + new Vector3(2 * T, 0, H + T * k), 90);          // east side
                }
                // The front: 1x1 walls, leaving a doorway in the left-hand bay.
                for (int col = 0; col < 6; col++)
                for (int row = 0; row < 3; row++)
                    if (!(col >= 1 && col <= 2 && row < 2))
                        Place("Wall 1x1", Scrap, o + new Vector3(0.5f + col, row, 0));
                // Beams round the top, flush with the pillar tops; the roof on them.
                for (int k = 0; k < 2; k++)
                {
                    Place("Beam 3 m", Scrap, o + new Vector3(H + T * k, T - B, 0));
                    Place("Beam 3 m", Scrap, o + new Vector3(H + T * k, T - B, 2 * T));
                    Place("Beam 3 m", Scrap, o + new Vector3(0, T - B, H + T * k), 90);
                    Place("Beam 3 m", Scrap, o + new Vector3(2 * T, T - B, H + T * k), 90);
                }
                for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    Place("Floor 3x3", Scrap, o + new Vector3(H + T * i, T, H + T * j));
                // The awning: a short post out front, a beam back to the corner pillar, a bench top on it.
                Place("Pillar 1 m", Scrap, o + new Vector3(2 * T + 1f, 0, 0));
                Place("Beam 1 m", Scrap, o + new Vector3(2 * T + 0.5f, 1f - B, 0));
                Place("Floor 1x1", Scrap, o + new Vector3(2 * T + 0.5f, 1f, 0));
            }
        }
    }
}
