using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Configura a importação dos modelos do Kenney (Assets/ThirdParty/Kenney): escala de importação 1 (o tamanho
    /// final é decidido no posicionamento, ver CityPieces.Model), sem animação e sem materiais embutidos
    /// (todos usam o material único com o colormap, criado em CityMaterials.Kenney).
    /// </summary>
    public sealed class KenneyImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/ThirdParty/Kenney/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.isReadable = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
        }
    }
}
