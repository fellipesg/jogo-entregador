using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Entregador.Editor
{
    /// <summary>Configura o projeto para celular: asset URP leve, orientação paisagem.</summary>
    public static class RenderSetup
    {
        public const string SettingsFolder = "Assets/_Jogo/Settings";
        public const string PipelinePath = SettingsFolder + "/Mobile_URP.asset";
        public const string RendererPath = SettingsFolder + "/Mobile_Renderer.asset";

        /// <summary>Procura o renderer feature de SSAO do renderer mobile (null se não existir).</summary>
        public static ScriptableRendererFeature FindSsao()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);

            if (renderer == null)
            {
                return null;
            }

            foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
            {
                if (feature is ScreenSpaceAmbientOcclusion)
                {
                    return feature;
                }
            }

            return null;
        }

        /// <summary>Garante o SSAO no renderer mobile, desligado: o WeatherLook o liga só com a flag de qualidade UseSsao.</summary>
        private static void EnsureSsao()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);

            if (renderer == null || FindSsao() != null)
            {
                return;
            }

            var feature = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            feature.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
            feature.SetActive(false);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Jogo/Configurar URP mobile e paisagem")]
        public static void Apply()
        {
            Directory.CreateDirectory(SettingsFolder);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);

            if (pipeline == null)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);

                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, RendererPath);
                }

                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 2;
            pipeline.shadowDistance = 60f;
            pipeline.shadowCascadeCount = 1;
            pipeline.mainLightShadowmapResolution = 1024;
            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
            serialized.FindProperty("m_SoftShadowQuality").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EnsureSsao();
            pipeline.renderScale = 1f;
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = pipeline;

            int original = QualitySettings.GetQualityLevel();

            for (int level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                QualitySettings.renderPipeline = pipeline;
            }

            QualitySettings.SetQualityLevel(original, false);

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            AssetDatabase.SaveAssets();
        }
    }
}
