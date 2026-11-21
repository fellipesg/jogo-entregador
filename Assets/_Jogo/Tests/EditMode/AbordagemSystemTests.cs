using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class AbordagemSystemTests
    {
        private static AbordagemSystem NewSystem() => new AbordagemSystem(new TransitoConfig());

        [Test]
        public void Blitz_ChanceFixaNosDiasUteis()
        {
            AbordagemSystem system = NewSystem();

            Assert.IsTrue(system.RollBlitz(new FakeRandom(0.34), 1));
            Assert.IsFalse(system.RollBlitz(new FakeRandom(0.35), 1));
            Assert.IsFalse(system.RollBlitz(new FakeRandom(0.4), 3));
        }

        [Test]
        public void Blitz_FimDeSemanaTemChanceMaior()
        {
            AbordagemSystem system = NewSystem();

            Assert.IsTrue(system.RollBlitz(new FakeRandom(0.4), 6));
            Assert.IsTrue(system.RollBlitz(new FakeRandom(0.4), 7));
            Assert.IsFalse(system.RollBlitz(new FakeRandom(0.5), 6));
        }

        [Test]
        public void Abordagem_OferecePararEFugir()
        {
            Abordagem abordagem = NewSystem().Iniciar(MotoOrigem.Alugada, true);

            CollectionAssert.AreEqual(new[] { AbordagemChoice.Parar, AbordagemChoice.Fugir }, abordagem.Choices);
            Assert.AreEqual(AbordagemMotivo.Blitz, abordagem.Motivo);
            Assert.AreEqual(AbordagemMotivo.Flagrante, NewSystem().Iniciar(MotoOrigem.Alugada, true, Infracao.Contramao).Motivo);
        }

        [Test]
        public void Parar_AlugadaComCnh_SoAtrasa()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Alugada, true);

            // atraso 0,0 -> 10 min; problema menor 0,9 -> não
            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Parar, new FakeRandom(0.5, 0.0, 0.9));

            Assert.AreEqual(AbordagemDesfecho.Liberado, outcome.Desfecho);
            Assert.AreEqual(10, outcome.DelayMinutes);
            Assert.IsEmpty(outcome.Infracoes);
            Assert.IsFalse(outcome.MotoRetida);
            Assert.IsFalse(outcome.EncerraDia);
        }

        [Test]
        public void Parar_AtrasoVaiDe10a20Minutos()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Alugada, true);

            Assert.AreEqual(20, system.Resolve(abordagem, AbordagemChoice.Parar, new FakeRandom(0.5, 0.999, 0.9)).DelayMinutes);
            Assert.AreEqual(15, system.Resolve(abordagem, AbordagemChoice.Parar, new FakeRandom(0.5, 0.5, 0.9)).DelayMinutes);
        }

        [Test]
        public void Parar_AlugadaComCnh_AsVezesAchamProblemaMenor()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Alugada, true);

            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Parar, new FakeRandom(0.5, 0.0, 0.19));

            Assert.AreEqual(AbordagemDesfecho.LiberadoComMulta, outcome.Desfecho);
            CollectionAssert.AreEqual(new[] { Infracao.EquipamentoIrregular }, outcome.Infracoes);
            Assert.IsFalse(outcome.MotoRetida);
        }

        [Test]
        public void Parar_AlugadaSemCnh_MotoVaiParaOPatio()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Alugada, false);

            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Parar, new FakeRandom());

            CollectionAssert.AreEqual(new[] { Infracao.DirigirSemCnh }, outcome.Infracoes);
            Assert.IsTrue(outcome.MotoNoPatio);
            Assert.IsFalse(outcome.MotoApreendida);
            Assert.IsTrue(outcome.EncerraDia);
            Assert.IsTrue(outcome.CancelsDelivery);
            Assert.AreEqual(AbordagemDesfecho.MotoNoPatio, outcome.Desfecho);
        }

        [Test]
        public void Parar_BobComCnh_MotoApreendida()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Bob, true);

            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Parar, new FakeRandom());

            CollectionAssert.AreEqual(new[] { Infracao.MotoSemLicenciamento }, outcome.Infracoes);
            Assert.IsTrue(outcome.MotoApreendida);
            Assert.IsFalse(outcome.MotoNoPatio);
            Assert.IsTrue(outcome.CancelsDelivery);
        }

        [Test]
        public void Parar_BobSemCnh_AutuaOsDoisEApreende()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Bob, false);

            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Parar, new FakeRandom());

            CollectionAssert.AreEqual(new[] { Infracao.DirigirSemCnh, Infracao.MotoSemLicenciamento }, outcome.Infracoes);
            Assert.IsTrue(outcome.MotoApreendida);
        }

        [Test]
        public void Parar_Flagrante_AutuaAInfracaoSemSortearProblemaMenor()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Alugada, true, Infracao.AvancarSinalVermelho);
            var random = new FakeRandom(0.5, 0.0);

            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Parar, random);

            CollectionAssert.AreEqual(new[] { Infracao.AvancarSinalVermelho }, outcome.Infracoes);
            Assert.AreEqual(AbordagemDesfecho.LiberadoComMulta, outcome.Desfecho);
            Assert.AreEqual(10, outcome.DelayMinutes);
            Assert.AreEqual(0, random.Remaining);
        }

        [Test]
        public void Fugir_Escapou_NaoTemConsequenciaImediata()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Alugada, false);

            // escapa (0,44 < 0,45); placa não anotada (0,5 >= 0,3)
            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Fugir, new FakeRandom(0.5, 0.44, 0.5));

            Assert.IsTrue(outcome.Escapou);
            Assert.IsFalse(outcome.PlacaAnotada);
            Assert.IsEmpty(outcome.Infracoes);
            Assert.IsFalse(outcome.MotoRetida);
            Assert.AreEqual(AbordagemDesfecho.Escapou, outcome.Desfecho);
        }

        [Test]
        public void Fugir_Escapou_AlugadaPodeTerPlacaAnotada()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Alugada, true);

            Assert.IsTrue(system.Resolve(abordagem, AbordagemChoice.Fugir, new FakeRandom(0.5, 0.1, 0.29)).PlacaAnotada);
            Assert.IsFalse(system.Resolve(abordagem, AbordagemChoice.Fugir, new FakeRandom(0.5, 0.1, 0.3)).PlacaAnotada);
        }

        [Test]
        public void Fugir_Escapou_BobNuncaTemPlacaAnotada()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Bob, true);
            var random = new FakeRandom(0.5, 0.1, 0.0);

            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Fugir, random);

            Assert.IsTrue(outcome.Escapou);
            Assert.IsFalse(outcome.PlacaAnotada);
            Assert.AreEqual(1, random.Remaining);
        }

        [Test]
        public void Fugir_Pego_AlugadaComCnh_PagaFugaEMotoVaiParaOPatio()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Alugada, true);

            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Fugir, new FakeRandom(0.5, 0.45));

            Assert.IsFalse(outcome.Escapou);
            CollectionAssert.AreEqual(new[] { Infracao.FugaDeAbordagem }, outcome.Infracoes);
            Assert.IsTrue(outcome.MotoNoPatio);
            Assert.AreEqual(60, outcome.DelayMinutes);
            Assert.IsTrue(outcome.EncerraDia);
        }

        [Test]
        public void Fugir_Pego_SomaTudoQueUmaParadaDaria()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Alugada, false);

            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Fugir, new FakeRandom(0.9));

            CollectionAssert.AreEqual(new[] { Infracao.DirigirSemCnh, Infracao.FugaDeAbordagem }, outcome.Infracoes);
            Assert.IsTrue(outcome.MotoNoPatio);
        }

        [Test]
        public void Fugir_Pego_BobApreendidaDeQualquerJeito()
        {
            AbordagemSystem system = NewSystem();
            Abordagem abordagem = system.Iniciar(MotoOrigem.Bob, true);

            AbordagemOutcome outcome = system.Resolve(abordagem, AbordagemChoice.Fugir, new FakeRandom(0.9));

            Assert.IsTrue(outcome.MotoApreendida);
            CollectionAssert.AreEqual(new[] { Infracao.MotoSemLicenciamento, Infracao.FugaDeAbordagem }, outcome.Infracoes);
        }
    }
}
