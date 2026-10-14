using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// Owns the world's build simulation (the server's job, once there is one) and its piece
    /// GameObjects (PieceViews). Also holds the look shared by every host: materials, textures
    /// and the ghost material, and builds the GameObject for a piece shape.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class StructureManager : BuildHost
    {
        public BuildConfig config = new BuildConfig();

        [Tooltip("Layers pieces rest on and the build tool aims at.")]
        public LayerMask solidMask = Physics.DefaultRaycastLayers;

        [Tooltip("Placeholder colours per material: wood, stone, iron, scrap.")]
        public Color[] materialColors =
        {
            new Color(0.80f, 0.56f, 0.32f), // wood
            new Color(0.72f, 0.72f, 0.70f), // stone
            new Color(0.48f, 0.58f, 0.72f), // iron
            new Color(0.66f, 0.40f, 0.24f), // scrap (rust)
        };

        [Tooltip("Colour of tyres.")]
        public Color tyreColor = new Color(0.12f, 0.12f, 0.13f);

        BuildSystem system;
        PieceViews views;

        public override BuildSystem System => system;
        public override PieceViews Views => views;
        public UnityBuildWorld World { get; private set; }
        public Material GhostMaterial { get; private set; }

        public Material MaterialFor(BuildMaterial m) => pieceMaterials[(int)m];
        public Texture TextureFor(BuildMaterial m) => textures[(int)m];
        public Color ColorFor(BuildMaterial m) => (int)m < materialColors.Length ? materialColors[(int)m] : Color.grey;

        /// <summary>Colour the world's pieces by how well supported they are (Valheim's hammer view).</summary>
        public bool ShowSupport
        {
            get => views.ShowSupport;
            set => views.ShowSupport = value;
        }

        Material[] pieceMaterials;
        Texture2D[] textures;
        Material tyreMaterial;

        void Awake()
        {
            int count = global::System.Enum.GetValues(typeof(BuildMaterial)).Length;
            textures = new Texture2D[count];
            pieceMaterials = new Material[count];
            for (int i = 0; i < count; i++)
            {
                textures[i] = BuildTextures.Create((BuildMaterial)i);
                pieceMaterials[i] = BuildMeshes.Lit((BuildMaterial)i + " piece", textures[i]);
            }
            GhostMaterial = BuildMeshes.Transparent("Build ghost");
            tyreMaterial = BuildMeshes.Lit("Tyre", null);
            tyreMaterial.SetColor("_BaseColor", tyreColor);

            World = new UnityBuildWorld(solidMask);
            system = new BuildSystem(config, World);
            views = new PieceViews(system, transform, this);
        }

        void OnDestroy()
        {
            views?.Dispose();
            if (pieceMaterials != null)
                foreach (Material m in pieceMaterials) Destroy(m);
            if (textures != null)
                foreach (Texture2D t in textures) Destroy(t);
            Destroy(GhostMaterial);
            Destroy(tyreMaterial);
        }

        void OnValidate()
        {
            // Material values edited in play mode: re-solve so the change shows straight away.
            if (Application.isPlaying && system != null)
                system.RecomputeAll();
        }

        void LateUpdate() => views.Tick();

        // ------------------------------------------------------------------ actions

        /// <summary>Dig a block of stand-in terrain away. Whatever rested on it may collapse.</summary>
        public BuildResult Dig(DiggableBlock dig)
        {
            var col = dig.GetComponent<Collider>();
            Bounds bounds = col != null ? col.bounds : new Bounds(dig.transform.position, Vector3.one);
            if (col != null) col.enabled = false; // leaves the physics scene now, not at end of frame
            dig.gameObject.SetActive(false);
            Destroy(dig.gameObject);
            return system.TerrainChanged(bounds);
        }

        public void SetRemovePreview(int target, List<int> wouldCollapse) => views.SetRemovePreview(target, wouldCollapse);
        public void ClearRemovePreview() => views.ClearRemovePreview();

        // ------------------------------------------------------------------ views

        /// <summary>
        /// Builds the GameObject for a piece shape: one child per box, plus a tyre for a wheel
        /// (drawn only, no collider; a vehicle moves it with the suspension).
        /// </summary>
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
                Mesh mesh = BuildMeshes.For(box);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                child.AddComponent<MeshRenderer>().sharedMaterial = material;
                if (colliders && box.IsWedge)
                {
                    var mc = child.AddComponent<MeshCollider>();
                    mc.convex = true;          // the player's box sweeps need a convex collider
                    mc.sharedMesh = mesh;
                }
                else if (colliders)
                {
                    child.AddComponent<BoxCollider>().size = box.Size;
                }
            }
            if (def.Wheel != null)
            {
                var tyre = new GameObject(WheelVisualName);
                tyre.transform.SetParent(root.transform, false);
                tyre.transform.localPosition = def.Wheel.center;
                tyre.AddComponent<MeshFilter>().sharedMesh = BuildMeshes.Wheel(def.Wheel.radius, def.Wheel.width);
                // The ghost keeps its see-through material; placed wheels get rubber.
                tyre.AddComponent<MeshRenderer>().sharedMaterial = colliders ? tyreMaterial : material;
            }
            return root;
        }

        /// <summary>The name of a wheel piece's tyre object, which vehicles look up to move it.</summary>
        public const string WheelVisualName = "Tyre";
    }
}
