using System.Linq;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class EntregaTests
    {
        private static readonly Order Pedido = new Order(7, 3.5, Money.Reais(15), false);

        private static Entrega Nova(EntregaSituacao situacao, IGameRandom random = null, EntregaConfig config = null)
        {
            var entrega = new Entrega(Pedido, "Rua das Flores, 120", random ?? new FakeRandom(0.5), config);
            entrega.Iniciar(situacao);

            return entrega;
        }

        private static bool Tem(Entrega e, EntregaAcao acao) => e.Opcoes.Any(o => o.Acao == acao);

        private static void EsperaTudo(Entrega e)
        {
            Assert.AreEqual(EntregaPasso.Espera, e.Passo);
            e.ConcluirEspera();
        }

        [Test]
        public void ClienteDesce_EsperaDe2a5MinutosETermina()
        {
            for (int i = 0; i < 50; i++)
            {
                Entrega e = Nova(EntregaSituacao.ClienteDesce, new SeededRandom(i));

                Assert.AreEqual(EntregaPasso.Espera, e.Passo, "O cliente descendo é uma espera de verdade.");
                Assert.That(e.EsperaMinutos, Is.InRange(2, 5));
                Assert.IsEmpty(e.Opcoes);
            }
        }

        [Test]
        public void ClienteDesce_DepoisDaEsperaEntregaEConclui()
        {
            Entrega e = Nova(EntregaSituacao.ClienteDesce);
            int minutos = e.EsperaMinutos;
            e.ConcluirEspera();

            Assert.AreEqual(EntregaPasso.Atendimento, e.Passo);
            Assert.IsTrue(Tem(e, EntregaAcao.Entregar));
            Assert.AreEqual(minutos, e.MinutosPerdidos);

            e.Escolher(EntregaAcao.Entregar);

            Assert.IsTrue(e.Concluida);
            Assert.AreEqual(0, e.PenalidadeEstrelas);
            Assert.AreEqual(1.0, e.FatorGorjeta);
            Assert.AreEqual(0.0, e.RiscoNaoRecebeu);
        }

        [Test]
        public void FalaDaEspera_MudaNoComecoMeioEFim()
        {
            Entrega e = Nova(EntregaSituacao.ClienteDesce);
            string a = e.FalaEspera(0.1);
            string b = e.FalaEspera(0.5);
            string c = e.FalaEspera(0.9);

            Assert.AreNotEqual(a, b);
            Assert.AreNotEqual(b, c);
            Assert.AreEqual(a, e.Fala);
        }

        [Test]
        public void Foto_AparecePorPadraoEDesapareceDepoisDeTirada()
        {
            Entrega e = Nova(EntregaSituacao.ClienteDesce);
            e.ConcluirEspera();

            Assert.IsTrue(Tem(e, EntregaAcao.Fotografar));

            e.Escolher(EntregaAcao.Fotografar);

            Assert.IsTrue(e.FotoTirada);
            Assert.IsFalse(Tem(e, EntregaAcao.Fotografar));
            Assert.IsFalse(e.Concluida);
        }

        [Test]
        public void Portaria_DeixarComOPorteiroLevaUnsMinutosEPesaNoRiscoENaGorjeta()
        {
            Entrega e = Nova(EntregaSituacao.Portaria);

            Assert.AreEqual("Porteiro", e.Interlocutor);
            Assert.IsTrue(Tem(e, EntregaAcao.DeixarNaPortaria));
            Assert.IsTrue(Tem(e, EntregaAcao.ChamarOCliente));

            e.Escolher(EntregaAcao.DeixarNaPortaria);

            Assert.That(e.EsperaMinutos, Is.InRange(1, 2));
            EsperaTudo(e);

            Assert.IsTrue(e.Concluida);
            Assert.IsTrue(e.DeixouNaPortaria);
            Assert.Less(e.FatorGorjeta, 1.0);
            Assert.AreEqual(0.25, e.RiscoNaoRecebeu, 1e-9);
        }

        [Test]
        public void Portaria_ComFotoORiscoDeNaoRecebeuCai()
        {
            Entrega e = Nova(EntregaSituacao.Portaria);
            e.Escolher(EntregaAcao.Fotografar);
            e.Escolher(EntregaAcao.DeixarNaPortaria);
            EsperaTudo(e);

            Assert.AreEqual(0.08, e.RiscoNaoRecebeu, 1e-9);
        }

        [Test]
        public void Portaria_PedirOClienteDescerEsperaDe2a5Min()
        {
            Entrega e = Nova(EntregaSituacao.Portaria);
            e.Escolher(EntregaAcao.ChamarOCliente);

            Assert.That(e.EsperaMinutos, Is.InRange(2, 5));
            EsperaTudo(e);
            Assert.IsTrue(Tem(e, EntregaAcao.Entregar));
            Assert.IsFalse(e.DeixouNaPortaria);
        }

        [Test]
        public void ClienteDemora_EsperaDe3a8Min()
        {
            for (int i = 0; i < 40; i++)
            {
                Entrega e = Nova(EntregaSituacao.ClienteDemora, new SeededRandom(i));

                Assert.That(e.EsperaMinutos, Is.InRange(3, 8));
                Assert.IsTrue(Tem(e, EntregaAcao.Esperar));
                Assert.IsTrue(Tem(e, EntregaAcao.LigarNoApp));
                Assert.IsTrue(Tem(e, EntregaAcao.DeixarNaPortaria));
            }
        }

        [Test]
        public void ClienteDemora_LigarQuePegaEncurtaAEspera()
        {
            // 0,5 de sorte: o duas primeiras faixas definem os minutos; depois a ligação atende (0,5 < 0,6).
            Entrega e = Nova(EntregaSituacao.ClienteDemora, new FakeRandom(0.5));
            int total = e.EsperaMinutos;
            e.Escolher(EntregaAcao.LigarNoApp);

            Assert.AreEqual(EntregaPasso.Espera, e.Passo);
            Assert.Less(e.EsperaMinutos, total);
            Assert.AreEqual(1, e.MinutosPerdidos, "A ligação custa 1 min.");
        }

        [Test]
        public void ClienteDemora_LigarQueNaoPegaVoltaAsOpcoesSemLigar()
        {
            var random = new FakeRandom(0.5);
            Entrega e = Nova(EntregaSituacao.ClienteDemora, random);
            random.Enqueue(0.99);
            e.Escolher(EntregaAcao.LigarNoApp);

            Assert.AreEqual(EntregaPasso.Atendimento, e.Passo);
            Assert.IsFalse(Tem(e, EntregaAcao.LigarNoApp));
            StringAssert.Contains("caixa postal", e.Fala);
        }

        [Test]
        public void ClienteDemora_DeixarNaPortariaZeraAGorjetaECustaEstrelaAsVezes()
        {
            var ruim = new FakeRandom(0.5);
            Entrega e = Nova(EntregaSituacao.ClienteDemora, ruim);
            ruim.Enqueue(0.1);
            e.Escolher(EntregaAcao.DeixarNaPortaria);

            Assert.IsTrue(e.Concluida);
            Assert.AreEqual(0.0, e.FatorGorjeta);
            Assert.AreEqual(1, e.PenalidadeEstrelas);

            var boa = new FakeRandom(0.5);
            Entrega f = Nova(EntregaSituacao.ClienteDemora, boa);
            boa.Enqueue(0.9);
            f.Escolher(EntregaAcao.DeixarNaPortaria);

            Assert.AreEqual(0, f.PenalidadeEstrelas);
        }

        [Test]
        public void Interfone_TresCaminhosELigarFalhaTiraAOpcao()
        {
            var random = new FakeRandom(0.5);
            Entrega e = Nova(EntregaSituacao.InterfoneQuebrado, random);

            Assert.IsTrue(Tem(e, EntregaAcao.LigarNoApp));
            Assert.IsTrue(Tem(e, EntregaAcao.BaterPalmas));
            Assert.IsTrue(Tem(e, EntregaAcao.EsperarMorador));

            random.Enqueue(0.95);
            e.Escolher(EntregaAcao.LigarNoApp);

            Assert.IsFalse(Tem(e, EntregaAcao.LigarNoApp));
            Assert.AreEqual(1, e.MinutosPerdidos);
        }

        [Test]
        public void Interfone_EsperarAlguemAbrirLevaDe2a4Min()
        {
            Entrega e = Nova(EntregaSituacao.InterfoneQuebrado, new SeededRandom(5));
            e.Escolher(EntregaAcao.EsperarMorador);

            Assert.That(e.EsperaMinutos, Is.InRange(2, 4));
            EsperaTudo(e);
            Assert.IsTrue(Tem(e, EntregaAcao.Entregar));
        }

        [Test]
        public void Subir_AceitarLevaDe4a8MinEPagaMelhor()
        {
            Entrega e = Nova(EntregaSituacao.PedeParaSubir);
            e.Escolher(EntregaAcao.SubirAteOApartamento);

            Assert.That(e.EsperaMinutos, Is.InRange(4, 8));
            int minutos = e.EsperaMinutos;
            EsperaTudo(e);

            Assert.IsTrue(e.Concluida);
            Assert.AreEqual(minutos, e.MinutosPerdidos);
            Assert.Greater(e.FatorGorjeta, 1.0);
            Assert.AreEqual(0, e.PenalidadeEstrelas);
        }

        [Test]
        public void Subir_RecusarAbreUmaDiscussaoDe1a2Min()
        {
            Entrega e = Nova(EntregaSituacao.PedeParaSubir);
            e.Escolher(EntregaAcao.RecusarSubir);

            Assert.AreEqual(EntregaPasso.Atendimento, e.Passo);
            Assert.That(e.MinutosPerdidos, Is.InRange(1, 2));
            Assert.IsTrue(Tem(e, EntregaAcao.MantemPosicao));
            Assert.IsTrue(Tem(e, EntregaAcao.CederESubir));
            Assert.IsFalse(e.Concluida);
        }

        [Test]
        public void Subir_DiscussaoMantendoAPosicao_TresDesfechos()
        {
            // Cliente desce bufando (sorte baixa): espera, gorjeta quase zero, sem penalidade.
            var r1 = new FakeRandom(0.5);
            Entrega desce = Nova(EntregaSituacao.PedeParaSubir, r1);
            desce.Escolher(EntregaAcao.RecusarSubir);
            r1.Enqueue(0.1);
            desce.Escolher(EntregaAcao.MantemPosicao);

            Assert.AreEqual(EntregaPasso.Espera, desce.Passo);
            Assert.AreEqual(0.3, desce.FatorGorjeta, 1e-9);
            desce.ConcluirEspera();
            Assert.IsTrue(desce.Concluida);

            // Reclama no app: estrela a menos e sem gorjeta.
            var r2 = new FakeRandom(0.5);
            Entrega reclama = Nova(EntregaSituacao.PedeParaSubir, r2);
            reclama.Escolher(EntregaAcao.RecusarSubir);
            r2.Enqueue(0.5);
            reclama.Escolher(EntregaAcao.MantemPosicao);

            Assert.IsTrue(reclama.Concluida);
            Assert.AreEqual(1, reclama.PenalidadeEstrelas);
            Assert.AreEqual(0.0, reclama.FatorGorjeta);

            // Fica por isso mesmo: desce sem espera.
            var r3 = new FakeRandom(0.5);
            Entrega paz = Nova(EntregaSituacao.PedeParaSubir, r3);
            paz.Escolher(EntregaAcao.RecusarSubir);
            r3.Enqueue(0.95);
            paz.Escolher(EntregaAcao.MantemPosicao);

            Assert.IsTrue(paz.Concluida);
            Assert.AreEqual(0, paz.PenalidadeEstrelas);
        }

        [Test]
        public void Subir_DiscussaoCedendoSobeSemPenalidade()
        {
            Entrega e = Nova(EntregaSituacao.PedeParaSubir);
            e.Escolher(EntregaAcao.RecusarSubir);
            e.Escolher(EntregaAcao.CederESubir);

            Assert.AreEqual(EntregaPasso.Espera, e.Passo);
            Assert.That(e.EsperaMinutos, Is.InRange(4, 8));
            e.ConcluirEspera();
            Assert.IsTrue(e.Concluida);
            Assert.AreEqual(0, e.PenalidadeEstrelas);
        }

        [Test]
        public void Codigo_ClienteEsqueceEProcuraDe1a3Min()
        {
            for (int i = 0; i < 40; i++)
            {
                Entrega e = Nova(EntregaSituacao.CodigoEntrega, new SeededRandom(i));

                Assert.That(e.EsperaMinutos, Is.InRange(1, 3));
                Assert.IsTrue(Tem(e, EntregaAcao.Esperar));
                Assert.IsTrue(Tem(e, EntregaAcao.LigarNoApp));
            }
        }

        [Test]
        public void Codigo_DepoisDeEsperarEscolheOCertoEntreTres()
        {
            Entrega e = Nova(EntregaSituacao.CodigoEntrega, new SeededRandom(21));
            e.Escolher(EntregaAcao.Esperar);
            EsperaTudo(e);

            Assert.AreEqual(3, e.Opcoes.Count(o => o.Acao == EntregaAcao.InformarCodigo));
            StringAssert.Contains(e.Codigo, e.Fala);
            CollectionAssert.AllItemsAreUnique(e.OpcoesCodigo);
            Assert.AreEqual(3, e.OpcoesCodigo.Count(c => c.Length == 4));

            int certo = System.Array.IndexOf(e.OpcoesCodigo.ToArray(), e.Codigo);
            int errado = (certo + 1) % 3;
            int antes = e.MinutosPerdidos;

            e.Escolher(EntregaAcao.InformarCodigo, errado);

            Assert.IsFalse(e.Concluida);
            Assert.AreEqual(antes + 1, e.MinutosPerdidos);
            Assert.AreEqual(2, e.Opcoes.Count(o => o.Acao == EntregaAcao.InformarCodigo), "O código errado some da lista.");

            e.Escolher(EntregaAcao.InformarCodigo, certo);

            Assert.IsTrue(e.Concluida);
        }

        [Test]
        public void Codigo_LigarPeloAppCustaUmMinutoEPodeSerMaisRapido()
        {
            var random = new FakeRandom(0.5);
            Entrega e = Nova(EntregaSituacao.CodigoEntrega, random);
            random.Enqueue(0.1);
            e.Escolher(EntregaAcao.LigarNoApp);

            Assert.AreEqual(EntregaPasso.Espera, e.Passo);
            Assert.AreEqual(1, e.EsperaMinutos);
            Assert.AreEqual(1, e.MinutosPerdidos);
        }

        [Test]
        public void Codigo_LigarQueFalhaCustaMaisUmMinuto()
        {
            var random = new FakeRandom(0.5);
            Entrega e = Nova(EntregaSituacao.CodigoEntrega, random);
            random.Enqueue(0.99);
            e.Escolher(EntregaAcao.LigarNoApp);

            Assert.AreEqual(EntregaPasso.Atendimento, e.Passo);
            Assert.AreEqual(2, e.MinutosPerdidos);
            Assert.IsFalse(Tem(e, EntregaAcao.LigarNoApp));
        }

        [Test]
        public void EscolhaForaDaLista_Lanca()
        {
            Entrega e = Nova(EntregaSituacao.Portaria);

            Assert.Throws<System.InvalidOperationException>(() => e.Escolher(EntregaAcao.CederESubir));

            Entrega espera = Nova(EntregaSituacao.ClienteDesce);

            Assert.Throws<System.InvalidOperationException>(() => espera.Escolher(EntregaAcao.Entregar), "Durante a espera não há escolha.");
            Assert.Throws<System.InvalidOperationException>(() => Nova(EntregaSituacao.Portaria).ConcluirEspera());
        }

        [Test]
        public void SorteioRespeitaOsPesos_ClienteDesceDominaEAsOutrasAparecem()
        {
            var random = new SeededRandom(99);
            var contagem = new int[6];

            for (int i = 0; i < 3000; i++)
            {
                contagem[(int)Entrega.Sortear(random)]++;
            }

            Assert.Greater(contagem[(int)EntregaSituacao.ClienteDesce], 1100);
            Assert.Greater(contagem[(int)EntregaSituacao.Portaria], 400);

            foreach (int c in contagem)
            {
                Assert.Greater(c, 100, "Toda situação aparece.");
            }
        }

        [Test]
        public void SituacoesTemFalasVariadas()
        {
            var falas = new System.Collections.Generic.HashSet<string>();

            for (int i = 0; i < 200; i++)
            {
                falas.Add(Nova(EntregaSituacao.Portaria, new SeededRandom(i)).Fala);
            }

            Assert.GreaterOrEqual(falas.Count, 3);
        }

        // ---------- Gorjeta e estrelas na sessão ----------

        private static GameSession Sessao() => new GameSession(new FakeRandom(0.5), null, null, new bool[7]);

        private static DeliveryResult Entrega5Estrelas(GameSession s, RunReport relatorio)
        {
            s.StartShift();
            Order order = s.NextOffer();
            s.Accept(order);

            return s.FinishRun(order, relatorio);
        }

        [Test]
        public void Gorjeta_FatorMaiorPagaMaisMasNuncaPassaDeMaxTip()
        {
            DeliveryResult normal = Entrega5Estrelas(Sessao(), RunReport.OnTime(3));
            DeliveryResult subiu = Entrega5Estrelas(Sessao(), new RunReport { KmDriven = 3, PhotoTaken = true, FatorGorjeta = 1.6 });
            DeliveryResult exagero = Entrega5Estrelas(Sessao(), new RunReport { KmDriven = 3, PhotoTaken = true, FatorGorjeta = 50 });

            Assert.AreEqual(Money.Reais(2.5m), normal.Tip);
            Assert.AreEqual(Money.Reais(4m), subiu.Tip);
            Assert.AreEqual(Money.Reais(5m), exagero.Tip);
        }

        [Test]
        public void Gorjeta_FatorZeroNaoPagaGorjeta()
        {
            DeliveryResult r = Entrega5Estrelas(Sessao(), new RunReport { KmDriven = 3, FatorGorjeta = 0 });

            Assert.AreEqual(Money.Zero, r.Tip);
        }

        [Test]
        public void Estrelas_PenalidadeDaEntregaTiraEstrelas()
        {
            Assert.AreEqual(5, RatingRules.Stars(new RunReport(), false, false));
            Assert.AreEqual(4, RatingRules.Stars(new RunReport { PenalidadeEntrega = 1 }, false, false));
            Assert.AreEqual(1, RatingRules.Stars(new RunReport { PenalidadeEntrega = 9 }, false, false));
        }

        [Test]
        public void RiscoNaoRecebeu_DeixarNaPortariaAumentaAChanceDoImprevisto()
        {
            var incidents = new IncidentSystem();

            // Sorte 0,2: a chance base (5%) não dispara; com +25% dispara.
            Assert.IsNull(incidents.RollAfterDelivery(new FakeRandom(0.2), false));
            Assert.IsNotNull(incidents.RollAfterDelivery(new FakeRandom(0.2), false, 0.25));
        }

        [Test]
        public void Retirada_EsperasComDuracoesReais()
        {
            var config = new RetiradaConfig();

            Assert.AreEqual(3, config.EsperaMin);
            Assert.AreEqual(15, config.EsperaMax);
            Assert.AreEqual(2, config.FilaMin);
            Assert.AreEqual(10, config.FilaMax);
        }

        [Test]
        public void Retirada_FalaDaEsperaMudaAoLongoDaEspera()
        {
            var retirada = new Retirada(Pedido, "Pizzaria", new FakeRandom(0.5));
            retirada.Iniciar(BalcaoSituacao.SaindoJaJa);
            retirada.Escolher(BalcaoAcao.Esperar);

            Assert.AreNotEqual(retirada.FalaEspera(0.1), retirada.FalaEspera(0.5));
            Assert.AreNotEqual(retirada.FalaEspera(0.5), retirada.FalaEspera(0.9));
        }
    }
}
