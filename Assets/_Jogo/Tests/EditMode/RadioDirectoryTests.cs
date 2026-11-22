using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;

namespace Entregador.Core.Tests
{
    /// <summary>Lógica pura do rádio pela internet: Radio Browser (resposta gravada), lista curada, filtros, cache, busca, favoritos, MP3 e ICY.</summary>
    public sealed class RadioDirectoryTests
    {
        private static string Fixture(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "_Jogo/Tests/EditMode/Fixtures", name), Encoding.UTF8);

        private static string CuradaPath => Path.Combine(Application.dataPath, "_Jogo/Audio/Radio/Resources/RadioCurada.json");

        private static List<RadioStationInfo> Api() => RadioDirectory.ParseApi(Fixture("radio_browser_sp.json"));

        private static List<RadioStationInfo> Curadas() => RadioDirectory.ParseCurada(File.ReadAllText(CuradaPath, Encoding.UTF8));

        // ---------- JSON ----------

        [Test]
        public void JsonParsesEscapesNumbersAndNesting()
        {
            var root = (Dictionary<string, object>)RadioJson.Parse("{\"a\":\"caf\\u00e9 \\\"x\\\"\\n\",\"n\":[1,2.5,-3],\"o\":{\"t\":true,\"f\":false,\"z\":null}}");
            Assert.AreEqual("café \"x\"\n", RadioJson.Str(root, "a"));
            var list = (List<object>)root["n"];
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(2.5, (double)list[1], 1e-9);
            var o = (Dictionary<string, object>)root["o"];
            Assert.IsTrue(RadioJson.Bool(o, "t"));
            Assert.IsFalse(RadioJson.Bool(o, "f"));
            Assert.AreEqual("x", RadioJson.Str(o, "z", "x"));
        }

        [Test]
        public void JsonRejectsBrokenInput()
        {
            Assert.Throws<FormatException>(() => RadioJson.Parse("{\"a\":"));
            Assert.Throws<FormatException>(() => RadioJson.Parse("[1,2"));
            Assert.Throws<FormatException>(() => RadioJson.Parse("{\"a\" 1}"));
            Assert.IsNull(RadioJson.Parse(string.Empty));
        }

        [Test]
        public void JsonQuoteRoundTrips()
        {
            string tricky = "Rádio \"A\" \\ B\nC\t\u0001";
            Assert.AreEqual(tricky, RadioJson.Parse(RadioJson.Quote(tricky)));
        }

        // ---------- Resposta do Radio Browser ----------

        [Test]
        public void ParsesTheRecordedRadioBrowserResponse()
        {
            List<RadioStationInfo> all = Api();
            Assert.AreEqual(16, all.Count);
            RadioStationInfo usp = all.First(s => s.Nome == "Rádio USP 93.7 FM");
            Assert.AreEqual("MP3", usp.Codec);
            Assert.AreEqual(128, usp.Bitrate);
            Assert.AreEqual("https://flow.emm.usp.br:8008/radiousp-128.mp3", usp.Url);
            Assert.IsTrue(usp.Ok);
            Assert.IsFalse(usp.Hls);
            Assert.AreEqual(54, usp.Votos);
            Assert.IsFalse(string.IsNullOrEmpty(usp.Uuid));

            RadioStationInfo cultura = all.First(s => s.Nome.StartsWith("Cultura Brasil"));
            Assert.IsTrue(cultura.Hls, "hls=1 e .m3u8");
            Assert.IsFalse(all.First(s => s.Uuid == "broken-uuid-0001").Ok, "lastcheckok = 0");
            Assert.AreEqual("Rádio \"Aspas\" & Cia\\n", all.First(s => s.Uuid == "quote-uuid-0001").Nome);
        }

        [Test]
        public void FilterForUnityKeepsOnlyWorkingMp3WithoutDuplicates()
        {
            List<RadioStationInfo> filtered = RadioDirectory.Filtrar(Api(), RadioCaps.UnityPadrao);
            string[] names = filtered.Select(s => s.Nome).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                "RÁDIO LIGA SAMBA", "Sertaneja 106.7", "Rádio Joli MPB", "LIGA SAMBA WEB RÁDIO", "Rádio Teomidia Instrumental", "Rádio USP 93.7 FM", "Rádio \"Aspas\" & Cia\\n",
            }, names, "Ordem por votos; sem AAC, FLV, HLS, fora do ar e a repetida");
            Assert.IsTrue(filtered.All(s => s.Codec == "MP3" && s.Ok));
        }

        [Test]
        public void FilterForNativeBackendKeepsAacAndHls()
        {
            List<RadioStationInfo> filtered = RadioDirectory.Filtrar(Api(), RadioCaps.Nativo);
            Assert.IsTrue(filtered.Any(s => s.Nome == "CBN São Paulo"), "AAC+");
            Assert.IsTrue(filtered.Any(s => s.Nome.StartsWith("Cultura Brasil")), "HLS");
            Assert.IsFalse(filtered.Any(s => s.Codec == "FLV"), "FLV não toca");
            Assert.IsFalse(filtered.Any(s => s.Uuid == "broken-uuid-0001"));
            Assert.AreEqual(13, filtered.Count);
        }

        [Test]
        public void CapabilitiesByCodec()
        {
            RadioCaps unity = RadioCaps.UnityPadrao;
            Assert.IsTrue(unity.Pode("mp3", false));
            Assert.IsTrue(unity.Pode("OGG", false));
            Assert.IsFalse(unity.Pode("AAC+", false));
            Assert.IsFalse(unity.Pode("MP3", true), "HLS só no nativo");
            Assert.IsFalse(unity.Pode("UNKNOWN", false));
            Assert.IsTrue(RadioCaps.Nativo.Pode("UNKNOWN", true));
            Assert.IsFalse(RadioCaps.Nativo.Pode("FLV", false));
            Assert.IsFalse(RadioCaps.Nenhum.Pode("MP3", false));
        }

        [Test]
        public void ClassifiesGenresFromTagsAndNames()
        {
            Assert.AreEqual(RadioDirectory.GeneroNoticias, RadioDirectory.Classificar("news,sports", "CBN São Paulo"));
            Assert.AreEqual(RadioDirectory.GeneroGospel, RadioDirectory.Classificar("gospel,instrumental", "Rádio Teomidia"));
            Assert.AreEqual(RadioDirectory.GeneroFunkRap, RadioDirectory.Classificar("funk,rap,rnb,soul", "Radio Soul One"));
            Assert.AreEqual(RadioDirectory.GeneroSamba, RadioDirectory.Classificar(string.Empty, "RÁDIO LIGA SAMBA"));
            Assert.AreEqual(RadioDirectory.GeneroSertanejo, RadioDirectory.Classificar("country,sertanejo", "Sertaneja 106.7"));
            Assert.AreEqual(RadioDirectory.GeneroRock, RadioDirectory.Classificar("classic rock", "Rádio Bla Rock"));
            Assert.AreEqual(RadioDirectory.GeneroMpb, RadioDirectory.Classificar("mpb,brazilian music", "Alpha MPB"));
            Assert.AreEqual(RadioDirectory.GeneroPop, RadioDirectory.Classificar("pop", "Band FM"));
            Assert.AreEqual(RadioDirectory.GeneroOutras, RadioDirectory.Classificar(string.Empty, "Rádio Sem Gênero"));
        }

        // ---------- Lista curada do repositório ----------

        [Test]
        public void CuratedListCoversTheWantedGenresAndPublicBroadcasters()
        {
            List<RadioStationInfo> curadas = Curadas();
            Assert.GreaterOrEqual(curadas.Count, 20);
            Assert.IsTrue(curadas.All(s => RadioDirectory.UrlValida(s.Url)), "Todas com URL http(s)");
            Assert.IsTrue(curadas.All(s => !string.IsNullOrEmpty(s.Nome) && !string.IsNullOrEmpty(s.Homepage) || s.Nome.StartsWith("Nova Brasil")), "Nome e página inicial");
            Assert.AreEqual(curadas.Count, curadas.Select(s => s.Nome).Distinct().Count(), "Sem nomes repetidos");

            foreach (string g in new[] { RadioDirectory.GeneroNoticias, RadioDirectory.GeneroPop, RadioDirectory.GeneroSertanejo, RadioDirectory.GeneroRock, RadioDirectory.GeneroFunkRap, RadioDirectory.GeneroSamba, RadioDirectory.GeneroGospel, RadioDirectory.GeneroMpb })
            {
                Assert.IsTrue(curadas.Any(s => s.Genero == g), "Falta o gênero " + g);
            }

            string[] publicas = curadas.Where(s => s.Publica).Select(s => s.Nome).ToArray();
            Assert.That(publicas, Has.Some.Contain("USP"));
            Assert.That(publicas, Has.Some.Contain("Cultura"));
            Assert.That(publicas, Has.Some.Contain("Nacional"));
            Assert.That(publicas, Has.Some.Contain("Câmara"));
            Assert.That(publicas, Has.Some.Contain("Senado"));
            Assert.IsTrue(curadas.Count(s => !string.IsNullOrEmpty(s.Uuid)) >= curadas.Count - 2, "Quase todas têm o uuid do Radio Browser");
        }

        [Test]
        public void CuratedListHasEnoughMp3ForTheUnityBackend()
        {
            List<RadioStationInfo> noUnity = RadioDirectory.Filtrar(Curadas(), RadioCaps.UnityPadrao);
            Assert.GreaterOrEqual(noUnity.Count, 15, "No editor e no iOS (sem AVPlayer) as MP3 da lista bastam para a roda");
            Assert.GreaterOrEqual(noUnity.Select(s => s.Genero).Distinct().Count(), 8, "Todos os gêneros seguem representados");
            Assert.IsTrue(noUnity.Any(s => s.Publica), "Há pública tocável (USP, Câmara)");
            Assert.AreEqual(Curadas().Count, RadioDirectory.Filtrar(Curadas(), RadioCaps.Nativo).Count, "No Android entram todas, inclusive AAC e HLS");
        }

        [Test]
        public void MergePutsPublicBroadcastersFirstThenCuratedThenApiWithoutDuplicates()
        {
            List<RadioStationInfo> curadas = Curadas();
            List<RadioStationInfo> merged = RadioDirectory.Juntar(curadas, RadioDirectory.Filtrar(Api(), RadioCaps.Nativo), 5);
            int firstCommercial = merged.FindIndex(s => !s.Publica);
            Assert.Greater(firstCommercial, 0);
            Assert.IsTrue(merged.Take(firstCommercial).All(s => s.Publica), "Públicas primeiro");
            Assert.IsTrue(merged.Take(curadas.Count).All(s => s.Curada));
            Assert.AreEqual(curadas.Count + 5, merged.Count, "Limite de 5 da API");
            Assert.AreEqual(merged.Count, merged.Select(s => s.Url).Distinct().Count());
            Assert.AreEqual(1, merged.Count(s => s.Nome.Contains("USP 93.7")), "A USP da API não repete a da lista curada");
        }

        [Test]
        public void RefreshDropsStationsThatRadioBrowserSaysAreDownAndKeepsPlayableUrls()
        {
            var curada = new RadioStationInfo { Uuid = "u1", Nome = "A", Url = "https://a/live.mp3", Codec = "MP3", Curada = true };
            var hls = new RadioStationInfo { Uuid = "u2", Nome = "B", Url = "https://b/x.m3u8", Codec = "HLS", Hls = true, Curada = true };
            var caida = new RadioStationInfo { Uuid = "u3", Nome = "C", Url = "https://c/live.mp3", Codec = "MP3", Curada = true };
            var minha = new RadioStationInfo { Uuid = "u4", Nome = "D", Url = "https://d/variante.mp3", Codec = "MP3", Curada = true };
            var fresco = new List<RadioStationInfo>
            {
                new RadioStationInfo { Uuid = "u1", Nome = "A", Url = "https://a/novo.aac", Codec = "AAC", Ok = true },
                new RadioStationInfo { Uuid = "u2", Nome = "B", Url = "https://b/novo.mp3", Codec = "MP3", Bitrate = 96, Ok = true },
                new RadioStationInfo { Uuid = "u3", Nome = "C", Url = "https://c/live.mp3", Codec = "MP3", Ok = false },
                new RadioStationInfo { Uuid = "u4", Nome = "D", Url = "https://d/aac-original", Codec = "AAC", Ok = false },
            };
            RadioDirectory.AtualizarCuradas(new[] { curada, hls, caida, minha }, fresco, RadioCaps.UnityPadrao);
            Assert.AreEqual("https://a/live.mp3", curada.Url, "Não troca o MP3 por AAC que o Unity não toca");
            Assert.AreEqual("https://b/novo.mp3", hls.Url, "Troca o HLS (intocável no Unity) por MP3");
            Assert.AreEqual("MP3", hls.Codec);
            Assert.IsFalse(hls.Hls);
            Assert.IsFalse(caida.Ok, "Mesma URL marcada fora do ar");
            Assert.IsTrue(minha.Ok, "A URL que o repositório escolheu segue valendo mesmo que a original tenha caído");
        }

        // ---------- Busca e favoritos ----------

        [Test]
        public void SearchIgnoresAccentsAndFiltersByGenre()
        {
            List<RadioStationInfo> list = Curadas();
            Assert.IsTrue(RadioDirectory.Buscar(list, "radio usp", null).Any(s => s.Nome.Contains("USP")), "sem acento");
            Assert.IsTrue(RadioDirectory.Buscar(list, "CAMARA", null).Any(s => s.Nome.Contains("Câmara")), "maiúsculas e acento");
            Assert.IsTrue(RadioDirectory.Buscar(list, string.Empty, RadioDirectory.GeneroSertanejo).All(s => s.Genero == RadioDirectory.GeneroSertanejo));
            Assert.IsEmpty(RadioDirectory.Buscar(list, "zzzxxx", null));
            Assert.AreEqual(list.Count, RadioDirectory.Buscar(list, "  ", null).Count);
        }

        [Test]
        public void FavoritesToggleAndRoundTrip()
        {
            HashSet<string> favs = RadioDirectory.DecodificarFavoritos(string.Empty);
            Assert.IsEmpty(favs);
            Assert.IsTrue(RadioDirectory.AlternarFavorito(favs, "abc"));
            Assert.IsTrue(RadioDirectory.AlternarFavorito(favs, "url:https://x/y"));
            HashSet<string> again = RadioDirectory.DecodificarFavoritos(RadioDirectory.CodificarFavoritos(favs));
            Assert.IsTrue(again.SetEquals(favs));
            Assert.IsFalse(RadioDirectory.AlternarFavorito(again, "abc"));
            Assert.AreEqual(1, again.Count);
            Assert.AreEqual("url:https://x/y", new RadioStationInfo { Url = "https://x/y" }.Chave, "Sem uuid, a chave é a URL");
        }

        // ---------- Cache e reconexão ----------

        [Test]
        public void CacheRoundTripsAndExpiresAfterOneDay()
        {
            List<RadioStationInfo> list = RadioDirectory.Filtrar(Api(), RadioCaps.Nativo);
            var when = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
            string json = RadioDirectory.ParaCache(list, when);
            Assert.IsTrue(RadioDirectory.LerCache(json, out List<RadioStationInfo> back, out DateTime at));
            Assert.AreEqual(when, at);
            Assert.AreEqual(list.Count, back.Count);

            for (int i = 0; i < list.Count; i++)
            {
                Assert.AreEqual(list[i].Nome, back[i].Nome);
                Assert.AreEqual(list[i].Url, back[i].Url);
                Assert.AreEqual(list[i].Uuid, back[i].Uuid);
                Assert.AreEqual(list[i].Codec, back[i].Codec);
                Assert.AreEqual(list[i].Hls, back[i].Hls);
                Assert.AreEqual(list[i].Genero, back[i].Genero);
                Assert.AreEqual(list[i].Votos, back[i].Votos);
            }

            Assert.IsTrue(RadioDirectory.CacheFresco(when, when.AddHours(23)));
            Assert.IsFalse(RadioDirectory.CacheFresco(when, when.AddHours(25)));
            Assert.IsFalse(RadioDirectory.CacheFresco(when, when.AddHours(-1)), "Relógio voltou: não confia");
        }

        [Test]
        public void CorruptOrEmptyCacheIsRejected()
        {
            Assert.IsFalse(RadioDirectory.LerCache(string.Empty, out _, out _));
            Assert.IsFalse(RadioDirectory.LerCache("{not json", out _, out _));
            Assert.IsFalse(RadioDirectory.LerCache("{\"buscadoEm\":\"ontem\",\"estacoes\":[]}", out _, out _));
            Assert.IsFalse(RadioDirectory.LerCache("{\"buscadoEm\":\"2026-11-22T12:00:00.0000000Z\",\"estacoes\":[]}", out _, out _), "Cache vazio não serve");
        }

        [Test]
        public void ReconnectBackoffDoublesUpToACap()
        {
            Assert.AreEqual(1f, RadioDirectory.EsperaReconexao(0));
            Assert.AreEqual(2f, RadioDirectory.EsperaReconexao(1));
            Assert.AreEqual(4f, RadioDirectory.EsperaReconexao(2));
            Assert.AreEqual(15f, RadioDirectory.EsperaReconexao(10));
            Assert.AreEqual(1f, RadioDirectory.EsperaReconexao(-3));
        }

        [Test]
        public void ApiUrlsAreWellFormedAndSendNoPersonalData()
        {
            string search = RadioDirectory.UrlBusca("https://de1.api.radio-browser.info", 200);
            StringAssert.StartsWith("https://de1.api.radio-browser.info/json/stations/search?", search);
            StringAssert.Contains("state=S%C3%A3o%20Paulo", search);
            StringAssert.Contains("countrycode=BR", search);
            StringAssert.Contains("order=votes", search);
            StringAssert.Contains("hidebroken=true", search);
            Assert.AreEqual("https://x/json/stations/byuuid?uuids=a,b", RadioDirectory.UrlPorUuid("https://x", new[] { "a", "b" }));
            Assert.AreEqual("https://x/json/url/abc", RadioDirectory.UrlClique("https://x", "abc"));
            Assert.AreEqual("JogoEntregador/0.1", RadioDirectory.UserAgent);
            Assert.IsTrue(RadioDirectory.EhHls("https://a/b/playlist.m3u8?x=1"));
            Assert.IsFalse(RadioDirectory.EhHls("https://a/b/stream.mp3"));
        }

        // ---------- MP3 ao vivo ----------

        private static byte[] Frame(int paddingBit = 0)
        {
            // MPEG1 camada III, 128 kbps, 44,1 kHz: 417 bytes (418 com padding), 1152 amostras = 26,12 ms.
            var f = new byte[paddingBit == 0 ? 417 : 418];
            f[0] = 0xFF;
            f[1] = 0xFB;
            f[2] = (byte)(0x90 | (paddingBit << 1));
            f[3] = 0x00;

            for (int i = 4; i < f.Length; i++)
            {
                f[i] = (byte)(1 + i % 200); // nunca 0xFF: não parece cabeçalho
            }

            return f;
        }

        [Test]
        public void Mp3HeaderLengthsMatchTheSpec()
        {
            byte[] f = Frame();
            Assert.IsTrue(Mp3Header.TryParse(f, 0, f.Length, out Mp3Header h));
            Assert.AreEqual(417, h.Comprimento);
            Assert.AreEqual(1152, h.Amostras);
            Assert.AreEqual(44100, h.TaxaAmostragem);
            Assert.AreEqual(128, h.Bitrate);
            Assert.AreEqual(1152.0 / 44100, h.Segundos, 1e-9);

            byte[] padded = Frame(1);
            Assert.IsTrue(Mp3Header.TryParse(padded, 0, padded.Length, out h));
            Assert.AreEqual(418, h.Comprimento);

            // MPEG2 camada III, 64 kbps, 22,05 kHz: 72 * 64000 / 22050 = 208, 576 amostras.
            var v2 = new byte[] { 0xFF, 0xF3, 0x80, 0x00 };
            Assert.IsTrue(Mp3Header.TryParse(v2, 0, 4, out h));
            Assert.AreEqual(576, h.Amostras);
            Assert.AreEqual(22050, h.TaxaAmostragem);
            Assert.AreEqual(208, h.Comprimento);

            Assert.IsFalse(Mp3Header.TryParse(new byte[] { 0xFF, 0xFB, 0xF0, 0x00 }, 0, 4, out _), "bitrate 15 inválido");
            Assert.IsFalse(Mp3Header.TryParse(new byte[] { 0xFF, 0xFB, 0x9C, 0x00 }, 0, 4, out _), "taxa 3 inválida");
            Assert.IsFalse(Mp3Header.TryParse(new byte[] { 0xFF, 0xE1, 0x90, 0x00 }, 0, 4, out _), "versão reservada / camada 0");
            Assert.IsFalse(Mp3Header.TryParse(new byte[] { 0x12, 0x34, 0x56, 0x78 }, 0, 4, out _));
        }

        private static List<byte[]> Segments(Mp3Segmenter seg, byte[] stream, int chunk, out List<double> seconds, out List<int> prerolls)
        {
            var result = new List<byte[]>();
            seconds = new List<double>();
            prerolls = new List<int>();

            for (int i = 0; i < stream.Length; i += chunk)
            {
                seg.Push(stream, i, Math.Min(chunk, stream.Length - i));

                while (seg.TryTake(out byte[] s, out double sec, out int pre))
                {
                    result.Add(s);
                    seconds.Add(sec);
                    prerolls.Add(pre);
                }
            }

            return result;
        }

        private static byte[] Build(int frames, byte[] prefix = null, int junkEvery = 0)
        {
            var ms = new MemoryStream();

            if (prefix != null)
            {
                ms.Write(prefix, 0, prefix.Length);
            }

            for (int i = 0; i < frames; i++)
            {
                byte[] f = Frame(i % 3 == 0 ? 1 : 0);
                ms.Write(f, 0, f.Length);

                if (junkEvery > 0 && i % junkEvery == junkEvery - 1)
                {
                    ms.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, 0, 7);
                }
            }

            return ms.ToArray();
        }

        [Test]
        public void SegmenterCutsLiveMp3AtFrameBoundariesAndSkipsId3()
        {
            var id3 = new List<byte> { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 40 };
            id3.AddRange(new byte[40]);
            byte[] stream = Build(400, id3.ToArray());
            var seg = new Mp3Segmenter(2.0);
            List<byte[]> parts = Segments(seg, stream, 1000, out List<double> seconds, out List<int> pre);

            Assert.GreaterOrEqual(parts.Count, 4, "400 quadros = 10 s = pelo menos 4 trechos de 2 s");
            Assert.IsTrue(seconds.All(s => s >= 2.0 && s < 2.1), "Cada trecho tem o alvo de duração");
            Assert.AreEqual(2 * 1152, pre[0], "O primeiro tem só os dois quadros de silêncio");
            Assert.IsTrue(pre.Skip(1).All(p => p == 5 * 1152), "Os demais têm o silêncio e repetem três quadros");

            foreach (byte[] p in parts)
            {
                // Cada trecho é uma cadeia de quadros inteiros, do começo ao fim.
                int at = 0;

                while (at < p.Length)
                {
                    Assert.IsTrue(Mp3Header.TryParse(p, at, p.Length, out Mp3Header h), "cabeçalho válido em " + at);
                    at += h.Comprimento;
                }

                Assert.AreEqual(p.Length, at, "Termina na fronteira do quadro");
            }
        }

        [Test]
        public void SegmenterStartsEachSegmentWithSilentFramesAndRepeatsTheLastThreeFramesOfThePreviousOne()
        {
            byte[] stream = Build(300);
            var seg = new Mp3Segmenter(1.0);
            List<byte[]> parts = Segments(seg, stream, 5000, out _, out _);
            Assert.GreaterOrEqual(parts.Count, 3);

            for (int i = 1; i < parts.Count; i++)
            {
                // Dois quadros de silêncio: cabeçalho igual ao do stream, tudo depois dele zerado.
                byte[] cur = parts[i];
                Assert.IsTrue(Mp3Header.TryParse(cur, 0, cur.Length, out Mp3Header h));
                int silent = h.Comprimento;

                for (int q = 0; q < Mp3Segmenter.QuadrosSilenciosos; q++)
                {
                    for (int k = 4; k < silent; k++)
                    {
                        Assert.AreEqual(0, cur[q * silent + k], "silêncio zerado");
                    }
                }

                // Os três últimos quadros do trecho anterior vêm repetidos logo depois.
                byte[] prev = parts[i - 1];
                var starts = new List<int>();
                int at = 0;

                // Pula o silêncio do próprio trecho anterior.
                Assert.IsTrue(Mp3Header.TryParse(prev, 0, prev.Length, out Mp3Header ph));
                at = Mp3Segmenter.QuadrosSilenciosos * ph.Comprimento;

                while (at < prev.Length)
                {
                    starts.Add(at);
                    Mp3Header.TryParse(prev, at, prev.Length, out Mp3Header fh);
                    at += fh.Comprimento;
                }

                int tail = prev.Length - starts[starts.Count - 3];

                for (int k = 0; k < tail; k++)
                {
                    Assert.AreEqual(prev[prev.Length - tail + k], cur[Mp3Segmenter.QuadrosSilenciosos * silent + k], "byte " + k + " do preâmbulo do trecho " + i);
                }
            }
        }

        [Test]
        public void SegmenterDoesNotDropAudioAcrossRandomChunkSizes()
        {
            byte[] stream = Build(500);
            int[] chunkSizes = { 1, 7, 333, 417, 4096 };
            double reference = -1;

            foreach (int chunk in chunkSizes)
            {
                var seg = new Mp3Segmenter(2.0, 0);
                List<byte[]> parts = Segments(seg, stream, chunk, out List<double> seconds, out _);
                double sec = seconds.Sum();

                if (reference < 0)
                {
                    reference = sec;
                }

                Assert.AreEqual(reference, sec, 1e-6, "Mesmo áudio com pedaços de " + chunk + " bytes");
                Assert.Greater(sec, 10.0, "Só sobra o que ainda não completou um trecho");
            }
        }

        [Test]
        public void SegmenterResyncsAfterJunkInTheMiddle()
        {
            byte[] stream = Build(300, new byte[] { 9, 9, 9, 9, 9 }, 20);
            var seg = new Mp3Segmenter(1.0, 0);
            List<byte[]> parts = Segments(seg, stream, 700, out List<double> seconds, out _);
            Assert.GreaterOrEqual(parts.Count, 5);
            Assert.GreaterOrEqual(seconds.Sum(), 6.0);

            foreach (byte[] p in parts)
            {
                Assert.IsTrue(Mp3Header.TryParse(p, 0, p.Length, out _), "Cada trecho começa num quadro");
            }
        }

        [Test]
        public void SegmenterIgnoresPureGarbage()
        {
            var seg = new Mp3Segmenter(1.0);
            var junk = new byte[20000];
            new System.Random(5).NextBytes(junk);

            for (int i = 0; i < junk.Length; i++)
            {
                if (junk[i] == 0xFF)
                {
                    junk[i] = 0x7F;
                }
            }

            seg.Push(junk, 0, junk.Length);
            Assert.IsFalse(seg.TryTake(out _, out _));
            Assert.Less(seg.Pendentes, 8, "O lixo é descartado, o buffer não cresce sem fim");
        }

        // ---------- ICY ----------

        private static byte[] IcyStream(int metaint, int blocks, string title)
        {
            var ms = new MemoryStream();
            byte[] meta = Encoding.UTF8.GetBytes("StreamTitle='" + title + "';");
            int padded = (meta.Length + 15) / 16 * 16;

            for (int b = 0; b < blocks; b++)
            {
                for (int i = 0; i < metaint; i++)
                {
                    ms.WriteByte((byte)(b * 31 + i));
                }

                if (b == 1)
                {
                    ms.WriteByte((byte)(padded / 16));
                    ms.Write(meta, 0, meta.Length);
                    ms.Write(new byte[padded - meta.Length], 0, padded - meta.Length);
                }
                else
                {
                    ms.WriteByte(0);
                }
            }

            return ms.ToArray();
        }

        [Test]
        public void IcyDemuxStripsMetadataAndReadsTheTitleWithAnyChunking()
        {
            const int metaint = 64;
            byte[] stream = IcyStream(metaint, 4, "Artista - Música");

            foreach (int chunk in new[] { 1, 5, 64, 65, 1000 })
            {
                var demux = new IcyDemux(metaint);
                var audio = new List<byte>();

                for (int i = 0; i < stream.Length; i += chunk)
                {
                    var piece = stream.Skip(i).Take(chunk).ToArray();
                    demux.Processar(piece, piece.Length, audio);
                }

                Assert.AreEqual(4 * metaint, audio.Count, "Só o áudio, com pedaços de " + chunk);
                Assert.AreEqual("Artista - Música", demux.Titulo);

                for (int b = 0; b < 4; b++)
                {
                    Assert.AreEqual((byte)(b * 31), audio[b * metaint], "Início do bloco de áudio " + b);
                }
            }
        }

        [Test]
        public void IcyDemuxPassesEverythingThroughWithoutMetaint()
        {
            var demux = new IcyDemux(0);
            var audio = new List<byte>();
            demux.Processar(new byte[] { 1, 2, 3, 4 }, 4, audio);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, audio);
            Assert.AreEqual(string.Empty, demux.Titulo);
        }

        [Test]
        public void IcyTitleParsing()
        {
            Assert.AreEqual("A - B", IcyDemux.LerTitulo("StreamTitle='A - B';StreamUrl='';"));
            Assert.AreEqual("It's ok", IcyDemux.LerTitulo("StreamTitle='It's ok';"));
            Assert.AreEqual(string.Empty, IcyDemux.LerTitulo("StreamTitle='';"));
            Assert.IsNull(IcyDemux.LerTitulo("StreamUrl='x';"));
        }
    }
}
