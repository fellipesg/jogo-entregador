using System.IO;
using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Cores realistas dos carros: o sorteio ponderado e a pintura dos carros do Kenney (vans) que ainda sobram; os carros reais (CarrosReais) usam as mesmas cores. Pinta os carros do Kenney (branco, prata, preto, vermelho, azul-escuro, grafite). O kit usa um colormap
    /// de gradientes; aqui cada variante é uma cópia dele em que as células saturadas (a carroceria) trocam de cor e as cinzas,
    /// brancas e claras (vidros, rodas, faróis) ficam como estavam. Um material por cor, compartilhado por todos os carros.
    /// </summary>
    public static class CarPaint
    {
        public static readonly (string name, Color color)[] Colors =
        {
            ("Branco", new Color(0.92f, 0.92f, 0.9f)),
            ("Prata", new Color(0.66f, 0.68f, 0.7f)),
            ("Preto", new Color(0.07f, 0.07f, 0.08f)),
            ("Vermelho", new Color(0.62f, 0.07f, 0.08f)),
            ("AzulEscuro", new Color(0.07f, 0.13f, 0.36f)),
            ("Grafite", new Color(0.26f, 0.27f, 0.29f)),
        };

        /// <summary>Pesos do sorteio (mistura de São Paulo): branco, prata, preto, vermelho, azul-escuro e grafite; cerca de 60% branco, prata e preto.</summary>
        private static readonly float[] Weights = { 0.26f, 0.19f, 0.17f, 0.13f, 0.11f, 0.14f };

        /// <summary>Sorteia o índice de uma cor com a distribuição realista.</summary>
        public static int Sorteio(System.Random random)
        {
            float roll = (float)random.NextDouble();

            for (int i = 0; i < Weights.Length; i++)
            {
                roll -= Weights[i];

                if (roll <= 0f)
                {
                    return i;
                }
            }

            return 0;
        }

        private const string Folder = "Assets/_Jogo/Materials/Carros";

        /// <summary>Material do carro na cor de índice index (módulo o número de cores).</summary>
        public static Material Get(int index)
        {
            index = ((index % Colors.Length) + Colors.Length) % Colors.Length;
            string name = Colors[index].name;
            string texturePath = $"{Folder}/Carro_{name}.png";
            string materialPath = $"{Folder}/Carro_{name}.mat";
            Directory.CreateDirectory(Folder);

            if (!File.Exists(texturePath))
            {
                WriteTexture(texturePath, Colors[index].color);
                AssetDatabase.ImportAsset(texturePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.65f);
            material.SetFloat("_Metallic", 0.25f);
            EditorUtility.SetDirty(material);

            return material;
        }

        private static void WriteTexture(string path, Color target)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes($"{KenneyImportSettings.Folder}CarKit/Models/Textures/colormap.png"));
            Color32[] pixels = source.GetPixels32();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color.RGBToHSV(pixels[i], out float h, out float s, out float v);

                if (s > 0.45f && v > 0.35f && pixels[i].a > 0)
                {
                    float shade = Mathf.Clamp(v / 0.9f, 0.55f, 1.05f);
                    Color c = target * shade;
                    c.a = 1f;
                    pixels[i] = c;
                }
            }

            source.SetPixels32(pixels);
            File.WriteAllBytes(path, source.EncodeToPNG());
            Object.DestroyImmediate(source);
        }
    }
}
