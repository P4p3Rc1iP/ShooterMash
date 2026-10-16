using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// A big driving range of procedural terrain, made at run time in square tiles, so the scene
    /// stays small however big it is: the whole range in coarse tiles (8 m apart, no collider) to
    /// see it from afar, and full-detail tiles (2 m apart, with a collider) within buildRadius of
    /// the camera and round every vehicle, let go again once both are far away. Heights come from
    /// `Height`, a pure function of the position, so tiles meet seamlessly and the map builder can
    /// place things on the ground.
    ///
    /// The layout, seen from the west edge (where you arrive, on a flat apron level with the test
    /// floor): the south half is plains, nearly flat and wide open for cruising at full throttle;
    /// the north-west quarter is rolling hills (waves of 35 to 110 m, a few metres high); the
    /// north-east quarter is steep hills (tens of metres high, slopes up to about 35°). The zones
    /// blend over a few hundred metres. A berm runs round the outside so you can't drive off.
    ///
    /// Streaming doesn't stall the game: a tile's heights are worked out on a worker thread and its
    /// collider is cooked on one (Physics.BakeMesh); the main thread only makes the mesh. Tiles a
    /// vehicle is on or next to are made at once if they're missing.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after the demo vehicles are built (their Start), so ground is made under them
    public class DrivingTerrain : MonoBehaviour
    {
        [Tooltip("Size of the square terrain (m); its west edge is at this object's position, centred on its z.")]
        public float size = 4096f;
        [Tooltip("Tile size (m) and vertex spacing (m).")]
        public float tileSize = 256f, step = 2f;
        [Tooltip("Tiles are made within this distance (m) of the camera.")]
        public float buildRadius = 1300f;
        [Tooltip("Tiles worked out on worker threads at once, and meshes made per frame.")]
        public int inFlight = 6, meshesPerFrame = 2;
        [Tooltip("Half the width (m) of the opening in the west berm, facing the test floor.")]
        public float westOpening = 300f;
        public Material material;

        // The zones: a flat apron by the west edge, plains in the south, rolling hills north-west,
        // steep hills north-east, and the berm round the outside.
        const float Apron = 150f, ApronBlend = 250f;
        const float ZoneBlend = 300f;
        const float BermWidth = 70f, BermHeight = 40f;

        /// <summary>A tile's mesh data, worked out off the main thread.</summary>
        sealed class TileData
        {
            public Vector3[] vertices, normals;
            public Vector2[] uvs;
            public int[] triangles;   // null: the shared full-detail ones
        }

        readonly Dictionary<Vector2Int, GameObject> tiles = new Dictionary<Vector2Int, GameObject>();   // finished (with a collider)
        readonly Dictionary<Vector2Int, Task<TileData>> computing = new Dictionary<Vector2Int, Task<TileData>>();
        readonly Dictionary<Vector2Int, (GameObject go, Mesh mesh, Task bake)> baking = new Dictionary<Vector2Int, (GameObject, Mesh, Task)>();
        // Coarse tiles (no collider) cover the whole range, so it can be seen from afar; a
        // full-detail tile hides the coarse one under it.
        const float CoarseStep = 8f;
        readonly Dictionary<Vector2Int, GameObject> coarse = new Dictionary<Vector2Int, GameObject>();
        readonly Dictionary<Vector2Int, Task<TileData>> coarseComputing = new Dictionary<Vector2Int, Task<TileData>>();
        readonly List<Vector2Int> coarseQueue = new List<Vector2Int>();
        readonly List<Vector2Int> wanted = new List<Vector2Int>();
        readonly List<Vector2Int> done = new List<Vector2Int>();
        int tileCount, n;
        int[] triangles;   // the same for every tile

        void Start()
        {
            tileCount = Mathf.CeilToInt(size / tileSize);
            n = Mathf.RoundToInt(tileSize / step) + 1;
            triangles = Triangles(n);
            // Every tile in coarse detail, nearest the arrival first, so the whole range can be seen.
            for (int i = 0; i < tileCount; i++)
            for (int j = 0; j < tileCount; j++)
                coarseQueue.Add(new Vector2Int(i, j));
            coarseQueue.Sort((a, b) => Distance(b, Vector3.zero).CompareTo(Distance(a, Vector3.zero)));   // popped from the end
            Update();   // ground under the vehicles that start on it (the lineup) before they drop
        }

        void Update()
        {
            // Under every vehicle (the tile it's on and those round it): at once if missing.
            foreach (Vehicle v in Vehicle.All)
            {
                Wanted(v.transform.position, tileSize);
                foreach (Vector2Int key in wanted)
                    FinishNow(key);
            }

            // Around the camera: finished collider cooks, then new meshes, then new work, nearest first.
            done.Clear();
            foreach (KeyValuePair<Vector2Int, (GameObject go, Mesh mesh, Task bake)> b in baking)
                if (b.Value.bake.IsCompleted)
                    done.Add(b.Key);
            foreach (Vector2Int key in done)
                AddCollider(key);
            done.Clear();
            foreach (KeyValuePair<Vector2Int, Task<TileData>> c in computing)
                if (c.Value.IsCompleted && done.Count < meshesPerFrame)
                    done.Add(c.Key);
            foreach (Vector2Int key in done)
                StartBake(key);
            if (Camera.main != null && computing.Count < inFlight)
            {
                Wanted(Camera.main.transform.position, buildRadius);
                for (int k = 0; k < wanted.Count && computing.Count < inFlight; k++)
                    if (!computing.ContainsKey(wanted[k]) && !baking.ContainsKey(wanted[k]))
                    {
                        Vector2Int key = wanted[k];
                        computing[key] = Task.Run(() => Compute(key));
                    }
            }

            // Once a second: let go of full-detail tiles far from the camera and every vehicle (the
            // coarse tile shows again), so roaming the whole range doesn't keep it all in memory.
            if (Time.unscaledTime > nextUnload)
            {
                nextUnload = Time.unscaledTime + 1f;
                Unload();
            }

            // Coarse tiles in the background, with whatever workers the full-detail ones leave free.
            done.Clear();
            foreach (KeyValuePair<Vector2Int, Task<TileData>> c in coarseComputing)
                if (c.Value.IsCompleted && done.Count < meshesPerFrame)
                    done.Add(c.Key);
            foreach (Vector2Int key in done)
            {
                GameObject go = TileObject(key, coarseComputing[key].Result, "Driving terrain (far)");
                coarseComputing.Remove(key);
                go.GetComponent<MeshRenderer>().enabled = !tiles.ContainsKey(key);
                coarse[key] = go;
            }
            while (coarseQueue.Count > 0 && computing.Count + coarseComputing.Count < inFlight)
            {
                Vector2Int key = coarseQueue[coarseQueue.Count - 1];
                coarseQueue.RemoveAt(coarseQueue.Count - 1);
                int count = Mathf.RoundToInt(tileSize / CoarseStep) + 1;
                coarseComputing[key] = Task.Run(() => Grid(key, CoarseStep, count, true));
            }
        }

        float nextUnload;

        void Unload()
        {
            done.Clear();
            float keep = buildRadius + 2f * tileSize;
            foreach (Vector2Int key in tiles.Keys)
            {
                bool near = Camera.main != null && Distance(key, transform.InverseTransformPoint(Camera.main.transform.position)) < keep;
                foreach (Vehicle v in Vehicle.All)
                    near |= Distance(key, transform.InverseTransformPoint(v.transform.position)) < 3f * tileSize;
                if (!near)
                    done.Add(key);
            }
            foreach (Vector2Int key in done)
            {
                GameObject go = tiles[key];
                tiles.Remove(key);
                Destroy(go.GetComponent<MeshFilter>().sharedMesh);
                Destroy(go);
                if (coarse.TryGetValue(key, out GameObject far))
                    far.GetComponent<MeshRenderer>().enabled = true;
            }
        }

        /// <summary>Gathers the missing tiles within `radius` of a world point, nearest first.</summary>
        void Wanted(Vector3 point, float radius)
        {
            wanted.Clear();
            Vector3 around = transform.InverseTransformPoint(point);
            if (around.x < -radius || around.x > size + radius || Mathf.Abs(around.z) > size / 2f + radius)
                return;
            int r = Mathf.CeilToInt(radius / tileSize);
            int cx = Mathf.FloorToInt(around.x / tileSize), cz = Mathf.FloorToInt((around.z + size / 2f) / tileSize);
            for (int i = cx - r; i <= cx + r; i++)
            for (int j = cz - r; j <= cz + r; j++)
            {
                if (i < 0 || j < 0 || i >= tileCount || j >= tileCount) continue;
                var key = new Vector2Int(i, j);
                if (!tiles.ContainsKey(key) && Distance(key, around) <= radius + tileSize)
                    wanted.Add(key);
            }
            wanted.Sort((a, b) => Distance(a, around).CompareTo(Distance(b, around)));
        }

        float Distance(Vector2Int tile, Vector3 local) =>
            Vector2.Distance(new Vector2((tile.x + 0.5f) * tileSize, (tile.y + 0.5f) * tileSize - size / 2f), new Vector2(local.x, local.z));


        /// <summary>A full-detail tile's vertices, normals and uvs (thread-safe: only the height function).</summary>
        TileData Compute(Vector2Int tile) => Grid(tile, step, n, false);

        /// <summary>
        /// A tile's grid of `count` x `count` vertices `spacing` apart. Heights get a one-vertex border, so
        /// normals at the edges match the next tile's. A coarse tile also gets a skirt hanging down
        /// from its edges (and its own triangles), which hides the cracks where it meets a
        /// full-detail tile.
        /// </summary>
        TileData Grid(Vector2Int tile, float spacing, int count, bool skirt)
        {
            float x0 = tile.x * tileSize, z0 = tile.y * tileSize - size / 2f;
            int m = count + 2;
            var heights = new float[m * m];
            for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++)
                heights[i * m + j] = Height(x0 + (i - 1) * spacing, z0 + (j - 1) * spacing);
            int edgeCount = skirt ? 4 * (count - 1) : 0;
            int total = count * count + edgeCount;
            var data = new TileData { vertices = new Vector3[total], normals = new Vector3[total], uvs = new Vector2[total] };
            for (int i = 0; i < count; i++)
            for (int j = 0; j < count; j++)
            {
                int h = (i + 1) * m + (j + 1);
                data.vertices[i * count + j] = new Vector3(i * spacing, heights[h], j * spacing);
                data.normals[i * count + j] = new Vector3(heights[h - m] - heights[h + m], 2f * spacing, heights[h - 1] - heights[h + 1]).normalized;
                data.uvs[i * count + j] = new Vector2(x0 + i * spacing, z0 + j * spacing);
            }
            if (!skirt)
                return data;

            var tris = new List<int>(Triangles(count));
            // The edge loop (each edge vertex once), with a copy 5 m lower, joined by quads drawn both ways.
            var loop = new List<int>();
            for (int j = 0; j < count - 1; j++) loop.Add(j);                              // x = 0, along +z
            for (int i = 0; i < count - 1; i++) loop.Add(i * count + count - 1);          // z = max, along +x
            for (int j = count - 1; j > 0; j--) loop.Add((count - 1) * count + j);       // x = max, back along z
            for (int i = count - 1; i > 0; i--) loop.Add(i * count);                     // z = 0, back along x
            int first = count * count;
            for (int k = 0; k < loop.Count; k++)
            {
                int top = loop[k];
                data.vertices[first + k] = data.vertices[top] - Vector3.up * 5f;
                data.normals[first + k] = data.normals[top];
                data.uvs[first + k] = data.uvs[top];
            }
            for (int k = 0; k < loop.Count; k++)
            {
                int a = loop[k], b = loop[(k + 1) % loop.Count], c = first + k, d = first + (k + 1) % loop.Count;
                tris.AddRange(new[] { a, b, d, a, d, c, a, d, b, a, c, d });
            }
            data.triangles = tris.ToArray();
            return data;
        }

        /// <summary>Two triangles per square of a `count` x `count` grid.</summary>
        static int[] Triangles(int count)
        {
            var triangles = new int[(count - 1) * (count - 1) * 6];
            int t = 0;
            for (int i = 0; i < count - 1; i++)
            for (int j = 0; j < count - 1; j++)
            {
                int a = i * count + j, b = a + 1, c = a + count, e = c + 1;   // b is +z of a, c is +x of a
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = e;
                triangles[t++] = a; triangles[t++] = e; triangles[t++] = c;
            }
            return triangles;
        }

        /// <summary>A tile's object: its mesh, drawn (no collider).</summary>
        GameObject TileObject(Vector2Int key, TileData data, string name)
        {
            var mesh = new Mesh { name = $"{name} {key.x},{key.y}" };
            mesh.SetVertices(data.vertices);
            mesh.SetNormals(data.normals);
            mesh.SetUVs(0, data.uvs);
            mesh.SetTriangles(data.triangles ?? triangles, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(mesh.name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(key.x * tileSize, 0f, key.y * tileSize - size / 2f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>Makes the tile's mesh and object (drawn, no collider yet) and starts cooking its collider.</summary>
        void StartBake(Vector2Int key)
        {
            TileData data = computing[key].Result;
            computing.Remove(key);
            GameObject go = TileObject(key, data, "Driving terrain");
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            EntityId id = mesh.GetEntityId();
            baking[key] = (go, mesh, Task.Run(() => Physics.BakeMesh(id, false)));
        }

        /// <summary>Gives a tile its (cooked) collider: it's finished.</summary>
        void AddCollider(Vector2Int key)
        {
            (GameObject go, Mesh mesh, Task bake) = baking[key];
            baking.Remove(key);
            if (!bake.IsCompleted)
                bake.Wait();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;
            tiles[key] = go;
            if (coarse.TryGetValue(key, out GameObject far))
                far.GetComponent<MeshRenderer>().enabled = false;
        }

        /// <summary>A tile needed now (a vehicle is on it): whatever stage it's at, finish it.</summary>
        void FinishNow(Vector2Int key)
        {
            if (tiles.ContainsKey(key))
                return;
            if (!computing.ContainsKey(key) && !baking.ContainsKey(key))
                computing[key] = Task.FromResult(Compute(key));
            if (computing.ContainsKey(key))
            {
                computing[key].Wait();
                StartBake(key);
            }
            AddCollider(key);
        }

        /// <summary>
        /// The ground's height (m) at a point in the terrain's space: x east from the west edge
        /// (0..size), z north from the middle (-size/2..size/2). Pure and thread-safe.
        /// </summary>
        public float Height(float x, float z)
        {
            float half = size / 2f;
            // Zone weights: plains in the south, hills in the north, rolling west and steep east.
            float north = Smooth(-ZoneBlend / 2f, ZoneBlend / 2f, z);
            float east = Smooth(size / 2f - ZoneBlend, size / 2f + ZoneBlend, x);
            float h = 0f;
            if (north < 1f) h += (1f - north) * Plains(x, z);
            if (north > 0f && east < 1f) h += north * (1f - east) * Rolling(x, z);
            if (north > 0f && east > 0f) h += north * east * Steep(x, z);
            // Flat by the west edge, where you arrive level with the test floor.
            h *= Smooth(Apron, Apron + ApronBlend, x);
            // The berm round the outside (open towards the test floor in the middle of the west edge).
            float edge = Mathf.Min(size - x, half - Mathf.Abs(z));
            if (Mathf.Abs(z) > westOpening)
                edge = Mathf.Min(edge, x);
            h += BermHeight * Smooth(BermWidth, 0f, edge);
            return h;
        }

        /// <summary>Nearly flat: long, low swells (about ±1.5 m over hundreds of metres).</summary>
        static float Plains(float x, float z) =>
            3f * (Noise(x / 450f + 11.3f, z / 450f + 7.1f) - 0.5f)
            + 0.6f * (Noise(x / 120f + 3.7f, z / 120f + 1.9f) - 0.5f);

        /// <summary>Rolling hills like the old test hills: waves 35 to 110 m long, a few metres high.</summary>
        static float Rolling(float x, float z)
        {
            float Wave(float dx, float dz, float length, float phase) =>
                Mathf.Sin(2f * Mathf.PI * (x * dx + z * dz) / length + phase);
            return 1.8f * Wave(0.8f, 0.6f, 110f, 0.3f) + 1.2f * Wave(-0.5f, 0.87f, 70f, 1.7f)
                   + 0.4f * Wave(0.95f, 0.3f, 35f, 4.1f)
                   + 6f * (Noise(x / 300f + 5.2f, z / 300f + 9.4f) - 0.5f);
        }

        /// <summary>Steep hills: big rounded hills tens of metres high with smaller ones on them, slopes up to about 35°.</summary>
        static float Steep(float x, float z)
        {
            float big = Noise(x / 260f + 2.1f, z / 260f + 4.3f);
            float mid = Noise(x / 95f + 8.8f, z / 95f + 6.6f);
            float small = Noise(x / 40f + 1.4f, z / 40f + 3.3f);
            return 70f * big * big + 22f * mid + 4f * small - 28f;
        }

        static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

        // ------------------------------------------------------------------ noise

        static readonly int[] Perm = MakePermutation();

        static int[] MakePermutation()
        {
            var p = new int[512];
            var random = new System.Random(1234);
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int k = random.Next(i + 1);
                (p[i], p[k]) = (p[k], p[i]);
            }
            for (int i = 0; i < 256; i++) p[256 + i] = p[i];
            return p;
        }

        /// <summary>
        /// 2D gradient (Perlin) noise, about 0..1 around 0.5 like Mathf.PerlinNoise, but plain C#:
        /// safe on worker threads, and the same on every platform.
        /// </summary>
        public static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            int X = xi & 255, Y = yi & 255;
            float u = Fade(xf), v = Fade(yf);
            int aa = Perm[Perm[X] + Y], ab = Perm[Perm[X] + Y + 1], ba = Perm[Perm[X + 1] + Y], bb = Perm[Perm[X + 1] + Y + 1];
            float x1 = Mathf.Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1f, yf), u);
            float x2 = Mathf.Lerp(Grad(ab, xf, yf - 1f), Grad(bb, xf - 1f, yf - 1f), u);
            return Mathf.Clamp01(0.5f + 0.7f * Mathf.Lerp(x1, x2, v));
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Grad(int hash, float x, float y)
        {
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }
    }
}
