using System.IO;
using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Materiais simples (URP) da cidade. Ficam como assets para sobreviverem em builds.</summary>
    public static class CityMaterials
    {
        public const string Folder = "Assets/_Jogo/Materials";

        public static Material Lit(string name, Color color, float smoothness = 0.05f)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);

            return material;
        }

        public static Material Unlit(string name, Color color, bool doubleSided = false)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
            material.SetColor("_BaseColor", color);

            if (doubleSided)
            {
                material.SetFloat("_Cull", 0f);
            }

            EditorUtility.SetDirty(material);

            return material;
        }

        private static Material LoadOrCreate(string name, string shaderName)
        {
            Directory.CreateDirectory(Folder);
            string path = $"{Folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                Shader shader = Shader.Find(shaderName);
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            return material;
        }
    }
}
