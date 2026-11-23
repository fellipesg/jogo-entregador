using Entregador.Game;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    /// <summary>Matemática da pilotagem: inclinação, curva coordenada, tempo de inclinação, aderência e moldagem da entrada.</summary>
    public sealed class PilotagemTests
    {
        private const float Kmh = 1f / 3.6f;

        [Test]
        public void TaxaGuinadaSegueARelacaoDaCurvaCoordenada()
        {
            // 30° a 20 m/s: g·tan(30°)/20 = 0,283 rad/s = 16,2°/s.
            Assert.AreEqual(16.2f, Pilotagem.TaxaGuinada(30f, 20f), 0.2f);
            Assert.AreEqual(-16.2f, Pilotagem.TaxaGuinada(-30f, 20f), 0.2f);
            Assert.AreEqual(0f, Pilotagem.TaxaGuinada(0f, 20f), 0.001f);
        }

        [Test]
        public void AMesmaInclinacaoDaCurvaMaisAbertaEmAltaVelocidade()
        {
            float lenta = Pilotagem.TaxaGuinada(30f, 8f);
            float rapida = Pilotagem.TaxaGuinada(30f, 30f);

            Assert.Greater(lenta, rapida * 3f, "Mesma inclinação: a 30 m/s gira bem menos que a 8 m/s.");
            Assert.Greater(Pilotagem.RaioDaCurva(30f, 30f), Pilotagem.RaioDaCurva(30f, 8f) * 10f);
        }

        [Test]
        public void AceleracaoLateralEGTanDaInclinacao()
        {
            Assert.AreEqual(9.81f, Pilotagem.AceleracaoLateral(45f), 0.01f);
            Assert.AreEqual(0f, Pilotagem.AceleracaoLateral(0f), 0.001f);
        }

        [Test]
        public void TempoDeInclinacaoCrescePorVelocidade()
        {
            Assert.AreEqual(0.15f, Pilotagem.TempoInclinacao(20f * Kmh), 0.005f);
            Assert.AreEqual(0.45f, Pilotagem.TempoInclinacao(110f * Kmh), 0.005f);
            Assert.AreEqual(0.15f, Pilotagem.TempoInclinacao(5f * Kmh), 0.005f, "Abaixo de 20 km/h fica no valor rápido.");
            Assert.AreEqual(0.45f, Pilotagem.TempoInclinacao(150f * Kmh), 0.005f, "Acima de 110 km/h fica no valor lento.");
            Assert.Greater(Pilotagem.TempoInclinacao(90f * Kmh), Pilotagem.TempoInclinacao(60f * Kmh));
            Assert.AreEqual(Pilotagem.TempoInclinacao(60f * Kmh), Pilotagem.TempoInclinacao(-60f * Kmh), 0.0001f);
        }

        [Test]
        public void PassoCriticoChegaAoAlvoSemPassar()
        {
            float valor = 0f;
            float taxa = 0f;
            float maximo = 0f;

            for (int i = 0; i < 300; i++)
            {
                Pilotagem.PassoCritico(ref valor, ref taxa, 40f, 0.45f, 0.02f);
                maximo = System.Math.Max(maximo, valor);
            }

            Assert.AreEqual(40f, valor, 0.05f);
            Assert.LessOrEqual(maximo, 40.001f, "Amortecimento crítico: não ultrapassa o alvo.");
        }

        [Test]
        public void PassoCriticoLevaPerto63PorCentoNaConstanteDeTempo()
        {
            float valor = 0f;
            float taxa = 0f;

            for (int i = 0; i < 22; i++)
            {
                Pilotagem.PassoCritico(ref valor, ref taxa, 1f, 0.44f, 0.02f);
            }

            Assert.AreEqual(0.63f, valor, 0.06f, "Em ~tempo (0,44 s) a resposta ao degrau está perto de 63%.");
        }

        [Test]
        public void PassoCriticoEIndependenteDoPasso()
        {
            float a = 0f;
            float ta = 0f;
            float b = 0f;
            float tb = 0f;

            for (int i = 0; i < 50; i++)
            {
                Pilotagem.PassoCritico(ref a, ref ta, 10f, 0.3f, 0.02f);
            }

            for (int i = 0; i < 10; i++)
            {
                Pilotagem.PassoCritico(ref b, ref tb, 10f, 0.3f, 0.1f);
            }

            Assert.AreEqual(a, b, 0.01f, "Solução exata: o mesmo resultado com passos diferentes.");
        }

        [Test]
        public void AderenciaLimitaAAceleracaoLateralEFreioTiraAtrito()
        {
            float seco = Pilotagem.AceleracaoLateralDisponivel(9.5f, 1f, 0f);
            float molhado = Pilotagem.AceleracaoLateralDisponivel(9.5f, 0.6f, 0f);
            float freando = Pilotagem.AceleracaoLateralDisponivel(9.5f, 1f, 8f);

            Assert.AreEqual(9.5f, seco, 0.001f);
            Assert.AreEqual(5.7f, molhado, 0.001f);
            Assert.Less(freando, seco * 0.6f, "Freada forte deixa pouco atrito para a curva.");
            Assert.Greater(freando, 0f);
        }

        [Test]
        public void InclinacaoMaximaRespeitaMotoAderenciaEFreio()
        {
            float seco = Pilotagem.InclinacaoMaxima(45f, 9.5f, 1f, 0f);
            float molhado = Pilotagem.InclinacaoMaxima(45f, 9.5f, 0.6f, 0f);
            float freando = Pilotagem.InclinacaoMaxima(45f, 9.5f, 1f, 8f);

            Assert.AreEqual(44.1f, seco, 0.3f, "No seco o teto é o atrito (~44°), logo abaixo dos 45° da moto.");
            Assert.AreEqual(30.2f, molhado, 0.3f);
            Assert.Less(freando, 30f);
            Assert.AreEqual(45f, Pilotagem.InclinacaoMaxima(45f, 20f, 1f, 0f), 0.001f, "Com muito atrito vale o teto da moto.");
        }

        [Test]
        public void ChuvaReduzAAceleracaoLateralMaximaPara60PorCento()
        {
            float seco = Pilotagem.AceleracaoLateral(Pilotagem.InclinacaoMaxima(48f, 10.5f, 1f, 0f));
            float molhado = Pilotagem.AceleracaoLateral(Pilotagem.InclinacaoMaxima(48f, 10.5f, 0.6f, 0f));

            Assert.AreEqual(0.6f, molhado / seco, 0.03f);
        }

        [Test]
        public void GuidaoDiretoVaiParaInclinacaoEntre11e20Kmh()
        {
            Assert.AreEqual(0f, Pilotagem.PesoInclinacao(8f * Kmh), 0.001f);
            Assert.AreEqual(1f, Pilotagem.PesoInclinacao(25f * Kmh), 0.001f);
            float meio = Pilotagem.PesoInclinacao(15f * Kmh);
            Assert.Greater(meio, 0.2f);
            Assert.Less(meio, 0.8f);
        }

        [Test]
        public void RetornoA10KmhCabeEm4MetrosDeRaio()
        {
            float v = 10f * Kmh;
            float guinada = Pilotagem.TaxaGuinadaGuidao(1f, v, 2.2f, 120f, 9.5f);
            float raio = v / (guinada * UnityEngine.Mathf.Deg2Rad);

            Assert.Less(raio, 4f);
            Assert.Greater(raio, 1.5f);
            Assert.AreEqual(0f, Pilotagem.TaxaGuinadaGuidao(1f, 0f, 2.2f, 120f, 9.5f), 0.001f, "Parado a moto não gira.");
            Assert.Less(Pilotagem.TaxaGuinadaGuidao(-1f, v, 2.2f, 120f, 9.5f), 0f);
        }

        [Test]
        public void EntradaEmAltaVelocidadeEMaisFinaPertoDoCentro()
        {
            float baixa = Pilotagem.FormarEntrada(0.5f, 5f);
            float alta = Pilotagem.FormarEntrada(0.5f, 30f);

            Assert.AreEqual(0.5f, baixa, 0.001f, "Em baixa velocidade é linear.");
            Assert.Less(alta, 0.4f);
            Assert.AreEqual(1f, Pilotagem.FormarEntrada(1f, 30f), 0.001f, "O fundo de curso continua valendo 1.");
            Assert.AreEqual(-alta, Pilotagem.FormarEntrada(-0.5f, 30f), 0.001f);
        }

        [Test]
        public void RampaDaEntradaEMaisLentaEmAltaVelocidadeEVoltaRapido()
        {
            float baixa = Pilotagem.TaxaRampaEntrada(3f, false);
            float alta = Pilotagem.TaxaRampaEntrada(30f, false);
            float volta = Pilotagem.TaxaRampaEntrada(30f, true);

            Assert.Greater(baixa, alta);
            Assert.Greater(volta, alta);
            Assert.Less(alta, 5f, "Teclado leva mais de 0,2 s para ir de 0 a 1 em alta velocidade.");
        }

        [Test]
        public void ZonaMortaZeraOPequenoESemDegrauNaSaida()
        {
            Assert.AreEqual(0f, Pilotagem.ZonaMorta(0.1f, 0.12f), 0.0001f);
            Assert.AreEqual(0f, Pilotagem.ZonaMorta(-0.12f, 0.12f), 0.0001f);
            Assert.AreEqual(0.01f, Pilotagem.ZonaMorta(0.1301f, 0.12f), 0.01f);
            Assert.AreEqual(1f, Pilotagem.ZonaMorta(1f, 0.12f), 0.0001f);
            Assert.AreEqual(-1f, Pilotagem.ZonaMorta(-3f, 0.12f), 0.0001f);
        }

        [Test]
        public void DerrapagemSobeSoQuandoAInclinacaoPassaDoDisponivel()
        {
            Assert.AreEqual(0f, Pilotagem.AlvoDerrapagem(40f, 44f), 0.0001f);
            Assert.AreEqual(0f, Pilotagem.AlvoDerrapagem(45f, 44f), 0.0001f, "Margem de 1°.");
            Assert.Greater(Pilotagem.AlvoDerrapagem(48f, 44f), 0f);
            Assert.AreEqual(1f, Pilotagem.AlvoDerrapagem(60f, 30f), 0.0001f);
            Assert.AreEqual(Pilotagem.AlvoDerrapagem(48f, 44f), Pilotagem.AlvoDerrapagem(-48f, 44f), 0.0001f);
        }

        [Test]
        public void InclinacaoDoFundoDeCursoCaiUmPoucoEmAltaVelocidade()
        {
            Assert.AreEqual(1f, Pilotagem.FatorInclinacaoRapida(10f), 0.0001f);
            Assert.AreEqual(0.85f, Pilotagem.FatorInclinacaoRapida(30f), 0.0001f);
        }

        [Test]
        public void PerfisDasMotosTemInclinacaoETempoCoerentes()
        {
            Assert.AreEqual(45f, MotoFisicaCatalogo.Titan.InclinacaoMaxima, 0.001f);
            Assert.AreEqual(48f, MotoFisicaCatalogo.Twister.InclinacaoMaxima, 0.001f);
            Assert.Less(MotoFisicaCatalogo.Titan.TempoInclinacaoLento, MotoFisicaCatalogo.Titan.TempoInclinacaoRapido);
            Assert.Less(MotoFisicaCatalogo.Twister.TempoInclinacaoLento, MotoFisicaCatalogo.Twister.TempoInclinacaoRapido);
        }
    }
}
