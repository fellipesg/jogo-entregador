using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>
    /// O jogo inteiro de ponta a ponta com a interface de verdade: menu principal, novo jogo, exploração livre, começar turno pelo celular,
    /// pedido, ida ao restaurante (cartão de navegação), parada no balcão, diálogo, entrega, fim do dia e volta à exploração livre.
    /// Também: a exploração livre não dispara retirada, e o balcão congela com o jogo pausado.
    /// </summary>
    public sealed class FluxoCompletoPlayTests : SceneFixture
    {
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Flow.SetProfile(new PerfilJogador());
        }

        [UnityTest]
        public IEnumerator MenuToFreeRoamToShiftToCounterToDeliveryAndBackToFreeRoam()
        {
            Flow.ShowMainMenu();
            Assert.IsTrue(Flow.Ui.ClickModalButton("Jogar"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar"));
            Assert.AreEqual(FlowState.FreeRoam, Flow.State);
            yield return null;

            // Exploração livre: parado diante de um restaurante nada acontece (sem retirada, sem botão, sem cartão de navegação).
            Place anyRestaurant = Flow.Map.Restaurants[0];
            TeleportNear(Flow, anyRestaurant);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            Assert.AreEqual(FlowState.FreeRoam, Flow.State);
            Assert.IsFalse(Flow.Balcao.PromptVisible, "Em exploração livre o balcão não age.");
            Assert.IsFalse(Flow.Balcao.HintVisible);
            Assert.IsFalse(Flow.Balcao.DialogueVisible);
            Assert.IsFalse(Flow.Balcao.ChipVisible);
            Assert.IsFalse(Flow.Ui.NavegacaoVisivel, "Sem corrida não há cartão de navegação.");

            // Celular: Começar turno leva ao resumo do dia e daí à primeira oferta.
            Flow.OpenPhoneMenu();
            Assert.AreEqual(FlowState.FreeRoamMenu, Flow.State);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.AreEqual(FlowState.DayStart, Flow.State);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.AreEqual(FlowState.Offer, Flow.State);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));
            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State);
            Assert.IsTrue(Flow.Balcao.ChipVisible, "O código do pedido aparece durante a corrida.");

            // A navegação vem do RouteGuide: o cartão fica visível com a próxima instrução.
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.Ui.NavegacaoVisivel);
            Assert.IsNotEmpty(Flow.Ui.NavegacaoTexto);

            // Pausa congela a espera do balcão e esconde o botão.
            Flow.PauseGame();
            Assert.AreEqual(FlowState.Paused, Flow.State);
            Assert.IsFalse(Flow.Balcao.PromptVisible);
            Flow.ResumeGame();

            // Balcão: para, retira, o diálogo termina com o pedido na mão.
            yield return PickUpOrder(Flow);
            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State);

            TeleportNear(Flow, Flow.CurrentPlan.Customer);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(FlowState.AtDoor, Flow.State);
            Flow.TakePhoto();
            Flow.Deliver();
            Assert.AreEqual(FlowState.Result, Flow.State);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Continuar"));

            // Recusa o resto do dia: fim do turno e volta à exploração livre.
            int guard = 0;

            while (Flow.State == FlowState.Offer && guard++ < 12)
            {
                Flow.RejectOffer();
            }

            if (Flow.State == FlowState.Claim)
            {
                Assert.IsTrue(Flow.Ui.ClickModalButton("Continuar"));
            }

            Assert.AreEqual(FlowState.ShiftSummary, Flow.State);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Próximo dia"));
            Assert.AreEqual(FlowState.FreeRoam, Flow.State);
            Assert.IsFalse(Flow.Balcao.ChipVisible);
            Assert.IsFalse(Flow.Balcao.DialogueVisible);
            Assert.IsNull(Flow.Retirada, "Fora da corrida não há retirada.");
            Assert.IsFalse(Flow.Ui.NavegacaoVisivel);
        }

        [UnityTest]
        public IEnumerator TheGpsCardFollowsTheRouteGuideAndHidesAtTheCounter()
        {
            Flow.NewWeek(7, new ScriptedRandom());
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));
            yield return null;
            yield return null;

            // O cartão mostra a manobra do RouteGuide (mesmo texto de distância e rua).
            InstrucaoGps next = Flow.Route.ProximaInstrucao;

            if (next.Existe && next.Manobra != Manobra.Chegada && !string.IsNullOrEmpty(next.Rua))
            {
                StringAssert.Contains(next.Rua, Flow.Ui.NavegacaoTexto);
            }

            Assert.IsTrue(Flow.Ui.NavegacaoVisivel);
            Assert.AreEqual(0, GameFlow.ManobraParaUi(Manobra.SigaEmFrente));
            Assert.AreEqual(1, GameFlow.ManobraParaUi(Manobra.VireADireita));
            Assert.AreEqual(2, GameFlow.ManobraParaUi(Manobra.VireAEsquerda));
            Assert.AreEqual(7, GameFlow.ManobraParaUi(Manobra.Chegada));

            Flow.SituacaoForcada = BalcaoSituacao.SaindoJaJa;
            TeleportNear(Flow, Flow.CurrentPlan.Restaurant);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 60 && !Flow.Balcao.PromptVisible; i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.Balcao.ClickPrompt());
            Assert.AreEqual(FlowState.NoBalcao, Flow.State);
            yield return null;
            yield return null;
            Assert.IsFalse(Flow.Ui.NavegacaoVisivel, "No balcão o cartão de navegação some.");
        }
    }
}
