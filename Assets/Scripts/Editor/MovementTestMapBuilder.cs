using System.Collections.Generic;
using System.Linq;
using ShooterMashup.Building;
using ShooterMashup.Player;
using ShooterMashup.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShooterMashup.EditorTools
{
    /// <summary>
    /// Builds a graybox test map for the player controller: stairs, ramps, jump crates,
    /// crouch and prone tunnels, ladders, fall-damage towers, water and slime tanks, a slick floor
    /// and a strafe-jump lane. Laid out in metres on a 1 m dev grid.
    /// </summary>
    public static class MovementTestMapBuilder
    {
        const float GridSize = 1f; // metres per dev-grid tile
        const string ScenePath = "Assets/Scenes/MovementTest.unity";
        const string ArtFolder = "Assets/Art/Dev";
        const string MeshAssetPath = ArtFolder + "/MovementTestMeshes.asset";

        // The hub (spawn, a buggy, portals) is at the origin; the areas are a short drive away.
        static readonly Vector3 CharacterArea = new Vector3(0f, 0f, 170f);
        static readonly Vector3 BuildingArea = new Vector3(-190f, 0f, 0f);
        static readonly Vector3 DrivingArea = new Vector3(190f, 0f, 0f);
        // Where you arrive (feet) and which way you face, by portal or road.
        static readonly Vector3 HubArrival = new Vector3(0f, 0f, -4f);
        static readonly (Vector3 at, float yaw) CharacterArrival = (new Vector3(0f, 0f, 120f), 0f);
        static readonly (Vector3 at, float yaw) BuildingArrival = (new Vector3(-134f, 0f, -18f), 0f);
        // The showcase structures in the building area's space, spread out with room between them.
        static readonly Vector3 FarmCorner = new Vector3(34f, 0f, 24f);
        static readonly Vector3 BarnCorner = new Vector3(60f, 0f, 24f);
        static readonly Vector3 TowerCorner = new Vector3(84f, 0f, 44f);
        static readonly (Vector3 at, float yaw) DrivingArrival = (new Vector3(108f, 0f, 0f), 90f);

        static Transform root;
        static Transform area;   // the area being built; groups go under it
        static Object meshContainer;
        static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();
        static Material matFloor, matWall, matTrim, matLadder, matSlick, matWater, matSlime, matTarget, matDirt;
        static Font labelFont;

        [MenuItem("ShooterMashup/Build Movement Test Map")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Build();
        }

        /// <summary>Entry point for batch mode: -executeMethod ShooterMashup.EditorTools.MovementTestMapBuilder.Build</summary>
        public static void Build()
        {
            EnsureFolder(ArtFolder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            MeshCache.Clear();
            AssetDatabase.DeleteAsset(MeshAssetPath);
            meshContainer = new Mesh { name = "MovementTestMeshes" };
            AssetDatabase.CreateAsset(meshContainer, MeshAssetPath);

            CreateMaterials();
            labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            root = new GameObject("Map").transform;
            BuildLighting();

            // Ground.
            Box("Floor", new Vector3(0, -0.4f, 0), new Vector3(560, 0.8f, 560), matFloor);

            // Three test areas around a hub at the origin, each in its own space.
            area = Area("Character tests", CharacterArea);
            BuildStairs(new Vector3(-29, 0, 8));
            BuildRamps(new Vector3(11, 0, 8));
            BuildJumpCrates(new Vector3(-6, 0, 13));
            BuildTunnels(new Vector3(-23, 0, 23));
            BuildLadderTower(new Vector3(-8, 0, -18));
            BuildFallTowers(new Vector3(5, 0, -18));
            BuildLiquidTank(new Vector3(8, 0, 33), Contents.Water, matWater, "water");
            BuildLiquidTank(new Vector3(26, 0, 33), Contents.Slime, matSlime, "slime\nslower swimming");
            BuildLeanCorner(new Vector3(-33, 0, -8));
            BuildSlickFloor(new Vector3(28, 0, -5));
            BuildStrafeLane(-38);

            area = Area("Building", BuildingArea);
            BuildBuildingArea(new Vector3(56, 0, 8));

            area = Area("Driving", DrivingArea);
            BuildVehicleTrack(new Vector3(-70f, 0f, -60f), 0f);
            BuildRaceTrack(new Vector3(-30f, 0f, -55f));

            area = Area("Hub", Vector3.zero);
            BuildHub();
            area = null;

            BuildPlayer(HubArrival + new Vector3(0, 0.65f, 0));

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log($"Movement test map saved to {ScenePath}");
        }

        // ------------------------------------------------------------------ stations

        static void BuildStairs(Vector3 at)
        {
            var parent = Group("Stairs");
            float[] risers = { 0.2f, 0.4f, 0.45f, 0.5f, 0.6f };
            const int steps = 8;
            const float tread = 0.8f, width = 3.2f;
            for (int i = 0; i < risers.Length; i++)
            {
                float riser = risers[i];
                float x = at.x + i * 4f;
                for (int k = 0; k < steps; k++)
                {
                    float h = (k + 1) * riser;
                    Box($"Step {riser}-{k}", new Vector3(x, h / 2, at.z + k * tread + tread / 2), new Vector3(width, h, tread),
                        k % 2 == 0 ? matWall : matTrim, parent);
                }
                float top = steps * riser;
                Box($"Landing {riser}", new Vector3(x, top / 2, at.z + steps * tread + 1.6f), new Vector3(width, top, 3.2f), matWall, parent);
                // ET steps up 0.457 m, ShooterMash 0.52 m.
                string verdict = riser <= 0.4572f ? "walkable" : riser <= 0.52f ? "walkable\n(not in ET)" : "too tall";
                Label($"{riser * 100:0} cm steps\n{verdict}", new Vector3(x, 2.3f, at.z - 0.6f), parent);
            }
        }

        static void BuildRamps(Vector3 at)
        {
            var parent = Group("Ramps");
            float[] angles = { 20, 35, 45, 46, 55, 70 };
            const float length = 10, width = 3.2f, thick = 0.4f;
            for (int i = 0; i < angles.Length; i++)
            {
                float a = angles[i];
                float rad = a * Mathf.Deg2Rad;
                float x = at.x + i * 4f;
                var start = new Vector3(x, 0, at.z);
                var dir = new Vector3(0, Mathf.Sin(rad), Mathf.Cos(rad));
                var normal = new Vector3(0, Mathf.Cos(rad), -Mathf.Sin(rad));
                Vector3 center = start + dir * (length / 2) - normal * (thick / 2);
                Box($"Ramp {a}", center, new Vector3(width, thick, length), i % 2 == 0 ? matWall : matTrim, parent,
                    Quaternion.Euler(-a, 0, 0));
                string verdict = Mathf.Cos(rad) >= 0.7f ? "walkable" : "slide";
                Label($"{a}°\n{verdict}", new Vector3(x, 2.3f, at.z - 0.6f), parent);
            }
        }

        static void BuildJumpCrates(Vector3 at)
        {
            var parent = Group("Jump crates");
            // A jump rises 1.16 m, and you can step up 0.46 m while coming down: about 1.6 m max.
            float[] heights = { 0.8f, 1.0f, 1.2f, 1.4f, 1.6f, 1.8f };
            for (int i = 0; i < heights.Length; i++)
            {
                float x = at.x + i * 2.4f;
                Box($"Crate {heights[i]}", new Vector3(x, heights[i] / 2f, at.z), new Vector3(1.6f, heights[i], 1.6f), matTrim, parent);
                Label($"{heights[i]:0.0} m", new Vector3(x, heights[i] + 0.6f, at.z - 1f), parent);
            }
        }

        static void BuildTunnels(Vector3 at)
        {
            var parent = Group("Tunnels");
            // Standing hull is 1.83 m tall, crouched 1.22 m, prone 1.02 m.
            Tunnel("Crouch tunnel", new Vector3(at.x, 0, at.z), 1.4f, 10, parent);
            Label("crouch\n(1.4 m)", new Vector3(at.x, 3.6f, at.z - 0.6f), parent);
            Tunnel("Prone tunnel", new Vector3(at.x + 6, 0, at.z), 1.1f, 13, parent);
            Label("prone\n(1.1 m)", new Vector3(at.x + 6, 3.6f, at.z - 0.6f), parent);
            // A floor on 1 m pillars leaves 0.8 m: only the ShooterMash prone hull (0.6 m) fits.
            Tunnel("Low prone tunnel", new Vector3(at.x + 12, 0, at.z), 0.8f, 13, parent);
            Label("prone\n(0.8 m, not ET)", new Vector3(at.x + 12, 3.6f, at.z - 0.6f), parent);
            BuildFloorOnPillars(new Vector3(at.x + 18, 0, at.z), parent);
        }

        /// <summary>
        /// A copy of what you get with build pieces: a floor (0.2 m thick) on the ground, 1 m pillars on
        /// its corners, and a floor on top flush with them. 0.8 m between the floors, and getting in
        /// means crawling up the lower floor's 0.2 m edge with the upper floor right above it.
        /// </summary>
        static void BuildFloorOnPillars(Vector3 entrance, Transform parent)
        {
            const float width = 2.5f, length = 5f, slab = 0.2f, pillar = 1f, post = 0.25f;
            float zc = entrance.z + length / 2;
            Box("Lower floor", new Vector3(entrance.x, slab / 2, zc), new Vector3(width, slab, length), matTrim, parent);
            Box("Upper floor", new Vector3(entrance.x, pillar + slab / 2, zc), new Vector3(width, slab, length), matWall, parent);
            foreach (float x in new[] { -width / 2, width / 2 })
            foreach (float z in new[] { 0f, length / 2, length })
                Box("Pillar", new Vector3(entrance.x + x, slab + (pillar - slab) / 2, entrance.z + z),
                    new Vector3(post, pillar - slab, post), matWall, parent);
            Label("prone onto a floor\nunder a floor on\n1 m pillars (0.8 m)", new Vector3(entrance.x, 2.6f, entrance.z - 0.6f), parent);
        }

        static void Tunnel(string name, Vector3 entrance, float innerHeight, float length, Transform parent)
        {
            const float inner = 2.4f, wall = 0.8f, total = 3.2f;
            float zc = entrance.z + length / 2;
            Box(name + " L", new Vector3(entrance.x - inner / 2 - wall / 2, total / 2, zc), new Vector3(wall, total, length), matWall, parent);
            Box(name + " R", new Vector3(entrance.x + inner / 2 + wall / 2, total / 2, zc), new Vector3(wall, total, length), matWall, parent);
            Box(name + " roof", new Vector3(entrance.x, (innerHeight + total) / 2, zc), new Vector3(inner, total - innerHeight, length), matTrim, parent);
        }

        static void BuildLadderTower(Vector3 at)
        {
            var parent = Group("Ladder tower");
            const float height = 10;
            Box("Tower", new Vector3(at.x, height / 2, at.z - 1.6f), new Vector3(6.4f, height, 3.2f), matWall, parent);
            Ladder(new Vector3(at.x, 0, at.z), height, parent);
            Label("ladder", new Vector3(at.x, 3.3f, at.z + 1f), parent, 180);
        }

        static void BuildFallTowers(Vector3 at)
        {
            var parent = Group("Fall towers");
            // Walk off the top. Damage starts at 6.15 m (see MoveConfig.fallDamage).
            (float h, string what)[] towers =
            {
                (5f, "no damage"), (7f, "10 dmg"), (8.5f, "15 dmg"),
                (10f, "25 dmg"), (11.5f, "50 dmg"), (13f, "death"),
            };
            for (int i = 0; i < towers.Length; i++)
            {
                float x = at.x + i * 5f;
                float h = towers[i].h;
                Box($"Tower {h}", new Vector3(x, h / 2, at.z - 1.6f), new Vector3(3.2f, h, 3.2f), i % 2 == 0 ? matWall : matTrim, parent);
                Ladder(new Vector3(x, 0, at.z), h, parent);
                Label($"{h:0.#} m\n{towers[i].what}", new Vector3(x, 3.3f, at.z + 1f), parent, 180);
            }
        }

        static void Ladder(Vector3 baseCenter, float height, Transform parent)
        {
            var ladder = Box("Ladder", new Vector3(baseCenter.x, height / 2, baseCenter.z + 0.1f), new Vector3(1.6f, height, 0.2f), matLadder, parent);
            ladder.AddComponent<PmSurface>().flags = SurfaceFlags.Ladder;
        }

        static void BuildLiquidTank(Vector3 at, Contents contents, Material liquidMat, string label)
        {
            var parent = Group(contents + " tank");
            const float inner = 13, wall = 0.8f, high = 5, water = 4;
            float zMin = at.z, zMax = at.z + inner;
            float zc = (zMin + zMax) / 2;

            // The near wall is flush with the water so you can water-jump out over it.
            Box("Wall near", new Vector3(at.x, water / 2, zMin - wall / 2), new Vector3(inner + 2 * wall, water, wall), matTrim, parent);
            Box("Wall far", new Vector3(at.x, high / 2, zMax + wall / 2), new Vector3(inner + 2 * wall, high, wall), matWall, parent);
            Box("Wall left", new Vector3(at.x - inner / 2 - wall / 2, high / 2, zc), new Vector3(wall, high, inner), matWall, parent);
            Box("Wall right", new Vector3(at.x + inner / 2 + wall / 2, high / 2, zc), new Vector3(wall, high, inner), matWall, parent);

            // Steps up to the near wall.
            const int steps = 10;
            const float riser = water / steps, tread = 0.4f;
            for (int k = 0; k < steps; k++)
            {
                float h = (k + 1) * riser;
                float z = zMin - wall - (steps - k) * tread + tread / 2;
                Box($"Tank step {k}", new Vector3(at.x, h / 2, z), new Vector3(3.2f, h, tread), k % 2 == 0 ? matWall : matTrim, parent);
            }

            var waterGo = Box(contents.ToString(), new Vector3(at.x, water / 2, zc), new Vector3(inner, water, inner), liquidMat, parent);
            waterGo.GetComponent<BoxCollider>().isTrigger = true;
            waterGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            waterGo.AddComponent<LiquidVolume>().contents = contents;
            GameObjectUtility.SetStaticEditorFlags(waterGo, 0);

            Label(label, new Vector3(at.x, 5f, zMin - 5.6f), parent);
        }

        static void BuildLeanCorner(Vector3 at)
        {
            var parent = Group("Lean corner");
            Box("Lean wall", new Vector3(at.x, 2, at.z), new Vector3(6.4f, 4, 0.8f), matWall, parent);
            Box("Lean wall side", new Vector3(at.x - 2.8f, 2, at.z - 3.2f), new Vector3(0.8f, 4, 5.6f), matWall, parent);
            Box("Target", new Vector3(at.x + 7.6f, 1.2f, at.z - 6.6f), new Vector3(0.8f, 2.4f, 0.8f), matTarget, parent);
            Label("lean Q/E\naround the corner", new Vector3(at.x, 5f, at.z + 0.6f), parent, 180);
        }

        static void BuildSlickFloor(Vector3 at)
        {
            var parent = Group("Slick floor");
            var slick = Box("Slick", new Vector3(at.x, 0.05f, at.z), new Vector3(13, 0.1f, 13), matSlick, parent);
            slick.AddComponent<PmSurface>().flags = SurfaceFlags.Slick;
            Label("slick", new Vector3(at.x, 2f, at.z + 7f), parent, 180);
        }

        static void BuildStrafeLane(float z)
        {
            var parent = Group("Strafe lane");
            for (int x = -70; x <= 70; x += 5)
            {
                bool major = x % 25 == 0;
                float h = major ? 3.2f : 1.6f;
                Box($"Marker {x}", new Vector3(x, h / 2, z), new Vector3(0.4f, h, 0.4f), major ? matTarget : matTrim, parent);
                if (major)
                    Label($"{x} m", new Vector3(x, 3.8f, z), parent, 180);
            }
            Label("strafe-jump lane\nmarkers every 5 m", new Vector3(0, 5.6f, z + 0.8f), parent, 180);
        }

        /// <summary>
        /// A rough track for vehicles, 10 m wide, starting at `at` and running towards `yaw`
        /// (0 = +z): washboard, ridges that lift one side at a time, two dunes, a jump with a
        /// landing ramp, and a field of rocks. Built in the track's own space (forward is +z).
        /// </summary>
        static void BuildVehicleTrack(Vector3 at, float yaw)
        {
            var parent = Group("Vehicle track");
            parent.SetLocalPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            const float width = 10f;
            float z = 0f;
            Label("vehicle test track\nwashboard, ridges, dunes, jump, rocks", new Vector3(0f, 3f, -1f), parent);

            // Washboard: low ridges every metre (boxes turned 45°: 21 cm tall).
            for (int i = 0; i < 10; i++)
                Box($"Washboard {i}", new Vector3(0f, 0f, z + 1f + i), new Vector3(width, 0.3f, 0.3f), i % 2 == 0 ? matTrim : matWall,
                    parent, Quaternion.Euler(45f, 0f, 0f));
            z += 16f;

            // Ridges under one side at a time, so the body rolls left and right.
            for (int i = 0; i < 6; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Box($"Side ridge {i}", new Vector3(side * width / 4f, 0f, z + i * 2f), new Vector3(width / 2f, 0.5f, 0.5f),
                    i % 2 == 0 ? matWall : matTrim, parent, Quaternion.Euler(45f, 0f, 0f));
            }
            z += 18f;

            // Two dunes: 1 m tall humps, 8° up and down.
            const float thick = 0.3f;
            for (int d = 0; d < 2; d++)
            {
                z = Slope(parent, "Dune up", z, 8f, 1f, width, thick, matWall);
                z = Slope(parent, "Dune down", z, -8f, 1f, width, thick, matTrim);
                z += 4f;
            }
            z += 4f;

            // A jump: 15° up to 1.2 m, a 6 m gap, and a landing ramp down.
            z = Slope(parent, "Jump ramp", z, 15f, 1.2f, width / 2f, thick, matTarget);
            z += 6f;
            z = Slope(parent, "Landing ramp", z, -15f, 1.2f, width / 2f, thick, matWall);
            z += 6f;

            // Rocks: tilted boxes of different sizes, half buried. Seeded, so the map is the same every build.
            var random = new System.Random(7);
            float F(float min, float max) => min + (float)random.NextDouble() * (max - min);
            for (int i = 0; i < 30; i++)
            {
                var size = new Vector3(F(0.6f, 1.6f), F(0.3f, 0.8f), F(0.6f, 1.6f));
                var center = new Vector3(F(-width / 2f, width / 2f), F(-0.15f, 0.1f), z + F(0f, 14f));
                Box($"Rock {i}", center, size, matTrim, parent, Quaternion.Euler(F(-20f, 20f), F(0f, 90f), F(-20f, 20f)));
            }
        }

        /// <summary>
        /// A ramp from `z` along +z, rising (positive angle) to `height` or falling from it to the
        /// ground (negative). Its top surface starts or ends at ground level. Returns where it ends.
        /// </summary>
        static float Slope(Transform parent, string name, float z, float angle, float height, float width, float thick, Material mat)
        {
            float a = Mathf.Abs(angle) * Mathf.Deg2Rad;
            float run = height / Mathf.Tan(a);
            float length = height / Mathf.Sin(a);
            // Euler x tips +z down for positive angles, so a rising ramp needs a negative one.
            Quaternion tilt = Quaternion.Euler(angle > 0f ? -Mathf.Abs(angle) : Mathf.Abs(angle), 0f, 0f);
            Vector3 surfaceMid = new Vector3(0f, height / 2f, z + run / 2f);
            Box(name, surfaceMid - tilt * Vector3.up * (thick / 2f), new Vector3(width, thick, length), mat, parent, tilt);
            return z + run;
        }


        /// <summary>
        /// A closed race circuit, 12 m wide between flat red and grey kerbs, starting at `start`
        /// heading +x: a straight with a table-top jump (6 m wide in the middle, so you can go round
        /// it), a wide sweeper, a chicane, a tight bend, an S-bend, and two corners back to the
        /// start. It's driven anticlockwise. Built from straights and arcs; the last straight
        /// lengths are worked out so the loop always closes.
        /// </summary>
        static void BuildRaceTrack(Vector3 start)
        {
            var parent = Group("Race track");
            parent.localPosition = start;
            var track = new TrackBuilder(parent, 12f);
            Label("race track\nstart / finish", new Vector3(0f, 4f, 9f), parent, 90f);
            Box("Start line", new Vector3(1f, 0.01f, 0f), new Vector3(1f, 0.02f, 12f), matSlick, parent);

            track.Straight(25f);
            // The jump: in the straight's own space (forward is the track direction).
            var jump = new GameObject("Jump").transform;
            jump.SetParent(parent, false);
            jump.SetLocalPositionAndRotation(track.Position, Quaternion.LookRotation(track.Direction));
            // A table top: up, 8 m flat at 1 m, down. Fast jumps clear the top, slow ones land on it.
            float z = Slope(jump, "Jump ramp", 0f, 15f, 1f, 6f, 0.3f, matTarget);
            Box("Jump table", new Vector3(0f, 0.5f, z + 4f), new Vector3(6f, 1f, 8f), matTrim, jump);
            Slope(jump, "Landing ramp", z + 8f, -15f, 1f, 6f, 0.3f, matWall);
            Label("jump", new Vector3(0f, 3f, 0f), jump, 0f);
            track.Straight(35f);

            track.Arc(25f, 90f);                  // wide sweeper, left
            track.Straight(15f);
            track.Arc(15f, -35f);                 // chicane: right, left, right
            track.Arc(15f, 70f);
            track.Arc(15f, -35f);
            track.Straight(10f);
            track.Arc(10f, 90f);                  // tight left
            track.Arc(15f, -40f);                 // S-bend: right, then left
            track.Arc(15f, 40f);

            // Heading back along -x now. Two 20 m corners close the loop; the straights between
            // them make up the rest.
            Vector3 q = track.Position;
            float across = q.x, down = q.z - 40f;
            if (across < 0f || down < 0f)
                Debug.LogError($"Race track doesn't close: {across}, {down}");
            track.Straight(across);
            track.Arc(20f, 90f);
            track.Straight(down);
            track.Arc(20f, 90f);
        }

        /// <summary>
        /// Lays out a track like a turtle, with painted kerbs along both edges: flat enough to
        /// drive over, so the track is a suggestion of turns rather than a channel you're stuck in.
        /// </summary>
        sealed class TrackBuilder
        {
            const float SampleSpacing = 2f, KerbHeight = 0.06f, KerbThickness = 1f;

            readonly Transform parent;
            readonly float halfWidth;
            float heading;          // degrees from +x towards +z
            int kerbs;

            public Vector3 Position { get; private set; }
            public Vector3 Direction => new Vector3(Mathf.Cos(heading * Mathf.Deg2Rad), 0f, Mathf.Sin(heading * Mathf.Deg2Rad));
            Vector3 Left => new Vector3(-Mathf.Sin(heading * Mathf.Deg2Rad), 0f, Mathf.Cos(heading * Mathf.Deg2Rad));

            public TrackBuilder(Transform parent, float width)
            {
                this.parent = parent;
                halfWidth = width / 2f;
            }

            public void Straight(float length)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / SampleSpacing));
                Vector3 from = Position, dir = Direction, left = Left;
                for (int i = 0; i < steps; i++)
                    Kerbs(from + dir * (length * i / steps), from + dir * (length * (i + 1) / steps), left, left);
                Position = from + dir * length;
            }

            /// <summary>A bend of `degrees` (positive: left) on a centre line of `radius`.</summary>
            public void Arc(float radius, float degrees)
            {
                float sign = Mathf.Sign(degrees);
                Vector3 center = Position + Left * (radius * sign);
                float startHeading = heading;
                int steps = Mathf.Max(2, Mathf.CeilToInt(radius * Mathf.Abs(degrees) * Mathf.Deg2Rad / SampleSpacing));
                Vector3 previous = Position, previousLeft = Left;
                for (int i = 1; i <= steps; i++)
                {
                    heading = startHeading + degrees * i / steps;
                    Vector3 p = center - Left * (radius * sign);
                    Kerbs(previous, p, previousLeft, Left);
                    previous = p;
                    previousLeft = Left;
                }
                Position = previous;
            }

            void Kerbs(Vector3 a, Vector3 b, Vector3 leftA, Vector3 leftB)
            {
                float offset = halfWidth + KerbThickness / 2f;
                foreach (float side in new[] { 1f, -1f })
                {
                    Vector3 p = a + leftA * (offset * side), q = b + leftB * (offset * side);
                    Vector3 d = q - p;
                    if (d.sqrMagnitude < 1e-4f)
                        continue;
                    Box("Kerb", (p + q) / 2f + Vector3.up * (KerbHeight / 2f), new Vector3(KerbThickness, KerbHeight, d.magnitude + 0.1f),
                        kerbs % 2 == 0 ? matTarget : matTrim, parent, Quaternion.LookRotation(d));
                }
                kerbs++;
            }
        }

        /// <summary>
        /// The hub at the origin: where you spawn, with a buggy to drive and a portal to each area
        /// (walk through it). Roads lead out to the areas, which each have a portal back.
        /// </summary>
        static void BuildHub()
        {
            var parent = Group("Hub");
            Label("ShooterMash test map\nwalk through a portal, or take the buggy (F)", new Vector3(0f, 5.5f, 9f), parent);

            PortalPair("Building", new Vector3(-6f, 0f, 5f), -45f, BuildingArrival, parent, matDirt);
            PortalPair("Character tests", new Vector3(0f, 0f, 7f), 0f, CharacterArrival, parent, matSlick);
            PortalPair("Driving", new Vector3(6f, 0f, 5f), 45f, DrivingArrival, parent, matTarget);

            // Roads to the areas, for driving there.
            Road(new Vector3(0f, 0f, 10f), CharacterArrival.at, parent);
            Road(new Vector3(-8f, 0f, 0f), BuildingArrival.at, parent);
            Road(new Vector3(8f, 0f, 0f), DrivingArrival.at, parent);

            // A buggy to drive, facing the road to the driving area.
            var buggy = new GameObject("Hub buggy").AddComponent<VehicleShowcase>();
            buggy.buggyPosition = new Vector3(7f, 0f, -6f);
            buggy.buggyYaw = 90f;
        }

        /// <summary>A portal at the hub to an area's arrival point, and one there back to the hub.</summary>
        static void PortalPair(string name, Vector3 at, float yaw, (Vector3 at, float yaw) arrival, Transform parent, Material frame)
        {
            PortalGate(name, at, yaw, arrival.at, arrival.yaw, parent, frame);
            Label(name, at + new Vector3(0f, 3.6f, 0f), parent, yaw);
            // The way back stands 7 m to the left of where you arrive (clear of the road).
            Quaternion facing = Quaternion.Euler(0f, arrival.yaw, 0f);
            Vector3 back = arrival.at + facing * Vector3.left * 7f;
            var areaParent = Group(name + " portal back");
            areaParent.SetParent(root, false);
            PortalGate("Back to the hub", back, arrival.yaw, HubArrival, 0f, areaParent, frame);
            Label("back to the hub", back + new Vector3(0f, 3.6f, 0f), areaParent, arrival.yaw + 180f);
        }

        static void PortalGate(string name, Vector3 at, float yaw, Vector3 destination, float destinationYaw, Transform parent, Material frame)
        {
            var gate = new GameObject("Portal " + name).transform;
            gate.SetParent(parent, false);
            gate.SetLocalPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            foreach (float x in new[] { -1.5f, 1.5f })
                Box("Post", new Vector3(x, 1.5f, 0f), new Vector3(0.4f, 3f, 0.4f), frame, gate);
            Box("Lintel", new Vector3(0f, 3.2f, 0f), new Vector3(3.4f, 0.4f, 0.4f), frame, gate);
            GameObject sheet = Box("Sheet", new Vector3(0f, 1.5f, 0f), new Vector3(2.6f, 3f, 0.05f), matWater, gate);
            Object.DestroyImmediate(sheet.GetComponent<BoxCollider>()); // walk-through
            sheet.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(sheet, 0);
            var portal = gate.gameObject.AddComponent<Portal>();
            portal.destination = destination;
            portal.destinationYaw = destinationYaw;
            portal.size = new Vector3(2.6f, 3f, 0.8f);
        }

        /// <summary>A flat strip on the ground from one point to another (world), to follow by car.</summary>
        static void Road(Vector3 from, Vector3 to, Transform parent)
        {
            Vector3 d = to - from;
            d.y = 0f;
            Box("Road", (from + to) / 2f + Vector3.up * 0.01f, new Vector3(6f, 0.02f, d.magnitude), matTrim, parent, Quaternion.LookRotation(d));
        }

        static void BuildBuildingArea(Vector3 at)
        {
            var parent = Group("Building area");
            Label("building area\nB: build tool", new Vector3(at.x, 5f, at.z - 12f), parent);

            // Owns all placed pieces.
            var structures = new GameObject("Structures");
            structures.AddComponent<StructureManager>();

            // Example structures, built from real pieces when play starts. The StructureManager stays
            // at the world origin (piece records are world positions), so the showcase is moved
            // into this area by giving it world corners; everything else here is in the area's space.
            var showcase = structures.AddComponent<BuildShowcase>();
            showcase.farmCorner = FarmCorner + BuildingArea;
            showcase.barnCorner = BarnCorner + BuildingArea;
            showcase.towerCorner = TowerCorner + BuildingArea;
            Label("farmstead\n45° roofs", FarmCorner + new Vector3(4.5f, 9f, -4f), parent);
            Label("barn\n27° roofs", BarnCorner + new Vector3(4.5f, 8.5f, -4f), parent);
            Label("watchtower\n63° roof",TowerCorner + new Vector3(1.5f, 16.5f, -1f), parent);
            Label("vehicles: L lift tool,\nB to build on the lift", new Vector3(78f, 3f, 8f), parent);

            // Diggable ground: a 1 m thick pad of blocks standing in for voxel terrain.
            Vector3 pad = new Vector3(at.x - 4f, 0f, at.z - 16f); // corner
            for (int i = 0; i < 8; i++)
            for (int k = 0; k < 8; k++)
                DiggableBlockAt(new Vector3(pad.x + i + 0.5f, 0.5f, pad.z + k + 0.5f), parent);
            Box("Dig pad step", new Vector3(pad.x + 4f, 0.25f, pad.z - 0.4f), new Vector3(8f, 0.5f, 0.8f), matTrim, parent);
            Label("diggable ground\nbuild on it, then hold\nright mouse to dig", new Vector3(pad.x + 4f, 3f, pad.z - 1f), parent);

            // Two diggable cliffs, 30 m apart: wood can't bridge that without a pillar, iron can.
            foreach (float side in new[] { -1f, 1f })
            {
                float inner = at.x + side * 15f;
                for (int i = 0; i < 3; i++)
                for (int k = 0; k < 3; k++)
                for (int y = 0; y < 4; y++)
                    DiggableBlockAt(new Vector3(inner + side * (i + 0.5f), y + 0.5f, at.z - 1f + k), parent);
            }
            Label("30 m gap\nbridge it", new Vector3(at.x, 6f, at.z - 2f), parent);
        }

        static void DiggableBlockAt(Vector3 center, Transform parent)
        {
            GameObject go = Box("Dirt", center, Vector3.one, matDirt, parent);
            GameObjectUtility.SetStaticEditorFlags(go, 0); // it gets removed at runtime
            go.AddComponent<DiggableBlock>();
        }

        static void BuildPlayer(Vector3 origin)
        {
            var player = new GameObject("Player");
            player.transform.position = origin;
            player.AddComponent<LocalPlayerInput>();
            var motor = player.AddComponent<PlayerMotor>();
            motor.SetPreset(MovePreset.ShooterMash);
            player.AddComponent<PlayerVitals>();
            player.AddComponent<PlayerDebugHud>();
            player.AddComponent<BuildTool>();
            player.AddComponent<LiftTool>();
            player.AddComponent<VehicleDriver>();

            var camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0, motor.movement.standViewHeight, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 1000f;
            camGo.AddComponent<AudioListener>();
            var view = camGo.AddComponent<PlayerView>();
            view.motor = motor;
            view.input = player.GetComponent<LocalPlayerInput>();
        }

        static void BuildLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.46f);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        }

        // ------------------------------------------------------------------ helpers

        static Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(area != null ? area : root, false);
            return t;
        }

        static Transform Area(string name, Vector3 origin)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.localPosition = origin;
            return t;
        }

        static GameObject Box(string name, Vector3 center, Vector3 size, Material mat, Transform parent = null, Quaternion? rotation = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : root, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size);
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<BoxCollider>().size = size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        /// <summary>A box mesh whose UVs tile the dev grid at a fixed world size, regardless of the box size.</summary>
        static Mesh BoxMesh(Vector3 size)
        {
            string key = $"{size.x:0.###}x{size.y:0.###}x{size.z:0.###}";
            if (MeshCache.TryGetValue(key, out Mesh cached))
                return cached;

            Vector3 h = size * 0.5f;
            // For each face: outward normal n, and in-plane axes u, v with Cross(v, u) == n,
            // which gives clockwise (front-facing) winding in Unity.
            (Vector3 n, Vector3 u, Vector3 v)[] faces =
            {
                (Vector3.right, Vector3.forward, Vector3.up),
                (Vector3.left, Vector3.back, Vector3.up),
                (Vector3.up, Vector3.right, Vector3.forward),
                (Vector3.down, Vector3.left, Vector3.forward),
                (Vector3.forward, Vector3.left, Vector3.up),
                (Vector3.back, Vector3.right, Vector3.up),
            };

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (var (n, u, v) in faces)
            {
                float hn = Mathf.Abs(Vector3.Dot(h, n));
                float hu = Mathf.Abs(Vector3.Dot(h, u));
                float hv = Mathf.Abs(Vector3.Dot(h, v));
                int b = verts.Count;
                foreach (var (su, sv) in new[] { (-1f, -1f), (-1f, 1f), (1f, 1f), (1f, -1f) })
                {
                    Vector3 p = n * hn + u * (su * hu) + v * (sv * hv);
                    verts.Add(p);
                    normals.Add(n);
                    uvs.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)) / GridSize);
                }
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }

            var mesh = new Mesh { name = "Box " + key };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            AssetDatabase.AddObjectToAsset(mesh, meshContainer);
            MeshCache[key] = mesh;
            return mesh;
        }

        static void Label(string text, Vector3 position, Transform parent, float yaw = 0f)
        {
            var go = new GameObject("Label " + text.Split('\n')[0]);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = labelFont;
            tm.fontSize = 64;
            tm.characterSize = 0.03f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.9f, 0.6f);
            go.GetComponent<MeshRenderer>().sharedMaterial = labelFont.material;
        }

        // ------------------------------------------------------------------ assets

        static void CreateMaterials()
        {
            Texture2D grid = CreateGridTexture();
            matFloor = Mat("DevFloor", grid, new Color(0.55f, 0.55f, 0.55f));
            matWall = Mat("DevWall", grid, new Color(0.72f, 0.62f, 0.48f));
            matTrim = Mat("DevTrim", grid, new Color(0.5f, 0.56f, 0.62f));
            matLadder = Mat("DevLadder", grid, new Color(0.95f, 0.55f, 0.15f));
            matSlick = Mat("DevSlick", grid, new Color(0.45f, 0.75f, 1f), 0.9f);
            matTarget = Mat("DevTarget", grid, new Color(0.85f, 0.2f, 0.2f));
            matWater = Mat("DevWater", null, new Color(0.15f, 0.4f, 0.6f, 0.45f), 0.95f);
            matDirt = Mat("DevDirt", grid, new Color(0.5f, 0.38f, 0.26f));
            MakeTransparent(matWater);
            matSlime = Mat("DevSlime", null, new Color(0.35f, 0.75f, 0.15f, 0.6f), 0.95f);
            MakeTransparent(matSlime);
        }

        static Material Mat(string name, Texture2D tex, Color color, float smoothness = 0.15f)
        {
            string path = $"{ArtFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void MakeTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
        }

        /// <summary>A Quake-style developer grid: one 1 m square per tile, with 25 cm subdivisions.</summary>
        static Texture2D CreateGridTexture()
        {
            string path = ArtFolder + "/DevGrid.asset";
            AssetDatabase.DeleteAsset(path);

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "DevGrid" };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                byte c = 235;
                bool major = x < 3 || y < 3;
                bool minor = x % 64 == 0 || y % 64 == 0;
                if (major) c = 120;
                else if (minor) c = 195;
                // A faint checker so motion is readable at a glance.
                if (!major && !minor && ((x / 128) + (y / 128)) % 2 == 0) c = 222;
                pixels[y * size + x] = new Color32(c, c, c, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path))
                return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
