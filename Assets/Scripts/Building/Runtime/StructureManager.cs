using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// Owns the build simulation for the scene (the server's job, once there is one) and keeps a
    /// GameObject with colliders for every placed piece. Also colours pieces: by material
    /// normally, by support while someone holds the build tool, and flashing for a remove preview.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class StructureManager : MonoBehaviour
    {
        public BuildConfig config = new BuildConfig();

        [Tooltip("Layers pieces rest on and the build tool aims at.")]
        public LayerMask solidMask = Physics.DefaultRaycastLayers;

        [Tooltip("Placeholder colours per material: wood, stone, iron.")]
        public Color[] materialColors =
        {
            new Color(0.80f, 0.56f, 0.32f), // wood
            new Color(0.72f, 0.72f, 0.70f), // stone
            new Color(0.48f, 0.58f, 0.72f), // iron
        };

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public BuildSystem System { get; private set; }
        public UnityBuildWorld World { get; private set; }
        public Material GhostMaterial { get; private set; }

        public Material MaterialFor(BuildMaterial m) => pieceMaterials[(int)m];
        public Texture TextureFor(BuildMaterial m) => textures[(int)m];
        public Color ColorFor(BuildMaterial m) => materialColors[(int)m];

        /// <summary>Colour pieces by how well supported they are (Valheim's hammer view).</summary>
        public bool ShowSupport
        {
            get => showSupport;
            set
            {
                if (showSupport == value) return;
                showSupport = value;
                RefreshColors();
            }
        }

        sealed class View
        {
            public GameObject go;
            public Renderer[] renderers;
        }

        readonly Dictionary<int, View> views = new Dictionary<int, View>();
        readonly HashSet<int> doomed = new HashSet<int>();
        MaterialPropertyBlock block;
        Material[] pieceMaterials;
        Texture2D[] textures;
        bool showSupport;
        int previewTarget = -1;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            int count = global::System.Enum.GetValues(typeof(BuildMaterial)).Length;
            textures = new Texture2D[count];
            pieceMaterials = new Material[count];
            for (int i = 0; i < count; i++)
            {
                textures[i] = BuildTextures.Create((BuildMaterial)i);
                pieceMaterials[i] = BuildMeshes.Lit((BuildMaterial)i + " piece", textures[i]);
            }
            GhostMaterial = BuildMeshes.Transparent("Build ghost");

            World = new UnityBuildWorld(solidMask);
            System = new BuildSystem(config, World);
            System.Changed += OnChanged;
        }

        void OnDestroy()
        {
            if (System != null) System.Changed -= OnChanged;
            if (pieceMaterials != null)
                foreach (Material m in pieceMaterials) Destroy(m);
            if (textures != null)
                foreach (Texture2D t in textures) Destroy(t);
            Destroy(GhostMaterial);
        }

        void OnValidate()
        {
            // Material values edited in play mode: re-solve so the change shows straight away.
            if (Application.isPlaying && System != null)
                System.RecomputeAll();
        }

        void LateUpdate()
        {
            if (previewTarget >= 0)
                RefreshColors(); // the preview pulses
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Dig a block of stand-in terrain away. Whatever rested on it may collapse.</summary>
        public BuildResult Dig(DiggableBlock dig)
        {
            var col = dig.GetComponent<Collider>();
            Bounds bounds = col != null ? col.bounds : new Bounds(dig.transform.position, Vector3.one);
            if (col != null) col.enabled = false; // leaves the physics scene now, not at end of frame
            dig.gameObject.SetActive(false);
            Destroy(dig.gameObject);
            return System.TerrainChanged(bounds);
        }

        public void SetRemovePreview(int target, List<int> wouldCollapse)
        {
            previewTarget = target;
            doomed.Clear();
            foreach (int id in wouldCollapse)
                doomed.Add(id);
            RefreshColors();
        }

        public void ClearRemovePreview()
        {
            if (previewTarget < 0)
                return;
            previewTarget = -1;
            doomed.Clear();
            RefreshColors();
        }

        // ------------------------------------------------------------------ views

        /// <summary>Builds the GameObject for a piece shape: one child per box.</summary>
        public GameObject CreatePieceObject(PieceDef def, Material material, bool colliders, string name, Transform parent = null)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            for (int i = 0; i < def.boxes.Length; i++)
            {
                Obb box = def.boxes[i];
                var child = new GameObject("Box " + i);
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = box.center;
                child.transform.localRotation = box.rotation;
                child.AddComponent<MeshFilter>().sharedMesh = BuildMeshes.Box(box.Size);
                child.AddComponent<MeshRenderer>().sharedMaterial = material;
                if (colliders)
                    child.AddComponent<BoxCollider>().size = box.Size;
            }
            return root;
        }

        void OnChanged(BuildResult result)
        {
            foreach (int id in result.removed) DestroyView(id);
            foreach (int id in result.collapsed) DestroyView(id);
            foreach (int id in result.added) CreateView(id);

            if (previewTarget >= 0 && !views.ContainsKey(previewTarget))
                ClearRemovePreview();
            RefreshColors();
        }

        void CreateView(int id)
        {
            Piece p = System.Get(id);
            if (p == null || views.ContainsKey(id))
                return;
            GameObject go = CreatePieceObject(p.def, MaterialFor(p.record.material), true, $"{p.record.material} {p.def.name} #{id}", transform);
            go.transform.SetPositionAndRotation(p.record.position, p.record.rotation);
            go.AddComponent<BuildPieceTag>().id = id;
            views[id] = new View { go = go, renderers = go.GetComponentsInChildren<Renderer>() };
        }

        void DestroyView(int id)
        {
            if (!views.TryGetValue(id, out View v))
                return;
            // Vanish for now. Falling debris goes here later (cosmetic only, never networked).
            Destroy(v.go);
            views.Remove(id);
        }

        void RefreshColors()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
            foreach (KeyValuePair<int, View> kv in views)
            {
                Piece p = System.Get(kv.Key);
                if (p == null)
                    continue;

                Color c;
                if (kv.Key == previewTarget)
                    c = Color.Lerp(new Color(1f, 1f, 1f), new Color(1f, 0.4f, 0.9f), pulse);
                else if (doomed.Contains(kv.Key))
                    c = Color.Lerp(new Color(0.55f, 0f, 0.35f), new Color(1f, 0.2f, 0.6f), pulse);
                else if (showSupport)
                    c = SupportColor(p, System.StatsOf(p));
                else
                    c = ColorFor(p.record.material);

                block.SetColor(BaseColor, c);
                foreach (Renderer r in kv.Value.renderers)
                    r.SetPropertyBlock(block);
            }
        }

        /// <summary>Blue when grounded, then green to yellow to red as support runs out.</summary>
        public static Color SupportColor(Piece p, MaterialStats stats)
        {
            if (p.grounded)
                return new Color(0.25f, 0.5f, 1f);
            float f = Mathf.InverseLerp(stats.minSupport, stats.maxSupport, p.support);
            var red = new Color(0.9f, 0.15f, 0.1f);
            var yellow = new Color(1f, 0.85f, 0.1f);
            var green = new Color(0.2f, 0.85f, 0.25f);
            return f < 0.5f ? Color.Lerp(red, yellow, f * 2f) : Color.Lerp(yellow, green, (f - 0.5f) * 2f);
        }
    }
}
