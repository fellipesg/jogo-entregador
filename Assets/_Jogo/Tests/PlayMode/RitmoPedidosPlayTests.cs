using System.Collections;
using System.Linq;
using Entregador.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    /// <summary>
    /// O ritmo dos pedidos como num app de verdade: exploração livre sem turno forçado, espera online, ofertas no meio da corrida,
    /// fila de "Próximo pedido", entrega na porta com diálogo e esperas de verdade, e o fim do turno. Todos com a agenda ligada
    /// (OfertasImediatas desligado).
    /// </summary>
    public sealed class RitmoPedidosPlayTests : SceneFixture
    {
        private const float Fast = 0.05f;

        [SetUp]
        public void Prepare()
        {
            Flow.OfertasImediatas = false;
            Flow.OfertasNaCorridaAutomaticas = false;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Flow.SegundosPorMinuto = RelogioJogo.SegundosPorMinuto;
            Flow.OfertasNaCorridaAutomaticas = true;
            Flow.OfertaConfig.SegundosOfertaNaCorrida = 12;
            Flow.SetProfile(new PerfilJogador());
        }

        private void StartOnline(int seed = 11)
        {
            Flow.NewWeek(seed);
            Flow.BeginShift();
            Assert.AreEqual(FlowState.Online, Flow.State);
        }

        /// <summary>Online → oferta → aceita → retira no balcão: termina rumo ao cliente.</summary>
        private IEnumerator TakeOrderToCustomer()
        {
            Flow.ForcarOfertaAgora();
            Assert.AreEqual(FlowState.Offer, Flow.State);
            Flow.AcceptOffer();
            yield return PickUpOrder(Flow);
        }

        /// <summary>Entrega comum (cliente desce) sem esperar o tempo real: acelera o relógio e clica Entregar.</summary>
        private IEnumerator FinishDeliveryQuickly()
        {
            Flow.SegundosPorMinuto = Fast;
            yield return ArriveAtCustomer(Flow);

            for (int i = 0; i < 600 && !Flow.Balcao.ChoiceLabels.Contains("Entregar o pedido"); i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.Balcao.ClickChoice("Entregar o pedido"));
            Flow.SegundosPorMinuto = RelogioJogo.SegundosPorMinuto;
        }

        // ---------- Exploração livre ----------

        [UnityTest]
        public IEnumerator AfterNewGameThePlayerRoamsFreeAndNothingPushesAShift()
        {
            Flow.ShowSetup();
            Flow.StartFromSetup();
            Assert.AreEqual(FlowState.FreeRoam, Flow.State);

            float until = Time.realtimeSinceStartup + 20f;

            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Assert.AreEqual(FlowState.FreeRoam, Flow.State, "20 s depois do novo jogo o jogador segue em exploração livre.");
            Assert.IsFalse(Flow.Ui.ModalVisible, "Nenhum painel (dia, oferta) aparece sozinho.");
            Assert.IsNull(Flow.CurrentOrder, "Nenhuma oferta.");
            Assert.IsFalse(Flow.Bike.Locked, "A moto está solta.");
            Assert.IsTrue(Flow.Ui.ComecarPillVisible, "A pílula Começar turno lembra do celular.");
            Assert.IsFalse(Session().IsShiftActive);
            Assert.Greater(Flow.Relogio.Minutos, RelogioJogo.HoraInicioDia * 60.0, "O relógio do dia anda na exploração.");
        }

        [UnityTest]
        public IEnumerator TheStartShiftPillOpensTheBriefingAndCanBeDismissed()
        {
            Flow.ShowSetup();
            Flow.StartFromSetup();
            yield return null;
            yield return null;
            Assert.IsTrue(Flow.Ui.ComecarPillVisible);
            Assert.IsTrue(Flow.Ui.ClickComecarDismiss());
            yield return null;
            Assert.IsFalse(Flow.Ui.ComecarPillVisible, "Dispensada, a pílula some.");
            Assert.AreEqual(FlowState.FreeRoam, Flow.State);

            Flow.ShowSetup();
            Flow.StartFromSetup();
            yield return null;
            yield return null;
            Assert.IsTrue(Flow.Ui.ClickComecarPill());
            Assert.AreEqual(FlowState.DayStart, Flow.State, "O resumo do dia só vem depois de escolher Começar turno.");
        }

        [UnityTest]
        public IEnumerator AfterTheShiftReportThePlayerGoesBackToFreeRoamWithoutAPrompt()
        {
            StartOnline();
            Flow.OpenOnlineMenu();
            Assert.IsTrue(Flow.Ui.ClickModalButton("Encerrar turno"));
            Assert.IsTrue(Flow.Ui.ClickModalButton("Encerrar turno"));
            Assert.AreEqual(FlowState.ShiftSummary, Flow.State);
            Assert.IsTrue(Flow.Ui.ClickModalButton("Próximo dia"));
            yield return null;
            Assert.AreEqual(FlowState.FreeRoam, Flow.State);
            Assert.IsFalse(Flow.Ui.ModalVisible);
            Assert.IsNull(Flow.CurrentOrder);
        }

        // ---------- Espera online e zonas quentes ----------

        [UnityTest]
        public IEnumerator OnlineWaitsThenTheOfferArrivesWhileTheBikeRidesFree()
        {
            StartOnline();
            yield return null;
            yield return null;

            Assert.IsTrue(Flow.Ui.ProcurandoVisivel, "A pílula Procurando pedidos aparece.");
            StringAssert.Contains("Procurando", Flow.Ui.ProcurandoTexto);
            Assert.IsFalse(Flow.Bike.Locked, "Online o jogador anda livre.");
            Assert.IsFalse(Flow.Route.HasTarget, "Sem rota.");
            Assert.IsFalse(Flow.Ui.ModalVisible, "Sem oferta imediata.");
            Assert.That(Flow.AgendaRestante, Is.InRange(8f, 170f));
            Assert.That(Flow.ZonasAtuais.Count, Is.InRange(1, 3));
            Assert.AreEqual(Flow.ZonasAtuais.Count, Flow.Ui.ZonasNoMinimapa, "As zonas quentes aparecem no minimapa.");

            int appeared = 0;
            Flow.OfertaApareceu += () => appeared++;
            Flow.ForcarEsperaDaAgenda(0.3f);

            float until = Time.realtimeSinceStartup + 3f;

            while (Flow.State == FlowState.Online && Time.realtimeSinceStartup < until)
            {
                yield return null;
            }

            Assert.AreEqual(FlowState.Offer, Flow.State, "Passada a espera, o app chama.");
            Assert.AreEqual(1, appeared, "O evento OfertaApareceu dispara (o áudio toca o sininho).");
            Assert.IsFalse(Flow.Ui.ProcurandoVisivel);
        }

        [UnityTest]
        public IEnumerator InsideAHotZoneTheWaitRunsAbout30PercentFaster()
        {
            StartOnline();
            yield return null;
            ZonaQuente zone = Flow.ZonasAtuais[0];
            Flow.Bike.Teleport(new Vector3((float)zone.X, 0.1f, (float)zone.Z), 0f);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            Flow.ForcarEsperaDaAgenda(100f);
            yield return null;
            float a = Flow.AgendaRestante;
            yield return new WaitForSeconds(1f);
            float insideDrop = a - Flow.AgendaRestante;

            Assert.IsTrue(Flow.NaZonaQuente);
            Assert.Greater(insideDrop, 1.25f, "Dentro da zona a espera anda 1/0,7 mais rápido.");
            StringAssert.Contains("Zona", Flow.Ui.ProcurandoTexto);

            Flow.Bike.Teleport(new Vector3(-900f, 0.1f, -900f), 0f);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
            Flow.ForcarEsperaDaAgenda(100f);
            yield return null;
            a = Flow.AgendaRestante;
            yield return new WaitForSeconds(1f);
            float outsideDrop = a - Flow.AgendaRestante;

            Assert.IsFalse(Flow.NaZonaQuente);
            Assert.Less(outsideDrop, 1.2f);
        }

        // ---------- Ofertas durante a corrida ----------

        [UnityTest]
        public IEnumerator MidRunOfferExpiresWithoutBlockingTheRide()
        {
            StartOnline();
            Flow.OfertaConfig.SegundosOfertaNaCorrida = 1.2;
            yield return TakeOrderToCustomer();
            double acceptanceBefore = Flow.Session.App.AcceptanceRate;
            int refused = 0;
            Flow.OfferRefused += () => refused++;
            Flow.ForcarOfertaNaCorrida();
            yield return null;

            Assert.IsTrue(Flow.Ui.OfferCompactVisible, "O cartão compacto aparece.");
            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State, "A corrida não muda de estado.");
            Assert.IsFalse(Flow.Ui.ModalVisible, "Não é modal.");
            Assert.IsFalse(Flow.Bike.Locked, "Os controles seguem livres.");
            Assert.AreEqual(1f, Time.timeScale, "A corrida não pausa.");
            Assert.IsNotNull(Flow.Session.Adicional);

            yield return new WaitForSeconds(1.6f);

            Assert.IsFalse(Flow.Ui.OfferCompactVisible, "Vencida, some sozinha.");
            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State);
            Assert.IsFalse(Flow.Bike.Locked);
            Assert.IsNull(Flow.ProximoPlan, "Ninguém na fila.");
            Assert.Less(Flow.Session.App.AcceptanceRate, acceptanceBefore, "Deixar vencer pesa na taxa de aceite.");
            Assert.AreEqual(1, refused, "O evento de oferta perdida dispara.");
        }

        [UnityTest]
        public IEnumerator MidRunOfferAcceptedGoesStraightToTheQueuedRestaurantAfterTheDelivery()
        {
            StartOnline();
            yield return TakeOrderToCustomer();
            int accepted = 0;
            Flow.OfertaAceita += () => accepted++;
            Flow.ForcarOfertaNaCorrida();
            yield return null;

            Assert.IsTrue(Flow.Ui.OfferCompactVisible);
            Assert.IsFalse(Flow.OfertaCompactaDupla);
            Assert.IsTrue(Flow.Ui.ClickOfferCompact(true));

            Assert.IsFalse(Flow.Ui.OfferCompactVisible);
            Assert.IsNotNull(Flow.Session.Proximo, "Aceito, vira o Próximo pedido.");
            RunPlan queued = Flow.ProximoPlan;
            Order queuedOrder = Flow.Session.Proximo;
            Assert.IsNotNull(queued);
            Assert.AreEqual(1, accepted);
            Assert.IsNull(Flow.Session.OferecerAdicional(), "Máximo de um pedido na fila.");

            yield return FinishDeliveryQuickly();
            yield return null;

            Assert.AreEqual(FlowState.DrivingToRestaurant, Flow.State, "Sem esperar: já está indo ao próximo restaurante.");
            Assert.AreSame(queued.Restaurant, Flow.CurrentPlan.Restaurant);
            Assert.AreEqual(queuedOrder.Id, Flow.CurrentOrder.Id);
            Assert.IsTrue(Flow.Route.HasTarget);
            Assert.IsNull(Flow.Session.Proximo);
            Assert.IsFalse(Flow.Ui.ProcurandoVisivel, "Não passa pela espera online.");
        }

        [UnityTest]
        public IEnumerator DoubleOrderFromTheSameRestaurantIsPickedTogetherAndDeliveredInSequence()
        {
            StartOnline();
            Flow.ForcarOfertaAgora();
            Flow.AcceptOffer();
            yield return null;
            Place restaurant = Flow.CurrentPlan.Restaurant;
            Flow.ForcarOfertaNaCorrida(true);
            yield return null;

            Assert.IsTrue(Flow.Ui.OfferCompactVisible);
            Assert.IsTrue(Flow.OfertaCompactaDupla);
            StringAssert.Contains("duplo", Flow.Ui.OfferCompactTag.ToLowerInvariant());
            Assert.IsTrue(Flow.Ui.ClickOfferCompact(true));
            Assert.IsTrue(Flow.ProximoDuplo);
            Assert.AreSame(restaurant, Flow.ProximoPlan.Restaurant, "O segundo pedido é do mesmo restaurante.");
            Assert.AreNotSame(Flow.CurrentPlan.Customer, Flow.ProximoPlan.Customer);

            yield return PickUpOrder(Flow);
            Place secondCustomer = Flow.ProximoPlan.Customer;
            yield return FinishDeliveryQuickly();
            yield return null;

            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State, "O segundo já está na bolsa: vai direto ao cliente, sem balcão.");
            Assert.AreSame(secondCustomer, Flow.CurrentPlan.Customer);
            Assert.IsNull(Flow.Retirada);
        }

        [UnityTest]
        public IEnumerator AnArmedOfferArrivesByItselfWhenTheRunPassesTheProgressPoint()
        {
            StartOnline();
            yield return TakeOrderToCustomer();
            Flow.ArmarOfertaNaCorridaAgora(0.99f);

            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            Assert.IsFalse(Flow.Ui.OfferCompactVisible, "Antes do ponto de progresso nada aparece.");

            Flow.ArmarOfertaNaCorridaAgora(0f);
            yield return null;
            yield return null;

            Assert.IsTrue(Flow.Ui.OfferCompactVisible, "Passado o ponto, a oferta aparece sozinha.");
            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State);
        }

        [UnityTest]
        public IEnumerator RefusingAMidRunOfferCountsAgainstAcceptance()
        {
            StartOnline();
            yield return TakeOrderToCustomer();
            double before = Flow.Session.App.AcceptanceRate;
            Flow.ForcarOfertaNaCorrida();
            yield return null;
            Assert.IsTrue(Flow.Ui.ClickOfferCompact(false));

            Assert.IsFalse(Flow.Ui.OfferCompactVisible);
            Assert.IsNull(Flow.Session.Proximo);
            Assert.Less(Flow.Session.App.AcceptanceRate, before);
        }

        [UnityTest]
        public IEnumerator AnOfferCanArriveWhileWaitingAtTheCounter()
        {
            StartOnline();
            Flow.ForcarOfertaAgora();
            Flow.AcceptOffer();
            Flow.SegundosPorMinuto = 1f;
            Flow.SituacaoForcada = BalcaoSituacao.SaindoJaJa;
            TeleportNear(Flow, Flow.CurrentPlan.Restaurant);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 60 && !Flow.Balcao.PromptVisible; i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.Balcao.ClickPrompt());
            Assert.IsTrue(Flow.Balcao.ClickChoice("Esperar"));
            Assert.AreEqual(FlowState.NoBalcao, Flow.State);
            Flow.ForcarOfertaNaCorrida();

            Assert.IsTrue(Flow.Ui.OfferCompactVisible, "Durante a espera o celular ainda recebe oferta.");
            Assert.AreEqual(FlowState.NoBalcao, Flow.State);
            Assert.IsTrue(Flow.Balcao.WaitBarVisible, "E a espera segue rodando.");
        }

        // ---------- Entrega na porta ----------

        [UnityTest]
        public IEnumerator DeliveryNeedsStoppingAndTheButtonThenADialogue()
        {
            StartOnline();
            yield return TakeOrderToCustomer();

            Flow.EntregaForcada = EntregaSituacao.Portaria;
            TeleportNear(Flow, Flow.CurrentPlan.Customer);
            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Flow.Bike.transform.forward * 12f;
            yield return null;

            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State, "Passar em velocidade não entrega.");
            Assert.IsFalse(Flow.Balcao.PromptVisible);

            Flow.Bike.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;

            for (int i = 0; i < 60 && !Flow.Balcao.PromptVisible; i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.Balcao.PromptVisible, "Parado na zona aparece Entregar pedido.");
            StringAssert.Contains("Entregar pedido", Flow.Balcao.PromptLabel);
            Assert.AreEqual(FlowState.DrivingToCustomer, Flow.State, "Só o botão (ou E) começa a entrega.");
            Assert.AreEqual(0, Flow.Session.App.DeliveriesThisWeek);

            Assert.IsTrue(Flow.Balcao.ClickPrompt());
            Assert.AreEqual(FlowState.AtDoor, Flow.State);
            Assert.IsTrue(Flow.Balcao.DialogueVisible, "Abre o diálogo com o porteiro.");
            Assert.AreEqual("Porteiro", Flow.Balcao.Subtitle);
            Assert.AreEqual(0, Flow.Session.App.DeliveriesThisWeek, "Ainda não entregou: falta o diálogo.");
            CollectionAssert.Contains(Flow.Balcao.ChoiceLabels, "Deixar com o porteiro");
            Assert.IsTrue(Flow.Bike.Locked);
        }

        [UnityTest]
        public IEnumerator DeliveryWaitPlaysOutWithTheLineChangingAndThenPaysWithAToast()
        {
            StartOnline();
            yield return TakeOrderToCustomer();
            Money before = Flow.Session.Wallet.Balance;
            Flow.SegundosPorMinuto = 1f;
            yield return ArriveAtCustomer(Flow);

            Assert.IsTrue(Flow.Balcao.WaitBarVisible, "O cliente descendo é uma espera de verdade.");
            Assert.AreEqual(EntregaPasso.Espera, Flow.Entrega.Passo);
            string first = Flow.Balcao.Speech;
            bool changed = false;
            float until = Time.realtimeSinceStartup + 12f;

            while (Flow.Entrega.Passo == EntregaPasso.Espera && Time.realtimeSinceStartup < until)
            {
                changed |= Flow.Balcao.Speech != first;
                yield return null;
            }

            Assert.IsTrue(changed, "A fala muda no meio da espera.");
            Assert.AreEqual(EntregaPasso.Atendimento, Flow.Entrega.Passo);
            Assert.AreEqual(0, Flow.Session.App.DeliveriesThisWeek);

            Flow.SegundosPorMinuto = RelogioJogo.SegundosPorMinuto;
            Assert.IsTrue(Flow.Balcao.ClickChoice("Entregar o pedido"));
            yield return null;

            Assert.AreEqual(FlowState.Online, Flow.State, "Depois da entrega o app volta a procurar pedidos.");
            Assert.AreEqual(1, Flow.Session.App.DeliveriesThisWeek);
            Assert.Greater(Flow.Session.Wallet.Balance.Cents, before.Cents);
            StringAssert.Contains("Pedido entregue", Flow.Ui.ToastMessage);
            Assert.IsTrue(Flow.Ui.ProcurandoVisivel);
            Assert.IsFalse(Flow.Ui.ModalVisible, "Nenhum painel encadeado.");
        }

        [UnityTest]
        public IEnumerator RefusingToClimbLeadsToAnArgumentWithConsequences()
        {
            StartOnline();
            yield return TakeOrderToCustomer();
            yield return ArriveAtCustomer(Flow, EntregaSituacao.PedeParaSubir);

            CollectionAssert.Contains(Flow.Balcao.ChoiceLabels, "Recusar educadamente");
            Assert.IsTrue(Flow.Balcao.ClickChoice("Recusar"));
            Assert.AreEqual(FlowState.AtDoor, Flow.State, "A discussão continua.");
            CollectionAssert.Contains(Flow.Balcao.ChoiceLabels, "Manter: não posso subir");
            CollectionAssert.Contains(Flow.Balcao.ChoiceLabels, "Ceder e subir");
            Assert.GreaterOrEqual(Flow.Entrega.MinutosPerdidos, 1, "A discussão custa tempo.");
        }

        [UnityTest]
        public IEnumerator ForgottenCodeLetsThePlayerCallAndPickTheRightCode()
        {
            StartOnline();
            yield return TakeOrderToCustomer();
            yield return ArriveAtCustomer(Flow, EntregaSituacao.CodigoEntrega);

            Assert.IsTrue(Flow.Balcao.ChoiceLabels.Count >= 2);
            CollectionAssert.Contains(Flow.Balcao.ChoiceLabels, "Ligar pelo app");
            Flow.SegundosPorMinuto = Fast;
            Assert.IsTrue(Flow.Balcao.ClickChoice("Esperar"));

            for (int i = 0; i < 600 && Flow.Entrega.Passo == EntregaPasso.Espera; i++)
            {
                yield return null;
            }

            Assert.AreEqual(3, Flow.Balcao.ChoiceLabels.Count, "Depois da busca, escolhe o código entre três.");
            Assert.IsTrue(Flow.Balcao.ClickChoice(Flow.Entrega.Codigo));
            Assert.AreEqual(FlowState.Online, Flow.State);
            Assert.AreEqual(1, Flow.Session.App.DeliveriesThisWeek);
        }

        [UnityTest]
        public IEnumerator PhotoOptionTiesIntoTheExistingDeliveryPhoto()
        {
            StartOnline();
            yield return TakeOrderToCustomer();
            Flow.SegundosPorMinuto = Fast;
            yield return ArriveAtCustomer(Flow);

            for (int i = 0; i < 600 && Flow.Entrega.Passo == EntregaPasso.Espera; i++)
            {
                yield return null;
            }

            Assert.IsTrue(Flow.Balcao.ClickChoice("Tirar foto da entrega"));
            Assert.IsTrue(Flow.Tracker.PhotoTaken);
            Assert.IsFalse(Flow.Balcao.ChoiceLabels.Contains("Tirar foto da entrega"), "Depois de tirada a opção some.");
        }

        // ---------- Turno ----------

        [UnityTest]
        public IEnumerator AShiftEndsAfterTheAppOfferedEverythingAndAllWasDelivered()
        {
            StartOnline(21);
            int target = Flow.Session.App.OrdersForShift();

            for (int i = 0; i < target; i++)
            {
                if (Flow.State == FlowState.Offer)
                {
                    Flow.AcceptOffer();
                    yield return PickUpOrder(Flow);
                }
                else
                {
                    yield return TakeOrderToCustomer();
                }

                yield return ArriveAtCustomer(Flow);
                Flow.Deliver();
                yield return null;

                if (Flow.State == FlowState.Result)
                {
                    Assert.IsTrue(Flow.Ui.ClickModalButton("Continuar"));

                    if (Flow.State == FlowState.Claim)
                    {
                        Assert.IsTrue(Flow.Ui.ClickModalButton("Aceitar"));
                        Assert.IsTrue(Flow.Ui.ClickModalButton("Continuar"));
                    }
                }

                if (i < target - 1)
                {
                    Assert.AreEqual(FlowState.Online, Flow.State, "Entre os pedidos o app volta a procurar.");
                }
            }

            yield return null;
            Assert.AreEqual(FlowState.ShiftSummary, Flow.State, "Entregues todos os pedidos do app, o turno acaba.");
            Assert.IsTrue(Flow.Ui.ClickModalButton("Próximo dia"));
            Assert.AreEqual(FlowState.FreeRoam, Flow.State);
        }

        [UnityTest]
        public IEnumerator AShiftAlsoEndsWhenTheTimeBudgetRunsOut()
        {
            StartOnline();
            Flow.Session.Relogio.AvancarMinutos(Flow.Session.Economy.ShiftMinutes + 5);
            yield return null;
            yield return null;

            Assert.AreEqual(FlowState.ShiftSummary, Flow.State, "Estourado o orçamento de tempo, o turno termina.");
        }

        [UnityTest]
        public IEnumerator EndShiftIsOnThePhoneMenuAndAsksToFinishAnActiveOrderFirst()
        {
            StartOnline();
            Flow.OpenOnlineMenu();
            Assert.AreEqual(FlowState.OnlineMenu, Flow.State);
            CollectionAssert.Contains(Flow.Ui.ModalButtonLabels, "Encerrar turno");
            Assert.AreEqual(0f, Time.timeScale);
            Flow.CloseOnlineMenu();
            Assert.AreEqual(FlowState.Online, Flow.State);
            Assert.AreEqual(1f, Time.timeScale);

            Flow.ForcarOfertaAgora();
            Flow.AcceptOffer();
            yield return null;
            Flow.PauseGame();
            Assert.IsTrue(Flow.Ui.ClickModalButton("Encerrar turno"));
            StringAssert.Contains("Termine a entrega", Flow.Ui.ModalTitle);
            CollectionAssert.Contains(Flow.Ui.ModalButtonLabels, "Voltar");
            Assert.AreEqual(FlowState.Paused, Flow.State, "Com pedido ativo nada é encerrado direto.");
        }

        [UnityTest]
        public IEnumerator TheGameClockRunsAtEightSecondsPerGameMinute()
        {
            StartOnline();
            yield return null;
            double start = Flow.Relogio.Minutos;
            yield return new WaitForSeconds(2f);
            double minutes = Flow.Relogio.Minutos - start;

            Assert.That(minutes, Is.InRange(2.0 / 8.0 * 0.7, 2.0 / 8.0 * 1.4), "2 s reais = 0,25 min de jogo.");
        }

        private GameSession Session() => Flow.Session;
    }
}
