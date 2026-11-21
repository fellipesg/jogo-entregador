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

            pipeline.supportsHDR = false;
            pipeline.msaaSampleCount = 2;
            pipeline.shadowDistance = 45f;
            pipeline.shadowCascadeCount = 1;
            pipeline.mainLightShadowmapResolution = 1024;
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
