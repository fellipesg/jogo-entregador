using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Importação dos modelos de terceiros: personagens do Mixamo (Humanoid, sem clipes de animação; a pose é feita por código)
    /// e carros do Sketchfab (escala do arquivo, sem câmeras e luzes, legíveis para o decimador de LOD). Texturas: ASTC 6x6 no celular.
    /// </summary>
    public sealed class ThirdPartyImport : AssetPostprocessor
    {
        public const string MixamoFolder = "Assets/ThirdParty/Mixamo/";
        public const string SketchfabFolder = "Assets/ThirdParty/Sketchfab/";

        private void OnPreprocessModel()
        {
            var importer = (ModelImporter)assetImporter;

            if (assetPath.StartsWith(MixamoFolder))
            {
                importer.globalScale = 1f;
                importer.useFileScale = true;
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.isReadable = true;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importBlendShapes = false;
            }
            else if (assetPath.StartsWith(SketchfabFolder))
            {
                importer.globalScale = 1f;
                importer.useFileScale = true;
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.isReadable = true;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.importBlendShapes = false;
            }
        }
    }
}
