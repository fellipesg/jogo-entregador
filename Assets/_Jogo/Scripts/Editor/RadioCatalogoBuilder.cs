using System.IO;
using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Cria o asset do catálogo do rádio (Assets/_Jogo/Audio/Radio/Resources/RadioCatalogo.asset) com as cinco estações fictícias e
    /// garante que o jogo convive com a música de outros apps (iOS não silencia as outras fontes de áudio).
    /// Para pôr faixas reais: arraste os AudioClips na lista "Faixas" da estação.
    /// </summary>
    public static class RadioCatalogoBuilder
    {
        private const string Folder = "Assets/_Jogo/Audio/Radio/Resources";
        private const string AssetPath = Folder + "/RadioCatalogo.asset";

        [MenuItem("Jogo/Criar catálogo do rádio")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            var existing = AssetDatabase.LoadAssetAtPath<Entregador.Game.RadioCatalogo>(AssetPath);

            if (existing == null)
            {
                var catalogo = ScriptableObject.CreateInstance<Entregador.Game.RadioCatalogo>();
                catalogo.Estacoes = Entregador.Game.RadioCatalogo.Padrao();
                AssetDatabase.CreateAsset(catalogo, AssetPath);
            }

            // Convive com a música do celular (Spotify etc.): não silencia outros apps e não prepara o iOS para gravação.
            PlayerSettings.muteOtherAudioSources = false;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>Para o -executeMethod.</summary>
        public static void BuildFromCommandLine()
        {
            Build();
        }
    }
}
