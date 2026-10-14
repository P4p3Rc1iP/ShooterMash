using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// Recorded engine loops, one per engine piece, swappable in the inspector (the asset is
    /// Assets/Audio/Vehicle sounds.asset). Each loop is a steady recording at one engine speed;
    /// it's pitched from idlePitch at idle to redlinePitch at the redline. Engines without a
    /// loop use VehicleAudio's synthesised one.
    ///
    /// Loops are cleaned up when first used: MP3s start (and often end) with tens of
    /// milliseconds of silence, which would hiccup every repeat, so that's trimmed and the seam
    /// crossfaded.
    /// </summary>
    [CreateAssetMenu(menuName = "ShooterMashup/Vehicle sound bank")]
    public class VehicleSoundBank : ScriptableObject
    {
        [Serializable]
        public class EngineSound
        {
            [Tooltip("The engine piece's name, e.g. \"Engine V8\".")]
            public string engine;
            public AudioClip clip;
            [Tooltip("Pitch at idle and at the redline: how the recording is stretched over the revs.")]
            public float idlePitch = 0.7f, redlinePitch = 2f;
            [Range(0f, 2f)] public float volume = 1f;
        }

        public List<EngineSound> engines = new List<EngineSound>();

        /// <summary>The bank in use (set by a VehicleSounds component in the scene), or null.</summary>
        public static VehicleSoundBank Current { get; set; }

        readonly Dictionary<AudioClip, AudioClip> prepared = new Dictionary<AudioClip, AudioClip>();

        public EngineSound For(string engine)
        {
            if (string.IsNullOrEmpty(engine))
                return null;
            foreach (EngineSound s in engines)
                if (s.engine == engine && s.clip != null)
                    return s;
            return null;
        }

        /// <summary>The clip with its silent ends trimmed and its seam crossfaded (made once).</summary>
        public AudioClip Loop(AudioClip clip)
        {
            if (clip == null)
                return null;
            if (prepared.TryGetValue(clip, out AudioClip done) && done != null)
                return done;
            if (clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();
            int ch = clip.channels, n = clip.samples;
            var data = new float[n * ch];
            if (!clip.GetData(data, 0))
                return clip;
            const float quiet = 0.003f;
            bool Loud(int i)
            {
                for (int c = 0; c < ch; c++)
                    if (Mathf.Abs(data[i * ch + c]) > quiet) return true;
                return false;
            }
            int start = 0, end = n;
            while (start < n && !Loud(start)) start++;
            while (end > start && !Loud(end - 1)) end--;
            int length = end - start;
            int fade = Mathf.Min(clip.frequency / 25, length / 4);    // 40 ms
            int outLength = length - fade;
            if (outLength <= 0)
                return clip;
            var output = new float[outLength * ch];
            for (int i = 0; i < outLength; i++)
            for (int c = 0; c < ch; c++)
            {
                float s = data[(start + i) * ch + c];
                if (i < fade)
                {
                    // The tail fades into the head, so the loop point is seamless.
                    float t = i / (float)fade;
                    s = s * t + data[(start + outLength + i) * ch + c] * (1f - t);
                }
                output[i * ch + c] = s;
            }
            AudioClip loop = AudioClip.Create(clip.name + " (loop)", outLength, ch, clip.frequency, false);
            loop.SetData(output, 0);
            prepared[clip] = loop;
            return loop;
        }
    }
}
