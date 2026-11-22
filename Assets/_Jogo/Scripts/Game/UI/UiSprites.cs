using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Sprites gerados em código na partida (nada de arquivos): retângulos arredondados com 9-slice, contorno de 1,5 px, sombra suave,
    /// círculo, anel e brilho radial. Todos brancos: a cor vem do Image.color.
    /// </summary>
    public static class UiSprites
    {
        /// <summary>A sombra é maior que o painel em ShadowPad pixels de cada lado.</summary>
        public const int ShadowPad = 18;

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Fill(int radius)
        {
            return Get("fill" + radius, () => Rounded(radius, 0, 0f, false, false));
        }

        public static Sprite Outline(int radius)
        {
            return Get("out" + radius, () => Rounded(radius, 0, 1.5f, true, false));
        }

        public static Sprite Shadow(int radius)
        {
            return Get("shadow" + radius, () => Rounded(radius, ShadowPad, 0f, false, true));
        }

        public static Sprite Circle()
        {
            return Get("circle", () => Disc(64, 0f));
        }

        public static Sprite Ring()
        {
            return Get("ring", () => Disc(64, 3f));
        }

        /// <summary>Degradê horizontal: branco opaco à esquerda, transparente à direita.</summary>
        public static Sprite GradientH()
        {
            return Get("gradh", () =>
            {
                const int n = 64;
                Texture2D tex = NewTexture(n);
                var pixels = new Color32[n * n];

                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float a = 1f - x / (n - 1f);
                        a = a * a * (3f - 2f * a);
                        pixels[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply(false, true);

                return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            });
        }

        /// <summary>Brilho radial suave (luzes do painel).</summary>
        public static Sprite Glow()
        {
            return Get("glow", () =>
            {
                const int n = 64;
                Texture2D tex = NewTexture(n);
                var pixels = new Color32[n * n];

                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = Mathf.Clamp01(1f - d);
                        a *= a;
                        pixels[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }

                tex.SetPixels32(pixels);
                tex.Apply(false, true);

                return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            });
        }

        private static Sprite Get(string key, System.Func<Sprite> make)
        {
            if (!Cache.TryGetValue(key, out Sprite sprite) || sprite == null)
            {
                sprite = make();
                Cache[key] = sprite;
            }

            return sprite;
        }

        private static Texture2D NewTexture(int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static Sprite Disc(int n, float stroke)
        {
            Texture2D tex = NewTexture(n);
            var pixels = new Color32[n * n];
            float r = n / 2f - 1f;

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) - r;
                    float a = stroke <= 0f ? Mathf.Clamp01(0.5f - d) : Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + stroke + 0.5f);
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        private static float Sdf(float px, float py, float half, float radius)
        {
            float qx = Mathf.Abs(px) - (half - radius);
            float qy = Mathf.Abs(py) - (half - radius);
            float ox = Mathf.Max(qx, 0f);
            float oy = Mathf.Max(qy, 0f);

            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static Sprite Rounded(int radius, int pad, float stroke, bool outlineOnly, bool shadow)
        {
            int body = radius * 2 + 2;
            int size = body + pad * 2;
            Texture2D tex = NewTexture(size);
            var pixels = new Color32[size * size];
            float half = body / 2f;
            float c = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Sdf(x + 0.5f - c, y + 0.5f - c, half, radius);
                    float a;

                    if (shadow)
                    {
                        a = Mathf.Clamp01(1f - (d + 2f) / pad);
                        a = a * a * 0.8f;
                    }
                    else if (outlineOnly)
                    {
                        a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + stroke + 0.5f);
                    }
                    else
                    {
                        a = Mathf.Clamp01(0.5f - d);
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            int border = radius + pad;

            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }
    }
}
