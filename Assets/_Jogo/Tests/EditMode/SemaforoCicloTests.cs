using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class SemaforoCicloTests
    {
        private static SemaforoConfig Padrao() => new SemaforoConfig { VerdeA = 25, VerdeB = 20, Amarelo = 3, TodoVermelho = 1.5 };

        [Test]
        public void Ciclo_DuracaoSomaAsFases()
        {
            Assert.AreEqual(25 + 20 + 2 * (3 + 1.5), Padrao().Duracao, 1e-9);
        }

        [Test]
        public void Ciclo_SequenciaDoGrupoA()
        {
            SemaforoConfig c = Padrao();
            Assert.AreEqual(SemaforoEstado.Verde, SemaforoCiclo.Estado(c, 0, 0));
            Assert.AreEqual(SemaforoEstado.Verde, SemaforoCiclo.Estado(c, 24.9, 0));
            Assert.AreEqual(SemaforoEstado.Amarelo, SemaforoCiclo.Estado(c, 25.1, 0));
            Assert.AreEqual(SemaforoEstado.Amarelo, SemaforoCiclo.Estado(c, 27.9, 0));
            Assert.AreEqual(SemaforoEstado.Vermelho, SemaforoCiclo.Estado(c, 28.1, 0));
            Assert.AreEqual(SemaforoEstado.Vermelho, SemaforoCiclo.Estado(c, c.Duracao - 0.1, 0));
            Assert.AreEqual(SemaforoEstado.Verde, SemaforoCiclo.Estado(c, c.Duracao + 0.1, 0));
        }

        [Test]
        public void Ciclo_AmareloDuraTresSegundos()
        {
            SemaforoConfig c = Padrao();
            Assert.AreEqual(3, SemaforoCiclo.SegundosParaMudar(c, 25, 0), 1e-9);
            Assert.AreEqual(1, SemaforoCiclo.SegundosParaMudar(c, 27, 0), 1e-9);
        }

        [Test]
        public void Ciclo_GrupoBAbreDepoisDoTodoVermelho()
        {
            SemaforoConfig c = Padrao();
            double abreB = 25 + 3 + 1.5;
            Assert.AreEqual(SemaforoEstado.Vermelho, SemaforoCiclo.Estado(c, abreB - 0.1, 1));
            Assert.AreEqual(SemaforoEstado.Verde, SemaforoCiclo.Estado(c, abreB + 0.1, 1));
            Assert.AreEqual(SemaforoEstado.Amarelo, SemaforoCiclo.Estado(c, abreB + 20.5, 1));
        }

        [Test]
        public void Ciclo_GruposConflitantesNuncaAbremJuntos()
        {
            SemaforoConfig c = Padrao();
            c.Deslocamento = 7.3;

            for (double t = 0; t < 3 * c.Duracao; t += 0.05)
            {
                SemaforoEstado a = SemaforoCiclo.Estado(c, t, 0);
                SemaforoEstado b = SemaforoCiclo.Estado(c, t, 1);
                Assert.IsFalse(a != SemaforoEstado.Vermelho && b != SemaforoEstado.Vermelho, $"Os dois grupos liberados em t={t}");
            }
        }

        [Test]
        public void Ciclo_TodoVermelhoExisteEntreAsFases()
        {
            SemaforoConfig c = Padrao();
            // 28,75 s: o A fechou há 0,75 s e o B ainda não abriu.
            Assert.AreEqual(SemaforoEstado.Vermelho, SemaforoCiclo.Estado(c, 28.75, 0));
            Assert.AreEqual(SemaforoEstado.Vermelho, SemaforoCiclo.Estado(c, 28.75, 1));
        }

        [Test]
        public void Ciclo_DeslocamentoMudaAFase()
        {
            SemaforoConfig c = Padrao();
            c.Deslocamento = 26;
            Assert.AreEqual(SemaforoEstado.Amarelo, SemaforoCiclo.Estado(c, 0, 0));
        }

        [Test]
        public void Ciclo_SegundosDesdeVermelho()
        {
            SemaforoConfig c = Padrao();
            Assert.AreEqual(-1, SemaforoCiclo.SegundosDesdeVermelho(c, 10, 0), 1e-9);
            Assert.AreEqual(-1, SemaforoCiclo.SegundosDesdeVermelho(c, 26, 0), 1e-9);
            Assert.AreEqual(0.5, SemaforoCiclo.SegundosDesdeVermelho(c, 28.5, 0), 1e-9);
            Assert.AreEqual(2, SemaforoCiclo.SegundosDesdeVermelho(c, 30, 0), 1e-9);
        }

        [Test]
        public void Ciclo_InicioDoEstado_BateComEstado()
        {
            SemaforoConfig c = Padrao();

            for (int grupo = 0; grupo < 2; grupo++)
            {
                foreach (SemaforoEstado estado in new[] { SemaforoEstado.Verde, SemaforoEstado.Amarelo, SemaforoEstado.Vermelho })
                {
                    double t = SemaforoCiclo.InicioDoEstado(c, grupo, estado) + 0.2;
                    Assert.AreEqual(estado, SemaforoCiclo.Estado(c, t, grupo));
                    Assert.AreEqual(0.2, SemaforoCiclo.SegundosNoEstado(c, t, grupo), 1e-9);
                }
            }
        }

        [Test]
        public void Regras_AvancoSoNoVermelhoDepoisDaTolerancia()
        {
            Assert.IsNull(SemaforoRegras.ClassificarAvanco(SemaforoEstado.Verde, -1));
            Assert.IsNull(SemaforoRegras.ClassificarAvanco(SemaforoEstado.Amarelo, -1));
            Assert.IsNull(SemaforoRegras.ClassificarAvanco(SemaforoEstado.Vermelho, 0.3));
            Assert.AreEqual(Infracao.AvancarSinalVermelho, SemaforoRegras.ClassificarAvanco(SemaforoEstado.Vermelho, 0.5));
            Assert.AreEqual(Infracao.AvancarSinalVermelho, SemaforoRegras.ClassificarAvanco(SemaforoEstado.Vermelho, 4));
        }

        [Test]
        public void Regras_AvancoEhGravissimaDe293e47Com7Pontos()
        {
            RegraInfracao regra = CodigoTransito.Regra(Infracao.AvancarSinalVermelho);
            Assert.AreEqual(Money.Reais(293.47m), regra.Valor);
            Assert.AreEqual(7, regra.Pontos);
            Assert.AreEqual(GravidadeInfracao.Gravissima, regra.Gravidade);
        }

        [Test]
        public void Regras_CarroSoParaNoAmareloSeConsegue()
        {
            // 9 m/s precisa de 9 m com a freada confortável de 4,5 m/s2.
            Assert.IsTrue(SemaforoRegras.DeveParar(SemaforoEstado.Amarelo, 9, 12));
            Assert.IsFalse(SemaforoRegras.DeveParar(SemaforoEstado.Amarelo, 9, 5));
            Assert.IsTrue(SemaforoRegras.DeveParar(SemaforoEstado.Vermelho, 9, 5));
            Assert.IsFalse(SemaforoRegras.DeveParar(SemaforoEstado.Verde, 9, 5));
        }

        [Test]
        public void Regras_VelocidadeParaPararChegaAZero()
        {
            Assert.AreEqual(0, SemaforoRegras.VelocidadeParaParar(0, 5), 1e-9);
            Assert.AreEqual(10, SemaforoRegras.VelocidadeParaParar(10, 5), 1e-9);
        }

        [Test]
        public void RunReport_AvancoFotografadoViraCaptura()
        {
            var report = new RunReport();
            report.AddAvancoSinal();
            Assert.AreEqual(1, report.Capturas.Count);
            Assert.AreEqual(Infracao.AvancarSinalVermelho, report.Capturas[0]);
        }
    }
}
