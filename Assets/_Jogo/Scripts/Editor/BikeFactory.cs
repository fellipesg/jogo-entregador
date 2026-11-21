using System.IO;
using Entregador.Game;
using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Monta a moto: Rigidbody e collider (inalterados), o visual procedural (MotoVisual) e os componentes de jogo.</summary>
    public static class BikeFactory
    {
        private const string MeshesPath = "Assets/_Jogo/Scenes/SantoAmaro_MotoMalhas.asset";
        private const string PalettePath = CityMaterials.Folder + "/Moto_Paleta.asset";

        public static ArcadeBikeController Build(Vector3 position, float yaw)
        {
            return Build(position, yaw, MotoModelo.Titan, MotoCor.Vermelha, Personagem.Jefinho);
        }

        public static ArcadeBikeController Build(Vector3 position, float yaw, MotoModelo modelo, MotoCor cor, Personagem personagem)
        {
            var root = new GameObject("Moto");
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var body = root.AddComponent<Rigidbody>();
            body.mass = 180f;
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.65f, 0f);
            collider.size = new Vector3(0.75f, 1.2f, 1.9f);
            collider.sharedMaterial = SlipperyMaterial();

            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            // Visual procedural (MotoModel/RiderModel): 4 materiais (paleta, metal, luzes e o baú do app).
            var motoVisual = visual.gameObject.AddComponent<MotoVisual>();
            motoVisual.DefinirMateriais(PaletteMaterial("Moto_Paleta", 0f), PaletteMaterial("Moto_Metal", 0.9f), LightMaterial(), CityMaterials.Lit("Moto_Bau", Color.white, 0.35f));
            AssetDatabase.DeleteAsset(PalettePath);
            motoVisual.Configurar(modelo, cor, personagem);
            SaveGenerated(motoVisual);

            var input = root.AddComponent<BikeInput>();
            var controller = root.AddComponent<ArcadeBikeController>();
            controller.Configure(input, visual);
            motoVisual.Ligar(controller);

            return controller;
        }

        /// <summary>Material Lit que lê cor (RGB) e suavidade (alfa) da textura de paleta.</summary>
        private static Material PaletteMaterial(string name, float metallic)
        {
            Material material = CityMaterials.Lit(name, Color.white, 1f);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 1f);
            material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>Material das lentes (farol e lanterna): cor e emissão vêm de MaterialPropertyBlock em jogo.</summary>
        private static Material LightMaterial()
        {
            Material material = CityMaterials.Lit("Moto_Luzes", Color.white, 0.8f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>Grava as malhas geradas (e a textura da paleta) como assets, para a cena não perdê-las.</summary>
        private static void SaveGenerated(MotoVisual visual)
        {
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);

            Material paletteMaterial = null;

            foreach (MeshRenderer renderer in visual.GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.name == "Moto_Paleta")
                {
                    paletteMaterial = renderer.sharedMaterial;

                    break;
                }
            }

            if (palette == null && paletteMaterial != null && paletteMaterial.GetTexture("_BaseMap") is Texture2D texture)
            {
                AssetDatabase.CreateAsset(texture, PalettePath);
            }

            AssetDatabase.DeleteAsset(MeshesPath);
            bool first = true;
            var seen = new System.Collections.Generic.HashSet<Mesh>();

            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.GetComponent<TMPro.TMP_Text>() != null || filter.sharedMesh == null || !seen.Add(filter.sharedMesh))
                {
                    continue;
                }

                if (first)
                {
                    AssetDatabase.CreateAsset(filter.sharedMesh, MeshesPath);
                    first = false;
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(filter.sharedMesh, MeshesPath);
                }
            }

            AssetDatabase.SaveAssets();
        }

        private static PhysicsMaterial SlipperyMaterial()
        {
            Directory.CreateDirectory(RenderSetup.SettingsFolder);
            string path = RenderSetup.SettingsFolder + "/MotoFisica.asset";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);

            if (material == null)
            {
                material = new PhysicsMaterial("MotoFisica");
                AssetDatabase.CreateAsset(material, path);
            }

            material.dynamicFriction = 0f;
            material.staticFriction = 0f;
            material.bounciness = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Minimum;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);

            return material;
        }
    }
}
