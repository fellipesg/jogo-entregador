using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class InfracoesTests
    {
        [Test]
        public void Velocidade_DentroDaTolerancia_SemInfracao()
        {
            // 57 km/h medidos num limite de 50: descontando 7, ficam 50. Não passou.
            Assert.IsNull(CodigoTransito.ClassificarVelocidade(57, 50));
            Assert.IsNull(CodigoTransito.ClassificarVelocidade(40, 50));
        }

        [Test]
        public void Velocidade_AteVintePorCento()
        {
            Assert.AreEqual(Infracao.ExcessoVelocidadeAte20, CodigoTransito.ClassificarVelocidade(58, 50));
            // 67 - 7 = 60 = exatamente 20% acima de 50
            Assert.AreEqual(Infracao.ExcessoVelocidadeAte20, CodigoTransito.ClassificarVelocidade(67, 50));
        }

        [Test]
        public void Velocidade_DeVinteACinquentaPorCento()
        {
            Assert.AreEqual(Infracao.ExcessoVelocidade20a50, CodigoTransito.ClassificarVelocidade(68, 50));
            // 82 - 7 = 75 = exatamente 50% acima
            Assert.AreEqual(Infracao.ExcessoVelocidade20a50, CodigoTransito.ClassificarVelocidade(82, 50));
        }

        [Test]
        public void Velocidade_AcimaDeCinquentaPorCento()
        {
            Assert.AreEqual(Infracao.ExcessoVelocidadeAcima50, CodigoTransito.ClassificarVelocidade(83, 50));
            Assert.AreEqual(Infracao.ExcessoVelocidadeAcima50, CodigoTransito.ClassificarVelocidade(120, 60));
        }

        [Test]
        public void Velocidade_AcimaDeCem_ToleranciaDeSetePorCento()
        {
            // 120 km/h x 0,93 = 111,6 num limite de 100: 11,6% acima
            Assert.AreEqual(Infracao.ExcessoVelocidadeAte20, CodigoTransito.ClassificarVelocidade(120, 100));
            // 130 x 0,93 = 120,9: 20,9% acima
            Assert.AreEqual(Infracao.ExcessoVelocidade20a50, CodigoTransito.ClassificarVelocidade(130, 100));
            Assert.IsNull(CodigoTransito.ClassificarVelocidade(107, 100));
        }

        [TestCase(Infracao.ExcessoVelocidadeAte20, 13016, 4, GravidadeInfracao.Media)]
        [TestCase(Infracao.ExcessoVelocidade20a50, 19523, 5, GravidadeInfracao.Grave)]
        [TestCase(Infracao.ExcessoVelocidadeAcima50, 88041, 7, GravidadeInfracao.Gravissima)]
        [TestCase(Infracao.Contramao, 29347, 7, GravidadeInfracao.Gravissima)]
        [TestCase(Infracao.FaixaExclusivaOnibus, 29347, 7, GravidadeInfracao.Gravissima)]
        [TestCase(Infracao.AvancarSinalVermelho, 29347, 7, GravidadeInfracao.Gravissima)]
        [TestCase(Infracao.DirigirSemCnh, 88041, 7, GravidadeInfracao.Gravissima)]
        [TestCase(Infracao.MotoSemLicenciamento, 29347, 7, GravidadeInfracao.Gravissima)]
        [TestCase(Infracao.FugaDeAbordagem, 29347, 7, GravidadeInfracao.Gravissima)]
        [TestCase(Infracao.EquipamentoIrregular, 19523, 5, GravidadeInfracao.Grave)]
        public void Tabela_ValoresDoCtb(Infracao infracao, int centavos, int pontos, GravidadeInfracao gravidade)
        {
            RegraInfracao regra = CodigoTransito.Regra(infracao);

            Assert.AreEqual(new Money(centavos), regra.Valor);
            Assert.AreEqual(pontos, regra.Pontos);
            Assert.AreEqual(gravidade, regra.Gravidade);
        }

        [Test]
        public void Tabela_ConsequenciasAdministrativas()
        {
            Assert.IsTrue(CodigoTransito.Regra(Infracao.ExcessoVelocidadeAcima50).SuspendeDireito);
            Assert.IsFalse(CodigoTransito.Regra(Infracao.ExcessoVelocidade20a50).SuspendeDireito);
            Assert.IsTrue(CodigoTransito.Regra(Infracao.DirigirSemCnh).RetemVeiculo);
            Assert.IsTrue(CodigoTransito.Regra(Infracao.MotoSemLicenciamento).RetemVeiculo);
            Assert.IsTrue(CodigoTransito.Regra(Infracao.MotoSemLicenciamento).DoVeiculo);
        }

        [TestCase(39, 0, false)]
        [TestCase(40, 0, true)]
        [TestCase(29, 1, false)]
        [TestCase(30, 1, true)]
        [TestCase(19, 2, false)]
        [TestCase(20, 2, true)]
        [TestCase(20, 3, true)]
        [TestCase(20, 0, false)]
        public void LimiteDeSuspensao_40_30_20(int pontos, int gravissimas, bool suspende)
        {
            Assert.AreEqual(suspende, CodigoTransito.DeveSuspender(pontos, gravissimas));
        }

        [Test]
        public void Carteira_SemGravissima_SuspendeEm40()
        {
            var cnh = new Carteira(true);

            for (int i = 0; i < 9; i++)
            {
                cnh.Registrar(Infracao.ExcessoVelocidadeAte20);
            }

            Assert.AreEqual(36, cnh.Pontos);
            Assert.IsTrue(cnh.Valida);

            cnh.Registrar(Infracao.ExcessoVelocidadeAte20);

            Assert.AreEqual(40, cnh.Pontos);
            Assert.IsTrue(cnh.Suspensa);
            Assert.AreEqual(CnhStatus.Suspensa, cnh.Status);
        }

        [Test]
        public void Carteira_ComUmaGravissima_SuspendeEm30()
        {
            var cnh = new Carteira(true);
            cnh.Registrar(Infracao.Contramao);

            for (int i = 0; i < 4; i++)
            {
                cnh.Registrar(Infracao.ExcessoVelocidade20a50);
            }

            Assert.AreEqual(27, cnh.Pontos);
            Assert.IsFalse(cnh.Suspensa);

            cnh.Registrar(Infracao.ExcessoVelocidadeAte20);

            Assert.AreEqual(31, cnh.Pontos);
            Assert.IsTrue(cnh.Suspensa);
        }

        [Test]
        public void Carteira_ComDuasGravissimas_SuspendeEm20()
        {
            var cnh = new Carteira(true);
            cnh.Registrar(Infracao.Contramao);
            cnh.Registrar(Infracao.AvancarSinalVermelho);
            cnh.Registrar(Infracao.ExcessoVelocidadeAte20);

            Assert.AreEqual(18, cnh.Pontos);
            Assert.IsFalse(cnh.Suspensa);

            cnh.Registrar(Infracao.ExcessoVelocidadeAte20);

            Assert.AreEqual(22, cnh.Pontos);
            Assert.IsTrue(cnh.Suspensa);
        }

        [Test]
        public void Carteira_ExcessoAcima50_SuspendeNaHora()
        {
            var cnh = new Carteira(true);

            cnh.Registrar(Infracao.ExcessoVelocidadeAcima50);

            Assert.IsTrue(cnh.Suspensa);
            Assert.IsFalse(cnh.Valida);
        }

        [Test]
        public void Carteira_SemCnh_NaoAcumulaPontos()
        {
            var cnh = new Carteira(false);

            cnh.Registrar(Infracao.Contramao);

            Assert.AreEqual(0, cnh.Pontos);
            Assert.IsFalse(cnh.Valida);
            Assert.AreEqual(CnhStatus.SemCnh, cnh.Status);
        }

        [Test]
        public void RunReport_AddRadar_SoRegistraQuandoPassaDaTolerancia()
        {
            var report = new RunReport();

            Assert.IsFalse(report.AddRadar(55, 50));
            Assert.IsTrue(report.AddRadar(70, 50));
            CollectionAssert.AreEqual(new[] { Infracao.ExcessoVelocidade20a50 }, report.Capturas);
        }
    }
}
