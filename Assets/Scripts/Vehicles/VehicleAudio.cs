using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// A vehicle's sounds, made up on the spot (no sound files yet): the engine, pitched by its revs
    /// (so wheelspin and the rev limiter are audible), brighter and louder under load; the tyres
    /// squealing as they slide, louder the faster; and a thump when the body hits something. The
    /// engine only runs while someone (or the autopilot) drives. Added to every vehicle.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class VehicleAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float engineVolume = 0.7f;
        [Range(0f, 1f)] public float tyreVolume = 0.6f;
        [Range(0f, 1f)] public float impactVolume = 0.8f;
        [Range(0f, 1f)] public float backfireVolume = 0.7f;

        /// <summary>The exhaust just popped (for the flames).</summary>
        public event System.Action Backfire;

        const int Rate = 44100;
        const float EngineBase = 150f;   // the engine loop's firing frequency (Hz) at pitch 1

        Vehicle vehicle;
        AudioSource engine, tyres, impacts;
        AudioLowPassFilter engineFilter;
        float lastThrottle, nextPop;
        int popsLeft;

        static AudioClip petrolClip, dieselClip, squealClip, thumpClip, popClip;

        void Awake()
        {
            vehicle = GetComponent<Vehicle>();
            engine = Source("Engine", 0.2f);
            engineFilter = engine.gameObject.AddComponent<AudioLowPassFilter>();
            tyres = Source("Tyres", 0.1f);
            tyres.clip = squealClip ??= Squeal();
            impacts = Source("Impacts", 0f);
            impacts.loop = false;
        }

        AudioSource Source(string name, float doppler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 5f;
            s.maxDistance = 160f;
            s.dopplerLevel = doppler;
            s.volume = 0f;
            return s;
        }

        void Update()
        {
            EngineRevs revs = vehicle.Revs;
            bool running = vehicle.EngineRunning && revs.Rpm > 1f;

            // The engine: a recorded loop for its engine piece if the sound bank has one, pitched
            // over the revs; otherwise the made-up one, pitched to the firing rate.
            VehicleSoundBank bank = VehicleSoundBank.Current;
            VehicleSoundBank.EngineSound recorded = bank != null ? bank.For(vehicle.EngineName) : null;
            AudioClip wanted = recorded != null ? bank.Loop(recorded.clip)
                : revs.Redline < 4000f ? dieselClip ??= Engine(true) : petrolClip ??= Engine(false);
            if (engine.clip != wanted)
            {
                engine.Stop();
                engine.clip = wanted;
            }
            float rev = Mathf.Clamp01(revs.Rpm / revs.Redline);
            float load = vehicle.Parked || vehicle.Braking ? 0f : Mathf.Abs(vehicle.Throttle);
            if (running)
            {
                if (!engine.isPlaying)
                    engine.Play();
                if (recorded != null)
                {
                    float range = Mathf.Max(revs.Redline - revs.Idle, 1f);
                    engine.pitch = Mathf.Clamp(Mathf.LerpUnclamped(recorded.idlePitch, recorded.redlinePitch, (revs.Rpm - revs.Idle) / range), 0.05f, 3f);
                }
                else
                    engine.pitch = Mathf.Clamp(revs.Rpm / 60f * revs.Cylinders / 2f / EngineBase, 0.05f, 3f);
                float volume = engineVolume * (recorded != null ? recorded.volume : 1f) * (0.35f + 0.4f * load + 0.25f * rev);
                if (revs.Limiter)
                    volume *= Random.Range(0.6f, 1f);                   // stuttering on the limiter
                engine.volume = Mathf.MoveTowards(engine.volume, volume, 4f * Time.deltaTime);
                // Off the throttle it sounds muffled, under load it opens up.
                engineFilter.cutoffFrequency = recorded != null
                    ? Mathf.Lerp(1500f, 22000f, 0.5f * load + 0.5f * rev * (0.4f + 0.6f * load))
                    : Mathf.Lerp(700f, 7000f, 0.35f * load + 0.65f * rev * (0.5f + 0.5f * load));
            }
            else
            {
                engine.volume = Mathf.MoveTowards(engine.volume, 0f, 2f * Time.deltaTime);
                if (engine.volume <= 0f && engine.isPlaying)
                    engine.Stop();
            }

            // Backfires: lifting off at high revs pops the exhaust a few times.
            if (running && lastThrottle > 0.5f && vehicle.Throttle <= 0.1f && rev > 0.6f && Random.value < 0.8f)
            {
                popsLeft = Random.Range(2, 6);
                nextPop = Time.time + Random.Range(0.03f, 0.15f);
            }
            lastThrottle = vehicle.Throttle;
            if (popsLeft > 0 && Time.time >= nextPop)
            {
                popsLeft--;
                nextPop = Time.time + Random.Range(0.06f, 0.25f);
                impacts.pitch = Random.Range(0.8f, 1.3f);
                impacts.PlayOneShot(popClip ??= Pop(), backfireVolume * Random.Range(0.6f, 1f));
                Backfire?.Invoke();
            }


            // The tyres: squeal with how fast they slide, all together; on ice hardly at all.
            float slip = 0f;
            int sliding = 0;
            for (int i = 0; i < vehicle.WheelCount; i++)
            {
                Vehicle.WheelState w = vehicle.GetWheel(i);
                if (!w.grounded || !w.sliding)
                    continue;
                slip += w.slip * (w.slick ? 0.2f : 1f);
                sliding++;
            }
            float loud = Mathf.Clamp01((slip - 1f) / 16f);
            if (loud > 0f && !tyres.isPlaying)
                tyres.Play();
            tyres.volume = Mathf.MoveTowards(tyres.volume, tyreVolume * loud, 3f * Time.deltaTime);
            tyres.pitch = 0.85f + 0.3f * Mathf.Clamp01(sliding > 0 ? slip / sliding / 15f : 0f);
            if (tyres.volume <= 0f && tyres.isPlaying)
                tyres.Stop();
        }

        void OnCollisionEnter(Collision c)
        {
            if (vehicle.Body == null || vehicle.Mass <= 0f)
                return;
            float dv = c.impulse.magnitude / vehicle.Mass;
            if (dv < 1f)
                return;
            impacts.pitch = Random.Range(0.85f, 1.15f) * Mathf.Lerp(1.2f, 0.7f, Mathf.Clamp01(vehicle.Mass / 10000f));
            impacts.PlayOneShot(thumpClip ??= Thump(), impactVolume * Mathf.Clamp01(dv / 8f));
        }

        // ------------------------------------------------------------------ synthesis

        static float Hash(int i)
        {
            unchecked
            {
                uint h = (uint)i * 2654435761u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>
        /// One second of engine at a 150 Hz firing rate (a whole number of cycles, so it loops
        /// cleanly): a stack of harmonics with a lope at half the firing rate and a little roughness
        /// per cycle. A diesel is brighter and knocks: a click of noise at each firing.
        /// </summary>
        static AudioClip Engine(bool diesel)
        {
            var samples = new float[Rate];
            int perCycle = Mathf.RoundToInt(Rate / EngineBase);
            var phases = new float[17];
            for (int k = 1; k <= 16; k++)
                phases[k] = Hash(k + (diesel ? 100 : 0)) * Mathf.PI * 2f;
            float noise = 0f;
            for (int i = 0; i < Rate; i++)
            {
                float t = i / (float)Rate;
                int cycle = i / perCycle;
                float inCycle = (i % perCycle) / (float)perCycle;
                float s = 0f;
                for (int k = 1; k <= 16; k++)
                    s += Mathf.Sin(2f * Mathf.PI * k * EngineBase * t + phases[k]) / Mathf.Pow(k, diesel ? 0.55f : 0.85f);
                s *= 1f + 0.25f * Mathf.Sin(2f * Mathf.PI * EngineBase / 2f * t);    // lope
                s *= 1f + (diesel ? 0.25f : 0.12f) * (Hash(cycle) - 0.5f) * 2f;      // roughness
                noise = noise * 0.85f + (Hash(i * 7 + 3) - 0.5f) * 0.15f;           // exhaust hiss
                s += noise * (diesel ? 1.5f : 0.8f);
                if (diesel)
                    s += (Hash(i * 13 + 5) - 0.5f) * 4f * Mathf.Exp(-inCycle * 40f); // knock
                samples[i] = s;
            }
            return Clip(diesel ? "Diesel engine" : "Petrol engine", samples, 0.9f);
        }

        /// <summary>One second of squeal: a wavering tone near 1 kHz over band-passed noise.</summary>
        static AudioClip Squeal()
        {
            var samples = new float[Rate];
            float low = 0f, lower = 0f;
            for (int i = 0; i < Rate; i++)
            {
                float t = i / (float)Rate;
                float tone = Mathf.Sin(2f * Mathf.PI * 950f * t + 40f / 7f * Mathf.Sin(2f * Mathf.PI * 7f * t))
                             + 0.4f * Mathf.Sin(2f * Mathf.PI * 1425f * t + 60f / 7f * Mathf.Sin(2f * Mathf.PI * 7f * t));
                float white = Hash(i * 31 + 17) - 0.5f;
                low += (white - low) * 0.35f;
                lower += (low - lower) * 0.05f;
                samples[i] = tone * 0.45f + (low - lower) * 3f;
            }
            return Clip("Tyre squeal", samples, 0.8f);
        }

        /// <summary>Half a second of thump: a low boom, a burst of noise and a metallic clang.</summary>
        static AudioClip Thump()
        {
            int n = Rate / 2;
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                samples[i] = Mathf.Sin(2f * Mathf.PI * 55f * t) * Mathf.Exp(-t * 10f)
                             + (Hash(i * 3 + 1) - 0.5f) * 1.2f * Mathf.Exp(-t * 35f)
                             + 0.3f * Mathf.Sin(2f * Mathf.PI * 420f * t) * Mathf.Exp(-t * 8f);
            }
            return Clip("Impact", samples, 0.95f);
        }

        /// <summary>A tenth of a second of backfire: a sharp crack of noise over a low thud.</summary>
        static AudioClip Pop()
        {
            int n = Rate / 10;
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                samples[i] = (Hash(i * 5 + 9) - 0.5f) * 2f * Mathf.Exp(-t * 60f)
                             + Mathf.Sin(2f * Mathf.PI * 90f * t) * Mathf.Exp(-t * 30f);
            }
            return Clip("Backfire", samples, 0.95f);
        }

        static AudioClip Clip(string name, float[] samples, float peak)
        {
            float max = 1e-6f;
            foreach (float s in samples)
                max = Mathf.Max(max, Mathf.Abs(s));
            for (int i = 0; i < samples.Length; i++)
                samples[i] *= peak / max;
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
