using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class OfertaAgendaTests
    {
        private static OfertaContexto Normal(double hora = 15, double aceite = 1) =>
            new OfertaContexto { Hora = hora, TaxaAceite = aceite };

        [Test]
        public void Espera_BaseFicaEntre25e90Segundos()
        {
            var random = new SeededRandom(7);

            for (int i = 0; i < 200; i++)
            {
                double espera = OfertaAgenda.SortearEspera(random, Normal());

                Assert.That(espera, Is.InRange(25.0, 90.0));
            }
        }

        [Test]
        public void Espera_ExtremosDoSorteio()
        {
            Assert.AreEqual(25, OfertaAgenda.SortearEspera(new FakeRandom(0.0), Normal()), 1e-9);
            Assert.AreEqual(90, OfertaAgenda.SortearEspera(new FakeRandom(1.0), Normal()), 1e-9);
        }

        [Test]
        public void Picos_AlmocoEJantar()
        {
            Assert.IsTrue(OfertaAgenda.EmPico(11));
            Assert.IsTrue(OfertaAgenda.EmPico(13.9));
            Assert.IsFalse(OfertaAgenda.EmPico(14));
            Assert.IsFalse(OfertaAgenda.EmPico(16));
            Assert.IsTrue(OfertaAgenda.EmPico(19));
            Assert.IsFalse(OfertaAgenda.EmPico(21));
            Assert.IsFalse(OfertaAgenda.EmPico(8));
        }

        [Test]
        public void Pico_EncurtaAEspera()
        {
            double normal = OfertaAgenda.SortearEspera(new FakeRandom(0.5), Normal(16));
            double almoco = OfertaAgenda.SortearEspera(new FakeRandom(0.5), Normal(12));

            Assert.AreEqual(normal * 0.7, almoco, 1e-9);
        }

        [Test]
        public void Chuva_EncurtaAEspera()
        {
            OfertaContexto chuva = Normal(16);
            chuva.Chuva = true;

            Assert.AreEqual(OfertaAgenda.SortearEspera(new FakeRandom(0.5), Normal(16)) * 0.75, OfertaAgenda.SortearEspera(new FakeRandom(0.5), chuva), 1e-9);
        }

        [Test]
        public void TaxaDeAceiteBaixa_AlongaAEspera()
        {
            double boa = OfertaAgenda.SortearEspera(new FakeRandom(0.5), Normal(16, 1.0));
            double limite = OfertaAgenda.SortearEspera(new FakeRandom(0.5), Normal(16, 0.8));
            double baixa = OfertaAgenda.SortearEspera(new FakeRandom(0.5), Normal(16, 0.4));
            double zero = OfertaAgenda.SortearEspera(new FakeRandom(0.5), Normal(16, 0.0));

            Assert.AreEqual(boa, limite, 1e-9, "No limite de 80% ainda não pune.");
            Assert.Greater(baixa, boa);
            Assert.Greater(zero, baixa);
            Assert.AreEqual(boa * 1.8, zero, 1e-9);
        }

        [Test]
        public void ZonaQuente_EspeiraTermina30PorCentoAntes()
        {
            Assert.AreEqual(1.0, OfertaAgenda.VelocidadeDaEspera(false), 1e-9);
            Assert.AreEqual(1.0 / 0.7, OfertaAgenda.VelocidadeDaEspera(true), 1e-9);

            // 70 s de espera sorteada acabam em 49 s dentro da zona.
            Assert.AreEqual(49.0, 70.0 / OfertaAgenda.VelocidadeDaEspera(true), 1e-9);
        }

        [Test]
        public void EsperaNuncaFicaMenorQueOPiso()
        {
            var config = new OfertaConfig { EsperaMin = 1, EsperaMax = 2 };

            Assert.AreEqual(config.EsperaMinima, OfertaAgenda.SortearEspera(new FakeRandom(0.0), Normal(), config), 1e-9);
        }

        [Test]
        public void OfertaNaCorrida_AceiteBaixoReduzAChance()
        {
            // Com sorte 0,4: passa com a chance base 0,5, não passa com aceite baixo (0,3).
            Assert.IsTrue(OfertaAgenda.SortearOfertaNaCorrida(new FakeRandom(0.4), Normal(16, 1)));
            Assert.IsFalse(OfertaAgenda.SortearOfertaNaCorrida(new FakeRandom(0.4), Normal(16, 0.5)));
        }

        [Test]
        public void OfertaNaCorrida_PicoEChuvaAumentamAChance()
        {
            Assert.IsFalse(OfertaAgenda.SortearOfertaNaCorrida(new FakeRandom(0.6), Normal(16, 1)));
            Assert.IsTrue(OfertaAgenda.SortearOfertaNaCorrida(new FakeRandom(0.6), Normal(12, 1)));
        }

        [Test]
        public void ProgressoDaOferta_ConcentraDepoisDos60PorCento()
        {
            var random = new SeededRandom(11);
            int depois = 0;
            const int n = 2000;

            for (int i = 0; i < n; i++)
            {
                double p = OfertaAgenda.SortearProgressoDaOferta(random);
                Assert.That(p, Is.InRange(0.15, 0.9));
                depois += p >= 0.6 ? 1 : 0;
            }

            Assert.That(depois / (double)n, Is.InRange(0.64, 0.76), "Cerca de 70% das ofertas chegam depois dos 60% do caminho.");
        }

        [Test]
        public void PedidoDuplo_ChanceConfiguravel()
        {
            Assert.IsTrue(OfertaAgenda.SortearPedidoDuplo(new FakeRandom(0.1)));
            Assert.IsFalse(OfertaAgenda.SortearPedidoDuplo(new FakeRandom(0.5)));
        }

        [Test]
        public void ZonasQuentes_SorteiaDistintasEDetectaDentro()
        {
            var candidatas = new List<ZonaQuente>
            {
                new ZonaQuente("A", 0, 0, 50), new ZonaQuente("B", 200, 0, 50), new ZonaQuente("C", 0, 200, 50), new ZonaQuente("D", 200, 200, 50),
            };
            var random = new SeededRandom(3);

            for (int i = 0; i < 50; i++)
            {
                List<ZonaQuente> zonas = ZonasQuentes.Sortear(random, candidatas, 1, 3);

                Assert.That(zonas.Count, Is.InRange(1, 3));
                CollectionAssert.AllItemsAreUnique(zonas);
            }

            Assert.IsNotNull(ZonasQuentes.Qual(candidatas, 10, 10));
            Assert.AreEqual("B", ZonasQuentes.Qual(candidatas, 210, 20).Nome);
            Assert.IsNull(ZonasQuentes.Qual(candidatas, 100, 100));
        }

        [Test]
        public void ZonasQuentes_NuncaPedeMaisQueAsCandidatas()
        {
            var candidatas = new List<ZonaQuente> { new ZonaQuente("A", 0, 0, 10) };

            Assert.AreEqual(1, ZonasQuentes.Sortear(new FakeRandom(0.99), candidatas, 1, 3).Count);
        }

        // ---------- Relógio ----------

        [Test]
        public void Relogio_Compressao8SegundosPorMinuto()
        {
            var relogio = new RelogioJogo();

            Assert.AreEqual("08:00", relogio.Texto);

            relogio.Avancar(8 * 60);

            Assert.AreEqual("09:00", relogio.Texto);
            Assert.AreEqual(9.0, relogio.Hora, 1e-9);

            // 3 minutos de jogo (cliente descendo) são 24 s reais.
            Assert.AreEqual(24.0, RelogioJogo.MinutosParaSegundos(3), 1e-9);
        }

        [Test]
        public void Relogio_ParaNoFimDoDiaEReiniciaNoDiaSeguinte()
        {
            var relogio = new RelogioJogo();
            relogio.AvancarMinutos(100000);

            Assert.IsTrue(relogio.FimDoDia);
            Assert.AreEqual("23:59", relogio.Texto);

            relogio.Reiniciar(2);

            Assert.AreEqual(2, relogio.Dia);
            Assert.AreEqual("08:00", relogio.Texto);
        }

        // ---------- Sessão: turno, fila, relógio ----------

        private static GameSession NewSession(int ordersPerShift = 3, IGameRandom random = null) =>
            new GameSession(random ?? new FakeRandom(0.5), null, new AppConfig { OrdersPerShift = ordersPerShift }, new bool[7]);

        [Test]
        public void Turno_AcabaPeloOrcamentoDeTempo()
        {
            GameSession session = NewSession();
            session.StartShift();

            Assert.IsFalse(session.TempoEsgotado);
            Assert.IsFalse(session.TurnoAcabou);

            session.Relogio.AvancarMinutos(239);
            Assert.IsFalse(session.TempoEsgotado);

            session.Relogio.AvancarMinutos(2);

            Assert.IsTrue(session.TempoEsgotado);
            Assert.IsNull(session.NextOffer(), "Sem tempo o app para de oferecer.");
            Assert.IsTrue(session.TurnoAcabou);
        }

        [Test]
        public void Turno_AcabaQuandoOAppOfereceuTudoETudoFoiEntregue()
        {
            GameSession session = NewSession(2);
            session.StartShift();

            for (int i = 0; i < 2; i++)
            {
                Order order = session.NextOffer();
                session.Accept(order);

                Assert.IsFalse(session.TurnoAcabou, "Com entrega em andamento o turno não acabou.");

                session.FinishRun(order, RunReport.OnTime(order.DistanceKm));
            }

            Assert.IsTrue(session.TurnoAcabou);
        }

        [Test]
        public void Turno_EmAndamentoAteDepoisDoTempoTerminaAEntrega()
        {
            GameSession session = NewSession();
            session.StartShift();
            Order order = session.NextOffer();
            session.Accept(order);
            session.Relogio.AvancarMinutos(300);

            Assert.IsTrue(session.TempoEsgotado);
            Assert.IsFalse(session.TurnoAcabou, "A entrega em andamento ainda termina.");

            session.FinishRun(order, RunReport.OnTime(order.DistanceKm));

            Assert.IsTrue(session.TurnoAcabou);
        }

        [Test]
        public void RelogioReiniciaNoDiaSeguinte()
        {
            GameSession session = NewSession();
            session.Relogio.AvancarMinutos(120);
            session.TakeDayOff();
            session.EndDay();

            Assert.AreEqual(2, session.Relogio.Dia);
            Assert.AreEqual("08:00", session.Relogio.Texto);
        }

        [Test]
        public void OfertaAdicional_AceitaViraProximoPedidoEPromoveAposAEntrega()
        {
            GameSession session = NewSession();
            session.StartShift();
            Order first = session.NextOffer();

            Assert.IsNull(session.OferecerAdicional(), "Sem entrega aceita não há oferta extra.");

            session.Accept(first);
            Order extra = session.OferecerAdicional();

            Assert.IsNotNull(extra);
            Assert.AreNotEqual(first.Id, extra.Id);
            Assert.IsNull(session.OferecerAdicional(), "Só uma oferta extra por vez.");

            session.AceitarAdicional(extra);

            Assert.AreSame(extra, session.Proximo);
            Assert.IsNull(session.Adicional);
            Assert.IsNull(session.OferecerAdicional(), "Máximo de um pedido na fila.");
            Assert.AreEqual(2, session.App.Accepted);

            session.FinishRun(first, RunReport.OnTime(first.DistanceKm));
            Order promoted = session.PromoverProximo();

            Assert.AreSame(extra, promoted);
            Assert.IsNull(session.Proximo);
            Assert.IsFalse(session.TurnoAcabou);

            DeliveryResult result = session.FinishRun(promoted, RunReport.OnTime(promoted.DistanceKm));

            Assert.AreEqual(extra.Id, result.Order.Id);
        }

        [Test]
        public void OfertaAdicional_RecusadaPesaNaTaxaDeAceite()
        {
            GameSession session = NewSession();
            session.StartShift();
            Order first = OfferAccept(session);
            Order extra = session.OferecerAdicional();

            session.RecusarAdicional(extra);

            Assert.IsNull(session.Proximo);
            Assert.AreEqual(2, session.App.Offered);
            Assert.AreEqual(1, session.App.Accepted);
            Assert.AreEqual(0.5, session.App.AcceptanceRate, 1e-9);
            Assert.IsNotNull(first);
        }

        [Test]
        public void OfertaAdicional_ConsomeUmPedidoDoTurno()
        {
            GameSession session = NewSession(2);
            session.StartShift();
            OfferAccept(session);
            Order extra = session.OferecerAdicional();
            session.AceitarAdicional(extra);

            Assert.IsNull(session.OferecerAdicional(), "O turno só tinha dois pedidos.");
        }

        private static Order OfferAccept(GameSession session)
        {
            Order order = session.NextOffer();
            session.Accept(order);

            return order;
        }

        [Test]
        public void OfertaAdicional_AbandonarTurnoCancelaAtualEFila()
        {
            GameSession session = NewSession();
            session.StartShift();
            OfferAccept(session);
            session.AceitarAdicional(session.OferecerAdicional());

            session.AbandonarTurno();

            Assert.IsNull(session.Proximo);
            Assert.AreEqual(0, session.App.Accepted);
            Assert.DoesNotThrow(() => session.EndDay());
        }

        [Test]
        public void TurnoAcabou_FalsoComPedidoNaFila()
        {
            GameSession session = NewSession(2);
            session.StartShift();
            Order first = OfferAccept(session);
            session.AceitarAdicional(session.OferecerAdicional());
            session.FinishRun(first, RunReport.OnTime(first.DistanceKm));

            Assert.IsFalse(session.TurnoAcabou, "O próximo pedido ainda está na fila.");

            Order second = session.PromoverProximo();
            session.FinishRun(second, RunReport.OnTime(second.DistanceKm));

            Assert.IsTrue(session.TurnoAcabou);
        }
    }
}
