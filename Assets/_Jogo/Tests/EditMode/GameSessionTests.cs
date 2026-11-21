using System;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class GameSessionTests
    {
        private static readonly bool[] NoRain = new bool[7];

        private static GameSession NewSession(IGameRandom random = null, AppConfig app = null, bool[] rain = null) =>
            new GameSession(random ?? new FakeRandom(0.5), null, app, rain ?? NoRain);

        /// <summary>Oferece o próximo pedido e aceita.</summary>
        private static Order OfferAndAccept(GameSession session)
        {
            Order order = session.NextOffer();
            session.Accept(order);

            return order;
        }

        [Test]
        public void Inicio_CaucaoDebitadaNoDia1()
        {
            GameSession session = NewSession();

            Assert.AreEqual(Money.Reais(100), session.Wallet.Balance);
            Assert.AreEqual(LedgerCategory.Caucao, session.Wallet.Entries[0].Category);
            Assert.AreEqual(1, session.Day);
            Assert.IsFalse(session.IsWeekOver);
        }

        [Test]
        public void SemanaPerfeita_21Entregas_SemanaBoa()
        {
            GameSession session = NewSession();

            for (int day = 1; day <= 7; day++)
            {
                Assert.IsTrue(session.StartShift());

                for (Order order = session.NextOffer(); order != null; order = session.NextOffer())
                {
                    session.Accept(order);
                    Assert.IsNull(session.RollRunIncident(false));

                    DeliveryResult result = session.FinishRun(order, RunReport.OnTime(order.DistanceKm));

                    Assert.AreEqual(5, result.Stars);
                    Assert.AreEqual(Money.Reais(2.5m), result.Tip);
                    Assert.IsNull(result.AfterDeliveryIncident);
                }

                session.EndDay();
            }

            Assert.IsTrue(session.IsWeekOver);

            WeekReport report = session.EndWeek();

            // 200 - 100 (caução) + 21 x 15,00 (entregas) + 21 x 2,50 (gorjetas) - 21 x 0,88 (combustível, 3,5 km x 0,25 arredondado)
            // + 80 (meta) - 350 (aluguel) + 100 (caução devolvida) = 279,02
            Assert.AreEqual(21, report.Deliveries);
            Assert.AreEqual(Money.Reais(80), report.Bonus);
            Assert.AreEqual(Money.Zero, report.Fines);
            Assert.AreEqual(Money.Reais(350), report.Rent);
            Assert.AreEqual(Money.Reais(100), report.DepositRefund);
            Assert.AreEqual(new Money(27902), report.FinalBalance);
            Assert.AreEqual(new Money(27902), session.Wallet.Balance);
            Assert.AreEqual(5.0, report.Rating, 0.0001);
            Assert.AreEqual(WeekResult.SemanaBoa, report.Result);
        }

        [Test]
        public void SemanaSoDeFolgas_NoVermelho()
        {
            GameSession session = NewSession();

            for (int day = 1; day <= 7; day++)
            {
                session.TakeDayOff();
                session.EndDay();
            }

            WeekReport report = session.EndWeek();

            // 200 - 100 - 350 + 100 = -150
            Assert.AreEqual(Money.Reais(-150), report.FinalBalance);
            Assert.AreEqual(0, report.Deliveries);
            Assert.AreEqual(Money.Zero, report.Bonus);
            Assert.AreEqual(WeekResult.NoVermelho, report.Result);
        }

        [Test]
        public void SemanaComSaldoPositivoMasAbaixoDoInicial_Sobreviveu()
        {
            var economy = new EconomyConfig { WeeklyRent = Money.Reais(250) };
            var session = new GameSession(new FakeRandom(0.5), economy, null, NoRain);

            for (int day = 1; day <= 7; day++)
            {
                session.StartShift();

                // Só uma entrega por dia no horário: 7 entregas, sem bônus.
                Order order = OfferAndAccept(session);
                session.FinishRun(order, RunReport.OnTime(order.DistanceKm));
                session.EndDay();
            }

            WeekReport report = session.EndWeek();

            // 200 + 7 x (15,00 + 2,50 - 0,88) - 250 = 66,34
            Assert.AreEqual(7, report.Deliveries);
            Assert.AreEqual(Money.Zero, report.Bonus);
            Assert.AreEqual(new Money(6634), report.FinalBalance);
            Assert.AreEqual(WeekResult.Sobreviveu, report.Result);
        }

        [Test]
        public void ContaBloqueada_RecusaOTurnoNoDiaSeguinte()
        {
            GameSession session = NewSession(app: new AppConfig { OrdersPerShift = 4 });

            Assert.IsTrue(session.StartShift());

            for (int i = 0; i < 4; i++)
            {
                Order order = OfferAndAccept(session);
                DeliveryResult result = session.FinishRun(order, new RunReport { KmDriven = 1, FoodCondition = 0.0 });

                Assert.IsTrue(result.Spoiled);
            }

            Assert.IsNull(session.NextOffer());
            session.EndDay();

            Assert.IsTrue(session.App.IsBlockedOn(2));
            Assert.IsFalse(session.StartShift());

            session.TakeDayOff();
            session.EndDay();

            Assert.IsTrue(session.StartShift());
        }

        [Test]
        public void ContaBloqueada_PodeEncerrarODiaSemTurno()
        {
            GameSession session = NewSession(app: new AppConfig { OrdersPerShift = 4 });
            session.StartShift();

            for (int i = 0; i < 4; i++)
            {
                session.FinishRun(OfferAndAccept(session), new RunReport { KmDriven = 1, FoodCondition = 0.0 });
            }

            session.EndDay();

            Assert.IsFalse(session.StartShift());
            session.EndDay();

            Assert.AreEqual(3, session.Day);
        }

        [Test]
        public void Capa_ComprasUmaVezSo()
        {
            GameSession session = NewSession();

            Assert.IsTrue(session.BuyRaincoat());
            Assert.IsFalse(session.BuyRaincoat());

            Assert.IsTrue(session.HasRaincoat);
            Assert.AreEqual(Money.Reais(70), session.Wallet.Balance);
            Assert.AreEqual(LedgerCategory.Compra, session.Wallet.Entries[1].Category);
        }

        [Test]
        public void Chuva_SemCapaTiraUmaEstrelaEPagaMais()
        {
            var rain = new[] { true, false, false, false, false, false, false };
            GameSession session = NewSession(rain: rain);

            session.StartShift();
            Order order = OfferAndAccept(session);

            Assert.IsTrue(order.IsRaining);
            Assert.AreEqual(Money.Reais(19.5m), order.Pay);

            DeliveryResult result = session.FinishRun(order, RunReport.OnTime(order.DistanceKm));

            Assert.AreEqual(4, result.Stars);
        }

        [Test]
        public void Chuva_ComCapaMantem5Estrelas()
        {
            var rain = new[] { true, false, false, false, false, false, false };
            GameSession session = NewSession(rain: rain);
            session.BuyRaincoat();

            session.StartShift();
            Order order = OfferAndAccept(session);
            DeliveryResult result = session.FinishRun(order, RunReport.OnTime(order.DistanceKm));

            Assert.AreEqual(5, result.Stars);
        }

        [Test]
        public void Gorjeta_4Estrelas_ChegaAte2Reais()
        {
            // km, gorjeta (0,9 x 2,00 = 1,80), sorteio de incidente
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.9));
            session.StartShift();
            Order order = OfferAndAccept(session);

            DeliveryResult result = session.FinishRun(order, new RunReport { KmDriven = 3.5, TimeRatio = 1.3 });

            Assert.AreEqual(4, result.Stars);
            Assert.AreEqual(Money.Reais(1.8m), result.Tip);
        }

        [Test]
        public void Gorjeta_AtrasoSemGorjetaMesmoCom5Estrelas()
        {
            GameSession session = NewSession();
            session.StartShift();
            Order order = OfferAndAccept(session);

            DeliveryResult result = session.FinishRun(order, new RunReport { KmDriven = 3.5, TimeRatio = 1.1 });

            Assert.AreEqual(5, result.Stars);
            Assert.AreEqual(Money.Zero, result.Tip);
        }

        [Test]
        public void Gorjeta_3EstrelasNaoDa()
        {
            GameSession session = NewSession();
            session.StartShift();
            Order order = OfferAndAccept(session);

            DeliveryResult result = session.FinishRun(order, new RunReport { KmDriven = 3.5, TimeRatio = 1.6 });

            Assert.AreEqual(3, result.Stars);
            Assert.AreEqual(Money.Zero, result.Tip);
        }

        [Test]
        public void ComidaEstragada_SemPagamentoNota1()
        {
            GameSession session = NewSession();
            session.StartShift();
            Order order = OfferAndAccept(session);

            DeliveryResult result = session.FinishRun(order, new RunReport { KmDriven = 3.5, FoodCondition = 0.1 });

            Assert.IsTrue(result.Spoiled);
            Assert.AreEqual(Money.Zero, result.Total);
            Assert.AreEqual(1, result.Stars);
            Assert.AreEqual(0, session.App.DeliveriesThisWeek);
            Assert.AreEqual(4.8, session.App.Rating, 0.0001);
            // só o combustível saiu: 100 - 0,88
            Assert.AreEqual(new Money(10000 - 88), session.Wallet.Balance);
        }

        [Test]
        public void Corrida_DescontaCombustivelDanoEMultas()
        {
            GameSession session = NewSession();
            session.StartShift();
            Order order = OfferAndAccept(session);

            session.FinishRun(order, new RunReport { KmDriven = 4, DamageAdded = 0.1, Fines = 2 });

            Assert.AreEqual(0.1, session.Rental.Damage, 0.0001);
            Assert.AreEqual(2, session.PendingFines);
            Assert.AreEqual(LedgerCategory.Combustivel, session.Wallet.Entries[1].Category);
            Assert.AreEqual(Money.Reais(-1), session.Wallet.Entries[1].Amount);

            // Multas e dano só pesam no fim da semana.
            session.EndDay();

            for (int day = 2; day <= 7; day++)
            {
                session.TakeDayOff();
                session.EndDay();
            }

            WeekReport report = session.EndWeek();

            Assert.AreEqual(Money.Reais(100), report.Fines);
            Assert.AreEqual(Money.Reais(90), report.DepositRefund);
        }

        [Test]
        public void Pular_Paga85PorCentoSemGorjeta()
        {
            // km, estrelas (0,1 -> 5), sorteio de incidente
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.1));
            session.StartShift();
            Order order = OfferAndAccept(session);

            DeliveryResult result = session.SkipRun(order);

            Assert.IsTrue(result.Skipped);
            Assert.AreEqual(Money.Reais(12.75m), result.Pay);
            Assert.AreEqual(Money.Zero, result.Tip);
            Assert.AreEqual(5, result.Stars);
            Assert.AreEqual(1, session.App.DeliveriesThisWeek);
            // 100 + 12,75 - 0,88
            Assert.AreEqual(new Money(10000 + 1275 - 88), session.Wallet.Balance);
        }

        [Test]
        public void Pular_SorteiaEntre4e5Estrelas()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.9));
            session.StartShift();

            DeliveryResult result = session.SkipRun(OfferAndAccept(session));

            Assert.AreEqual(4, result.Stars);
        }

        [Test]
        public void Pular_AtrasoBaixaANota()
        {
            // 3,5 km x 4 min/km = 14 min previstos; 20 min de atraso = razão 2,43 -> -2 estrelas
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.1));
            session.StartShift();

            DeliveryResult result = session.SkipRun(OfferAndAccept(session), 20);

            Assert.AreEqual(3, result.Stars);
        }

        [Test]
        public void Pular_SemFoto_NaoTemMostrarFoto()
        {
            // km, estrelas, sorteio de incidente (0,0 -> acontece)
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.1, 0.0));
            session.StartShift();

            DeliveryResult result = session.SkipRun(OfferAndAccept(session));

            Assert.IsNotNull(result.AfterDeliveryIncident);
            CollectionAssert.DoesNotContain(result.AfterDeliveryIncident.Choices, IncidentChoice.MostrarFoto);
        }

        [Test]
        public void PneuFurado_Empurrar_Cobra40ReaisEAEntregaSegue()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.0));
            session.StartShift();
            Order order = OfferAndAccept(session);

            Incident incident = session.RollRunIncident(false);

            Assert.AreEqual(IncidentKind.PneuFurado, incident.Kind);

            IncidentOutcome outcome = session.ResolveRunIncident(order, incident, IncidentChoice.EmpurrarAteBorracharia);

            Assert.AreEqual(15, outcome.DelayMinutes);
            Assert.AreEqual(Money.Reais(60), session.Wallet.Balance);
            Assert.AreEqual(LedgerCategory.Conserto, session.Wallet.Entries[1].Category);

            DeliveryResult result = session.FinishRun(order, new RunReport { KmDriven = 3.5, TimeRatio = 1.4 });

            Assert.AreEqual(4, result.Stars);
        }

        [Test]
        public void PneuFurado_Anuncio_NaoCustaNada()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.0));
            session.StartShift();
            Order order = OfferAndAccept(session);
            Incident incident = session.RollRunIncident(false);

            IncidentOutcome outcome = session.ResolveRunIncident(order, incident, IncidentChoice.VerAnuncio);

            Assert.IsTrue(outcome.RequiresRewardedAd);
            Assert.AreEqual(Money.Reais(100), session.Wallet.Balance);
        }

        [Test]
        public void PneuFurado_Cancelar_SemPagamentoEContaComoRecusa()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.0));
            session.StartShift();
            Order order = OfferAndAccept(session);
            Incident incident = session.RollRunIncident(false);

            IncidentOutcome outcome = session.ResolveRunIncident(order, incident, IncidentChoice.CancelarEntrega);

            Assert.IsTrue(outcome.CancelsDelivery);
            Assert.AreEqual(Money.Reais(100), session.Wallet.Balance);
            Assert.AreEqual(0, session.App.Accepted);
            Assert.AreEqual(0, session.App.DeliveriesThisWeek);

            // A entrega acabou: o próximo pedido pode vir.
            Assert.IsNotNull(session.NextOffer());
        }

        [Test]
        public void NaoRecebeu_ComFoto_MostrarFotoNaoPerdeNada()
        {
            // km, gorjeta, sorteio de incidente (0,0 -> acontece)
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.5, 0.0));
            session.StartShift();
            Order order = OfferAndAccept(session);

            DeliveryResult result = session.FinishRun(order, RunReport.OnTime(3.5, true));

            Assert.IsNotNull(result.AfterDeliveryIncident);
            CollectionAssert.Contains(result.AfterDeliveryIncident.Choices, IncidentChoice.MostrarFoto);

            Money before = session.Wallet.Balance;
            session.ResolveAfterDelivery(order, result.AfterDeliveryIncident, IncidentChoice.MostrarFoto);

            Assert.AreEqual(before, session.Wallet.Balance);
            Assert.AreEqual(5.0, session.App.Rating, 0.0001);
        }

        [Test]
        public void NaoRecebeu_SemFoto_NaoOferecaMostrarFoto()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.5, 0.0));
            session.StartShift();
            Order order = OfferAndAccept(session);

            DeliveryResult result = session.FinishRun(order, RunReport.OnTime(3.5, false));

            CollectionAssert.DoesNotContain(result.AfterDeliveryIncident.Choices, IncidentChoice.MostrarFoto);
            Assert.Throws<ArgumentException>(() =>
                session.ResolveAfterDelivery(order, result.AfterDeliveryIncident, IncidentChoice.MostrarFoto));
        }

        [Test]
        public void NaoRecebeu_Aceitar_DevolvePagamentoComGorjetaEDa1Estrela()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.5, 0.0));
            session.StartShift();
            Order order = OfferAndAccept(session);
            DeliveryResult result = session.FinishRun(order, RunReport.OnTime(3.5, true));
            Money before = session.Wallet.Balance;

            session.ResolveAfterDelivery(order, result.AfterDeliveryIncident, IncidentChoice.Aceitar);

            Assert.AreEqual(before - Money.Reais(17.5m), session.Wallet.Balance);
            Assert.AreEqual(LedgerCategory.Estorno, session.Wallet.Entries[session.Wallet.Entries.Count - 1].Category);
            Assert.AreEqual(4.8, session.App.Rating, 0.0001);
        }

        [Test]
        public void NaoRecebeu_Contestar_PerdeOuGanhaConformeSorte()
        {
            // km, gorjeta, incidente, contestação perdida (0,9)
            GameSession lost = NewSession(new FakeRandom(0.5, 0.5, 0.5, 0.0, 0.9));
            lost.StartShift();
            Order lostOrder = OfferAndAccept(lost);
            DeliveryResult lostResult = lost.FinishRun(lostOrder, RunReport.OnTime(3.5, true));
            Money lostBefore = lost.Wallet.Balance;

            IncidentOutcome lostOutcome = lost.ResolveAfterDelivery(lostOrder, lostResult.AfterDeliveryIncident, IncidentChoice.Contestar);

            Assert.IsTrue(lostOutcome.LosesPay);
            Assert.AreEqual(lostBefore - Money.Reais(17.5m), lost.Wallet.Balance);
            Assert.AreEqual(5.0, lost.App.Rating, 0.0001);

            GameSession won = NewSession(new FakeRandom(0.5, 0.5, 0.5, 0.0, 0.1));
            won.StartShift();
            Order wonOrder = OfferAndAccept(won);
            DeliveryResult wonResult = won.FinishRun(wonOrder, RunReport.OnTime(3.5, true));
            Money wonBefore = won.Wallet.Balance;

            won.ResolveAfterDelivery(wonOrder, wonResult.AfterDeliveryIncident, IncidentChoice.Contestar);

            Assert.AreEqual(wonBefore, won.Wallet.Balance);
        }

        [Test]
        public void Recusar_LiberaOProximoPedido()
        {
            GameSession session = NewSession();
            session.StartShift();

            Order first = session.NextOffer();
            session.Reject(first);
            Order second = session.NextOffer();

            Assert.AreNotEqual(first.Id, second.Id);
            Assert.AreEqual(2, session.App.Offered);
        }

        [Test]
        public void Turno_TemOPedidosDoApp_E_RecusarMuitoReduz()
        {
            GameSession session = NewSession();
            session.StartShift();

            int offers = 0;

            for (Order o = session.NextOffer(); o != null; o = session.NextOffer())
            {
                offers++;
                session.Reject(o);
            }

            Assert.AreEqual(3, offers);

            session.EndDay();
            session.StartShift();

            offers = 0;

            for (Order o = session.NextOffer(); o != null; o = session.NextOffer())
            {
                offers++;
                session.Reject(o);
            }

            Assert.AreEqual(2, offers);
        }

        [Test]
        public void ForaDeOrdem_Lanca()
        {
            GameSession session = NewSession();

            Assert.Throws<InvalidOperationException>(() => session.NextOffer());
            Assert.Throws<InvalidOperationException>(() => session.RollRunIncident(false));
            Assert.Throws<InvalidOperationException>(() => session.EndWeek());

            session.StartShift();

            Assert.Throws<InvalidOperationException>(() => session.StartShift());
            Assert.Throws<InvalidOperationException>(() => session.TakeDayOff());

            Order order = session.NextOffer();

            Assert.Throws<InvalidOperationException>(() => session.NextOffer());
            Assert.Throws<InvalidOperationException>(() => session.FinishRun(order, RunReport.OnTime(1)));
            Assert.Throws<InvalidOperationException>(() => session.SkipRun(order));
            Assert.Throws<InvalidOperationException>(() => session.EndDay());

            session.Accept(order);

            Assert.Throws<InvalidOperationException>(() => session.Accept(order));
            Assert.Throws<InvalidOperationException>(() => session.Reject(order));
            Assert.Throws<InvalidOperationException>(() => session.ResolveRunIncident(order, null, IncidentChoice.VerAnuncio));

            session.FinishRun(order, RunReport.OnTime(1));

            Assert.Throws<InvalidOperationException>(() => session.FinishRun(order, RunReport.OnTime(1)));
            var other = new Order(99, 1, Money.Zero, false);
            var claim = new Incident(IncidentKind.ClienteDizQueNaoRecebeu, new[] { IncidentChoice.Aceitar });

            Assert.Throws<InvalidOperationException>(() => session.ResolveAfterDelivery(other, claim, IncidentChoice.Aceitar));
        }

        [Test]
        public void DepoisDaSemana_NadaMaisAcontece()
        {
            GameSession session = NewSession();

            for (int day = 1; day <= 7; day++)
            {
                session.TakeDayOff();
                session.EndDay();
            }

            Assert.Throws<InvalidOperationException>(() => session.StartShift());
            Assert.Throws<InvalidOperationException>(() => session.EndDay());
            Assert.Throws<InvalidOperationException>(() => session.BuyRaincoat());

            session.EndWeek();

            Assert.Throws<InvalidOperationException>(() => session.EndWeek());
        }

        [Test]
        public void ListaDeChuvaComTamanhoErrado_Lanca()
        {
            Assert.Throws<ArgumentException>(() => new GameSession(new FakeRandom(), null, null, new bool[3]));
        }

        [Test]
        public void SemListaDeChuva_SorteiaDoisDiasChuvosos()
        {
            var session = new GameSession(new SeededRandom(7));
            int rainy = 0;

            for (int day = 1; day <= 7; day++)
            {
                if (session.IsRainyDay(day))
                {
                    rainy++;
                }
            }

            Assert.AreEqual(2, rainy);
        }
    }
}
