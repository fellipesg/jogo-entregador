using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Entregador.Game
{
    /// <summary>
    /// Ícones das estações (favicon do Radio Browser): baixa de forma assíncrona, guarda em memória e em disco (persistentDataPath/radio_icones)
    /// e devolve null quando não dá (a interface mostra um círculo com a inicial). Cada URL é baixada uma vez; falhas não são repetidas na sessão.
    /// </summary>
    public static class RadioFavicons
    {
        private const int MaxBytes = 512 * 1024;

        private static readonly Dictionary<string, Texture2D> Memoria = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> Falhas = new HashSet<string>();
        private static readonly Dictionary<string, List<Action<Texture2D>>> Pendentes = new Dictionary<string, List<Action<Texture2D>>>();

        private static string Pasta => Path.Combine(Application.persistentDataPath, "radio_icones");

        public static bool Tem(string url) => !string.IsNullOrEmpty(url) && Memoria.ContainsKey(url);

        /// <summary>Pede o ícone. A chamada de volta vem (no máximo) uma vez, com a textura ou null. Pode vir já, se estiver em memória.</summary>
        public static void Pedir(MonoBehaviour host, string url, Action<Texture2D> pronto)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || Falhas.Contains(url))
            {
                pronto?.Invoke(null);

                return;
            }

            if (Memoria.TryGetValue(url, out Texture2D tex))
            {
                pronto?.Invoke(tex);

                return;
            }

            if (Pendentes.TryGetValue(url, out List<Action<Texture2D>> fila))
            {
                fila.Add(pronto);

                return;
            }

            Pendentes[url] = new List<Action<Texture2D>> { pronto };
            host.StartCoroutine(Baixar(url));
        }

        private static string Arquivo(string url)
        {
            uint h = 2166136261u;

            foreach (char c in url)
            {
                h = (h ^ c) * 16777619u;
            }

            return Path.Combine(Pasta, h.ToString("x8") + ".img");
        }

        private static IEnumerator Baixar(string url)
        {
            Texture2D result = null;
            string file = Arquivo(url);
            byte[] bytes = null;

            try
            {
                if (File.Exists(file))
                {
                    bytes = File.ReadAllBytes(file);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                bytes = null;
            }

            if (bytes == null)
            {
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    req.SetRequestHeader("User-Agent", Entregador.Core.RadioDirectory.UserAgent);
                    req.timeout = 10;
                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.Success && req.downloadHandler.data != null && req.downloadHandler.data.Length > 0 &&
                        req.downloadHandler.data.Length <= MaxBytes)
                    {
                        bytes = req.downloadHandler.data;
                    }
                }
            }

            if (bytes != null)
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);

                if (t.LoadImage(bytes) && t.width > 8 && t.height > 8)
                {
                    t.wrapMode = TextureWrapMode.Clamp;
                    t.filterMode = FilterMode.Bilinear;
                    result = t;

                    try
                    {
                        Directory.CreateDirectory(Pasta);

                        if (!File.Exists(file))
                        {
                            File.WriteAllBytes(file, bytes);
                        }
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        // Sem cache em disco: segue só com a memória.
                    }
                }
                else
                {
                    UnityEngine.Object.Destroy(t);
                }
            }

            if (result != null)
            {
                Memoria[url] = result;
            }
            else
            {
                Falhas.Add(url);
            }

            List<Action<Texture2D>> fila = Pendentes[url];
            Pendentes.Remove(url);

            foreach (Action<Texture2D> cb in fila)
            {
                cb?.Invoke(result);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Memoria.Clear();
            Falhas.Clear();
            Pendentes.Clear();
        }
    }
}
