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
    /// Builds the graybox test map: a spawn hub with portals and roads to three areas. Character
    /// tests (stairs, ramps, jump crates, crouch and prone tunnels, a lean corner, ladders,
    /// fall-damage towers, water and slime tanks, a slick floor), building (the showcase,
    /// diggable dirt, bridge cliffs) and off-road (a high-speed rally loop and stunt tests: crash,
    /// flip, loop). Laid out in metres on a 1 m dev grid.
    /// </summary>
    public static class MovementTestMapBuilder
    {
        const float GridSize = 1f; // metres per dev-grid tile
        const string ScenePath = "Assets/Scenes/MovementTest.unity";
        const string ArtFolder = "Assets/Art/Dev";
        const string MeshAssetPath = ArtFolder + "/MovementTestMeshes.asset";

        // The hub (spawn, a buggy, portals) is at the origin; the areas are a short drive away.
        static readonly Vector3 CharacterArea = new Vector3(0f, 0f, 140f);
        static readonly Vector3 BuildingArea = new Vector3(-190f, 0f, 0f);
        static readonly Vector3 OffRoadArea = new Vector3(0f, 0f, -200f);
        // Where you arrive (feet) and which way you face, by portal or road.
        static readonly Vector3 HubArrival = new Vector3(0f, 0f, -4f);
        static readonly (Vector3 at, float yaw) CharacterArrival = (new Vector3(0f, 0f, 140f), 0f); // the middle of the area
        static readonly (Vector3 at, float yaw) BuildingArrival = (new Vector3(-134f, 0f, -18f), 0f);
        // The showcase structures in the building area's space, spread out with room between them.
        static readonly Vector3 FarmCorner = new Vector3(34f, 0f, 24f);
        static readonly Vector3 BarnCorner = new Vector3(60f, 0f, 24f);
        static readonly Vector3 TowerCorner = new Vector3(84f, 0f, 44f);
        static readonly (Vector3 at, float yaw) OffRoadArrival = (new Vector3(-200f, 0f, -98f), 180f); // facing the rally truck

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
            Box("Floor", new Vector3(0, -0.4f, 0), new Vector3(720, 0.8f, 720), matFloor);

            // Three test areas around a hub at the origin, each in its own space.
            // Character tests: you arrive in the middle, with the tests in groups around you. The
            // south group leaves a gap for the road from the hub.
            area = Area("Character tests", CharacterArea);
            // North: stepping and climbing.
            BuildStairs(new Vector3(-22, 0, 22));
            BuildRamps(new Vector3(6, 0, 22));
            BuildJumpCrates(new Vector3(-6, 0, 42));
            GroupLabel("stairs, ramps\nand jump crates", new Vector3(0, 6f, 18f));
            // East: liquids and slick floor.
            BuildLiquidTank(new Vector3(40, 0, -2), Contents.Water, matWater, "water");
            BuildLiquidTank(new Vector3(40, 0, 18), Contents.Slime, matSlime, "slime\nslower swimming");
            BuildSlickFloor(new Vector3(47, 0, -20));
            GroupLabel("water, slime\nand a slick floor", new Vector3(26f, 6f, 0f), 90f);
            // West: crouching, prone and leaning.
            BuildTunnels(new Vector3(-40, 0, -5));
            BuildLeanCorner(new Vector3(-34, 0, 20));
            GroupLabel("tunnels (crouch, prone)\nand leaning", new Vector3(-20f, 6f, 0f), -90f);
            // North-west: moving platforms to ride.
            BuildMovingPlatforms(new Vector3(-50, 0, 40));
            GroupLabel("moving platforms:\nelevator, slider, carousel", new Vector3(-36f, 7f, 30f), -45f);
            // South, either side of the road: heights.
            BuildLadderTower(new Vector3(-16, 0, -24));
            BuildFallTowers(new Vector3(10, 0, -24));
            GroupLabel("ladders and\nfall damage", new Vector3(0f, 6f, -18f), 180f);

            area = Area("Building", BuildingArea);
            BuildBuildingArea(new Vector3(56, 0, 8));

            area = Area("Off-road", OffRoadArea);
            BuildOffRoad();
            BuildCrashTest(new Vector3(-100f, 0f, 112f));
            BuildVehicleLineup();
            BuildFlipTest(new Vector3(40f, 0f, 125f));
            BuildLoopTest(new Vector3(40f, 0f, 150f));

            area = Area("Hub", Vector3.zero);
            BuildHub();
            area = null;

            BuildPlayer(HubArrival + new Vector3(0, 0.65f, 0));
            BuildVehicleSounds();

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

        /// <summary>
        /// Platforms that move, to test riding them: an elevator up to a 6 m ledge, a platform
        /// sliding 16 m between two 1 m pads, and a slowly turning carousel.
        /// </summary>
        static void BuildMovingPlatforms(Vector3 at)
        {
            var parent = Group("Moving platforms");

            // Elevator: from the ground up to the top of a 6 m block, and back.
            Box("Elevator ledge", new Vector3(at.x - 5f, 3f, at.z + 4f), new Vector3(3f, 6f, 3f), matWall, parent);
            Platform("Elevator", new Vector3(at.x - 5f, 0.15f, at.z + 1f), new Vector3(3f, 0.3f, 3f), parent,
                MovingPlatform.Motion.BackAndForth, new Vector3(0f, 6f, 0f));
            Label("elevator", new Vector3(at.x - 5f, 2f, at.z - 1f), parent);

            // Slider: between two 1 m pads, at their height.
            Box("Slider pad A", new Vector3(at.x - 2f, 0.5f, at.z + 12f), new Vector3(3f, 1f, 3f), matTrim, parent);
            Box("Slider pad B", new Vector3(at.x + 22f, 0.5f, at.z + 12f), new Vector3(3f, 1f, 3f), matTrim, parent);
            Platform("Slider", new Vector3(at.x + 2f, 0.85f, at.z + 12f), new Vector3(3f, 0.3f, 3f), parent,
                MovingPlatform.Motion.BackAndForth, new Vector3(16f, 0f, 0f));
            Label("slider", new Vector3(at.x + 10f, 2.5f, at.z + 10f), parent);

            // Carousel: a turning 8 x 8 m deck.
            Platform("Carousel", new Vector3(at.x + 5f, 0.15f, at.z - 10f), new Vector3(8f, 0.3f, 8f), parent,
                MovingPlatform.Motion.Turn, Vector3.zero);
            Label("carousel", new Vector3(at.x + 5f, 2f, at.z - 15f), parent);
        }

        static void Platform(string name, Vector3 center, Vector3 size, Transform parent, MovingPlatform.Motion motion, Vector3 travel)
        {
            GameObject go = Box(name, center, size, matTarget, parent);
            GameObjectUtility.SetStaticEditorFlags(go, 0); // it moves
            go.AddComponent<Rigidbody>().isKinematic = true;
            var platform = go.AddComponent<MovingPlatform>();
            platform.motion = motion;
            platform.travel = travel;
            platform.speed = 2f;
            platform.degreesPerSecond = 25f;
        }

        /// <summary>A big label naming a group of tests, readable from the middle of the area.</summary>
        static void GroupLabel(string text, Vector3 position, float yaw = 0f)
        {
            var parent = Group("Label " + text.Split('\n')[0]);
            Label(text, position, parent, yaw);
            parent.GetComponentInChildren<TextMesh>().characterSize = 0.06f;
        }

        // Bumps, each laid out along +z of `parent` from `z`, across `width`. They return where they end.

        /// <summary>Low ridges a metre apart (boxes turned 45°: 21 cm tall).</summary>
        static float Washboard(Transform parent, float z, int count, float width)
        {
            for (int i = 0; i < count; i++)
                Box($"Washboard {i}", new Vector3(0f, 0f, z + i), new Vector3(width, 0.3f, 0.3f), i % 2 == 0 ? matTrim : matWall,
                    parent, Quaternion.Euler(45f, 0f, 0f));
            return z + count - 1f;
        }

        /// <summary>Ridges (35 cm tall at the default size) under one side at a time, `spacing` apart, so the body rolls left and right.</summary>
        static float SideRidges(Transform parent, float z, int count, float width, float spacing = 2f, float size = 0.5f)
        {
            for (int i = 0; i < count; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                Box($"Side ridge {i}", new Vector3(side * width / 4f, 0f, z + i * spacing), new Vector3(width / 2f, size, size),
                    i % 2 == 0 ? matWall : matTrim, parent, Quaternion.Euler(45f, 0f, 0f));
            }
            return z + (count - 1) * spacing;
        }

        /// <summary>Boxes of different sizes tilted up to `tilt` degrees, half buried, over `length` m. Seeded, so the map is the same every build.</summary>
        static float Rocks(Transform parent, float z, float length, float width, int count, float minHeight, float maxHeight, int seed, float tilt = 20f)
        {
            var random = new System.Random(seed);
            float F(float min, float max) => min + (float)random.NextDouble() * (max - min);
            for (int i = 0; i < count; i++)
            {
                var size = new Vector3(F(0.6f, 1.6f), F(minHeight, maxHeight), F(0.6f, 1.6f));
                var center = new Vector3(F(-width / 2f, width / 2f), F(-0.15f, 0.1f), z + F(0f, length));
                Box($"Rock {i}", center, size, matTrim, parent, Quaternion.Euler(F(-tilt, tilt), F(0f, 90f), F(-tilt, tilt)));
            }
            return z + length;
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
        /// A long off-road loop for high speed (about 1160 m, 20 m wide, driven clockwise): two 400 m
        /// straights joined by 70 m bends. The north straight has small rocks, washboard, long
        /// rollers and a long table-top jump; the south one side ridges, bigger rollers and rocks.
        /// The bumps are sized for the big wheels: they toss the truck about but rarely touch its
        /// chassis, so it can still reach 150 km/h. A rally
        /// truck on big wheels waits at the start and only drives while someone stands on it,
        /// up to 44 m/s (about 158 km/h).
        /// </summary>
        static void BuildOffRoad()
        {
            var parent = Group("Off-road track");
            parent.localPosition = new Vector3(-200f, 0f, 80f);
            const float width = 20f;
            var track = new TrackBuilder(parent, width, 4f);
            Label("off-road track\nstart", new Vector3(-8f, 4f, 11f), parent, 180f);
            Box("Start line", new Vector3(1f, 0.01f, 0f), new Vector3(1f, 0.02f, width), matSlick, parent);

            // North straight, heading +x: 400 m. A flat run-up with small rocks (the suspension
            // soaks them up), washboard, long gentle rollers, then the jump at full speed, and room
            // to brake for the bend.
            track.Straight(20f);
            Rocks(track.Section("Small rocks"), 0f, 40f, width - 2f, 40, 0.1f, 0.25f, 21, 6f);
            track.Straight(40f);
            track.Straight(20f);
            Washboard(track.Section("Washboard"), 0f, 15, width);
            track.Straight(60f);
            Rollers(track.Section("Rollers"), 0f, 3, width, 0.5f, 2f, 10f);
            track.Straight(110f);
            track.Straight(15f);
            Transform jump = track.Section("Jump");
            float z = Slope(jump, "Jump ramp", 0f, 4f, 0.8f, width, 0.3f, matTarget);   // full width: no edge to catch
            Box("Jump table", new Vector3(0f, 0.4f, z + 12.5f), new Vector3(width, 0.8f, 25f), matTrim, jump);
            Slope(jump, "Landing ramp", z + 25f, -4f, 0.8f, width, 0.3f, matWall);
            Label("jump", new Vector3(0f, 3f, 0f), jump, 0f);
            track.Straight(135f);

            track.Arc(70f, -90f);                 // right
            track.Straight(20f);
            track.Arc(70f, -90f);

            // South straight, heading -x: 400 m. Side ridges, big rollers, more small rocks.
            track.Straight(40f);
            SideRidges(track.Section("Side ridges"), 0f, 8, width, 6f, 0.35f);
            track.Straight(90f);
            Rollers(track.Section("Big rollers"), 0f, 3, width, 0.6f, 3f, 12f);
            track.Straight(110f);
            Rocks(track.Section("Small rocks 2"), 0f, 50f, width - 2f, 50, 0.08f, 0.18f, 23, 6f);
            track.Straight(160f);

            track.Arc(70f, -90f);
            track.Straight(20f);
            track.Arc(70f, -90f);

            var line = new List<Vector3>();
            foreach (Vector3 p in track.Line)
                line.Add(parent.TransformPoint(p));
            if (Vector3.Distance(line[0], line[line.Count - 1]) < 0.1f)
                line.RemoveAt(line.Count - 1);

            var truck = new GameObject("Rally truck").AddComponent<VehicleShowcase>();
            truck.design = VehicleShowcase.Design.RallyTruck;
            truck.autopilot = true;
            truck.autopilotPath = line.ToArray();
            truck.autopilotMaxSpeed = 44f;
            truck.autopilotStopSeconds = 0f;
            truck.autopilotWaitForRider = true;
            truck.buggyPosition = line[0];
            truck.buggyYaw = 90f;
            Label("rally truck: stand on the back and it drives\n(up to 150 km/h); step off and it stops",
                new Vector3(0f, 4.5f, 11f), parent, 180f);
        }

        const string SoundBankPath = "Assets/Audio/Vehicle sounds.asset";

        /// <summary>
        /// The engine sound bank: made with a first guess of which recorded loop suits which
        /// engine if it doesn't exist yet (then it's left alone, so changes made in the inspector
        /// stay), and put in use by a VehicleSounds object.
        /// </summary>
        static void BuildVehicleSounds()
        {
            var bank = AssetDatabase.LoadAssetAtPath<VehicleSoundBank>(SoundBankPath);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<VehicleSoundBank>();
                const string loops = "Assets/Audio/Motor Loops/";
                (string engine, string file, float idle, float redline, float volume)[] guesses =
                {
                    ("Engine 4-cyl", "freesound_community-motor-loop-3-83478.mp3", 0.75f, 2.2f, 1f),
                    ("Engine V6", "freesound_community-motor-loop-2-103532.mp3", 0.7f, 2f, 1f),
                    ("Engine V8", "freesound_community-engine-47745.mp3", 0.75f, 2.3f, 0.8f),
                    ("Engine diesel", "freesound_community-bus-engine-looped-88729.mp3", 0.8f, 1.7f, 0.9f),
                };
                foreach ((string engine, string file, float idle, float redline, float volume) in guesses)
                    bank.engines.Add(new VehicleSoundBank.EngineSound
                    {
                        engine = engine,
                        clip = AssetDatabase.LoadAssetAtPath<AudioClip>(loops + file),
                        idlePitch = idle,
                        redlinePitch = redline,
                        volume = volume,
                    });
                AssetDatabase.CreateAsset(bank, SoundBankPath);
            }
            var go = new GameObject("Vehicle sounds");
            go.transform.SetParent(root, false);
            go.AddComponent<VehicleSounds>().bank = bank;
        }

        /// <summary>
        /// A lineup of vehicles to drive, west of the off-road arrival, facing the rally track
        /// (south): sit in one (F) and drive onto the track. Each has a sign with its drive,
        /// engine and weight.
        /// </summary>
        static void BuildVehicleLineup()
        {
            var parent = Group("Vehicle lineup");
            (VehicleShowcase.Design design, float x, string sign)[] lineup =
            {
                (VehicleShowcase.Design.Buggy, -212f, "buggy\nrear-wheel drive\nV6, 200 kW, 0.6 t"),
                (VehicleShowcase.Design.Saloon, -220f, "saloon\nfront-wheel drive\nV6, 200 kW, 1.6 t"),
                (VehicleShowcase.Design.Van, -228f, "van\nrear-wheel drive\nV8, 320 kW, 3.7 t"),
                (VehicleShowcase.Design.RallyTruck, -237f, "rally truck\nall-wheel drive\n2 x V8, 640 kW, 2 t"),
                (VehicleShowcase.Design.HeavyTruck, -247f, "heavy truck, 8x8\n15 t of iron\ndiesel, 420 kW, 20 t"),
                (VehicleShowcase.Design.Semi, -258f, "semi, 6x4\ntrailer with 13 t of stone\ndiesel, 420 kW, 19 t"),
            };
            foreach ((VehicleShowcase.Design design, float x, string sign) in lineup)
            {
                var vehicle = new GameObject($"Lineup {design}").AddComponent<VehicleShowcase>();
                vehicle.design = design;
                vehicle.buggyPosition = new Vector3(x, 0f, -100f);
                vehicle.buggyYaw = 180f;
                Label(sign, new Vector3(x, 6.5f, -100f + 200f), parent, 180f);
            }
        }

        /// <summary>
        /// A crash test north of the off-road loop, heading +x: a flatbed on big wheels (no cab)
        /// waits at the start; stand on it and it drives 90 km/h into a concrete block 100 m on,
        /// throwing you off the front. The block is narrower than the truck's wheel track, so
        /// the wheels pass either side and the chassis hits it (wheels would drive up a wider
        /// one), and lower than the deck, so riders fly over it. The truck is put back once
        /// you're off it.
        /// </summary>
        static void BuildCrashTest(Vector3 start)
        {
            var parent = Group("Crash test");
            parent.localPosition = start;
            const float run = 100f;
            Box("Crash lane", new Vector3(run / 2f, 0.005f, 0f), new Vector3(run + 8f, 0.01f, 4f), matSlick, parent);
            Box("Crash block", new Vector3(run + 1.5f, 0.5f, 0f), new Vector3(3f, 1f, 2.4f), matWall, parent);
            Label("crash test: stand on the truck,\nit drives 90 km/h into the block ahead", new Vector3(-2f, 4f, 4f), parent, 180f);
            Label("crash block", new Vector3(run + 1.5f, 2.5f, 0f), parent, -90f);

            var truck = new GameObject("Crash truck").AddComponent<VehicleShowcase>();
            truck.design = VehicleShowcase.Design.StuntTruck;
            truck.stuntSpeed = 25f;
            truck.buggyPosition = parent.TransformPoint(Vector3.zero);
            truck.buggyYaw = 90f;
        }

        /// <summary>
        /// A flip test beside the crash test, heading +x: stand on the stunt truck and it drives
        /// 90 km/h at a steep kicker under its right wheels only, which throws it onto its roof
        /// (its self-levelling assists are off: they would catch it).
        /// Shows what happens to a rider when a vehicle goes over. The truck is put back once
        /// you're off it.
        /// </summary>
        static void BuildFlipTest(Vector3 start)
        {
            var parent = Group("Flip test");
            parent.localPosition = start;
            const float run = 80f;
            Box("Flip lane", new Vector3(run / 2f + 20f, 0.005f, 0f), new Vector3(run + 48f, 0.01f, 4f), matSlick, parent);
            // The kicker: a ramp along +x under the right-hand wheels (the truck faces +x, so its right is -z).
            var kicker = new GameObject("Kicker").transform;
            kicker.SetParent(parent, false);
            kicker.SetLocalPositionAndRotation(new Vector3(run, 0f, -1.8f), Quaternion.Euler(0f, 90f, 0f));
            Slope(kicker, "Kicker ramp", 0f, 35f, 2.5f, 1.6f, 0.3f, matTarget);
            Label("flip test: stand on the truck, it hits a kicker\nunder one side at 90 km/h (no self-levelling)",new Vector3(-2f, 4f, 4f), parent, 180f);
            Label("kicker", new Vector3(run, 2.5f, -1.8f), parent, -90f);

            var truck = new GameObject("Flip truck").AddComponent<VehicleShowcase>();
            truck.design = VehicleShowcase.Design.StuntTruck;
            truck.stuntSpeed = 25f;
            truck.stuntRunLength = run + 40f;
            truck.stuntUprightAssists = false;   // they would catch it: the point is to see it roll
            truck.buggyPosition = parent.TransformPoint(Vector3.zero);
            truck.buggyYaw = 90f;
        }

        /// <summary>
        /// A loop test, heading +x: stand on the stunt truck and it drives 108 km/h through a
        /// 26 m tall loop and out the other side. Like real loops it's a teardrop, a 20 m radius at
        /// the bottom tightening to 6 m at the top, and the bend eases in and out, so the bottom
        /// isn't one sudden jolt (it still pulls about 4 g: the stunt truck has stiff springs). A
        /// flat loop would block its own way in, so it shifts 7 m to the left as it goes round and
        /// the exit lane runs beside the entry lane. The truck steers along the loop's centre line,
        /// in its own frame, so it keeps steering upside down.
        /// </summary>
        static void BuildLoopTest(Vector3 start)
        {
            var parent = Group("Loop test");
            parent.localPosition = start;
            const float approach = 90f, bottomRadius = 20f, topRadius = 6f, ease = 25f * Mathf.Deg2Rad;
            const float shift = 7f, width = 5f, thick = 0.4f, exit = 70f;

            // Lay the running surface out by bending a line: its heading turns from 0 to a full
            // circle, the bend's radius depending on the heading (wide at the bottom, tight at the
            // top) and easing in from straight over the first and last `ease` of the turn.
            var surface = new List<Vector3> { new Vector3(approach, 0f, 0f) };
            {
                const float ds = 0.05f, every = 1f;
                float heading = 0f, sinceLast = 0f;
                Vector2 p = Vector2.zero;
                while (heading < 2f * Mathf.PI)
                {
                    float radius = topRadius + (bottomRadius - topRadius) * (1f + Mathf.Cos(heading)) / 2f;
                    float easing = Mathf.Clamp(Mathf.Min(heading, 2f * Mathf.PI - heading) / ease, 0.05f, 1f);
                    heading = Mathf.Min(heading + ds * easing / radius, 2f * Mathf.PI);
                    p += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * ds;
                    sinceLast += ds;
                    if (sinceLast >= every || heading >= 2f * Mathf.PI)
                    {
                        surface.Add(new Vector3(approach + p.x, p.y, shift * heading / (2f * Mathf.PI)));
                        sinceLast = 0f;
                    }
                }
            }
            Vector3 loopEnd = surface[surface.Count - 1];
            loopEnd.y = 0f;
            surface[surface.Count - 1] = loopEnd;

            Box("Loop entry lane", new Vector3(approach / 2f - 4f, 0.005f, 0f), new Vector3(approach + 8f, 0.01f, width), matSlick, parent);
            Box("Loop exit lane", new Vector3(loopEnd.x + exit / 2f, 0.005f, shift), new Vector3(exit, 0.01f, width), matSlick, parent);
            for (int i = 0; i + 1 < surface.Count; i++)
            {
                Vector3 p0 = surface[i], p1 = surface[i + 1], along = p1 - p0;
                // The surface's inside faces the loop's middle: its normal is the heading turned
                // a quarter turn up (left in the vertical plane).
                Vector3 inward = Vector3.Cross(new Vector3(0f, 0f, 1f), new Vector3(along.x, along.y, 0f)).normalized;
                Box($"Loop {i}", (p0 + p1) / 2f - inward * (thick / 2f), new Vector3(width, thick, along.magnitude + 0.15f),
                    i % 2 == 0 ? matTarget : matTrim, parent, Quaternion.LookRotation(along, inward));
            }
            Label("loop test: stand on the truck,\nit drives 108 km/h through the loop", new Vector3(-2f, 4f, 4f), parent, 180f);

            // The line to steer along: the approach, round the loop, and out along the exit lane.
            var line = new List<Vector3>();
            for (float x = 0f; x < approach; x += 4f)
                line.Add(parent.TransformPoint(new Vector3(x, 0f, 0f)));
            foreach (Vector3 p in surface)
                line.Add(parent.TransformPoint(p));
            for (float x = 4f; x <= exit; x += 4f)
                line.Add(parent.TransformPoint(loopEnd + new Vector3(x, 0f, 0f)));

            float loopLength = surface.Count - 1f;   // samples are about a metre apart
            var truck = new GameObject("Loop truck").AddComponent<VehicleShowcase>();
            truck.design = VehicleShowcase.Design.StuntTruck;
            truck.stuntSpeed = 30f;
            truck.stuntRideHeight = 0.2f;   // stiff springs, or it bottoms out at the bottom of the loop
            truck.stuntPath = line.ToArray();
            truck.stuntRunLength = approach + loopLength + exit - 20f;
            truck.buggyPosition = parent.TransformPoint(Vector3.zero);
            truck.buggyYaw = 90f;
        }

        /// <summary>Long, gentle humps `height` tall rising and falling at `angle`, `gap` apart.</summary>
        static float Rollers(Transform parent, float z, int count, float width, float height, float angle, float gap)
        {
            for (int i = 0; i < count; i++)
            {
                if (i > 0) z += gap;
                z = Slope(parent, "Roller up", z, angle, height, width, 0.3f, matWall);
                z = Slope(parent, "Roller down", z, -angle, height, width, 0.3f, matTrim);
            }
            return z;
        }

        /// <summary>
        /// Lays out a track like a turtle, with painted kerbs along both edges: flat enough to
        /// drive over, so the track is a suggestion of turns rather than a channel you're stuck in.
        /// Keeps its centre line, and makes sections to put features in.
        /// </summary>
        sealed class TrackBuilder
        {
            const float KerbHeight = 0.06f, KerbThickness = 1f;
            readonly float sampleSpacing;

            readonly Transform parent;
            readonly float halfWidth;
            float heading;          // degrees from +x towards +z
            int kerbs;

            public Vector3 Position { get; private set; }
            public Vector3 Direction => new Vector3(Mathf.Cos(heading * Mathf.Deg2Rad), 0f, Mathf.Sin(heading * Mathf.Deg2Rad));
            Vector3 Left => new Vector3(-Mathf.Sin(heading * Mathf.Deg2Rad), 0f, Mathf.Cos(heading * Mathf.Deg2Rad));

            /// <summary>The centre line so far, in the parent's space, about 2 m apart.</summary>
            public readonly List<Vector3> Line = new List<Vector3> { Vector3.zero };

            public TrackBuilder(Transform parent, float width, float sampleSpacing = 2f)
            {
                this.parent = parent;
                halfWidth = width / 2f;
                this.sampleSpacing = sampleSpacing;
            }

            /// <summary>A child object at the current position facing along the track (its +z), to build features in.</summary>
            public Transform Section(string name)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.SetLocalPositionAndRotation(Position, Quaternion.LookRotation(Direction));
                return t;
            }

            public void Straight(float length)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / sampleSpacing));
                Vector3 from = Position, dir = Direction, left = Left;
                for (int i = 0; i < steps; i++)
                {
                    Vector3 a = from + dir * (length * i / steps), b = from + dir * (length * (i + 1) / steps);
                    Kerbs(a, b, left, left);
                    Line.Add(b);
                }
                Position = from + dir * length;
            }

            /// <summary>A bend of `degrees` (positive: left) on a centre line of `radius`.</summary>
            public void Arc(float radius, float degrees)
            {
                float sign = Mathf.Sign(degrees);
                Vector3 center = Position + Left * (radius * sign);
                float startHeading = heading;
                int steps = Mathf.Max(2, Mathf.CeilToInt(radius * Mathf.Abs(degrees) * Mathf.Deg2Rad / sampleSpacing));
                Vector3 previous = Position, previousLeft = Left;
                for (int i = 1; i <= steps; i++)
                {
                    heading = startHeading + degrees * i / steps;
                    Vector3 p = center - Left * (radius * sign);
                    Kerbs(previous, p, previousLeft, Left);
                    Line.Add(p);
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
            PortalPair("Off-road", new Vector3(0f, 0f, -9f), 180f, OffRoadArrival, parent, matSlime);

            // Roads to the areas, for driving there.
            Road(new Vector3(0f, 0f, 10f), CharacterArrival.at, parent);
            Road(new Vector3(-8f, 0f, 0f), BuildingArrival.at, parent);
            Road(new Vector3(0f, 0f, -14f), OffRoadArrival.at, parent);

            // A buggy to drive, facing east.
            var buggy = new GameObject("Hub buggy").AddComponent<VehicleShowcase>();
            buggy.buggyPosition = new Vector3(7f, 0f, -6f);
            buggy.buggyYaw = 90f;

            // An ice sheet east of the spawn, where the buggy is pointing: slick for players
            // (SurfaceFlags.Slick) and vehicles alike (wheels grip a fraction as well on it).
            var ice = Box("Ice sheet", new Vector3(50f, 0.05f, 0f), new Vector3(60f, 0.1f, 60f), matSlick, parent);
            ice.AddComponent<PmSurface>().flags = SurfaceFlags.Slick;
            Label("ice: slick for players and vehicles", new Vector3(20f, 3f, 0f), parent, 90f);
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
