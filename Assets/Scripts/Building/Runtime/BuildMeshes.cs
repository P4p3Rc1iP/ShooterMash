using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShooterMashup.Building
{
    /// <summary>Greybox meshes and materials for build pieces, made at runtime.</summary>
    public static class BuildMeshes
    {
        const float GridSize = 1f; // metres per dev-grid tile, as in the test map
        static readonly Dictionary<Vector3, Mesh> Boxes = new Dictionary<Vector3, Mesh>();

        /// <summary>A box mesh whose UVs tile the dev grid at a fixed world size, whatever the box size.</summary>
        public static Mesh Box(Vector3 size)
        {
            if (Boxes.TryGetValue(size, out Mesh cached) && cached != null)
                return cached;

            Vector3 h = size * 0.5f;
            // Per face: outward normal n and in-plane axes u, v with Cross(v, u) == n (front-facing in Unity).
            (Vector3 n, Vector3 u, Vector3 v)[] faces =
            {
                (Vector3.right, Vector3.forward, Vector3.up),
                (Vector3.left, Vector3.back, Vector3.up),
                (Vector3.up, Vector3.right, Vector3.forward),
                (Vector3.down, Vector3.left, Vector3.forward),
                (Vector3.forward, Vector3.left, Vector3.up),
                (Vector3.back, Vector3.right, Vector3.up),
            };

            var verts = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var tris = new List<int>(36);
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

            var mesh = new Mesh { name = $"Piece box {size.x:0.##}x{size.y:0.##}x{size.z:0.##}" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            Boxes[size] = mesh;
            return mesh;
        }

        static readonly Dictionary<(Vector3, Vector3), Mesh> Wedges = new Dictionary<(Vector3, Vector3), Mesh>();

        /// <summary>The mesh for a piece box: a box, or a wedge (see <see cref="Obb.cut"/>).</summary>
        public static Mesh For(in Obb box) => box.IsWedge ? Wedge(box) : Box(box.Size);

        /// <summary>A wedge mesh, with UVs tiling the dev grid like <see cref="Box"/>.</summary>
        static Mesh Wedge(in Obb box)
        {
            var key = (box.Size, box.cut);
            if (Wedges.TryGetValue(key, out Mesh cached) && cached != null)
                return cached;

            var shape = new Obb(Vector3.zero, box.Size) { cut = box.cut };
            var corners = new Vector3[8];
            int count = shape.LocalCorners(corners);

            // The box's six faces (those that keep at least three corners) and the cut face.
            var normals = new List<Vector3> { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3 cutNormal = shape.LocalCutNormal;
            normals.Add(cutNormal);

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (Vector3 n in normals)
            {
                // The corners lying on this face's plane, sorted around it.
                float extent = float.MinValue;
                for (int i = 0; i < count; i++) extent = Mathf.Max(extent, Vector3.Dot(corners[i], n));
                var face = new List<Vector3>();
                for (int i = 0; i < count; i++)
                    if (Vector3.Dot(corners[i], n) > extent - 1e-4f)
                        face.Add(corners[i]);
                if (face.Count < 3)
                    continue;

                Vector3 mid = Vector3.zero;
                foreach (Vector3 p in face) mid += p;
                mid /= face.Count;
                Vector3 u = Vector3.Cross(Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.forward, n).normalized;
                Vector3 v = Vector3.Cross(n, u);
                face.Sort((a, b) => Mathf.Atan2(Vector3.Dot(a - mid, v), Vector3.Dot(a - mid, u))
                    .CompareTo(Mathf.Atan2(Vector3.Dot(b - mid, v), Vector3.Dot(b - mid, u))));

                int start = verts.Count;
                foreach (Vector3 p in face)
                {
                    verts.Add(p);
                    norms.Add(n);
                    uvs.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)) / GridSize);
                }
                // Sorted by increasing angle in (u, v); since Cross(v, u) == -n, that's front-facing in Unity.
                for (int i = 1; i < face.Count - 1; i++)
                    tris.AddRange(new[] { start, start + i, start + i + 1 });
            }

            var mesh = new Mesh { name = $"Piece wedge {box.Size.x:0.##}x{box.Size.y:0.##}x{box.Size.z:0.##}" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            Wedges[key] = mesh;
            return mesh;
        }

        static readonly Dictionary<(float, float), Mesh> Wheels = new Dictionary<(float, float), Mesh>();

        /// <summary>A tyre: a cylinder with its axle along x, centred on the origin.</summary>
        public static Mesh Wheel(float radius, float width)
        {
            if (Wheels.TryGetValue((radius, width), out Mesh cached) && cached != null)
                return cached;

            const int segments = 24;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float h = width / 2f;

            // Tread: a quad strip round the axle, with its own normals so it shades smoothly.
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var n = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
                verts.Add(new Vector3(-h, 0, 0) + n * radius);
                verts.Add(new Vector3(h, 0, 0) + n * radius);
                normals.Add(n);
                normals.Add(n);
                uvs.Add(new Vector2(i / (float)segments * 4f, 0f));
                uvs.Add(new Vector2(i / (float)segments * 4f, 1f));
                if (i < segments)
                {
                    int b = i * 2;
                    tris.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
                }
            }

            // Side walls: triangle fans facing -x and +x.
            foreach (float side in new[] { -1f, 1f })
            {
                int center = verts.Count;
                var n = new Vector3(side, 0, 0);
                verts.Add(new Vector3(side * h, 0, 0));
                normals.Add(n);
                uvs.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    verts.Add(new Vector3(side * h, Mathf.Sin(a) * radius, Mathf.Cos(a) * radius));
                    normals.Add(n);
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                }
                for (int i = 0; i < segments; i++)
                {
                    int a = center + 1 + i, b = a + 1;
                    if (side > 0) tris.AddRange(new[] { center, b, a });
                    else tris.AddRange(new[] { center, a, b });
                }
            }

            var mesh = new Mesh { name = $"Wheel {radius:0.##}x{width:0.##}" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            Wheels[(radius, width)] = mesh;
            return mesh;
        }

        public static Material Lit(string name, Texture texture)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.15f);
            return mat;
        }

        public static Material Transparent(string name)
        {
            Material mat = Lit(name, null);
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
            return mat;
        }
    }
}
