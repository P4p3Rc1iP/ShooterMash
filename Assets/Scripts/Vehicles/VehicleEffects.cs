using UnityEngine;
using UnityEngine.Rendering;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// What the tyres are doing, made visible: smoke (dust) where a tyre slides, more the faster
    /// it slides, and skid marks left on the ground. A spinning wheel, a locked one and one sliding
    /// sideways all smoke; on ice there's only a little mist. The dust and marks take the colour of
    /// the ground. Backfires (from VehicleAudio) shoot flames from the exhausts. Added to every vehicle.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class VehicleEffects : MonoBehaviour
    {
        [Tooltip("Smoke puffs per second per m/s a tyre slides (and at most this many times 10 per wheel).")]
        public float smokeRate = 5f;
        [Tooltip("A tyre leaves skid marks once it slides this fast (m/s).")]
        public float skidSpeed = 1.2f;
        [Tooltip("Dust colour on ground that doesn't say what it's made of (and alpha for all dust).")]
        public Color smokeColor = new Color(0.82f, 0.78f, 0.7f, 0.55f);

        Vehicle vehicle;
        ParticleSystem smoke;
        float[] owed = new float[0];          // puffs owed per wheel (fractions carry over)
        Vector3[] lastMark = new Vector3[0];
        bool[] marking = new bool[0];

        static Material particleMaterial;
        static Material flameMaterial;
        ParticleSystem flames;
        static Texture2D puff;

        void Awake()
        {
            vehicle = GetComponent<Vehicle>();
            var go = new GameObject("Tyre smoke");
            go.transform.SetParent(transform, false);
            smoke = go.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = smoke.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = 1500;
            main.startLifetime = 1.8f;
            main.gravityModifier = -0.04f;                   // drifts up a little
            ParticleSystem.EmissionModule emission = smoke.emission;
            emission.enabled = false;                        // puffs are emitted by hand
            ParticleSystem.ShapeModule shape = smoke.shape;
            shape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = smoke.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.6f)));
            ParticleSystem.ColorOverLifetimeModule color = smoke.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            ParticleSystem.LimitVelocityOverLifetimeModule drag = smoke.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.5f;
            ParticleSystem.RotationOverLifetimeModule spin = smoke.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (particleMaterial == null)
                particleMaterial = ParticleMaterial("Tyre smoke", Puff());
            renderer.sharedMaterial = particleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            smoke.Play();
            flames = MakeFlames();
            if (TryGetComponent(out VehicleAudio sounds))
                sounds.Backfire += Flames;
        }

        void Update()
        {
            int n = vehicle.WheelCount;
            if (owed.Length != n)
            {
                owed = new float[n];
                lastMark = new Vector3[n];
                marking = new bool[n];
            }
            Vector3 carried = vehicle.Body != null ? vehicle.Body.linearVelocity * 0.3f : Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vehicle.WheelState w = vehicle.GetWheel(i);
                bool sliding = w.grounded && w.sliding && w.slip > 0.3f;
                // Smoke.
                if (sliding)
                {
                    float rate = Mathf.Min(w.slip * smokeRate, smokeRate * 10f) * (w.slick ? 0.25f : 1f);
                    owed[i] += rate * Time.deltaTime;
                    while (owed[i] >= 1f)
                    {
                        owed[i] -= 1f;
                        float strength = Mathf.Clamp01(w.slip / 10f);
                        Color c = w.slick ? new Color(0.92f, 0.95f, 1f, 0.35f) : Dust(w.ground);
                        c.a *= 0.4f + 0.6f * strength;
                        smoke.Emit(new ParticleSystem.EmitParams
                        {
                            position = w.contact + w.normal * 0.25f + Random.insideUnitSphere * 0.2f,
                            velocity = carried + Random.insideUnitSphere * 0.8f + w.normal * 0.6f,
                            startSize = 0.6f + 0.06f * w.slip,
                            startLifetime = Random.Range(1.3f, 2.3f),
                            startColor = c,
                            rotation = Random.Range(0f, 360f),
                        }, 1);
                    }
                }
                else
                    owed[i] = 0f;

                // Skid marks, on the ground only (not on other vehicles or moving things).
                bool mark = w.grounded && w.sliding && w.slip > skidSpeed && w.ground != null && w.ground.attachedRigidbody == null;
                if (mark)
                {
                    if (!marking[i])
                        lastMark[i] = w.contact;
                    else if ((w.contact - lastMark[i]).sqrMagnitude > 0.09f)
                    {
                        float strength = Mathf.Clamp01(w.slip / 8f) * (w.slick ? 0.3f : 1f);
                        SkidMarks.Add(lastMark[i], w.contact, w.normal, w.width, 0.25f + 0.75f * strength, Mark(w.ground));
                        lastMark[i] = w.contact;
                    }
                }
                marking[i] = mark;
            }
        }

        // ------------------------------------------------------------------ surfaces

        static readonly System.Collections.Generic.Dictionary<Collider, Color> GroundColors = new System.Collections.Generic.Dictionary<Collider, Color>();

        /// <summary>
        /// The colour of the ground: from what its PmSurface says it's made of (dirt, sand, snow...),
        /// else from its material's colour, else plain dust. Remembered per collider.
        /// </summary>
        Color Ground(Collider c)
        {
            if (c == null)
                return smokeColor;
            if (GroundColors.TryGetValue(c, out Color known))
                return known;
            Color color = smokeColor;
            if (c.TryGetComponent(out Player.PmSurface surface) && surface.surfaceType != Player.SurfaceType.Default)
            {
                color = surface.surfaceType switch
                {
                    Player.SurfaceType.Dirt => new Color(0.5f, 0.38f, 0.26f),
                    Player.SurfaceType.Sand => new Color(0.86f, 0.76f, 0.55f),
                    Player.SurfaceType.Gravel => new Color(0.62f, 0.6f, 0.56f),
                    Player.SurfaceType.Snow => new Color(0.95f, 0.97f, 1f),
                    _ => smokeColor,
                };
            }
            else if (c.TryGetComponent(out Renderer r) && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor"))
                color = Color.Lerp(r.sharedMaterial.GetColor("_BaseColor"), smokeColor, 0.4f);
            color.a = 1f;
            GroundColors[c] = color;
            return color;
        }

        /// <summary>Dust thrown up from this ground.</summary>
        Color Dust(Collider ground)
        {
            Color c = Ground(ground);
            c.a = smokeColor.a;
            return c;
        }

        /// <summary>The colour of a skid mark on this ground: rubber, tinted by the ground and darker than it.</summary>
        Color Mark(Collider ground) => Color.Lerp(new Color(0.08f, 0.07f, 0.06f), Ground(ground) * 0.5f, 0.35f);

        // ------------------------------------------------------------------ backfires

        /// <summary>A burst of flame (and a puff of smoke) from each exhaust.</summary>
        void Flames()
        {
            if (flames == null)
                return;
            Vector3 back = -transform.TransformDirection(vehicle.Forward);
            foreach (Vector3 exhaust in vehicle.Exhausts)
            {
                Vector3 at = transform.TransformPoint(exhaust);
                Vector3 carried = vehicle.Body.linearVelocity;
                for (int k = 0; k < 6; k++)
                    flames.Emit(new ParticleSystem.EmitParams
                    {
                        position = at,
                        velocity = carried + (back * 4f + Vector3.up * 1.5f + Random.insideUnitSphere * 1.5f),
                        startSize = Random.Range(0.25f, 0.5f),
                        startLifetime = Random.Range(0.08f, 0.18f),
                        startColor = Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.35f, 0.05f), Random.value),
                    }, 1);
                smoke.Emit(new ParticleSystem.EmitParams
                {
                    position = at, velocity = carried + back + Vector3.up, startSize = 0.5f, startLifetime = 1f,
                    startColor = new Color(0.25f, 0.25f, 0.25f, 0.5f),
                }, 1);
            }
        }

        /// <summary>A short-lived, glowing particle system for exhaust flames.</summary>
        ParticleSystem MakeFlames()
        {
            var go = new GameObject("Exhaust flames");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = 200;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (flameMaterial == null)
            {
                flameMaterial = ParticleMaterial("Exhaust flames", Puff());
                flameMaterial.SetFloat("_Blend", 2f);                      // additive: it glows
                flameMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                flameMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
            }
            renderer.sharedMaterial = flameMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        /// <summary>The particle material asset (in a Resources folder) that runtime materials are copied from.</summary>
        public const string ParticleTemplate = "VehicleEffects/Particles";

        /// <summary>An unlit, alpha-blended material that uses vertex colours (particles, skid marks).</summary>
        public static Material ParticleMaterial(string name, Texture texture)
        {
            // From a material asset in Resources: a build only has the shaders (and keyword variants)
            // that some asset uses, so Shader.Find alone returns null there (no smoke in builds).
            var template = Resources.Load<Material>(ParticleTemplate);
            var mat = template != null ? new Material(template) { name = name }
                : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
            if (texture != null)
                mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }

        /// <summary>A soft, slightly lumpy round puff.</summary>
        static Texture2D Puff()
        {
            if (puff != null)
                return puff;
            const int size = 64;
            puff = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Smoke puff", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float lumps = 0.85f + 0.15f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f);
                float a = Mathf.Clamp01(1f - r) * lumps;
                a = a * a * (3f - 2f * a);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            puff.SetPixels32(pixels);
            puff.Apply(true);
            return puff;
        }
    }
}
