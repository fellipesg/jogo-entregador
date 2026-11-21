using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class FiscalizacaoTests
    {
        [TestCase("50", 50)]
        [TestCase("40 km/h", 40)]
        [TestCase("60;50", 60)]
        [TestCase("30", 30)]
        [TestCase("", 0)]
        [TestCase(null, 0)]
        [TestCase("signals", 0)]
        [TestCase("30 mph", 0)]
        [TestCase("5", 0)]
        [TestCase("200", 0)]
        public void LerTag_InterpretaOMaxspeedDoOsm(string tag, int esperado)
        {
            Assert.AreEqual(esperado, LimitesDeVelocidade.LerTag(tag));
        }

        [Test]
        public void Resolver_UsaATagQuandoExisteESenaoOPadraoDaClasse()
        {
            Assert.AreEqual(60, LimitesDeVelocidade.Resolver(60, LimitesDeVelocidade.Avenida));
            Assert.AreEqual(50, LimitesDeVelocidade.Resolver(0, LimitesDeVelocidade.Avenida));
            Assert.AreEqual(40, LimitesDeVelocidade.Resolver(0, LimitesDeVelocidade.Coletora));
            Assert.AreEqual(30, LimitesDeVelocidade.Resolver(0, LimitesDeVelocidade.Local));
        }

        [Test]
        public void Flagrante_ContramaoTemPrioridade()
        {
            Assert.AreEqual(Infracao.Contramao, Flagrantes.Avaliar(true, true, 120, 50));
        }

        [Test]
        public void Flagrante_FaixaDeOnibusQuandoNaoHaContramao()
        {
            Assert.AreEqual(Infracao.FaixaExclusivaOnibus, Flagrantes.Avaliar(false, true, 30, 50));
        }

        [Test]
        public void Flagrante_VelocidadeSoContaAcimaDeVintePorCentoDoLimite()
        {
            // 60 km/h num limite de 50 é exatamente 20%: a polícia ainda não autua.
            Assert.IsNull(Flagrantes.Avaliar(false, false, 60, 50));
            Assert.IsNull(Flagrantes.Avaliar(false, false, 45, 50));

            // 62 km/h: passou de 20%; com a tolerância do radar (-7) o excesso é de 10%: até 20%.
            Assert.AreEqual(Infracao.ExcessoVelocidadeAte20, Flagrantes.Avaliar(false, false, 62, 50));
            Assert.AreEqual(Infracao.ExcessoVelocidade20a50, Flagrantes.Avaliar(false, false, 80, 50));
            Assert.AreEqual(Infracao.ExcessoVelocidadeAcima50, Flagrantes.Avaliar(false, false, 100, 50));
        }

        [Test]
        public void Flagrante_NadaQuandoEstaTudoEmOrdem()
        {
            Assert.IsNull(Flagrantes.Avaliar(false, false, 40, 50));
        }
    }
}
