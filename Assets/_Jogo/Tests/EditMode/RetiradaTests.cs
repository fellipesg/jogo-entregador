using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class RetiradaTests
    {
        private static Order Pedido() => new Order(7, 3.5, Money.Reais(15), false);

        private static Retirada Nova(IGameRandom random = null, RetiradaConfig config = null) =>
            new Retirada(Pedido(), "Pastelaria do Zé", random ?? new FakeRandom(0.5), config);

        [Test]
        public void Codigo_Tem4DigitosETresOpcoesDiferentesComUmaCerta()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                Retirada retirada = Nova(new SeededRandom(seed));

                Assert.AreEqual(4, retirada.Codigo.Length);
                Assert.AreEqual(3, retirada.OpcoesCodigo.Count);
                Assert.AreEqual(3, retirada.OpcoesCodigo.Distinct().Count(), "Opções repetidas na semente " + seed);
                Assert.AreEqual(1, retirada.OpcoesCodigo.Count(c => c == retirada.Codigo));
                Assert.IsTrue(retirada.OpcoesCodigo.All(c => c.Length == 4 && c[0] != '0'));
            }
        }

        [Test]
        public void Codigo_ComSorteConstanteAindaGeraTresDiferentes()
        {
            foreach (double v in new[] { 0.0, 0.5, 0.999 })
            {
                Retirada retirada = Nova(new FakeRandom(v));

                Assert.AreEqual(3, retirada.OpcoesCodigo.Distinct().Count(), "valor " + v);
            }
        }

        [Test]
        public void Sorteio_RespeitaOsPesos()
        {
            var counts = new Dictionary<BalcaoSituacao, int>();
            var random = new SeededRandom(11);

            for (int i = 0; i < 5000; i++)
            {
                BalcaoSituacao s = Retirada.Sortear(random);
                counts[s] = counts.ContainsKey(s) ? counts[s] + 1 : 1;
            }

            Assert.AreEqual(5, counts.Count, "Todas as situações aparecem.");
            Assert.Greater(counts[BalcaoSituacao.PedidoPronto], counts[BalcaoSituacao.SaindoJaJa]);
            Assert.Greater(counts[BalcaoSituacao.SaindoJaJa], counts[BalcaoSituacao.Lotado]);
            Assert.AreEqual(0.40, counts[BalcaoSituacao.PedidoPronto] / 5000.0, 0.03);
        }

        [Test]
        public void Sorteio_ConfigurandoUmPesoSoTodasSaemIguais()
        {
            var config = new RetiradaConfig { PesoPronto = 0, PesoSaindo = 0, PesoCodigo = 0, PesoTrocado = 1, PesoLotado = 0 };
            var random = new SeededRandom(3);

            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual(BalcaoSituacao.PedidoTrocado, Retirada.Sortear(random, config));
            }
        }

        [Test]
        public void PedidoPronto_UmBotaoEPegaSemPerderTempo()
        {
            Retirada retirada = Nova();
            retirada.Iniciar(BalcaoSituacao.PedidoPronto);

            Assert.AreEqual(1, retirada.Opcoes.Count);
            Assert.AreEqual(BalcaoAcao.PegarPedido, retirada.Opcoes[0].Acao);
            StringAssert.Contains("prontinho", retirada.Fala);

            retirada.Escolher(BalcaoAcao.PegarPedido);

            Assert.IsTrue(retirada.Concluida);
            Assert.AreEqual(0, retirada.MinutosPerdidos);
            Assert.AreEqual(0, retirada.PenalidadeEstrelas);
        }

        [Test]
        public void SaindoJaJa_EsperaConsomeOsMinutosDoPreparo()
        {
            var config = new RetiradaConfig { EsperaMin = 1, EsperaMax = 8 };
            Retirada retirada = Nova(new FakeRandom(0.99), config);
            retirada.Iniciar(BalcaoSituacao.SaindoJaJa);

            Assert.AreEqual(8, retirada.EsperaMinutos);
            CollectionAssert.AreEqual(new[] { BalcaoAcao.Esperar, BalcaoAcao.Reclamar }, retirada.Opcoes.Select(o => o.Acao).ToArray());
            StringAssert.Contains("8 min", retirada.Fala);

            retirada.Escolher(BalcaoAcao.Esperar);
            Assert.AreEqual(RetiradaPasso.Espera, retirada.Passo);
            Assert.AreEqual(0, retirada.Opcoes.Count, "Durante a espera não há botões.");
            Assert.Throws<System.InvalidOperationException>(() => retirada.Escolher(BalcaoAcao.PegarPedido));

            retirada.ConcluirEspera();
            Assert.AreEqual(8, retirada.MinutosPerdidos);
            Assert.AreEqual(RetiradaPasso.Entrega, retirada.Passo);

            retirada.Escolher(BalcaoAcao.PegarPedido);
            Assert.IsTrue(retirada.Concluida);
        }

        [Test]
        public void EsperaSorteadaFicaNoIntervalo()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                Retirada retirada = Nova(new SeededRandom(seed));
                retirada.Iniciar(BalcaoSituacao.SaindoJaJa);
                Assert.That(retirada.EsperaMinutos, Is.InRange(3, 15), "Cozinha de verdade: 3 a 15 min de jogo.");

                Retirada fila = Nova(new SeededRandom(seed));
                fila.Iniciar(BalcaoSituacao.Lotado);
                Assert.That(fila.EsperaMinutos, Is.InRange(2, 10), "Fila: 2 a 10 min de jogo.");
                StringAssert.Contains("lotado", fila.Fala);
            }
        }

        [Test]
        public void Reclamar_CancelaSemPerderTempo()
        {
            Retirada retirada = Nova();
            retirada.Iniciar(BalcaoSituacao.Lotado);
            retirada.Escolher(BalcaoAcao.Reclamar);

            Assert.IsTrue(retirada.Cancelada);
            Assert.IsTrue(retirada.Encerrada);
            Assert.IsFalse(retirada.Concluida);
        }

        [Test]
        public void PedeCodigo_CodigoCertoLiberaOPedido()
        {
            Retirada retirada = Nova();
            retirada.Iniciar(BalcaoSituacao.PedeCodigo);

            Assert.AreEqual(3, retirada.Opcoes.Count);
            Assert.IsTrue(retirada.Opcoes.All(o => o.Acao == BalcaoAcao.InformarCodigo));
            CollectionAssert.AreEquivalent(retirada.OpcoesCodigo, retirada.Opcoes.Select(o => o.Rotulo).ToArray());

            int certo = retirada.Opcoes.First(o => o.Rotulo == retirada.Codigo).Indice;
            retirada.Escolher(BalcaoAcao.InformarCodigo, certo);

            Assert.AreEqual(RetiradaPasso.Entrega, retirada.Passo);
            Assert.AreEqual(0, retirada.ErrosCodigo);
            Assert.AreEqual(0, retirada.MinutosPerdidos);
            retirada.Escolher(BalcaoAcao.PegarPedido);
            Assert.IsTrue(retirada.Concluida);
        }

        [Test]
        public void PedeCodigo_CodigoErradoCustaUmMinutoERetiraAOpcao()
        {
            Retirada retirada = Nova();
            retirada.Iniciar(BalcaoSituacao.PedeCodigo);
            int errado = retirada.Opcoes.First(o => o.Rotulo != retirada.Codigo).Indice;

            retirada.Escolher(BalcaoAcao.InformarCodigo, errado);

            Assert.AreEqual(RetiradaPasso.Atendimento, retirada.Passo);
            Assert.AreEqual(1, retirada.ErrosCodigo);
            Assert.AreEqual(1, retirada.MinutosPerdidos);
            Assert.AreEqual(2, retirada.Opcoes.Count);
            Assert.IsFalse(retirada.Opcoes.Any(o => o.Indice == errado), "A opção errada some.");
            Assert.Throws<System.InvalidOperationException>(() => retirada.Escolher(BalcaoAcao.InformarCodigo, errado));
        }

        [Test]
        public void PedidoTrocado_ConferirCustaDoisMinutosEEvitaAReclamacao()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                Retirada retirada = Nova(new SeededRandom(seed));
                retirada.Iniciar(BalcaoSituacao.PedidoTrocado);

                CollectionAssert.AreEqual(new[] { BalcaoAcao.Conferir, BalcaoAcao.PegarESair }, retirada.Opcoes.Select(o => o.Acao).ToArray());
                retirada.Escolher(BalcaoAcao.Conferir);

                Assert.IsTrue(retirada.Conferiu);
                Assert.AreEqual(2, retirada.MinutosPerdidos);
                StringAssert.Contains(retirada.ItemFaltando, retirada.Fala);
                retirada.Escolher(BalcaoAcao.PegarPedido);
                Assert.IsTrue(retirada.Concluida);
                Assert.AreEqual(0, retirada.PenalidadeEstrelas);
            }
        }

        [Test]
        public void PedidoTrocado_PegarESairTemPenalidadeDeUmADoisEstrelas()
        {
            var config = new RetiradaConfig { ChanceReclamacao = 1.0 };
            var seen = new HashSet<int>();

            for (int seed = 0; seed < 100; seed++)
            {
                Retirada retirada = Nova(new SeededRandom(seed), config);
                retirada.Iniciar(BalcaoSituacao.PedidoTrocado);
                retirada.Escolher(BalcaoAcao.PegarESair);

                Assert.IsTrue(retirada.Concluida);
                Assert.AreEqual(0, retirada.MinutosPerdidos, "Pegar e sair não gasta tempo.");
                Assert.That(retirada.PenalidadeEstrelas, Is.InRange(1, 2));
                seen.Add(retirada.PenalidadeEstrelas);
            }

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, seen);
        }

        [Test]
        public void PedidoTrocado_PegarESairNemSempreGeraReclamacao()
        {
            var config = new RetiradaConfig { ChanceReclamacao = 0.0 };
            Retirada retirada = Nova(new SeededRandom(1), config);
            retirada.Iniciar(BalcaoSituacao.PedidoTrocado);
            retirada.Escolher(BalcaoAcao.PegarESair);

            Assert.AreEqual(0, retirada.PenalidadeEstrelas);
        }

        [Test]
        public void EscolhaForaDeOrdemOuIndisponivelLancaErro()
        {
            Retirada retirada = Nova();
            Assert.Throws<System.InvalidOperationException>(() => retirada.Escolher(BalcaoAcao.PegarPedido), "Antes de iniciar.");

            retirada.Iniciar(BalcaoSituacao.PedidoPronto);
            Assert.Throws<System.InvalidOperationException>(() => retirada.Escolher(BalcaoAcao.Conferir));
            Assert.Throws<System.InvalidOperationException>(() => retirada.Iniciar(BalcaoSituacao.Lotado), "Só inicia uma vez.");
        }

        [Test]
        public void Penalidade_TiraEstrelasDaEntrega()
        {
            Assert.AreEqual(5, RatingRules.Stars(RunReport.OnTime(3), false, false));
            Assert.AreEqual(4, RatingRules.Stars(new RunReport { PenalidadeRetirada = 1 }, false, false));
            Assert.AreEqual(3, RatingRules.Stars(new RunReport { PenalidadeRetirada = 2 }, false, false));
            Assert.AreEqual(1, RatingRules.Stars(new RunReport { PenalidadeRetirada = 2, TimeRatio = 2 }, false, false), "Nunca abaixo de 1.");
        }

        [Test]
        public void Sessao_PedidoTrocadoSemConferirDerrubaANotaDaEntrega()
        {
            var session = new GameSession(new FakeRandom(0.5), null, null, new bool[7]);
            Assert.IsTrue(session.StartShift());
            Order order = session.NextOffer();
            session.Accept(order);

            var retirada = new Retirada(order, "Pastelaria", new FakeRandom(0.0), new RetiradaConfig { ChanceReclamacao = 1.0 });
            retirada.Iniciar(BalcaoSituacao.PedidoTrocado);
            retirada.Escolher(BalcaoAcao.PegarESair);

            RunReport report = RunReport.OnTime(order.DistanceKm);
            report.PenalidadeRetirada = retirada.PenalidadeEstrelas;
            report.MinutosNoBalcao = retirada.MinutosPerdidos;
            DeliveryResult result = session.FinishRun(order, report);

            Assert.AreEqual(5 - retirada.PenalidadeEstrelas, result.Stars);
        }

        [Test]
        public void Sessao_ReclamarNoAppCancelaEContaComoRecusa()
        {
            var session = new GameSession(new FakeRandom(0.5), null, null, new bool[7]);
            Assert.IsTrue(session.StartShift());
            Order order = session.NextOffer();
            session.Accept(order);
            Assert.AreEqual(1.0, session.App.AcceptanceRate, 0.0001);
            Money before = session.Wallet.Balance;

            session.CancelarNaRetirada(order);

            Assert.AreEqual(0.0, session.App.AcceptanceRate, 0.0001);
            Assert.AreEqual(before, session.Wallet.Balance, "Sem pagamento e sem combustível.");
            Assert.Throws<System.InvalidOperationException>(() => session.FinishRun(order, RunReport.OnTime(1)), "A entrega acabou.");
        }
    }
}
