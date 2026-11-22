using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Entregador.Core;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Copia o que sai de um AudioSource (OnAudioFilterRead) para gravar um WAV e medir o volume.</summary>
    public sealed class AudioTap : MonoBehaviour
    {
        private readonly object _lock = new object();
        private readonly List<float> _samples = new List<float>();
        public int Channels;
        public bool Gravando;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!Gravando)
            {
                return;
            }

            lock (_lock)
            {
                Channels = channels;
                _samples.AddRange(data);
            }
        }

        public float[] Copiar()
        {
            lock (_lock)
            {
                return _samples.ToArray();
            }
        }

        public int Contagem
        {
            get
            {
                lock (_lock)
                {
                    return _samples.Count;
                }
            }
        }
    }

    /// <summary>
    /// Testes com a internet de verdade: tocam um stream ao vivo pelo Unity. Sem rede viram Inconclusive.
    /// Opcional: JOGO_WAV_DIR grava a captura de uns 10 s em radio_stream.wav (prova de que o áudio saiu).
    /// </summary>
    [Category("Network")]
    public sealed class RadioStreamNetworkTests
    {
        private const string Mp3Usp = "https://flow.emm.usp.br:8008/radiousp-128.mp3";
        private const string Mp3Cbn = "https://playerservices.streamtheworld.com/api/livestream-redirect/CBN_SP.mp3";
        private const string AacMetropolitana = "https://ice.fabricahost.com.br/metropolitana985sp";

        private static IEnumerator RequireOnline(string url)
        {
            var head = UnityWebRequest.Head(url);
            head.timeout = 10;
            head.SetRequestHeader("User-Agent", "JogoEntregador/0.1");
            yield return head.SendWebRequest();
            bool offline = head.result == UnityWebRequest.Result.ConnectionError;
            string error = head.error;
            head.Dispose();

            if (offline)
            {
                Assert.Ignore("Sem rede (" + error + "): teste de rede ignorado.");
            }
        }

        private static void WriteWav(string path, float[] samples, int channels, int rate)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(bytes);

                foreach (float f in samples)
                {
                    w.Write((short)(Mathf.Clamp(f, -1f, 1f) * 32767f));
                }
            }
        }

        private static float Rms(float[] s, int from)
        {
            double sum = 0;
            int n = 0;

            for (int i = from; i < s.Length; i++)
            {
                sum += s[i] * s[i];
                n++;
            }

            return n == 0 ? 0f : (float)Math.Sqrt(sum / n);
        }

        private static IEnumerator WaitState(UnityRadioBackend backend, RadioStreamState want, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;

            while (backend.Estado != want && backend.Estado != RadioStreamState.Erro && Time.realtimeSinceStartup < until)
            {
                backend.Tick(Time.unscaledDeltaTime);

                yield return null;
            }
        }

        /// <summary>Janelas de 20 ms com quase nenhuma energia (falhas na emenda dos trechos), depois de pular o começo.</summary>
        private static int CountDropouts(float[] s, int channels, int rate, float skipSeconds)
        {
            int win = rate * channels / 50;
            int from = (int)(skipSeconds * rate * channels);
            var energies = new List<float>();

            for (int i = from; i + win <= s.Length; i += win)
            {
                double sum = 0;

                for (int k = 0; k < win; k++)
                {
                    sum += s[i + k] * s[i + k];
                }

                energies.Add((float)Math.Sqrt(sum / win));
            }

            if (energies.Count == 0)
            {
                return 0;
            }

            var sorted = new List<float>(energies);
            sorted.Sort();
            float median = sorted[sorted.Count / 2];
            int drops = 0;

            foreach (float e in energies)
            {
                if (e < median * 0.05f)
                {
                    drops++;
                }
            }

            return drops;
        }

        private static GameObject NewListener(out AudioTap tap)
        {
            var go = new GameObject("RadioNetListener", typeof(AudioListener));
            tap = go.AddComponent<AudioTap>();
            tap.Gravando = true;

            return go;
        }

        private static IEnumerator PlayAndRecord(string url, float seconds, string wavName, Action<float[], int, UnityRadioBackend> done)
        {
            GameObject listener = NewListener(out AudioTap tap);
            var go = new GameObject("RadioNet");
            var backend = new UnityRadioBackend(go);
            backend.SetVolume(1f);
            backend.Tocar(url);
            yield return WaitState(backend, RadioStreamState.Tocando, 30f);
            Assert.AreEqual(RadioStreamState.Tocando, backend.Estado, "O stream começa a tocar. Erro: " + backend.Erro);
            Debug.Log($"[RadioNet] {url} tocando; bitrate visto={backend.BitrateVisto} kbps");

            float until = Time.realtimeSinceStartup + seconds;

            while (Time.realtimeSinceStartup < until)
            {
                backend.Tick(Time.unscaledDeltaTime);

                yield return null;
            }

            Assert.AreEqual(RadioStreamState.Tocando, backend.Estado, "Segue tocando depois de " + seconds + " s. Erro: " + backend.Erro);
            tap.Gravando = false;
            float[] samples = tap.Copiar();
            int channels = Mathf.Max(1, tap.Channels);
            Debug.Log($"[RadioNet] bytes={backend.BytesRecebidos} amostras={samples.Length} canais={channels} rms={Rms(samples, 0):F4} titulo={backend.AgoraTocando}");
            string dir = Environment.GetEnvironmentVariable("JOGO_WAV_DIR");

            if (!string.IsNullOrEmpty(dir) && wavName != null && samples.Length > 0)
            {
                Directory.CreateDirectory(dir);
                WriteWav(Path.Combine(dir, wavName), samples, channels, AudioSettings.outputSampleRate);
            }

            done?.Invoke(samples, channels, backend);
            backend.Parar();
            Assert.AreEqual(RadioStreamState.Parado, backend.Estado);
            backend.Dispose();
            UnityEngine.Object.Destroy(go);
            UnityEngine.Object.Destroy(listener);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator UnityBackendPlaysAnInfiniteMp3Stream()
        {
            yield return RequireOnline(Mp3Usp);

            yield return PlayAndRecord(Mp3Usp, 14f, "radio_stream.wav", (samples, channels, backend) =>
            {
                Assert.Greater(backend.BytesRecebidos, 100_000L, "Chegaram bytes do stream.");
                Assert.Greater(samples.Length, AudioSettings.outputSampleRate * channels * 8, "A captura tem pelo menos 8 s de áudio.");
                Assert.Greater(Rms(samples, samples.Length / 2), 0.005f, "O áudio não é silêncio.");
                int drops = CountDropouts(samples, channels, AudioSettings.outputSampleRate, 2f);
                Debug.Log($"[RadioNet] janelas de 20 ms sem áudio (falhas): {drops}");
                Assert.LessOrEqual(drops, 3, "A emenda dos trechos não deixa buracos.");
            });
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator UnityBackendFollowsRedirectsAndReadsIcyMetadata()
        {
            yield return RequireOnline(Mp3Cbn);

            // O streamtheworld redireciona para um servidor numerado; o Shoutcast v1 abaixo manda metadados ICY.
            yield return PlayAndRecord(Mp3Cbn, 8f, null, (samples, channels, backend) =>
            {
                Assert.Greater(Rms(samples, samples.Length / 2), 0.003f);
            });
            yield return PlayAndRecord("http://srv1.braudio.com.br:7308/;stream", 8f, null, (samples, channels, backend) =>
            {
                Assert.Greater(Rms(samples, samples.Length / 2), 0.003f);
            });
        }

        /// <summary>Prova do porquê do recorte em trechos: o streamAudio nativo do Unity não toca stream sem tamanho. Só registra; não falha.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator DiagnosticoStreamAudioNativoDoUnity()
        {
            yield return RequireOnline(Mp3Usp);

            LogAssert.ignoreFailingMessages = true;
            var handler = new DownloadHandlerAudioClip(Mp3Usp, AudioType.MPEG) { streamAudio = true };
            var req = new UnityWebRequest(Mp3Usp, "GET", handler, null);
            req.SetRequestHeader("User-Agent", "JogoEntregador/0.1");
            req.SendWebRequest();
            float until = Time.realtimeSinceStartup + 8f;
            AudioClip clip = null;

            while (Time.realtimeSinceStartup < until && clip == null)
            {
                if (req.downloadedBytes > 100_000)
                {
                    clip = handler.audioClip;
                }

                yield return null;
            }

            Debug.Log($"[RadioNet] streamAudio nativo: bytes={req.downloadedBytes} clip={(clip != null ? "criado, samples=" + clip.samples + " loadState=" + clip.loadState : "null (tamanho desconhecido)")}");
            req.Abort();
            req.Dispose();
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void UnityBackendDoesNotClaimAacOrHls()
        {
            RadioCaps caps = RadioCaps.UnityPadrao;
            Assert.IsTrue(caps.Pode("MP3", false));
            Assert.IsFalse(caps.Pode("AAC", false));
            Assert.IsFalse(caps.Pode("MP3", true));
        }
    }
}
