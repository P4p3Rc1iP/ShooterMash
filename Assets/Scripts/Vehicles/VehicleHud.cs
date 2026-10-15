using UnityEngine;

namespace ShooterMashup.Vehicles
{
    /// <summary>
    /// The driver's gauges (IMGUI, bottom right): speed and gear, a rev bar that goes yellow and
    /// red towards the redline and flashes on the limiter, a TC light when traction control holds
    /// the wheels back (T turns it off: "TC OFF"), and the wheels seen from above, coloured by
    /// what each tyre is doing:
    /// green gripping, orange sliding sideways, red spinning under power, blue locked, grey in the
    /// air. Driven wheels have a white edge.
    /// </summary>
    public static class VehicleHud
    {
        static readonly Color Grip = new Color(0.35f, 0.85f, 0.35f);
        static readonly Color Slide = new Color(1f, 0.6f, 0.1f);
        static readonly Color Spin = new Color(1f, 0.15f, 0.1f);
        static readonly Color Lock = new Color(0.3f, 0.55f, 1f);
        static readonly Color Air = new Color(0.45f, 0.45f, 0.45f);

        static GUIStyle big, small, label;
        static float tcUntil, escUntil;

        public static void Draw(Vehicle v)
        {
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
                big.normal.textColor = Color.white;
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.UpperLeft, richText = true };
                small.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
                label = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
                label.normal.textColor = Color.white;
            }
            const float width = 400f, height = 215f;
            var panel = new Rect(Screen.width - width - 20f, Screen.height - height - 20f, width, height);
            Fill(panel, new Color(0f, 0f, 0f, 0.45f));

            // The wheels, seen from above, on the left.
            var plan = new Rect(panel.x + 10f, panel.y + 10f, 100f, panel.height - 20f);
            DrawWheels(v, plan);

            // Speed and gear.
            float x = plan.xMax + 15f, right = panel.xMax - 12f;
            EngineRevs revs = v.Revs;
            string gear = revs.Gear < 0 ? "R" : revs.Gear.ToString();
            GUI.Label(new Rect(x, panel.y + 4f, right - x - 70f, 44f), $"{Mathf.Abs(v.ForwardSpeed) * 3.6f:0}", big);
            GUI.Label(new Rect(right - 68f, panel.y + 22f, 68f, 24f), "km/h", small);
            Fill(new Rect(right - 40f, panel.y + 52f, 40f, 40f), new Color(1f, 1f, 1f, 0.12f));
            GUI.Label(new Rect(right - 40f, panel.y + 52f, 40f, 40f), revs.Shifting ? "-" : gear, label);
            GUI.Label(new Rect(right - 68f, panel.y + 36f, 68f, 16f), revs.Manual ? "manual (Q/E)" : "auto", small);

            // The rev bar: segments up to the redline, the last tenth red; flashing on the limiter.
            const int segments = 24;
            float barX = x, barW = right - x - 50f, barY = panel.y + 60f, barH = 26f;
            float fraction = revs.Redline > 0f ? revs.Rpm / revs.Redline : 0f;
            bool flash = revs.Limiter && Mathf.Repeat(Time.time * 12f, 1f) < 0.5f;
            float gap = 2f, segW = (barW - gap * (segments - 1)) / segments;
            for (int i = 0; i < segments; i++)
            {
                float at = (i + 1f) / segments;
                Color c = at > 0.9f ? Spin : at > 0.7f ? new Color(1f, 0.85f, 0.2f) : Grip;
                bool lit = fraction >= at - 0.5f / segments;
                if (flash)
                    c = Color.white;
                Fill(new Rect(barX + i * (segW + gap), barY, segW, barH), lit ? c : c * new Color(1f, 1f, 1f, 0.18f));
            }
            GUI.Label(new Rect(barX, barY + barH + 2f, barW, 18f), $"{revs.Rpm:0} rpm" + (revs.Limiter ? "  <b><color=#ff5040>LIMITER</color></b>" : ""), small);

            // Traction control: lit while it holds the wheels back (and a moment after, so you see it).
            if (v.TractionControlActive)
                tcUntil = Time.time + 0.2f;
            bool tc = Time.time < tcUntil;
            var tcRect = new Rect(right - 46f, barY + barH + 4f, 46f, 22f);
            bool tcOff = v.ActiveAssists.tractionSlack < 0f;
            Fill(tcRect, tcOff ? new Color(0.5f, 0.1f, 0.1f, 0.8f) : tc ? new Color(1f, 0.7f, 0.1f) : new Color(1f, 1f, 1f, 0.1f));
            GUI.Label(tcRect, tcOff ? "<size=9>TC OFF</size>" : "TC", label);
            // Stability control: lit while it pulls the car back from a slide.
            if (v.StabilityActive)
                escUntil = Time.time + 0.2f;
            var escRect = new Rect(tcRect.x - 50f, tcRect.y, 46f, 22f);
            Fill(escRect, Time.time < escUntil ? new Color(1f, 0.7f, 0.1f) : new Color(1f, 1f, 1f, 0.1f));
            GUI.Label(escRect, "ESC", label);

            // Steering: where the front wheels point (white) against where the car is going (blue),
            // both from straight ahead, over the lock there is (the band). Line them up to steer
            // along a slide.
            float steerY = barY + barH + 34f, half = (right - x) / 2f, mid = x + half;
            float degrees = 40f;
            float Px(float angle) => mid + Mathf.Clamp(angle / degrees, -1f, 1f) * half;
            Fill(new Rect(x, steerY, right - x, 12f), new Color(1f, 1f, 1f, 0.08f));
            Fill(new Rect(Px(-v.SteerLock), steerY, Px(v.SteerLock) - Px(-v.SteerLock), 12f), new Color(1f, 1f, 1f, 0.12f));
            Fill(new Rect(mid - 0.5f, steerY - 2f, 1f, 16f), new Color(1f, 1f, 1f, 0.4f));
            Fill(new Rect(Px(v.SlipAngle) - 2f, steerY - 3f, 4f, 18f), Lock);
            Fill(new Rect(Px(v.SteerAngle) - 1.5f, steerY, 3f, 12f), Color.white);
            GUI.Label(new Rect(x, steerY + 13f, right - x, 18f),
                $"steering {v.SteerAngle:0}°   <color=#4d8cff>slip {v.SlipAngle:0}°</color>   lean {v.RollAngle:0}°   <color=#aaa>assists {v.ActiveAssists.level}</color>", small);

            GUI.Label(new Rect(x, panel.yMax - 40f, right - x, 38f),
                "<color=#5bd95b>grip</color>  <color=#ff9a1a>sliding</color>  <color=#ff2a1a>wheelspin</color>  " +
                "<color=#4d8cff>locked</color>  <color=#777>in the air</color>\nwhite edge: driven wheel", small);
        }

        static void DrawWheels(Vehicle v, Rect area)
        {
            int n = v.WheelCount;
            if (n == 0)
                return;
            // Fit the wheel layout (x across, z along: front up) into the area.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            Vector3 right = Vector3.Cross(Vector3.up, v.Forward).normalized;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = v.GetWheel(i).localAnchor;
                float px = Vector3.Dot(p, right), pz = Vector3.Dot(p, v.Forward);
                minX = Mathf.Min(minX, px); maxX = Mathf.Max(maxX, px);
                minZ = Mathf.Min(minZ, pz); maxZ = Mathf.Max(maxZ, pz);
            }
            float spanX = Mathf.Max(maxX - minX, 1f), spanZ = Mathf.Max(maxZ - minZ, 1f);
            float scale = Mathf.Min((area.width - 24f) / spanX, (area.height - 40f) / spanZ);
            Vector2 center = area.center;
            // The body, faintly.
            Fill(new Rect(center.x - spanX * scale / 2f + 6f, center.y - spanZ * scale / 2f - 8f, spanX * scale - 12f, spanZ * scale + 16f),
                new Color(1f, 1f, 1f, 0.08f));
            for (int i = 0; i < n; i++)
            {
                Vehicle.WheelState w = v.GetWheel(i);
                float px = Vector3.Dot(w.localAnchor, right) - (minX + maxX) / 2f;
                float pz = Vector3.Dot(w.localAnchor, v.Forward) - (minZ + maxZ) / 2f;
                float tw = Mathf.Clamp(w.width * scale, 8f, 16f), th = Mathf.Clamp(tw * 2f, 18f, 30f);
                var r = new Rect(center.x + px * scale - tw / 2f, center.y - pz * scale - th / 2f, tw, th);
                Color c = !w.grounded ? Air : w.locked ? Lock : w.spinning ? Spin : w.sliding ? Slide : Grip;
                if (w.driven)
                    Fill(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Color.white);
                Fill(r, c);
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
