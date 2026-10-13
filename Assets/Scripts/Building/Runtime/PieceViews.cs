using System;
using System.Collections.Generic;
using ShooterMashup.Player;
using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// The GameObjects of a BuildSystem's pieces, kept in step with it: one per piece, with
    /// colliders, under a parent whose space the piece records are in (the world for buildings,
    /// a vehicle's body for a vehicle). Also colours them: by material normally, by support while
    /// someone holds the build tool, and flashing for a remove preview.
    /// </summary>
    public sealed class PieceViews : IDisposable
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        sealed class View
        {
            public GameObject go;
            public Renderer[] renderers;
        }

        readonly BuildSystem system;
        readonly Transform parent;
        readonly StructureManager look;
        readonly Dictionary<int, View> views = new Dictionary<int, View>();
        readonly HashSet<int> doomed = new HashSet<int>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        bool showSupport;
        int previewTarget = -1;

        /// <summary>Raised after pieces' GameObjects were added or removed.</summary>
        public event Action Rebuilt;

        public PieceViews(BuildSystem system, Transform parent, StructureManager look)
        {
            this.system = system;
            this.parent = parent;
            this.look = look;
            system.Changed += OnChanged;
            foreach (int id in system.Pieces.Keys)
                CreateView(id);
            RefreshColors();
        }

        public void Dispose()
        {
            system.Changed -= OnChanged;
            foreach (View v in views.Values)
                if (v.go != null) UnityEngine.Object.Destroy(v.go);
            views.Clear();
        }

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

        public GameObject ViewOf(int id) => views.TryGetValue(id, out View v) ? v.go : null;

        /// <summary>Call every frame: the remove preview pulses.</summary>
        public void Tick()
        {
            if (previewTarget >= 0)
                RefreshColors();
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

        void OnChanged(BuildResult result)
        {
            foreach (int id in result.removed) DestroyView(id);
            foreach (int id in result.collapsed) DestroyView(id);
            foreach (int id in result.added) CreateView(id);

            if (previewTarget >= 0 && !views.ContainsKey(previewTarget))
                ClearRemovePreview();
            RefreshColors();
            if (result.added.Count + result.removed.Count + result.collapsed.Count > 0)
                Rebuilt?.Invoke();
        }

        void CreateView(int id)
        {
            Piece p = system.Get(id);
            if (p == null || views.ContainsKey(id))
                return;
            GameObject go = look.CreatePieceObject(p.def, look.MaterialFor(p.record.material), true,
                $"{p.record.material} {p.def.name} #{id}", parent);
            go.transform.SetLocalPositionAndRotation(p.record.position, p.record.rotation);
            go.AddComponent<BuildPieceTag>().id = id;
            if (p.def.Surface != 0)
                go.AddComponent<PmSurface>().flags = p.def.Surface; // e.g. ladders
            // Tyres keep their own colour.
            Renderer[] renderers = Array.FindAll(go.GetComponentsInChildren<Renderer>(), r => r.name != StructureManager.WheelVisualName);
            views[id] = new View { go = go, renderers = renderers };
        }

        void DestroyView(int id)
        {
            if (!views.TryGetValue(id, out View v))
                return;
            // Vanish for now. Falling debris goes here later (cosmetic only, never networked).
            UnityEngine.Object.Destroy(v.go);
            views.Remove(id);
        }

        void RefreshColors()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
            foreach (KeyValuePair<int, View> kv in views)
            {
                Piece p = system.Get(kv.Key);
                if (p == null)
                    continue;

                Color c;
                if (kv.Key == previewTarget)
                    c = Color.Lerp(new Color(1f, 1f, 1f), new Color(1f, 0.4f, 0.9f), pulse);
                else if (doomed.Contains(kv.Key))
                    c = Color.Lerp(new Color(0.55f, 0f, 0.35f), new Color(1f, 0.2f, 0.6f), pulse);
                else if (showSupport)
                    c = SupportColor(p, system.StatsOf(p));
                else
                    c = look.ColorFor(p.record.material);

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
