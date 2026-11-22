using System.Collections;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>Retirada do pedido no balcão do restaurante: parar na zona, botão, diálogo com a atendente e o que sai dele.</summary>
    public sealed class RetiradaPlayTests : SceneFixture
    {
        private ScriptedRandom _random;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        private void AcceptOrder()
        {
            _random = new ScriptedRandom();
            Flow.NewWeek(1, _random);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Começar turno"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));
            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State);
        }

        private IEnumerator ParkAtCounter()
        {
            TeleportNear(Flow, Flow.CurrentPlan.Restaurant);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 60 && !Flow.Balcao.PromptVisible; i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.Balcao.PromptVisible);
        }

        [UnityTest]
        public IEnumerator TheAppShowsTheOrderCodeWhenTheRunStarts()
        {
            AcceptOrder();
            yield return null;

            Assert.IsNotNull(Flow.Retirada);
            Assert.AreEqual(4, Flow.Retirada.Codigo.Length);
            Assert.IsTrue(Flow.Balcao.ChipVisible);
            StringAssert.Contains(Flow.Retirada.Codigo, Flow.Balcao.ChipText);
            StringAssert.Contains(Flow.Retirada.Codigo, Flow.Ui.ToastMessage);
        }

        [UnityTest]
        public IEnumerator EachRestaurantHasAnAttendantNpcAtItsCounter()
        {
            AtendenteRestaurante[] all = Object.FindObjectsByType<AtendenteRestaurante>(FindObjectsSortMode.None);
            Assert.AreEqual(Flow.Map.Restaurants.Length, all.Length);

            foreach (Place place in Flow.Map.Restaurants)
            {
                Assert.IsTrue(place.TemBalcao, place.Name);
                AtendenteRestaurante npc = System.Array.Find(all, a => a.Restaurante == place.Name);
                Assert.IsNotNull(npc, "Atendente de " + place.Name);
                Assert.IsNotNull(npc.Corpo, "O personagem do Mixamo foi instanciado (não é uma cápsula).");
                Assert.IsNotNull(npc.GetComponentInChildren<SkinnedMeshRenderer>());
                Assert.Less(Vector3.Distance(npc.transform.position, place.Balcao), 0.01f);
                Assert.Less(Vector3.Distance(place.Balcao, place.Position), 14f, "O balcão fica em frente da zona de parada.");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator EnteringTheZoneAtSpeedDoesNothing()
        {
            AcceptOrder();
            Place restaurant = Flow.CurrentPlan.Restaurant;
            var body = Flow.Bike.GetComponent<Rigidbody>();

            for (int i = 0; i < 6; i++)
            {
                TeleportNear(Flow, restaurant);
                body.linearVelocity = Vector3.forward * 11f;
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State, "Passar de moto não retira o pedido.");
            Assert.IsFalse(Flow.Balcao.PromptVisible);
            Assert.IsTrue(Flow.Balcao.HintVisible, "Aparece a dica de parar a moto.");
            Flow.BeginCounter();
            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State, "Nem à força em velocidade.");
            Assert.IsFalse(Flow.Balcao.DialogueVisible);
        }

        [UnityTest]
        public IEnumerator StoppingInTheZoneShowsThePromptAndLeavingHidesIt()
        {
            AcceptOrder();
            yield return ParkAtCounter();
            StringAssert.Contains("Retirar pedido", Flow.Balcao.PromptLabel);
            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State);

            Place restaurant = Flow.CurrentPlan.Restaurant;
            Flow.Bike.Teleport(new Vector3(restaurant.Position.x + 60f, 0.1f, restaurant.Position.z), 0f);
            yield return null;
            yield return null;
            Assert.IsFalse(Flow.Balcao.PromptVisible, "Saiu da zona: o botão some.");
            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State, "Sair sem retirar não pega o pedido.");
        }

        [UnityTest]
        public IEnumerator ReadyOrderTakesOneButtonAndSwitchesTheRouteToTheCustomer()
        {
            AcceptOrder();
            Flow.SituacaoForcada = BalcaoSituacao.PedidoPronto;
            yield return ParkAtCounter();
            Assert.IsTrue(Flow.Balcao.ClickPrompt());

            Assert.AreEqual(FlowState.NoBalcao, Flow.State);
            Assert.IsTrue(Flow.Balcao.DialogueVisible);
            Assert.AreEqual(Flow.CurrentPlan.Restaurant.Name, Flow.Balcao.RestaurantName);
            StringAssert.Contains("prontinho", Flow.Balcao.Speech);
            Assert.IsTrue(Flow.Bike.Locked, "A moto fica parada durante o diálogo.");
            Assert.IsFalse(Flow.Tracker.Running);
            Assert.IsTrue(Flow.Route.HasTarget);
            Vector3 beforeTarget = Flow.Route.Route[Flow.Route.Route.Count - 1];
            Assert.Less(Vector3.Distance(beforeTarget, Flow.CurrentPlan.Restaurant.Position), 1f);

            Assert.IsTrue(Flow.Balcao.ClickChoice("Pegar o pedido"));
            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State);
            Assert.IsFalse(Flow.Bike.Locked);
            Assert.IsTrue(Flow.Tracker.Running);
            Assert.IsFalse(Flow.Balcao.DialogueVisible);
            Assert.IsFalse(Flow.Balcao.ChipVisible, "O código some depois da retirada.");
            Vector3 afterTarget = Flow.Route.Route[Flow.Route.Route.Count - 1];
            Assert.Less(Vector3.Distance(afterTarget, Flow.CurrentPlan.Customer.Position), 1f, "A rota agora vai para o cliente.");
        }

        [UnityTest]
        public IEnumerator WaitingShowsAProgressBarAndCostsTheMinutes()
        {
            AcceptOrder();
            Flow.Tuning.SecondsPerGameMinute = 0.2f;
            Flow.SituacaoForcada = BalcaoSituacao.SaindoJaJa;
            yield return ParkAtCounter();
            Assert.IsTrue(Flow.Balcao.ClickPrompt());
            CollectionAssert.AreEqual(new[] { "Esperar", "Reclamar" }, new[] { FirstWord(Flow.Balcao.ChoiceLabels[0]), FirstWord(Flow.Balcao.ChoiceLabels[1]) });

            int minutes = Flow.Retirada.EsperaMinutos;
            float before = Flow.Tracker.ElapsedSeconds;
            Assert.IsTrue(Flow.Balcao.ClickChoice("Esperar"));
            Assert.IsTrue(Flow.Balcao.WaitBarVisible);
            Assert.AreEqual(0, Flow.Balcao.ChoiceLabels.Count, "Sem botões durante a espera: não dá para sair sem o pedido.");
            Assert.AreEqual(FlowState.NoBalcao, Flow.State);

            float waited = 0f;

            while (waited < 15f && Flow.State == FlowState.NoBalcao)
            {
                waited += Time.deltaTime;
                if (Flow.Retirada.Passo == RetiradaPasso.Entrega)
                {
                    Assert.IsTrue(Flow.Balcao.ClickChoice("Pegar o pedido"));

                    break;
                }

                yield return null;
            }

            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State);
            Assert.AreEqual(minutes, Flow.Retirada.MinutosPerdidos);
            Assert.GreaterOrEqual(Flow.Tracker.ElapsedSeconds - before, minutes * 0.2f - 0.05f, "Os minutos de espera entram no tempo da corrida.");
        }

        private static string FirstWord(string text) => text.Split(' ')[0];

        [UnityTest]
        public IEnumerator ComplainingInTheAppCancelsTheDeliveryWithAPenalty()
        {
            AcceptOrder();
            double acceptanceBefore = Flow.Session.App.AcceptanceRate;
            Flow.SituacaoForcada = BalcaoSituacao.Lotado;
            yield return ParkAtCounter();
            Assert.IsTrue(Flow.Balcao.ClickPrompt());
            StringAssert.Contains("lotado", Flow.Balcao.Speech);

            Assert.IsTrue(Flow.Balcao.ClickChoice("Reclamar"));
            Assert.AreEqual(FlowState.Result, Flow.State);
            StringAssert.Contains("cancel", Flow.Ui.ModalTitle.ToLowerInvariant());
            Assert.Less(Flow.Session.App.AcceptanceRate, acceptanceBefore);
            Assert.IsFalse(Flow.Balcao.DialogueVisible);
            Assert.IsFalse(Flow.Tracker.Running);
        }

        [UnityTest]
        public IEnumerator CounterAsksForTheCodeAndWrongAnswersCostTime()
        {
            AcceptOrder();
            Flow.SituacaoForcada = BalcaoSituacao.PedeCodigo;
            yield return ParkAtCounter();
            Assert.IsTrue(Flow.Balcao.ClickPrompt());
            Assert.AreEqual(4, Flow.Balcao.ChoiceLabels.Count, "Três códigos e o atalho para olhar o app.");

            string right = Flow.Retirada.Codigo;
            string wrong = null;

            foreach (string code in Flow.Retirada.OpcoesCodigo)
            {
                if (code != right)
                {
                    wrong = code;
                }
            }

            Assert.IsTrue(Flow.Balcao.ClickChoice(wrong));
            Assert.AreEqual(FlowState.NoBalcao, Flow.State);
            Assert.AreEqual(1, Flow.Retirada.MinutosPerdidos);
            StringAssert.Contains("não é", Flow.Balcao.Speech);

            Assert.IsTrue(Flow.Balcao.ClickChoice(right));
            Assert.AreEqual(RetiradaPasso.Entrega, Flow.Retirada.Passo);
            Assert.IsTrue(Flow.Balcao.ClickChoice("Pegar o pedido"));
            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State);
        }

        [UnityTest]
        public IEnumerator ReplacedOrderTakenWithoutCheckingLowersTheRating()
        {
            AcceptOrder();
            Flow.SituacaoForcada = BalcaoSituacao.PedidoTrocado;
            yield return ParkAtCounter();
            Assert.IsTrue(Flow.Balcao.ClickPrompt());

            _random.Value = 0.0; // a cliente reclama (chance 80%) e a perda é de 1 estrela
            Assert.IsTrue(Flow.Balcao.ClickChoice("Pegar e sair"));
            _random.Value = 0.5;
            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State);
            Assert.AreEqual(1, Flow.Retirada.PenalidadeEstrelas);

            TeleportNear(Flow, Flow.CurrentPlan.Customer);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(FlowState.AtDoor, Flow.State);
            Flow.TakePhoto();
            Flow.Deliver();

            Assert.LessOrEqual(Flow.LastResult.Stars, 4, "A estrela perdida no balcão aparece na nota.");
            StringAssert.Contains("faltando", Flow.Ui.ModalBody);
        }

        [UnityTest]
        public IEnumerator CheckingTheOrderCostsAMinuteAndKeepsFiveStars()
        {
            AcceptOrder();
            Flow.SituacaoForcada = BalcaoSituacao.PedidoTrocado;
            yield return ParkAtCounter();
            Assert.IsTrue(Flow.Balcao.ClickPrompt());

            _random.Value = 0.0;
            Assert.IsTrue(Flow.Balcao.ClickChoice("Conferir"));
            StringAssert.Contains("faltava", Flow.Balcao.Speech);
            Assert.IsTrue(Flow.Balcao.ClickChoice("Pegar o pedido"));
            _random.Value = 0.5;
            Assert.AreEqual(0, Flow.Retirada.PenalidadeEstrelas);
            Assert.AreEqual(1, Flow.Retirada.MinutosPerdidos);

            TeleportNear(Flow, Flow.CurrentPlan.Customer);
            yield return new WaitForSeconds(0.2f);
            Flow.TakePhoto();
            Flow.Deliver();
            Assert.AreEqual(Flow.Session.IsRainingToday ? 4 : 5, Flow.LastResult.Stars, "Sem perda por pedido trocado (só a chuva, se houver).");
        }
    }
}
