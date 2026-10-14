using UnityEngine;
using UnityEngine.Rendering;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// Skid marks: dark strips left on the ground where tyres slide, darker the faster they slide.
    /// One mesh for the whole world, a ring buffer of quads: the oldest marks are reused when it
    /// fills up. Each wheel adds a quad from where it was to where it is while it slides.
    /// </summary>
    public sealed class SkidMarks : MonoBehaviour
    {
        const int MaxQuads = 3000;
        const float Lift = 0.02f;   // above the ground, so the marks don't flicker into it

        static SkidMarks instance;

        Mesh mesh;
        Vector3[] vertices;
        Color32[] colors;
        Vector2[] uvs;
        int next;
        bool dirty;

        static SkidMarks Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("Skid marks");
                    instance = go.AddComponent<SkidMarks>();
                }
                return instance;
            }
        }

        void Awake()
        {
            vertices = new Vector3[MaxQuads * 4];
            colors = new Color32[MaxQuads * 4];
            uvs = new Vector2[MaxQuads * 4];
            var triangles = new int[MaxQuads * 6];
            for (int q = 0; q < MaxQuads; q++)
            {
                int v = q * 4, t = q * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                uvs[v] = new Vector2(0, 0); uvs[v + 1] = new Vector2(1, 0); uvs[v + 2] = new Vector2(0, 1); uvs[v + 3] = new Vector2(1, 1);
            }
            mesh = new Mesh { name = "Skid marks", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = VehicleEffects.ParticleMaterial("Skid marks", null);
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Add a strip from `from` to `to` on a surface with this normal, `width` wide, `strength` 0..1 dark, in this colour.</summary>
        public static void Add(Vector3 from, Vector3 to, Vector3 normal, float width, float strength, Color tint) =>
            Instance.AddQuad(from, to, normal, width, strength, tint);

        void AddQuad(Vector3 from, Vector3 to, Vector3 normal, float width, float strength, Color tint)
        {
            Vector3 along = to - from;
            if (along.sqrMagnitude < 1e-4f)
                return;
            Vector3 across = Vector3.Cross(normal, along).normalized * (width / 2f);
            Vector3 lift = normal * Lift;
            int v = next * 4;
            vertices[v] = from - across + lift;
            vertices[v + 1] = from + across + lift;
            vertices[v + 2] = to - across + lift;
            vertices[v + 3] = to + across + lift;
            var c = (Color32)new Color(tint.r, tint.g, tint.b, Mathf.Clamp01(strength) * 0.8f);
            colors[v] = colors[v + 1] = colors[v + 2] = colors[v + 3] = c;
            next = (next + 1) % MaxQuads;
            dirty = true;
        }

        void LateUpdate()
        {
            if (!dirty)
                return;
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            dirty = false;
        }
    }
}
