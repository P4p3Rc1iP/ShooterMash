using UnityEngine;

namespace ShooterMashup.Building
{
    /// <summary>
    /// Placeholder textures per material, generated at runtime: wood planks, stone bricks and
    /// riveted iron plates. They're greyscale so the colour can come from the material (or from
    /// the support view) while the pattern still tells materials apart. One tile is 1 m, like
    /// the dev grid, and they tile seamlessly.
    /// </summary>
    public static class BuildTextures
    {
        const int Size = 256; // pixels per metre

        public static Texture2D Create(BuildMaterial material)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = material + " (placeholder)" };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float v = material switch
                {
                    BuildMaterial.Stone => Stone(x, y),
                    BuildMaterial.Iron => Iron(x, y),
                    _ => Wood(x, y),
                };
                byte c = (byte)(Mathf.Clamp01(v) * 255f);
                pixels[y * Size + x] = new Color32(c, c, c, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
            return tex;
        }

        /// <summary>25 cm boards with staggered end joints and grain.</summary>
        static float Wood(int x, int y)
        {
            const int board = Size / 4;
            int b = y / board, by = y % board;
            if (by < 2) return 0.35f; // gap between boards

            int joint = (int)(Hash(b, 0, 11) * Size);
            int dx = Mod(x - joint, Size);
            if (dx < 2) return 0.4f;  // board end

            float grain = Mathf.Sin((by + Noise(x / 32f, b * 3.1f, Size / 32, 1000, 3) * 10f) * 0.8f);
            float tone = 0.72f + 0.16f * Hash(b, 0, 5); // one board per row per metre
            float edge = by < 4 || by > board - 3 ? 0.85f : 1f;
            return tone * (0.9f + 0.08f * grain) * edge + 0.06f * Noise(x / 8f, y / 8f, Size / 8, Size / 8, 9);
        }

        /// <summary>50 x 25 cm bricks in running bond.</summary>
        static float Stone(int x, int y)
        {
            const int w = Size / 2, h = Size / 4, mortar = 4;
            int row = y / h;
            int sx = x + (row % 2) * (w / 2);
            int col = Mod(sx, Size) / w;
            int bx = Mod(sx, w), by = y % h;
            if (bx < mortar || by < mortar) return 0.42f;

            float tone = 0.68f + 0.22f * Hash(col, row, 21);
            float bevel = bx < mortar + 3 || by < mortar + 3 ? 0.88f : bx > w - 3 || by > h - 3 ? 0.92f : 1f;
            float speckle = Noise(x / 4f, y / 4f, Size / 4, Size / 4, 4);
            return tone * bevel * (0.88f + 0.14f * speckle);
        }

        /// <summary>50 cm plates with seams, a bevel and corner rivets, brushed.</summary>
        static float Iron(int x, int y)
        {
            const int plate = Size / 2;
            int px = x % plate, py = y % plate;
            if (px < 3 || py < 3) return 0.3f;                  // seam
            if (px < 5 || py < 5) return 0.95f;                  // lit bevel
            if (px > plate - 3 || py > plate - 3) return 0.55f;  // shaded bevel

            // Rivets 12 px in from each corner.
            int rx = px < plate / 2 ? 14 : plate - 12;
            int ry = py < plate / 2 ? 14 : plate - 12;
            float d = Mathf.Sqrt((px - rx) * (px - rx) + (py - ry) * (py - ry));
            if (d < 4f) return 1f - d * 0.06f;
            if (d < 5.5f) return 0.45f;

            float brushed = Noise(x / 2f, y / 32f, Size / 2, Size / 32, 7);
            float tone = 0.7f + 0.08f * Hash(x / plate, y / plate, 31);
            return tone * (0.92f + 0.12f * brushed);
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;

        /// <summary>Integer hash to [0, 1).</summary>
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>Smooth value noise that repeats every periodX by periodY lattice cells.</summary>
        static float Noise(float x, float y, int periodX, int periodY, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float Corner(int cx, int cy) => Hash(Mod(cx, periodX), Mod(cy, periodY), seed);
            float a = Mathf.Lerp(Corner(x0, y0), Corner(x0 + 1, y0), fx);
            float b = Mathf.Lerp(Corner(x0, y0 + 1), Corner(x0 + 1, y0 + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }
    }
}
