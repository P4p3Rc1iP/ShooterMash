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
