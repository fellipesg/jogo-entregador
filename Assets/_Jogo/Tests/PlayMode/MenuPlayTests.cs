using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Menu principal, exploração livre, pausa, cortina de informações e painel da moto.</summary>
    public sealed class MenuPlayTests : SceneFixture
    {
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Flow.SetProfile(new PerfilJogador());
        }

        [UnityTest]
        public IEnumerator MainMenuListsTheEntriesAndPlayLeadsToSetupThenFreeRoam()
        {
            Flow.ShowMainMenu();
            Assert.AreEqual(FlowState.MainMenu, Flow.State);
            Assert.IsTrue(Flow.Ui.ModalVisible);
            Assert.AreEqual("Menu principal", Flow.Ui.ModalTitle);
            CollectionAssert.Contains(new System.Collections.Generic.List<string>(Flow.Ui.ModalButtonLabels), "Jogar");
            CollectionAssert.Contains(new System.Collections.Generic.List<string>(Flow.Ui.ModalButtonLabels), "Continuar", "Já existe uma partida.");
            yield return null;

            Assert.IsTrue(Flow.Ui.ClickModalButton("Jogar"));
            Assert.AreEqual(FlowState.Setup, Flow.State);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar"));
            Assert.AreEqual(FlowState.FreeRoam, Flow.State);
            Assert.IsTrue(Flow.Ui.HudVisible);
            Assert.IsNull(Flow.CurrentOrder, "Na exploração livre não há pedido.");
            Assert.IsFalse(Flow.IsDriving);

            // O celular abre o menu; Começar turno leva ao resumo do dia, com a volta ao mapa.
            Flow.OpenPhoneMenu();
            Assert.AreEqual(FlowState.FreeRoamMenu, Flow.State);
            Assert.AreEqual("Menu", Flow.Ui.ModalTitle);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.AreEqual(FlowState.DayStart, Flow.State);
            CollectionAssert.Contains(new System.Collections.Generic.List<string>(Flow.Ui.ModalButtonLabels), "Voltar ao mapa");
            Assert.IsTrue(Flow.Ui.ClickModalButton("Voltar ao mapa"));
            Assert.AreEqual(FlowState.FreeRoam, Flow.State);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator EndOfShiftReturnsToFreeRoamAndTheNextShiftStartsFromThePhone()
        {
            Flow.NewWeek(123);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));

            // Recusa tudo até o resumo do turno.
            int guard = 0;

            while (Flow.State == FlowState.Offer && guard++ < 10)
            {
                Flow.RejectOffer();
            }

            Assert.AreEqual(FlowState.ShiftSummary, Flow.State);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Próximo dia"));
            Assert.AreEqual(FlowState.FreeRoam, Flow.State, "Depois do turno volta a explorar.");
            Assert.AreEqual(2, Flow.Session.Day);
            Assert.IsTrue(Flow.Ui.DayTitleVisible);
            StringAssert.Contains("Dia 2", Flow.Ui.DayTitleText);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PauseDuringTheShiftOffersContinueOptionsAndEnd()
        {
            Flow.NewWeek(123);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));
            Assert.IsTrue(Flow.IsDriving);

            Flow.PauseGame();
            Assert.AreEqual(FlowState.Paused, Flow.State);
            Assert.AreEqual(0f, Time.timeScale);
            CollectionAssert.AreEqual(new[] { "Continuar", "Opções", "Encerrar turno" }, new System.Collections.Generic.List<string>(Flow.Ui.ModalButtonLabels));

            Assert.IsTrue(Flow.Ui.ClickModalButton("Continuar"));
            Assert.IsTrue(Flow.IsDriving);
            Assert.AreEqual(1f, Time.timeScale);

            Flow.PauseGame();
            Assert.IsTrue(Flow.Ui.ClickModalButton("Encerrar turno"));
            StringAssert.Contains("Termine a entrega", Flow.Ui.ModalTitle, "Com entrega ativa o app pede para terminá-la primeiro.");
            Assert.IsTrue(Flow.Ui.ClickModalButton("Voltar"));
            Assert.AreEqual(FlowState.Paused, Flow.State);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Encerrar turno"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Cancelar entrega e encerrar"));
            Assert.AreEqual(FlowState.ShiftSummary, Flow.State);
            Assert.AreEqual(1f, Time.timeScale);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PoliceStillWorksInFreeRoam()
        {
            Flow.NewWeek(5);
            Flow.EnterFreeRoam();
            yield return null;
            Flow.ForceFlagrante(Infracao.Contramao);
            Assert.AreEqual(FlowState.Abordagem, Flow.State, "A polícia também para quem anda livre.");
        }

        [UnityTest]
        public IEnumerator InfoShadeFollowsTheSwipeAndSnapsOpenOrClosed()
        {
            Flow.NewWeek(5);
            Flow.EnterFreeRoam();
            yield return null;
            GameUI ui = Flow.Ui;
            Assert.AreEqual(0f, ui.ShadeOpen);

            // Deslizar para baixo a partir do topo: acompanha o dedo e, ao soltar rápido, abre.
            ui.ShadeBegin();
            ui.ShadeDrag(120f);
            Assert.That(ui.ShadeOpen, Is.GreaterThan(0.1f).And.LessThan(0.6f));
            ui.ShadeEnd(900f);

            yield return new WaitForSecondsRealtime(1.5f);

            Assert.AreEqual(1f, ui.ShadeOpen, 0.02f);
            Assert.IsTrue(ui.InfoVisible);

            // Deslizar para cima fecha.
            ui.ShadeBegin();
            ui.ShadeDrag(-200f);
            ui.ShadeEnd(-900f);

            yield return new WaitForSecondsRealtime(1.5f);

            Assert.AreEqual(0f, ui.ShadeOpen, 0.02f);

            // Segurar o minimapa mostra enquanto estiver pressionado.
            ui.SetInfoHold(true);

            yield return new WaitForSecondsRealtime(1.5f);

            Assert.IsTrue(ui.InfoVisible);
            ui.SetInfoHold(false);

            yield return new WaitForSecondsRealtime(1.5f);

            Assert.IsFalse(ui.InfoVisible);
        }

        [UnityTest]
        public IEnumerator TouchControlsDoNotCoverTheTopStripReservedForTheShade()
        {
            Flow.NewWeek(5);
            Flow.EnterFreeRoam();
            yield return null;
            Transform strip = Flow.Ui.transform.Find("Canvas/AreaSegura/Hud/FaixaCortina");
            Assert.IsNotNull(strip);
            var rect = (RectTransform)strip;
            Assert.AreEqual(0.88f, rect.anchorMin.y, 0.001f);
            Assert.Greater(strip.GetSiblingIndex(), Flow.Ui.transform.Find("Canvas/AreaSegura/Hud/Controles").GetSiblingIndex(), "Fica acima das zonas de toque.");
        }

        [UnityTest]
        public IEnumerator NavigationCardAndWarningLightsRender()
        {
            Flow.NewWeek(5);
            Flow.EnterFreeRoam();
            Flow.enabled = false;
            Flow.Ui.SetNavegacao(1, 200f, "Av. Adolfo Pinheiro", "esquerda");
            StringAssert.Contains("200 m", Flow.Ui.NavegacaoTexto);
            Flow.Ui.SetNavegacao(7, 1234f, "Hamburgueria Zona Sul");
            StringAssert.Contains("1,2 km", Flow.Ui.NavegacaoTexto);
            Flow.Ui.HideNavegacao();
            Assert.IsFalse(Flow.Ui.NavegacaoVisivel);

            Flow.Ui.Painel.AcenderTudo();
            Flow.Ui.SetRacingData(9500f, 4, 9000f, 1f, 0f, 40, true, true, 80f);
            yield return null;
            Flow.Ui.Painel.Limpar();
            Flow.enabled = true;
        }

        [Test]
        public void FuelDrainsSlowlyAndReserveLightComesOnBelowFifteenPercent()
        {
            var painel = new PainelMotoEstado();
            painel.Simular(1f, 0f, 0f, 0);
            Assert.IsTrue(painel.Neutro);
            Assert.IsFalse(painel.Reserva);
            painel.Simular(1f, 60f, 1f, 3);
            Assert.IsFalse(painel.Neutro);
            Assert.Less(painel.Combustivel, 1f);
            painel.Combustivel = 0.1f;
            painel.Simular(0.1f, 30f, 0.5f, 2);
            Assert.IsTrue(painel.Reserva);
            painel.Abastecer();
            Assert.AreEqual(1f, painel.Combustivel);
        }
    }
}
