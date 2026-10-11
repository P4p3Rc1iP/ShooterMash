using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShooterMashup.Building
{
    /// <summary>
    /// Renders a small picture of each piece in each material for the build menu, with a hidden
    /// camera, far away from the map. Pictures are made on first use and kept.
    /// </summary>
    public sealed class PieceThumbnails
    {
        const int Size = 128;
        const int Layer = 31; // only the thumbnail camera looks at this layer
        static readonly Vector3 Studio = new Vector3(0f, -5000f, 0f);
        static readonly Vector3 ViewDirection = new Vector3(-0.55f, -0.5f, 0.68f).normalized; // from front-left, above
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        readonly StructureManager structures;
        readonly Dictionary<(int, BuildMaterial), RenderTexture> cache = new Dictionary<(int, BuildMaterial), RenderTexture>();
        Camera cam;

        public PieceThumbnails(StructureManager structures)
        {
            this.structures = structures;
        }

        public Texture Get(int type, BuildMaterial material) =>
            cache.TryGetValue((type, material), out RenderTexture rt) && rt != null && rt.IsCreated() ? rt : null;

        /// <summary>Render any missing pictures for this material. Call outside OnGUI.</summary>
        public void Prepare(BuildMaterial material)
        {
            for (int type = 0; type < PieceCatalog.All.Length; type++)
                if (Get(type, material) == null)
                    cache[(type, material)] = Render(type, material);
        }

        public void Dispose()
        {
            foreach (RenderTexture rt in cache.Values)
                if (rt != null) Object.Destroy(rt);
            cache.Clear();
            if (cam != null) Object.Destroy(cam.gameObject);
        }

        RenderTexture Render(int type, BuildMaterial material)
        {
            EnsureCamera();
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "Thumbnail", antiAliasing = 4 };
            rt.Create();

            PieceDef def = PieceCatalog.All[type];
            GameObject go = structures.CreatePieceObject(def, structures.MaterialFor(material), false, "Thumbnail piece");
            go.transform.position = Studio;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, structures.ColorFor(material));
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers)
            {
                r.gameObject.layer = Layer;
                r.SetPropertyBlock(block);
                r.shadowCastingMode = ShadowCastingMode.Off;
                bounds.Encapsulate(r.bounds);
            }

            // Fit the bounding sphere in view.
            float radius = bounds.extents.magnitude;
            float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            cam.transform.position = bounds.center - ViewDirection * distance;
            cam.transform.rotation = Quaternion.LookRotation(ViewDirection);
            cam.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            cam.farClipPlane = distance + radius * 2f;

            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
            }
            else
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
            }

            Object.DestroyImmediate(go);
            return rt;
        }

        void EnsureCamera()
        {
            if (cam != null)
                return;
            var go = new GameObject("Thumbnail camera") { hideFlags = HideFlags.HideAndDontSave };
            cam = go.AddComponent<Camera>();
            cam.enabled = false; // renders only on request
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);
            cam.fieldOfView = 30f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
        }
    }
}
