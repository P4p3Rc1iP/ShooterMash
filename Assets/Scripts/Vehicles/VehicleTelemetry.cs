using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// A debug overlay for tuning (F5 while driving): the last six seconds of speed, revs,
    /// throttle, steering, slip angle, lean and sliding wheels as graphs, and per wheel its load,
    /// how much of its grip it uses, how fast it slides and what it's doing.
    /// </summary>
    public static class VehicleTelemetry
    {
        public static bool Show;

        const int Samples = 360;          // 6 s at 60 samples a second
        const float Interval = 1f / 60f;

        sealed class Channel
        {
            public readonly string name;
            public readonly Color color;
            public readonly float min, max;
            public readonly float[] values = new float[Samples];
            public Channel(string name, Color color, float min, float max)
            {
                this.name = name;
                this.color = color;
                this.min = min;
                this.max = max;
            }
        }

        static readonly Channel[] Channels =
        {
            new Channel("speed km/h", new Color(1f, 1f, 1f), 0f, 200f),
            new Channel("revs / redline", new Color(1f, 0.3f, 0.2f), 0f, 1.05f),
            new Channel("throttle", new Color(0.4f, 1f, 0.4f), -1f, 1f),
            new Channel("steering", new Color(1f, 1f, 0.4f), -1f, 1f),
            new Channel("slip angle °", new Color(0.3f, 0.6f, 1f), -60f, 60f),
            new Channel("lean °", new Color(1f, 0.6f, 1f), -30f, 30f),
            new Channel("wheels sliding", new Color(1f, 0.6f, 0.1f), 0f, 8f),
        };

        static int head;
        static float nextSample;
        static GUIStyle style;

        /// <summary>Record a sample (call every frame while driving).</summary>
        public static void Sample(Vehicle v)
        {
            if (Time.time < nextSample)
                return;
            nextSample = Time.time + Interval;
            head = (head + 1) % Samples;
            Channels[0].values[head] = Mathf.Abs(v.ForwardSpeed) * 3.6f;
            Channels[1].values[head] = v.Revs.Redline > 0f ? v.Revs.Rpm / v.Revs.Redline : 0f;
            Channels[2].values[head] = v.Throttle;
            Channels[3].values[head] = v.SteerLock > 0f ? v.SteerAngle / v.SteerLock : 0f;
            Channels[4].values[head] = v.SlipAngle;
            Channels[5].values[head] = v.RollAngle;
            Channels[6].values[head] = v.WheelsSliding;
        }

        public static void Draw(Vehicle v)
        {
            if (!Show)
                return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
                style.normal.textColor = Color.white;
            }
            const float x = 16f, w = 360f, h = 34f, gap = 4f;
            float y = 16f;
            Fill(new Rect(x - 6f, y - 6f, w + 12f, Channels.Length * (h + gap) + 30f + v.WheelCount * 15f), new Color(0f, 0f, 0f, 0.55f));
            foreach (Channel c in Channels)
            {
                var r = new Rect(x, y, w, h);
                Fill(r, new Color(1f, 1f, 1f, 0.06f));
                if (c.min < 0f)
                {
                    float zero = r.yMax - (0f - c.min) / (c.max - c.min) * r.height;
                    Fill(new Rect(r.x, zero, r.width, 1f), new Color(1f, 1f, 1f, 0.2f));
                }
                for (int i = 0; i < Samples; i++)
                {
                    float value = c.values[(head + 1 + i) % Samples];
                    float t = Mathf.Clamp01((value - c.min) / (c.max - c.min));
                    Fill(new Rect(r.x + i * r.width / Samples, r.yMax - t * r.height - 1f, 1.5f, 2f), c.color);
                }
                GUI.Label(new Rect(r.x + 3f, r.y, r.width, 16f), $"{c.name}  <b>{c.values[head]:0.##}</b>", style);
                y += h + gap;
            }
            // Per wheel.
            GUI.Label(new Rect(x, y, w, 16f), "<b>wheel   load kN   grip used   slip m/s   state</b>", style);
            y += 15f;
            for (int i = 0; i < v.WheelCount; i++)
            {
                Vehicle.WheelState s = v.GetWheel(i);
                string state = !s.grounded ? "in the air" : s.locked ? "<color=#4d8cff>locked</color>" : s.spinning ? "<color=#ff2a1a>wheelspin</color>"
                    : s.sliding ? "<color=#ff9a1a>sliding</color>" : "<color=#5bd95b>grip</color>";
                float used = s.capacity > 1f ? s.force / s.capacity : 0f;
                GUI.Label(new Rect(x, y, w, 16f),
                    $"{i + 1}{(s.driven ? " D" : "  ")}      {s.load / 1000f,5:0.0}       {used * 100f,4:0}%       {s.slip,5:0.0}      {state}", style);
                y += 15f;
            }
        }

        static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
