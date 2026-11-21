using System;
using System.Linq;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class TransitoSessionTests
    {
        private static readonly bool[] NoRain = new bool[7];

        private static PerfilInicial Perfil(MotoOrigem origem, bool cnh = true) =>
            new PerfilInicial { Origem = origem, TemCnh = cnh };

        private static EconomyConfig Rico() => new EconomyConfig { StartingMoney = Money.Reais(2000) };

        private static GameSession NewSession(IGameRandom random, PerfilInicial perfil = null, EconomyConfig economy = null) =>
            new GameSession(random, economy, null, NoRain, perfil);

        /// <summary>Começa o turno e aceita o pedido. Consome um sorteio (a distância do pedido).</summary>
        private static Order Aceitar(GameSession session)
        {
            Assert.IsTrue(session.StartShift());
            Order order = session.NextOffer();
            session.Accept(order);

            return order;
        }

        private static void PassarDia(GameSession session)
        {
            if (!session.IsShiftActive && session.Day <= session.Economy.DaysPerWeek)
            {
                // dia já fechado (apreensão) ou sem turno
                try
                {
                    session.TakeDayOff();
                }
                catch (InvalidOperationException)
                {
                }
            }

            session.EndDay();
        }

        private static WeekReport SemanaPerfeita(GameSession session)
        {
            for (int day = 1; day <= 7; day++)
            {
                Assert.IsTrue(session.StartShift());

                for (Order order = session.NextOffer(); order != null; order = session.NextOffer())
                {
                    session.Accept(order);
                    session.FinishRun(order, RunReport.OnTime(order.DistanceKm));
                }

                session.EndDay();
            }

            return session.EndWeek();
        }

        // ---------- perfil e economia ----------

        [Test]
        public void Perfil_Padrao_EAlugadaComCnh()
        {
            GameSession session = NewSession(new FakeRandom());

            Assert.AreEqual(MotoOrigem.Alugada, session.Origem);
            Assert.IsNotNull(session.Rental);
            Assert.IsTrue(session.Cnh.Valida);
            Assert.AreEqual(MotoStatus.Ok, session.MotoStatus);
        }

        [Test]
        public void Bob_ComecaSoComEntrada_SemCaucaoNemAluguel()
        {
            GameSession session = NewSession(new FakeRandom(), Perfil(MotoOrigem.Bob));

            Assert.IsNull(session.Rental);
            Assert.AreEqual(8, session.ParcelasRestantes);
            // 200 - 150 de entrada
            Assert.AreEqual(Money.Reais(50), session.Wallet.Balance);
            Assert.AreEqual(LedgerCategory.Entrada, session.Wallet.Entries[0].Category);
            Assert.IsFalse(session.Wallet.Entries.Any(e => e.Category == LedgerCategory.Caucao));
        }

        [Test]
        public void Bob_ParcelaSemanalNoFimDaSemana_SemCaucaoDevolvida()
        {
            GameSession session = NewSession(new FakeRandom(), Perfil(MotoOrigem.Bob));

            for (int day = 1; day <= 7; day++)
            {
                session.TakeDayOff();
                session.EndDay();
            }

            WeekReport report = session.EndWeek();

            Assert.AreEqual(Money.Reais(100), report.Rent);
            Assert.AreEqual(Money.Zero, report.DepositRefund);
            Assert.AreEqual(7, session.ParcelasRestantes);
            Assert.AreEqual(Money.Reais(-50), report.FinalBalance);
            Assert.IsTrue(session.Wallet.Entries.Any(e => e.Category == LedgerCategory.Parcela));
        }

        [Test]
        public void Bob_SemanaPerfeita_SaiMaisBaratoQueAlugada()
        {
            GameSession alugada = NewSession(new FakeRandom());
            GameSession bob = NewSession(new FakeRandom(), Perfil(MotoOrigem.Bob));

            WeekReport relatorioAlugada = SemanaPerfeita(alugada);
            WeekReport relatorioBob = SemanaPerfeita(bob);

            Assert.AreEqual(new Money(27902), relatorioAlugada.FinalBalance);
            // 200 - 150 + 315 + 52,50 - 18,48 + 80 - 100 (parcela) = 379,02
            Assert.AreEqual(new Money(37902), relatorioBob.FinalBalance);
            Assert.IsTrue(relatorioBob.FinalBalance > relatorioAlugada.FinalBalance);
            Assert.AreEqual(WeekResult.SemanaBoa, relatorioBob.Result);
        }

        // ---------- radar ----------

        [Test]
        public void Radar_MultaChegaDepoisDeDoisDias_ComTaxaDaLocadora()
        {
            // km do pedido, chegada sorteada (0,0 -> 2 dias)
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.0));
            Order order = Aceitar(session);
            var report = RunReport.OnTime(order.DistanceKm);
            report.Capturas.Add(Infracao.ExcessoVelocidadeAte20);

            session.FinishRun(order, report);
            Money saldo = session.Wallet.Balance;

            Assert.AreEqual(1, session.MultasPendentes.Count);
            Assert.AreEqual(3, session.MultasPendentes[0].DiaChegada);
            Assert.AreEqual(0, session.Cnh.Pontos);

            session.EndDay();
            Assert.AreEqual(1, session.MultasPendentes.Count);

            PassarDia(session);

            // dia 3: chegou
            Assert.AreEqual(0, session.MultasPendentes.Count);
            Assert.AreEqual(1, session.MultasPagas.Count);
            Assert.AreEqual(saldo - Money.Reais(130.16m) - Money.Reais(20), session.Wallet.Balance);
            Assert.AreEqual(4, session.Cnh.Pontos);
            Assert.AreEqual(2, session.Wallet.Entries.Count(e => e.Category == LedgerCategory.Multa));
        }

        [Test]
        public void Radar_ChegadaMaximaEmCincoDias()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.999));
            Order order = Aceitar(session);
            var report = RunReport.OnTime(order.DistanceKm);
            report.Capturas.Add(Infracao.Contramao);

            session.FinishRun(order, report);

            Assert.AreEqual(6, session.MultasPendentes[0].DiaChegada);
        }

        [Test]
        public void Radar_MotoBob_MultaNuncaChega()
        {
            GameSession session = NewSession(new FakeRandom(), Perfil(MotoOrigem.Bob));
            Order order = Aceitar(session);
            var report = RunReport.OnTime(order.DistanceKm);
            report.Capturas.Add(Infracao.AvancarSinalVermelho);
            Money saldo = session.Wallet.Balance;

            session.FinishRun(order, report);

            Assert.AreEqual(0, session.MultasPendentes.Count);

            for (int day = 1; day <= 7; day++)
            {
                PassarDia(session);
            }

            Assert.AreEqual(0, session.MultasPagas.Count);
            Assert.AreEqual(0, session.Cnh.Pontos);
            Assert.IsFalse(session.Wallet.Entries.Any(e => e.Category == LedgerCategory.Multa));
        }

        [Test]
        public void Radar_SemCnh_PagaMasNaoPontua()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.0), Perfil(MotoOrigem.Alugada, false));
            Order order = Aceitar(session);
            var report = RunReport.OnTime(order.DistanceKm);
            report.Capturas.Add(Infracao.ExcessoVelocidade20a50);

            session.FinishRun(order, report);
            PassarDia(session);
            PassarDia(session);

            Assert.AreEqual(1, session.MultasPagas.Count);
            Assert.AreEqual(0, session.Cnh.Pontos);
        }

        [Test]
        public void Radar_ChegandoDepoisDaSemana_FicaPendenteNoRelatorio()
        {
            GameSession session = NewSession(new FakeRandom());

            for (int day = 1; day <= 6; day++)
            {
                session.TakeDayOff();
                session.EndDay();
            }

            Order order = Aceitar(session);
            var report = RunReport.OnTime(order.DistanceKm);
            report.Capturas.Add(Infracao.ExcessoVelocidadeAte20);
            session.FinishRun(order, report);
            session.EndDay();

            WeekReport week = session.EndWeek();

            Assert.AreEqual(1, week.Transito.QuantidadePendentes);
            Assert.AreEqual(Money.Reais(150.16m), week.Transito.MultasPendentes);
            Assert.AreEqual(Money.Zero, week.Transito.MultasPagas);
        }

        // ---------- blitz do dia ----------

        [Test]
        public void Blitz_SorteioUmaVezPorDia()
        {
            var random = new FakeRandom(0.9, 0.1);
            GameSession session = NewSession(random);

            Assert.IsTrue(session.SortearBlitzDoDia());
            Assert.IsTrue(session.SortearBlitzDoDia());
            Assert.IsTrue(session.BlitzHoje);
            Assert.AreEqual(0, random.Remaining);

            session.TakeDayOff();
            session.EndDay();

            Assert.IsFalse(session.BlitzHoje);
            Assert.IsFalse(session.SortearBlitzDoDia());
        }

        [Test]
        public void Blitz_SemBlitzNoDia_NaoDaParaIniciar()
        {
            GameSession session = NewSession(new FakeRandom());
            session.SortearBlitzDoDia();
            Aceitar(session);

            Assert.Throws<InvalidOperationException>(() => session.IniciarBlitz());
        }

        // ---------- blitz: os quatro perfis ----------

        [Test]
        public void Blitz_AlugadaComCnh_SoAtrasaEAEntregaContinua()
        {
            // blitz, km, atraso (0,0 -> 10 min), sem problema menor
            GameSession session = NewSession(new FakeRandom(0.5, 0.1, 0.5, 0.0, 0.9));
            session.SortearBlitzDoDia();
            Order order = Aceitar(session);
            Money saldo = session.Wallet.Balance;

            Abordagem abordagem = session.IniciarBlitz();
            AbordagemOutcome outcome = session.ResolverAbordagem(abordagem, AbordagemChoice.Parar);

            Assert.AreEqual(10, outcome.DelayMinutes);
            Assert.AreEqual(saldo, session.Wallet.Balance);
            Assert.AreEqual(MotoStatus.Ok, session.MotoStatus);
            Assert.IsTrue(session.IsShiftActive);
            Assert.IsNotNull(session.FinishRun(order, RunReport.OnTime(order.DistanceKm)));
        }

        [Test]
        public void Blitz_AlugadaComCnh_ProblemaMenorCustaMultaEPontos()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1, 0.5, 0.0, 0.1));
            session.SortearBlitzDoDia();
            Aceitar(session);
            Money saldo = session.Wallet.Balance;

            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);

            Assert.AreEqual(saldo - Money.Reais(195.23m), session.Wallet.Balance);
            Assert.AreEqual(5, session.Cnh.Pontos);
            Assert.AreEqual(MotoStatus.Ok, session.MotoStatus);
        }

        [Test]
        public void Blitz_AlugadaSemCnh_MultaPesadaMotoNoPatioEDiaPerdido()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Alugada, false), Rico());
            session.SortearBlitzDoDia();
            Order order = Aceitar(session);
            Money saldo = session.Wallet.Balance;

            AbordagemOutcome outcome = session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);

            Assert.IsTrue(outcome.MotoNoPatio);
            Assert.AreEqual(MotoStatus.NoPatio, session.MotoStatus);
            Assert.AreEqual(saldo - Money.Reais(880.41m), session.Wallet.Balance);
            Assert.IsFalse(session.IsShiftActive);
            Assert.Throws<InvalidOperationException>(() => session.FinishRun(order, RunReport.OnTime(1)));
            Assert.DoesNotThrow(() => session.EndDay());
        }

        [Test]
        public void Blitz_BobComCnh_ApreendidaSemCobrarALicenciamento()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Bob), Rico());
            session.SortearBlitzDoDia();
            Aceitar(session);
            Money saldo = session.Wallet.Balance;

            AbordagemOutcome outcome = session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);

            Assert.IsTrue(outcome.MotoApreendida);
            Assert.AreEqual(MotoStatus.Apreendida, session.MotoStatus);
            // a multa é da moto, que não tem dono: não cobra e não pontua
            Assert.AreEqual(saldo, session.Wallet.Balance);
            Assert.AreEqual(0, session.Cnh.Pontos);
            Assert.IsNull(session.Rental);
        }

        [Test]
        public void Blitz_BobSemCnh_PagaPorDirigirSemCnhEPerdeAMoto()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Bob, false), Rico());
            session.SortearBlitzDoDia();
            Aceitar(session);
            Money saldo = session.Wallet.Balance;

            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);

            Assert.AreEqual(MotoStatus.Apreendida, session.MotoStatus);
            Assert.AreEqual(saldo - Money.Reais(880.41m), session.Wallet.Balance);
        }

        [Test]
        public void CnhSuspensa_ContaComoSemCnhNaProximaAbordagem()
        {
            // flagrante de excesso acima de 50% com a parada: suspende o direito de dirigir
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.0), null, Rico());
            Aceitar(session);

            session.ResolverAbordagem(session.ReportarFlagrante(Infracao.ExcessoVelocidadeAcima50), AbordagemChoice.Parar);

            Assert.IsTrue(session.Cnh.Suspensa);
            Assert.AreEqual(7, session.Cnh.Pontos);

            Abordagem proxima = session.ReportarFlagrante(Infracao.Contramao);
            Assert.IsFalse(proxima.CnhValida);
        }

        // ---------- fuga ----------

        [Test]
        public void Fuga_Escapou_NadaAcontece_AlugadaPodeReceberAvisoDepois()
        {
            // blitz, km, escapa, placa anotada, chegada (0,0 -> 2 dias)
            GameSession session = NewSession(new FakeRandom(0.5, 0.1, 0.5, 0.1, 0.1, 0.0), null, Rico());
            session.SortearBlitzDoDia();
            Order order = Aceitar(session);
            Money saldo = session.Wallet.Balance;

            AbordagemOutcome outcome = session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Fugir);

            Assert.IsTrue(outcome.Escapou);
            Assert.AreEqual(saldo, session.Wallet.Balance);
            Assert.AreEqual(MotoStatus.Ok, session.MotoStatus);
            Assert.AreEqual(1, session.MultasPendentes.Count);
            Assert.AreEqual(Infracao.FugaDeAbordagem, session.MultasPendentes[0].Infracao);
            Assert.IsNotNull(session.FinishRun(order, RunReport.OnTime(order.DistanceKm)));
        }

        [Test]
        public void Fuga_Escapou_Bob_NuncaRecebeAviso()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1, 0.5, 0.1), Perfil(MotoOrigem.Bob), Rico());
            session.SortearBlitzDoDia();
            Aceitar(session);

            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Fugir);

            Assert.AreEqual(0, session.MultasPendentes.Count);
            Assert.AreEqual(MotoStatus.Ok, session.MotoStatus);
        }

        [Test]
        public void Fuga_Pego_AlugadaComCnh_FugaMaisPatioECancelaAEntrega()
        {
            // blitz, km, não escapa
            GameSession session = NewSession(new FakeRandom(0.5, 0.1, 0.5, 0.9), null, Rico());
            session.SortearBlitzDoDia();
            Order order = Aceitar(session);
            Money saldo = session.Wallet.Balance;

            AbordagemOutcome outcome = session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Fugir);

            Assert.AreEqual(60, outcome.DelayMinutes);
            Assert.AreEqual(MotoStatus.NoPatio, session.MotoStatus);
            Assert.AreEqual(saldo - Money.Reais(293.47m), session.Wallet.Balance);
            Assert.AreEqual(7, session.Cnh.Pontos);
            Assert.AreEqual(0, session.App.Accepted);
            Assert.IsFalse(session.IsShiftActive);
        }

        // ---------- apreensão ----------

        [Test]
        public void Apreensao_CancelaEntregaEAsParcelasRestantes()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Bob), Rico());
            session.SortearBlitzDoDia();
            Aceitar(session);

            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);

            Assert.AreEqual(0, session.App.Accepted);
            Assert.AreEqual(0, session.ParcelasRestantes);
            Assert.AreEqual(1, session.Abordagens.Count);
            Assert.AreEqual(AbordagemDesfecho.MotoApreendida, session.Abordagens[0].Desfecho);

            session.EndDay();
            Assert.IsFalse(session.StartShift());
        }

        [Test]
        public void Apreensao_SemanaTerminaSemMoto_ESemParcela()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Bob), Rico());
            session.SortearBlitzDoDia();
            Aceitar(session);
            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);

            for (int day = 1; day <= 7; day++)
            {
                PassarDia(session);
            }

            WeekReport report = session.EndWeek();

            Assert.AreEqual(WeekResult.SemMoto, report.Result);
            Assert.AreEqual(Money.Zero, report.Rent);
            Assert.IsFalse(session.Wallet.Entries.Any(e => e.Category == LedgerCategory.Parcela));
            Assert.AreEqual(MotoStatus.Apreendida, report.Transito.MotoStatus);
            Assert.AreEqual(1, report.Transito.Blitz);
        }

        [Test]
        public void NovaMoto_AposApreensao_AlugaComCaucao()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Bob), Rico());
            session.SortearBlitzDoDia();
            Aceitar(session);
            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);
            session.EndDay();
            Money saldo = session.Wallet.Balance;

            Assert.IsTrue(session.PodeEscolherNovaMoto(MotoOrigem.Alugada));
            Assert.IsTrue(session.EscolherNovaMoto(MotoOrigem.Alugada));

            Assert.AreEqual(MotoOrigem.Alugada, session.Origem);
            Assert.IsNotNull(session.Rental);
            Assert.AreEqual(saldo - Money.Reais(100), session.Wallet.Balance);
            Assert.IsTrue(session.StartShift());
        }

        [Test]
        public void NovaMoto_OutraBob_ReiniciaAsParcelas()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Bob), Rico());
            session.SortearBlitzDoDia();
            Aceitar(session);
            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);
            session.EndDay();

            Assert.IsTrue(session.EscolherNovaMoto(MotoOrigem.Bob));

            Assert.AreEqual(8, session.ParcelasRestantes);
            Assert.IsNull(session.Rental);
        }

        [Test]
        public void NovaMoto_SemDinheiro_NaoDaParaEscolher()
        {
            // começa com R$ 50 depois da entrada da bob: não paga caução (100) nem outra entrada (150)
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Bob));
            session.SortearBlitzDoDia();
            Aceitar(session);
            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);
            session.EndDay();

            Assert.IsFalse(session.PodeEscolherNovaMoto(MotoOrigem.Alugada));
            Assert.IsFalse(session.EscolherNovaMoto(MotoOrigem.Alugada));
            Assert.IsFalse(session.EscolherNovaMoto(MotoOrigem.Bob));
            Assert.AreEqual(MotoStatus.Apreendida, session.MotoStatus);
        }

        [Test]
        public void NovaMoto_ComMotoOk_Lanca()
        {
            GameSession session = NewSession(new FakeRandom());

            Assert.Throws<InvalidOperationException>(() => session.EscolherNovaMoto(MotoOrigem.Bob));
        }

        // ---------- pátio ----------

        [Test]
        public void Patio_RetiradaCustaGuinchoMaisDiarias()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Alugada, false), Rico());
            session.SortearBlitzDoDia();
            Aceitar(session);
            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);

            // no dia da apreensão não dá para retirar
            Assert.Throws<InvalidOperationException>(() => session.RetirarDoPatio());

            session.EndDay();
            Assert.AreEqual(Money.Reais(225), session.CustoPatio);

            session.TakeDayOff();
            session.EndDay();
            Assert.AreEqual(Money.Reais(270), session.CustoPatio);
        }

        [Test]
        public void Patio_RetirarPerdeODiaMasDevolveAMoto()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Alugada, false), Rico());
            session.SortearBlitzDoDia();
            Aceitar(session);
            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);
            session.EndDay();
            Money saldo = session.Wallet.Balance;

            Assert.IsTrue(session.RetirarDoPatio());

            Assert.AreEqual(saldo - Money.Reais(225), session.Wallet.Balance);
            Assert.AreEqual(MotoStatus.Ok, session.MotoStatus);
            Assert.AreEqual(LedgerCategory.Patio, session.Wallet.Entries.Last().Category);
            // o dia da retirada também se perde
            Assert.Throws<InvalidOperationException>(() => session.StartShift());

            session.EndDay();
            Assert.IsTrue(session.StartShift());
        }

        [Test]
        public void Patio_SemDinheiro_NaoRetira()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Alugada, false));
            session.SortearBlitzDoDia();
            Aceitar(session);
            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);
            session.EndDay();

            Assert.IsFalse(session.RetirarDoPatio());
            Assert.AreEqual(MotoStatus.NoPatio, session.MotoStatus);
            Assert.IsFalse(session.StartShift());
        }

        [Test]
        public void Patio_NoFimDaSemana_SemMotoEDivida()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1), Perfil(MotoOrigem.Alugada, false));
            session.SortearBlitzDoDia();
            Aceitar(session);
            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);

            for (int day = 1; day <= 7; day++)
            {
                PassarDia(session);
            }

            WeekReport report = session.EndWeek();

            Assert.AreEqual(WeekResult.SemMoto, report.Result);
            Assert.AreEqual(MotoStatus.NoPatio, report.Transito.MotoStatus);
            Assert.AreEqual(Money.Reais(180 + 45 * 7), report.Transito.PatioDevido);
            Assert.AreEqual(CnhStatus.SemCnh, report.Transito.Cnh);
        }

        // ---------- misc ----------

        [Test]
        public void Abordagem_EmAndamento_ImpedeFecharODia()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1));
            session.SortearBlitzDoDia();
            session.StartShift();
            session.IniciarBlitz();

            Assert.Throws<InvalidOperationException>(() => session.EndDay());
            Assert.Throws<InvalidOperationException>(() => session.IniciarBlitz());
        }

        [Test]
        public void Flagrante_ParandoNaMotoAlugada_PagaAMultaNaHora()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.5, 0.0), null, Rico());
            Aceitar(session);
            Money saldo = session.Wallet.Balance;

            session.ResolverAbordagem(session.ReportarFlagrante(Infracao.AvancarSinalVermelho), AbordagemChoice.Parar);

            Assert.AreEqual(saldo - Money.Reais(293.47m), session.Wallet.Balance);
            Assert.AreEqual(7, session.Cnh.Pontos);
            Assert.AreEqual(AbordagemMotivo.Flagrante, session.Abordagens[0].Motivo);
        }

        [Test]
        public void Flagrante_NaBob_PagaInfracaoDoCondutorEPerdeAMoto()
        {
            GameSession session = NewSession(new FakeRandom(), Perfil(MotoOrigem.Bob), Rico());
            Aceitar(session);
            Money saldo = session.Wallet.Balance;

            session.ResolverAbordagem(session.ReportarFlagrante(Infracao.Contramao), AbordagemChoice.Parar);

            Assert.AreEqual(saldo - Money.Reais(293.47m), session.Wallet.Balance);
            Assert.AreEqual(MotoStatus.Apreendida, session.MotoStatus);
        }

        [Test]
        public void Relatorio_ResumeMultasPontosMotoEBlitz()
        {
            GameSession session = NewSession(new FakeRandom(0.5, 0.1, 0.5, 0.0, 0.1), null, Rico());
            session.SortearBlitzDoDia();
            Order order = Aceitar(session);
            session.ResolverAbordagem(session.IniciarBlitz(), AbordagemChoice.Parar);
            session.FinishRun(order, RunReport.OnTime(order.DistanceKm));

            for (int day = 1; day <= 7; day++)
            {
                PassarDia(session);
            }

            TransitoReport transito = session.EndWeek().Transito;

            Assert.AreEqual(Money.Reais(195.23m), transito.MultasPagas);
            Assert.AreEqual(5, transito.PontosCnh);
            Assert.AreEqual(CnhStatus.Valida, transito.Cnh);
            Assert.AreEqual(MotoStatus.Ok, transito.MotoStatus);
            Assert.AreEqual(MotoOrigem.Alugada, transito.OrigemMoto);
            Assert.AreEqual(1, transito.Blitz);
            Assert.AreEqual(AbordagemDesfecho.LiberadoComMulta, transito.Abordagens[0].Desfecho);
            Assert.AreEqual(0, transito.QuantidadePendentes);
        }
    }
}
