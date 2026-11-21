using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Pipeline dos materiais fotográficos (Poly Haven, CC0) em Assets/ThirdParty/PolyHaven/Textures.
    ///
    /// Cada conjunto tem `nome_diff`, `nome_nor_gl` (EXR) e `nome_arm` (R = AO, G = rugosidade, B = metal). O URP Lit não
    /// tem "mask map" do HDRP, então o <see cref="ProcessTextures"/> gera `nome_ms.png` com R = metal, G = AO e
    /// A = suavidade (1 - rugosidade): a mesma textura serve de _MetallicGlossMap (R e A) e de _OcclusionMap (G).
    /// As normais EXR viram `nome_nor.png` (os EXR são apagados) e todas as texturas ganham importação mobile (ASTC).
    /// Os UVs da cidade são gerados em METROS do mundo (ver <see cref="MeshBatch"/>); por isso o "tiling" do material
    /// é 1 / (metros por repetição da textura), definido na tabela <see cref="Sets"/>.
    /// </summary>
    public static class PbrMaterials
    {
        public const string TexFolder = "Assets/ThirdParty/PolyHaven/Textures/";
        public const string MaterialFolder = "Assets/_Jogo/Materials/Pbr";

        private struct SetInfo
        {
            public string Folder;
            public string Res;
            public float TileMeters;
            public int MaxSize;
            public float NormalScale;

            public SetInfo(string folder, string res, float tile, int maxSize, float normalScale = 1f)
            {
                Folder = folder;
                Res = res;
                TileMeters = tile;
                MaxSize = maxSize;
                NormalScale = normalScale;
            }
        }

        // Metros por repetição: reboco 2,5 m; tijolo 1,6 m (uma textura de 1k tem ~25 tijolos de largura); asfalto 4 m.
        private static readonly Dictionary<string, SetInfo> Sets = new Dictionary<string, SetInfo>
        {
            { "asphalt", new SetInfo("asphalt_02", "2k", 6f, 2048, 0.7f) },
            { "sidewalk", new SetInfo("concrete_pavement", "2k", 3f, 2048) },
            { "paving", new SetInfo("hexagonal_concrete_paving", "1k", 3f, 1024) },
            { "floor", new SetInfo("concrete_floor_02", "1k", 3f, 1024) },
            { "plaster", new SetInfo("painted_plaster_wall", "1k", 2.5f, 1024) },
            { "plaster2", new SetInfo("white_plaster_02", "1k", 2.5f, 1024) },
            { "paint", new SetInfo("beige_wall_001", "1k", 2.5f, 1024) },
            { "crack", new SetInfo("cracked_concrete_wall", "1k", 3f, 1024) },
            { "concrete", new SetInfo("concrete_wall_003", "1k", 3f, 1024) },
            { "grey", new SetInfo("grey_plaster", "1k", 2.5f, 1024) },
            { "brick", new SetInfo("brick_4", "1k", 2.0f, 1024) },
            { "brick2", new SetInfo("red_brick_03", "1k", 2.0f, 1024) },
            { "roof", new SetInfo("clay_roof_tiles_02", "1k", 2f, 1024) },
            { "iron", new SetInfo("corrugated_iron", "1k", 2f, 1024) },
            { "rust", new SetInfo("rusty_metal_02", "1k", 1.6f, 1024) },
            { "greenmetal", new SetInfo("green_metal_rust", "1k", 1.6f, 1024) },
            { "grass", new SetInfo("sparse_grass", "1k", 3f, 1024) },
        };

        /// <summary>Conjuntos que ganham a variante molhada (suavidade alta) para o dia de chuva.</summary>
        private static readonly string[] WetSets = { "asphalt", "sidewalk", "paving" };

        private static string Path(string folder, string suffix, string ext) => $"{TexFolder}{folder}/{folder}_{suffix}.{ext}";

        // ---------- Texturas ----------

        [MenuItem("Jogo/Processar texturas PBR")]
        public static void ProcessTextures()
        {
            AssetDatabase.StartAssetEditing();

            try
            {
                foreach (var pair in Sets)
                {
                    GenerateFiles(pair.Key, pair.Value);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh();

            foreach (var pair in Sets)
            {
                ConfigureImports(pair.Key, pair.Value);
            }

            AssetDatabase.SaveAssets();
        }

        private static void GenerateFiles(string key, SetInfo set)
        {
            string name = set.Folder;
            string exr = $"{TexFolder}{name}/{name}_nor_gl_{set.Res}.exr";
            string normalPng = $"{TexFolder}{name}/{name}_nor.png";

            if (File.Exists(exr) && !File.Exists(normalPng))
            {
                ConvertExr(exr, normalPng);
            }

            string mask = $"{TexFolder}{name}/{name}_ms.png";

            if (!File.Exists(mask))
            {
                string arm = $"{TexFolder}{name}/{name}_arm_{set.Res}.jpg";
                WriteMask(arm, mask, false);

                if (System.Array.IndexOf(WetSets, key) >= 0)
                {
                    WriteMask(arm, $"{TexFolder}{name}/{name}_ms_wet.png", true);
                }
            }
        }

        private static void ConvertExr(string exrPath, string pngPath)
        {
            AssetDatabase.StopAssetEditing();

            try
            {
                AssetDatabase.ImportAsset(exrPath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(exrPath);
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 4096;
                importer.SaveAndReimport();

                var source = AssetDatabase.LoadAssetAtPath<Texture2D>(exrPath);
                Color[] pixels = source.GetPixels();
                var target = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color c = pixels[i];
                    pixels[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
                }

                target.SetPixels(pixels);
                File.WriteAllBytes(pngPath, target.EncodeToPNG());
                Object.DestroyImmediate(target);

                // Sanidade: a normal convertida precisa ter o azul dominante (vetor para fora da superfície).
                float blue = 0f;

                for (int i = 0; i < pixels.Length; i += 97)
                {
                    blue += pixels[i].b;
                }

                Debug.Log($"[PbrMaterials] {System.IO.Path.GetFileName(exrPath)} -> PNG ({source.width}x{source.height}, azul médio {blue / (pixels.Length / 97f):0.00})");

                AssetDatabase.DeleteAsset(exrPath);
            }
            finally
            {
                AssetDatabase.StartAssetEditing();
            }
        }

        /// <summary>Gera o mapa R = metal, G = AO, A = suavidade a partir do ARM do Poly Haven (molhado = suavidade empurrada para 1).</summary>
        private static void WriteMask(string armPath, string outPath, bool wet)
        {
            var arm = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            arm.LoadImage(File.ReadAllBytes(armPath));
            Color32[] pixels = arm.GetPixels32();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                float ao = p.r / 255f;
                float rough = p.g / 255f;
                float metal = p.b / 255f;
                float smooth = 1f - rough;

                if (wet)
                {
                    smooth = Mathf.Lerp(smooth, 0.95f, 0.8f);
                }

                pixels[i] = new Color32((byte)(metal * 255f), (byte)(ao * 255f), 0, (byte)(smooth * 255f));
            }

            arm.SetPixels32(pixels);
            File.WriteAllBytes(outPath, arm.EncodeToPNG());
            Object.DestroyImmediate(arm);
        }

        private static void ConfigureImports(string key, SetInfo set)
        {
            string name = set.Folder;
            Configure(Path(name, "diff_" + set.Res, "jpg"), set.MaxSize, true, false);
            Configure($"{TexFolder}{name}/{name}_nor.png", set.MaxSize, false, true);
            Configure($"{TexFolder}{name}/{name}_ms.png", set.MaxSize, false, false);

            if (System.Array.IndexOf(WetSets, key) >= 0)
            {
                Configure($"{TexFolder}{name}/{name}_ms_wet.png", set.MaxSize, false, false);
            }
        }

        private static void Configure(string path, int maxSize, bool srgb, bool normal)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null)
            {
                return;
            }

            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;

            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = maxSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);

            var ios = importer.GetPlatformTextureSettings("iPhone");
            ios.overridden = true;
            ios.maxTextureSize = maxSize;
            ios.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(ios);

            importer.SaveAndReimport();
        }

        // ---------- Materiais ----------

        /// <summary>
        /// Material URP/Lit do conjunto (key da tabela Sets) com tinta opcional. O nome do asset inclui a tinta,
        /// então cores iguais reaproveitam o mesmo material. wet = variante de chuva (mais escura e lisa).
        /// </summary>
        public static Material Surface(string key, string variant = null, Color? tint = null, bool wet = false, float smoothness = -1f)
        {
            if (smoothness < 0f)
            {
                smoothness = DefaultSmoothness(key);
            }

            SetInfo set = Sets[key];
            string assetName = "Pbr_" + key + (variant != null ? "_" + variant : string.Empty) + (wet ? "_molhado" : string.Empty);
            string path = $"{MaterialFolder}/{assetName}.mat";
            Directory.CreateDirectory(MaterialFolder);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            string name = set.Folder;
            var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(Path(name, "diff_" + set.Res, "jpg"));
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexFolder}{name}/{name}_nor.png");
            bool useWet = wet && System.Array.IndexOf(WetSets, key) >= 0;
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexFolder}{name}/{name}_ms{(useWet ? "_wet" : string.Empty)}.png");
            Vector2 scale = Vector2.one / set.TileMeters;

            material.SetTexture("_BaseMap", diffuse);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTexture("_BumpMap", normal);
            material.SetTextureScale("_BumpMap", scale);
            material.SetFloat("_BumpScale", set.NormalScale);
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetTextureScale("_MetallicGlossMap", scale);
            material.SetTexture("_OcclusionMap", mask);
            material.SetTextureScale("_OcclusionMap", scale);
            material.SetFloat("_OcclusionStrength", 1f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 1f);
            Color color = tint ?? Color.white;

            if (wet)
            {
                color *= 0.62f;
                color.a = 1f;
            }

            material.SetColor("_BaseColor", color);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>Escala da suavidade por conjunto: reboco e concreto quase foscos, metal e asfalto mais lisos.</summary>
        private static float DefaultSmoothness(string key)
        {
            switch (key)
            {
                case "plaster":
                case "plaster2":
                case "paint":
                case "crack":
                case "concrete":
                case "grey":
                    return 0.4f;
                case "sidewalk":
                case "paving":
                case "floor":
                    return 0.55f;
                case "asphalt":
                    return 0.65f;
                case "grass":
                    return 0.2f;
                default:
                    return 1f;
            }
        }

        /// <summary>Material sem textura (cores lisas: vidro, pintura de faixas, plástico). Reaproveita CityMaterials.Lit.</summary>
        public static Material Flat(string name, Color color, float smoothness = 0.2f, float metallic = 0f)
        {
            Material material = CityMaterials.Lit(name, color, smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>Textura procedural de folhagem (manchas verdes claras e escuras), 256x256, repetível.</summary>
        public static Texture2D FoliageTexture()
        {
            string path = $"{CityMaterials.Folder}/Folhagem.png";

            if (File.Exists(path))
            {
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var random = new System.Random(5);
            var pixels = new Color32[size * size];
            var dark = new Color(0.07f, 0.2f, 0.06f);
            var mid = new Color(0.2f, 0.42f, 0.12f);
            var light = new Color(0.5f, 0.68f, 0.22f);

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = mid;
            }

            // Folhinhas: pequenas elipses espalhadas, com repetição nas bordas.
            for (int leaf = 0; leaf < 1800; leaf++)
            {
                float cx = (float)random.NextDouble() * size;
                float cy = (float)random.NextDouble() * size;
                float angle = (float)random.NextDouble() * Mathf.PI;
                float radius = 3f + 4f * (float)random.NextDouble();
                float t = (float)random.NextDouble();
                Color color = t < 0.45f ? Color.Lerp(dark, mid, t * 2f) : Color.Lerp(mid, light, (t - 0.45f) * 1.8f);
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                for (int dy = -8; dy <= 8; dy++)
                {
                    for (int dx = -8; dx <= 8; dx++)
                    {
                        float u = dx * cos + dy * sin;
                        float v = -dx * sin + dy * cos;

                        if ((u * u) / (radius * radius) + (v * v) / (radius * radius * 0.35f) <= 1f)
                        {
                            int x = (int)((cx + dx + size) % size);
                            int y = (int)((cy + dy + size) % size);
                            pixels[y * size + x] = color;
                        }
                    }
                }
            }

            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Material Foliage(string name, Color tint)
        {
            Material material = CityMaterials.Lit(name, tint, 0.05f);
            material.SetTexture("_BaseMap", FoliageTexture());
            material.SetTextureScale("_BaseMap", Vector2.one / 1.4f);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>Entrada de linha de comando: processa as texturas e sai.</summary>
        public static void ProcessFromCommandLine()
        {
            ProcessTextures();
        }
    }
}
