using System.IO;
using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Importa os "TMP Essential Resources" (fonte LiberationSans SDF, shaders e TMP Settings) do pacote com.unity.ugui.
    /// Roda uma vez; os arquivos ficam em Assets/TextMesh Pro. Uso: -executeMethod Entregador.Editor.TmpSetup.ImportEssentials (sem -quit).
    /// </summary>
    public static class TmpSetup
    {
        /// <summary>
        /// A fonte padrão (LiberationSans SDF) vem com atlas estático só de ASCII. Liga a ela a fonte "Fallback" dinâmica
        /// do mesmo pacote, para ã, ç, é etc. aparecerem. Idempotente.
        /// </summary>
        public static void EnsureFallbackFont()
        {
            const string folder = "Assets/TextMesh Pro/Resources/Fonts & Materials/";
            var main = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(folder + "LiberationSans SDF.asset");
            var fallback = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(folder + "LiberationSans SDF - Fallback.asset");

            if (main == null || fallback == null)
            {
                Debug.LogWarning("[TmpSetup] Fontes do TMP não encontradas; rode ImportEssentials antes.");

                return;
            }

            if (main.fallbackFontAssetTable == null)
            {
                main.fallbackFontAssetTable = new System.Collections.Generic.List<TMPro.TMP_FontAsset>();
            }

            if (!main.fallbackFontAssetTable.Contains(fallback))
            {
                main.fallbackFontAssetTable.Add(fallback);
                EditorUtility.SetDirty(main);
                AssetDatabase.SaveAssets();
            }
        }

        public static void ImportEssentials()
        {
            string path = Path.GetFullPath("Library/PackageCache");
            string package = null;

            foreach (string dir in Directory.GetDirectories(path, "com.unity.ugui@*"))
            {
                string candidate = Path.Combine(dir, "Package Resources", "TMP Essential Resources.unitypackage");

                if (File.Exists(candidate))
                {
                    package = candidate;
                }
            }

            if (package == null)
            {
                Debug.LogError("[TmpSetup] Pacote TMP Essential Resources não encontrado.");
                EditorApplication.Exit(1);

                return;
            }

            AssetDatabase.importPackageCompleted += _ =>
            {
                Debug.Log("[TmpSetup] TMP Essentials importado.");
                EditorApplication.Exit(0);
            };
            AssetDatabase.importPackageFailed += (_, error) =>
            {
                Debug.LogError("[TmpSetup] Falhou: " + error);
                EditorApplication.Exit(1);
            };
#pragma warning disable CS0618
            AssetDatabase.ImportPackage(package, false);
#pragma warning restore CS0618
        }
    }
}
