using System.Collections;
using System.Linq;
using Entregador.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    public sealed partial class ScreenshotTests
    {
        /// <summary>
        /// O rádio pela internet: aviso de troca de estação, pílula do HUD, o app "Rádio" do celular (lista com ícones baixados da rede, chips de
        /// gênero, favoritos, busca), as Opções com o aviso de dados móveis e os Créditos. Usa a lista curada de verdade e um backend de mentira
        /// (não toca áudio). Os ícones vêm da internet: sem rede aparecem as iniciais.
        /// </summary>
        [UnityTest]
        public IEnumerator CaptureRadio()
        {
            string dir = Dir;
            Camera main = Camera.main;
            StartDryWeek();
            Flow.EnterFreeRoam();
            yield return PlaceOnAvenue();

            yield return new WaitUntil(() => RadioPlayer.Instance != null);
            RadioPlayer radio = RadioPlayer.Instance;
            var fake = new FakeRadioBackend();
            radio.ReiniciarParaTestes();
            radio.Stream.UsarBackend(fake);
            RadioPlayer.NaoCarregarInternet = false;
            radio.AplicarInternet();
            float until = Time.realtimeSinceStartup + 25f;

            while ((radio.Carregando || radio.Estacoes.Count < 8) && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            RadioPlayer.NaoCarregarInternet = true;
            Debug.Log($"[Radio] {radio.Estacoes.Count} estações, status da lista: {radio.StatusLista}");

            // Pílula e aviso ao sintonizar.
            int cbn = radio.Estacoes.ToList().FindIndex(e => e.Nome.StartsWith("CBN"));
            Flow.Ui.ClearToast();
            yield return Wait(0.3f);
            Save(dir, "r01_hud_pilula_desligado.png", main);
            radio.Sintonizar(cbn);
            yield return Wait(0.7f);
            Save(dir, "r02_aviso_e_pilula_tocando.png", main);
            fake.AgoraTocando = "Jornal da CBN · Milton Jung";
            yield return Wait(0.5f);
            Save(dir, "r02b_pilula_agora_tocando.png", main);
            radio.Sintonizar(radio.Estacoes.ToList().FindIndex(e => e.Nome.Contains("USP")));
            yield return Wait(0.2f);
            Save(dir, "r03_pilula_sintonizando.png", main);

            // App Rádio no celular.
            Flow.OpenPhoneMenu();
            yield return Wait(0.6f);
            Save(dir, "r04_celular_menu.png", main);
            Flow.Ui.ClickModalButton("Rádio");
            yield return Wait(5f);
            Save(dir, "r05_app_radio_lista.png", main);

            radio.AlternarFavorito(radio.Estacoes.First(e => e.Nome.Contains("USP")));
            radio.AlternarFavorito(radio.Estacoes.First(e => e.Nome.StartsWith("Mix")));
            Flow.Ui.SetRadioGenero(RadioDirectory.GeneroSertanejo);
            yield return Wait(0.5f);
            Save(dir, "r06_app_radio_chip_sertanejo.png", main);

            Flow.Ui.SetRadioGenero("Favoritas");
            yield return Wait(0.4f);
            Save(dir, "r07_app_radio_favoritas.png", main);

            Flow.Ui.SetRadioGenero("Todas");
            Flow.Ui.SetRadioBusca("cultura");
            yield return Wait(0.4f);
            Save(dir, "r08_app_radio_busca.png", main);
            Flow.Ui.SetRadioBusca(string.Empty);
            Flow.Ui.ClickModalButton("Voltar");
            Flow.CloseFreeRoamMenu();

            // Opções e créditos.
            Flow.ShowMainMenu();
            yield return Wait(0.8f);
            Flow.Ui.ClickModalButton("Opções");
            yield return Wait(0.5f);
            Save(dir, "r09_opcoes_radio_internet.png", main);
            Flow.Ui.ClickModalButton("Voltar");
            Flow.Ui.ClickModalButton("Créditos");
            yield return Wait(0.8f);
            Save(dir, "r10_creditos_radios.png", main);
            Flow.Ui.ClickModalButton("Voltar");
            PlayerPrefs.DeleteKey(RadioPlayer.FavoritosKey);
            radio.Desligar();
        }
    }
}
