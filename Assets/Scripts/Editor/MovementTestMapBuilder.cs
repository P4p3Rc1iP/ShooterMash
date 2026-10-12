using System.Collections.Generic;
using System.Linq;
using ShooterMashup.Building;
using ShooterMashup.Player;
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

        static Transform root;
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
            Box("Floor", new Vector3(0, -0.4f, 0), new Vector3(160, 0.8f, 160), matFloor);

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
            BuildBuildingArea(new Vector3(56, 0, 8));

            BuildPlayer(new Vector3(0, 0.65f, 0));

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

        static void BuildBuildingArea(Vector3 at)
        {
            var parent = Group("Building area");
            Label("building area\n2 / 3: build tool\n(2.5 m or 3 m kit)", new Vector3(at.x, 4f, at.z - 22f), parent);

            // Owns all placed pieces.
            var structures = new GameObject("Structures");
            structures.AddComponent<StructureManager>();

            // Two example structures, built from real pieces when play starts.
            var showcase = structures.AddComponent<BuildShowcase>();
            showcase.houseCorner = new Vector3(48f, 0f, 20f);
            showcase.pavilionCorner = new Vector3(48f, 0f, 35f);
            Label("2.5 m kit\nhouse", new Vector3(51.75f, 7f, 18.5f), parent);
            Label("3 m kit\npavilion", new Vector3(52.5f, 8.5f, 32f), parent);

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
            t.SetParent(root, false);
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
