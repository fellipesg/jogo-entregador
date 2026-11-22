using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Backend de mentira: conecta depois de 0,2 s e pode ser mandado cair.</summary>
    public sealed class FakeRadioBackend : IRadioStreamBackend
    {
        private float _t;

        public string Nome => "Fake";

        public RadioCaps Caps => RadioCaps.Nativo;

        public RadioStreamState Estado { get; set; }

        public string Erro { get; set; }

        public string AgoraTocando { get; set; }

        public bool Falhar;
        public readonly List<string> Chamadas = new List<string>();
        public float Volume = -1f;

        public int Tocadas(string url) => Chamadas.Count(c => c == "tocar:" + url);

        public void Tocar(string url)
        {
            Chamadas.Add("tocar:" + url);
            Estado = RadioStreamState.Conectando;
            _t = 0f;
        }

        public void Parar()
        {
            Chamadas.Add("parar");
            Estado = RadioStreamState.Parado;
        }

        public void SetVolume(float volume) => Volume = volume;

        public void Tick(float dt)
        {
            if (Estado != RadioStreamState.Conectando)
            {
                return;
            }

            _t += dt;

            if (_t >= 0.2f)
            {
                Estado = Falhar ? RadioStreamState.Erro : RadioStreamState.Tocando;
                Erro = Falhar ? "queda simulada" : null;
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>O rádio pela internet ligado à cena: estados, reconexão, pausa do app, volume e ducking, app do celular, pílula do HUD e opções.</summary>
    public sealed class RadioPlayTests : SceneFixture
    {
        private FakeRadioBackend _fake;
        private RadioPlayer _radio;

        private static RadioStationInfo Info(string nome, string genero, string tags = "", bool publica = false)
        {
            return new RadioStationInfo
            {
                Uuid = "t-" + nome,
                Nome = nome,
                Url = "https://teste.example.com/" + nome.Replace(' ', '_') + ".mp3",
                Codec = "MP3",
                Bitrate = 128,
                Genero = genero,
                Tags = tags,
                Cidade = "São Paulo",
                Publica = publica,
                Curada = true,
            };
        }

        private static List<RadioStationInfo> Fakes()
        {
            return new List<RadioStationInfo>
            {
                Info("Rádio Pública Um", RadioDirectory.GeneroMpb, "mpb", true),
                Info("Notícias SP", RadioDirectory.GeneroNoticias, "news"),
                Info("Sertaneja Total", RadioDirectory.GeneroSertanejo, "sertanejo"),
                Info("Rock da Cidade", RadioDirectory.GeneroRock, "rock"),
                Info("Notícias 2", RadioDirectory.GeneroNoticias, "news"),
                Info("Pagode Show", RadioDirectory.GeneroSamba, "pagode"),
            };
        }

        private IEnumerator Prepare()
        {
            yield return WaitFor(() => RadioPlayer.Instance != null, 5f);
            _radio = RadioPlayer.Instance;
            Assert.IsNotNull(_radio);
            _radio.ReiniciarParaTestes();
            _fake = new FakeRadioBackend();
            _radio.Stream.UsarBackend(_fake);
            _radio.DefinirEstacoesInternet(Fakes());
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(RadioPlayer.MusicaExternaKey);
            PlayerPrefs.DeleteKey(RadioPlayer.InternetKey);
            PlayerPrefs.DeleteKey(RadioPlayer.FavoritosKey);
            PlayerPrefs.DeleteKey("vol_radio"); // o teste de volume grava o barramento; não deixa sujeira nas preferências do editor
            PlayerPrefs.DeleteKey("jogo.audio.master");
            RadioPlayer.Instance?.ReiniciarParaTestes();
            AudioSettingsJogo.Reset();
            Time.timeScale = 1f;
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;

            while (!condition() && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator StreamStationShowsTuningThenPlaying()
        {
            yield return Prepare();
            _radio.Sintonizar(0);
            Assert.AreEqual("Rádio Pública Um", _radio.EstacaoAtual.Nome);
            Assert.IsTrue(_radio.EstacaoAtual.EhStream);
            Assert.AreEqual("Rádio pública", _radio.EstacaoAtual.Origem);
            Assert.IsTrue(_radio.Sintonizando, "Enquanto conecta, é 'Sintonizando…'");
            Assert.AreEqual("Sintonizando…", _radio.Status);
            Assert.AreEqual(1, _fake.Tocadas(_radio.EstacaoAtual.Info.Url));

            yield return WaitFor(() => _radio.Tocando, 3f);
            Assert.IsTrue(_radio.Tocando);
            Assert.IsFalse(_radio.Sintonizando);
            Assert.AreEqual(RadioDirectory.GeneroMpb, _radio.Status, "Sem título ICY, mostra o gênero");

            _fake.AgoraTocando = "Artista - Música";
            Assert.AreEqual("Artista - Música", _radio.Status, "Com ICY, mostra o que está tocando");

            _radio.Desligar();
            Assert.AreEqual("parar", _fake.Chamadas.Last());
            Assert.AreEqual("Desligado", _radio.Status);
        }

        [UnityTest]
        public IEnumerator InternetStationsComeFirstAndGameStationsStayAsOfflineFallback()
        {
            yield return Prepare();
            Assert.AreEqual(6 + 5, _radio.Estacoes.Count);
            Assert.IsTrue(_radio.Estacoes.Take(6).All(e => e.EhStream));
            Assert.IsTrue(_radio.Estacoes.Skip(6).All(e => !e.EhStream && e.Origem == "Rádio do jogo"));
            Assert.AreEqual("Quebrada FM", _radio.Estacoes[6].Nome);

            // A roda do R tem as primeiras da lista e depois as do jogo; passa por todas e fecha no desligado.
            var seen = new List<int>();
            _radio.Desligar();

            for (int i = 0; i < 40; i++)
            {
                _radio.Proxima();

                if (_radio.IndiceAtual < 0)
                {
                    break;
                }

                seen.Add(_radio.IndiceAtual);
            }

            Assert.AreEqual(6 + 5, seen.Count, "Seis da internet (menos que o tamanho da roda) e as cinco do jogo");
            CollectionAssert.AreEqual(Enumerable.Range(0, 11).ToList(), seen);
            _radio.Anterior();
            Assert.AreEqual(10, _radio.IndiceAtual, "Shift+R do desligado vai para a última");
            _radio.Desligar();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DroppedStreamReconnectsThenSkipsToTheNextStationWithAToast()
        {
            yield return Prepare();
            Flow.EnterFreeRoam();
            Flow.Ui.ClearToast();
            _fake.Falhar = true;
            RadioEstacao first = _radio.Estacoes[0];
            int downs = 0;
            _radio.ForaDoAr += e =>
            {
                downs++;
                _fake.Falhar = false; // a próxima estação conecta
            };
            _radio.Sintonizar(0);

            yield return WaitFor(() => downs > 0, 12f);
            Assert.AreEqual(1, downs, "Depois das tentativas, avisa que caiu");
            Assert.AreEqual(1 + RadioDirectory.MaxReconexoes, _fake.Tocadas(first.Info.Url), "A primeira conexão mais as reconexões");
            Assert.AreEqual(1, _radio.IndiceAtual, "Pulou para a próxima estação");
            yield return null;
            Assert.AreEqual("Rádio fora do ar", Flow.Ui.ToastMessage);
            yield return WaitFor(() => _radio.Tocando, 3f);
            Assert.IsTrue(_radio.Tocando, "A estação seguinte toca");
            _radio.Desligar();
        }

        [UnityTest]
        public IEnumerator WhenTheOnlyInternetStationIsDownTheRadioMovesToAGameStation()
        {
            yield return Prepare();
            _radio.DefinirEstacoesInternet(new List<RadioStationInfo> { Info("Só Uma", RadioDirectory.GeneroPop) });
            _radio.Desligar();
            _fake.Falhar = true;
            _radio.Sintonizar(0);
            yield return WaitFor(() => _radio.IndiceAtual != 0, 15f);
            // Cai a internet; a próxima da roda é uma estação do jogo (sempre toca).
            Assert.IsFalse(_radio.EstacaoAtual != null && _radio.EstacaoAtual.EhStream, "Não fica preso na estação caída");
            _radio.Desligar();
        }

        [UnityTest]
        public IEnumerator AppPauseStopsTheStreamAndResumeTunesAgain()
        {
            yield return Prepare();
            _radio.Sintonizar(1);
            yield return WaitFor(() => _radio.Tocando, 3f);
            string url = _radio.EstacaoAtual.Info.Url;
            int before = _fake.Tocadas(url);

            _radio.Stream.SendMessage("OnApplicationPause", true);
            Assert.AreEqual("parar", _fake.Chamadas.Last(), "Ao pausar o app, o stream para");
            Assert.AreEqual(RadioStreamState.Parado, _fake.Estado);

            _radio.Stream.SendMessage("OnApplicationPause", false);
            Assert.AreEqual(before + 1, _fake.Tocadas(url), "Ao voltar, sintoniza de novo");
            yield return WaitFor(() => _radio.Tocando, 3f);
            Assert.IsTrue(_radio.Tocando);
            _radio.Desligar();
        }

        [UnityTest]
        public IEnumerator StreamVolumeFollowsTheRadioBusAndTheDucking()
        {
            yield return Prepare();
            AudioSettingsJogo.Master = 1f;
            AudioSettingsJogo.Set(AudioBus.Radio, 0.5f);
            _radio.Sintonizar(2);
            yield return WaitFor(() => _radio.Tocando, 3f);
            yield return new WaitForSecondsRealtime(1.2f);
            float normal = _fake.Volume;
            Assert.AreEqual(0.7f * AudioSettingsJogo.Efetivo(AudioBus.Radio), normal, 0.04f, "Volume = 0,7 x geral x barramento x ducking");
            Assert.Greater(normal, 0.2f);

            AudioSettingsJogo.GpsFalandoAte = Time.unscaledTime + 3f; // a voz do GPS abaixa o rádio
            yield return new WaitForSecondsRealtime(1.2f);
            float ducked = _fake.Volume;
            Assert.Less(ducked, normal * 0.6f, "O ducking abaixa o volume do stream");
            Assert.AreEqual(0.7f * AudioSettingsJogo.Efetivo(AudioBus.Radio), ducked, 0.04f);

            AudioSettingsJogo.Set(AudioBus.Radio, 0f);
            yield return new WaitForSecondsRealtime(1.0f);
            Assert.Less(_fake.Volume, 0.02f, "Barramento em zero cala o rádio");
            _radio.Desligar();
        }

        [UnityTest]
        public IEnumerator ExternalMusicTurnsTheInternetRadioOff()
        {
            yield return Prepare();
            _radio.Sintonizar(0);
            yield return WaitFor(() => _radio.Tocando, 3f);
            RadioPlayer.MusicaExterna = true;
            Assert.AreEqual(-1, _radio.IndiceAtual);
            Assert.AreEqual("parar", _fake.Chamadas.Last());
            Assert.IsFalse(RadioPlayer.InternetAtiva);
            Assert.IsTrue(_radio.Estacoes.All(e => !e.EhStream), "Sem a internet, só as do jogo");
            _radio.Proxima();
            Assert.IsFalse(_radio.EstacaoAtual != null && _radio.EstacaoAtual.EhStream);
            RadioPlayer.MusicaExterna = false;
            _radio.Desligar();
        }

        [UnityTest]
        public IEnumerator OptionsToggleRemovesAndKeepsTheSettingForTheInternetRadio()
        {
            yield return Prepare();
            Assert.IsTrue(RadioPlayer.InternetPreferida, "Padrão: ligado");
            _radio.Sintonizar(0);
            yield return null;
            Flow.Ui.ToggleInternetRadio();
            Assert.IsFalse(RadioPlayer.InternetPreferida);
            Assert.AreEqual(5, _radio.Estacoes.Count, "Só as cinco estações do jogo");
            Assert.AreEqual(-1, _radio.IndiceAtual, "A que tocava era da internet: para");
            Assert.AreEqual("parar", _fake.Chamadas.Last());
            Flow.Ui.ToggleInternetRadio();
            Assert.IsTrue(RadioPlayer.InternetPreferida);
        }

        [UnityTest]
        public IEnumerator PhoneRadioAppListsSearchesFiltersAndFavorites()
        {
            yield return Prepare();
            Flow.EnterFreeRoam();
            Flow.OpenPhoneMenu();
            Assert.IsTrue(Flow.Ui.ClickModalButton("Rádio"), "O celular tem o app Rádio");
            yield return null;
            Assert.IsTrue(Flow.Ui.RadioAppVisivel);
            Assert.AreEqual(11, Flow.Ui.RadioAppLinhasVisiveis, "6 da internet e 5 do jogo");

            Flow.Ui.SetRadioBusca("sertaneja");
            Assert.AreEqual(1, Flow.Ui.RadioAppLinhasVisiveis, "Busca por nome");
            Flow.Ui.SetRadioBusca("SAO PAULO");
            Assert.AreEqual(6, Flow.Ui.RadioAppLinhasVisiveis, "Sem acento nem maiúsculas, pela cidade");
            Flow.Ui.SetRadioBusca("zzz");
            Assert.AreEqual(0, Flow.Ui.RadioAppLinhasVisiveis);
            Flow.Ui.SetRadioBusca(string.Empty);

            Flow.Ui.SetRadioGenero(RadioDirectory.GeneroNoticias);
            Assert.AreEqual(2, Flow.Ui.RadioAppLinhasVisiveis, "Chip de gênero");
            Flow.Ui.SetRadioGenero(RadioDirectory.GeneroRock);
            Assert.AreEqual(2, Flow.Ui.RadioAppLinhasVisiveis, "Rock da cidade e a Rock Marginal do jogo");

            Flow.Ui.SetRadioGenero("Favoritas");
            Assert.AreEqual(0, Flow.Ui.RadioAppLinhasVisiveis);
            Assert.IsTrue(_radio.AlternarFavorito(_radio.Estacoes[2]));
            yield return null;
            Assert.AreEqual(1, Flow.Ui.RadioAppLinhasVisiveis, "A favorita aparece no chip Favoritas");
            StringAssert.Contains("t-Sertaneja Total", PlayerPrefs.GetString(RadioPlayer.FavoritosKey), "Favorita guardada nas PlayerPrefs");
            Assert.AreEqual(1 + 5, _radio.Roleta.Count, "Com favoritas, a roda do R tem elas e as do jogo");
            Assert.AreEqual(2, _radio.Roleta[0]);

            Flow.Ui.SetRadioGenero("Todas");
            Assert.IsTrue(Flow.Ui.ClickRadioLinha(3), "Toque numa estação sintoniza");
            Assert.AreEqual("Rock da Cidade", _radio.EstacaoAtual.Nome);
            Assert.IsTrue(Flow.Ui.ClickRadioDesligar(), "Desligar rádio");
            Assert.AreEqual(-1, _radio.IndiceAtual);
            Assert.IsFalse(Flow.Ui.ClickRadioDesligar(), "Desligado, o botão não faz nada");

            Assert.IsTrue(Flow.Ui.ClickModalButton("Voltar"));
            yield return null;
            Assert.IsFalse(Flow.Ui.RadioAppVisivel);
            Assert.AreEqual("Menu", Flow.Ui.ModalTitle, "Volta ao menu do celular");
            Flow.CloseFreeRoamMenu();
        }

        [UnityTest]
        public IEnumerator HudRadioPillShowsTheStationAndTheButtonsSwitchIt()
        {
            yield return Prepare();
            Flow.EnterFreeRoam();
            yield return null;
            yield return null;
            Assert.AreEqual("Rádio desligado", Flow.Ui.RadioPilulaTexto);

            Flow.Ui.ClickRadioPilula(true);
            Assert.AreEqual(0, _radio.IndiceAtual, "Próxima, desligado, liga na primeira");
            yield return null;
            yield return null;
            Assert.AreEqual("Rádio Pública Um", Flow.Ui.RadioPilulaTexto);
            Assert.IsNotEmpty(Flow.Ui.RadioPilulaStatus, "A pílula mostra o estado (sintonizando ou o gênero)");

            Flow.Ui.ClickRadioPilula(true);
            Assert.AreEqual(1, _radio.IndiceAtual);
            Flow.Ui.ClickRadioPilula(false);
            Flow.Ui.ClickRadioPilula(false);
            Assert.AreEqual(-1, _radio.IndiceAtual, "Anterior da primeira desliga");
            yield return null;
            yield return null;
            Assert.AreEqual("Rádio desligado", Flow.Ui.RadioPilulaTexto);
        }

        [UnityTest]
        public IEnumerator TuningShowsTheStationNameToast()
        {
            yield return Prepare();
            Flow.EnterFreeRoam();
            Flow.Ui.ClearToast();
            yield return null;
            _radio.Sintonizar(1);
            yield return null;
            Assert.AreEqual("Rádio: Notícias SP", Flow.Ui.ToastMessage);
            _radio.Desligar();
        }

        [UnityTest]
        public IEnumerator CreditsListTheLiveStationsAndRadioBrowser()
        {
            yield return Prepare();
            Flow.ShowMainMenu();
            Assert.IsTrue(Flow.Ui.ClickModalButton("Créditos"));
            StringAssert.Contains("Rádios ao vivo", Flow.Ui.ModalBody);
            StringAssert.Contains("USP FM", Flow.Ui.ModalBody);
            StringAssert.Contains("Cultura", Flow.Ui.ModalBody);
            StringAssert.Contains("radio-browser.info", Flow.Ui.ModalBody);
        }

        /// <summary>Sem rede e sem cache a lista curada do repositório já vale; no editor só as MP3 (o Unity não decodifica AAC nem HLS).</summary>
        [UnityTest]
        public IEnumerator RealListLoadsFromTheCuratedFileAndKeepsGameStationsLast()
        {
            yield return WaitFor(() => RadioPlayer.Instance != null, 5f);
            _radio = RadioPlayer.Instance;
            _radio.ReiniciarParaTestes();
            _radio.Stream.UsarBackend(null); // o backend de verdade da plataforma (só decide o que a lista mostra)
            RadioPlayer.NaoCarregarInternet = false;
            _radio.AplicarInternet();
            yield return WaitFor(() => !_radio.Carregando && _radio.Estacoes.Count > 5, 30f);
            RadioPlayer.NaoCarregarInternet = true;

            int net = _radio.Estacoes.Count(e => e.EhStream);
            Assert.GreaterOrEqual(net, 15, "As curadas tocáveis entram (" + _radio.StatusLista + ")");
            Assert.IsTrue(_radio.Estacoes.Take(net).All(e => e.EhStream));
            Assert.IsTrue(_radio.Estacoes.Skip(net).All(e => !e.EhStream), "As sintetizadas ficam no fim");
            Assert.IsTrue(_radio.Estacoes[0].Info.Publica, "Pública primeiro");
            Assert.IsTrue(_radio.Estacoes.Where(e => e.EhStream).All(e => _radio.Stream.Caps.Pode(e.Info)), "Só o que o backend toca");
        }
    }
}
