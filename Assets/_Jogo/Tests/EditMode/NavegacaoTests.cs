using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class NavegacaoTests
    {
        private static NavPonto P(double x, double z, string rua = "", bool cruz = false, bool rot = false) => new NavPonto(x, z, rua, cruz, rot);

        /// <summary>Segue o norte por 300 m na Rua A e vira para o leste (direita) para a Rua B por 200 m.</summary>
        private static List<NavPonto> RightTurnRoute() => new List<NavPonto>
        {
            P(0, 0, "Rua A"), P(0, 150, "Rua A", true), P(0, 300, "Rua B", true), P(200, 300, "Rua B"), P(210, 300),
        };

        [Test]
        public void CurvaADireita_ViraManobraComRuaEDistanciaAteOFim()
        {
            List<Instrucao> list = Navegacao.Calcular(RightTurnRoute());

            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(Manobra.VireADireita, list[0].Manobra);
            Assert.AreEqual(90.0, list[0].Angulo, 0.01);
            Assert.AreEqual("Rua B", list[0].Rua);
            Assert.AreEqual("Rua A", list[0].RuaAnterior);
            Assert.IsTrue(list[0].TrocaDeRua);
            Assert.AreEqual(210.0, list[0].MetrosAteOFim, 0.01, "200 m na Rua B mais os 10 m finais.");
            Assert.AreEqual("Vire à direita na Rua B", list[0].Texto);
            Assert.AreEqual(Manobra.Chegada, list[1].Manobra);
            Assert.AreEqual(0.0, list[1].MetrosAteOFim);
        }

        [Test]
        public void CurvaAEsquerda_ViraManobraComAnguloNegativo()
        {
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Avenida C", true), P(-150, 100, "Avenida C"), P(-160, 100) };
            List<Instrucao> list = Navegacao.Calcular(route);

            Assert.AreEqual(Manobra.VireAEsquerda, list[0].Manobra);
            Assert.AreEqual(-90.0, list[0].Angulo, 0.01);
            Assert.AreEqual("Vire à esquerda na Avenida C", list[0].Texto);
        }

        [Test]
        public void NomeComLargo_UsaNo()
        {
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Largo Treze de Maio", true), P(100, 100, "Largo Treze de Maio"), P(110, 100) };

            Assert.AreEqual("Vire à direita no Largo Treze de Maio", Navegacao.Calcular(route)[0].Texto);
        }

        [Test]
        public void SemNomeNoMapa_UsaRuaSemNome()
        {
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "", true), P(100, 100, ""), P(110, 100) };
            List<Instrucao> list = Navegacao.Calcular(route);

            Assert.AreEqual(Navegacao.SemNome, list[0].Rua);
            Assert.AreEqual("Vire à direita na rua sem nome", list[0].Texto);
        }

        [Test]
        public void DesvioSuaveNumBifurcacao_ViraMantenha()
        {
            double a = 35 * System.Math.PI / 180;
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Rua A", true), P(100 * System.Math.Sin(a), 100 + 100 * System.Math.Cos(a), "Rua A"), P(200 * System.Math.Sin(a), 100 + 200 * System.Math.Cos(a)) };
            List<Instrucao> list = Navegacao.Calcular(route);

            Assert.AreEqual(Manobra.MantenhaADireita, list[0].Manobra);
            Assert.AreEqual("Mantenha-se à direita", list[0].Texto, "Mesma rua: sem 'na Rua X'.");
        }

        [Test]
        public void DesvioSuaveSemCruzamento_NaoViraManobra()
        {
            double a = 35 * System.Math.PI / 180;
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Rua A"), P(100 * System.Math.Sin(a), 100 + 100 * System.Math.Cos(a), "Rua A"), P(200 * System.Math.Sin(a), 100 + 200 * System.Math.Cos(a)) };

            Assert.AreEqual(1, Navegacao.Calcular(route).Count, "Só a chegada: é só uma curva da rua.");
        }

        [Test]
        public void PequenoAngulo_NaoViraManobra()
        {
            double a = 15 * System.Math.PI / 180;
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Rua A", true), P(60 * System.Math.Sin(a), 100 + 60 * System.Math.Cos(a), "Rua A"), P(120 * System.Math.Sin(a), 100 + 120 * System.Math.Cos(a)) };

            Assert.AreEqual(1, Navegacao.Calcular(route).Count);
        }

        [Test]
        public void VerticesDeUmaMesmaCurva_SomamUmaManobraSo()
        {
            // Curva de 90 graus em três vértices de 30 graus, em 20 m.
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Rua A") };
            double x = 0;
            double z = 100;

            foreach (double heading in new[] { 30.0, 60.0, 90.0 })
            {
                double r = heading * System.Math.PI / 180;
                x += 8 * System.Math.Sin(r);
                z += 8 * System.Math.Cos(r);
                route.Add(P(x, z, "Rua A"));
            }

            route.Add(P(x + 200, z, "Rua A"));
            route.Add(P(x + 210, z));
            List<Instrucao> list = Navegacao.Calcular(route);

            Assert.AreEqual(2, list.Count, "Uma manobra e a chegada.");
            Assert.AreEqual(Manobra.VireADireita, list[0].Manobra);
            Assert.AreEqual(90.0, list[0].Angulo, 1.0);
        }

        [Test]
        public void MeiaVolta_ViraRetorno()
        {
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Rua A", true), P(3, 0, "Rua A"), P(3, -50), P(3, -60) };
            List<Instrucao> list = Navegacao.Calcular(route);

            Assert.AreEqual(Manobra.Retorno, list[0].Manobra);
            Assert.AreEqual("Faça o retorno", list[0].Texto);
            Assert.IsTrue(list[0].Aguda);
        }

        [Test]
        public void RuaReta_SoComChegada()
        {
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Rua A", true), P(0, 200, "Rua A", true), P(0, 300, "Rua A") };
            List<Instrucao> list = Navegacao.Calcular(route);

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(Manobra.Chegada, list[0].Manobra);
        }

        [Test]
        public void TrocaDeNomeSemCurva_ViraSigaEmFrente()
        {
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Avenida B", true), P(0, 300, "Avenida B"), P(0, 310) };
            List<Instrucao> list = Navegacao.Calcular(route);

            Assert.AreEqual(Manobra.SigaEmFrente, list[0].Manobra);
            Assert.AreEqual("Siga em frente na Avenida B", list[0].Texto);
        }

        [Test]
        public void Chegada_DizDeQueLadoFicaODestino()
        {
            // Rua para o norte; o destino fica 8 m a leste (direita) ou a oeste (esquerda) do ponto colado na rua.
            var right = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Rua A"), P(0, 200, "Rua A"), P(8, 200) };
            var left = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Rua A"), P(0, 200, "Rua A"), P(-8, 200) };
            var onRoad = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Rua A"), P(0, 200, "Rua A"), P(1, 200) };

            Instrucao r = Navegacao.Calcular(right).Last();
            Instrucao l = Navegacao.Calcular(left).Last();
            Instrucao c = Navegacao.Calcular(onRoad).Last();

            Assert.AreEqual(1, r.Lado);
            Assert.AreEqual(-1, l.Lado);
            Assert.AreEqual(0, c.Lado);
            StringAssert.Contains("à direita", r.Texto);
            StringAssert.Contains("à esquerda", l.Texto);
        }

        [Test]
        public void Rotatoria_ContaAsSaidasAteASua()
        {
            // Entra na rotatória em (0,100), passa por uma saída intermediária e sai na segunda.
            var route = new List<NavPonto>
            {
                P(0, 0, "Rua A"),
                P(0, 100, "Rotatória", true, true),
                P(20, 120, "Rotatória", true, true),
                P(40, 100, "Rua B", true),
                P(140, 100, "Rua B"),
                P(140, 110),
            };
            List<Instrucao> list = Navegacao.Calcular(route);

            Assert.AreEqual(Manobra.RotatoriaSaida, list[0].Manobra);
            Assert.AreEqual(2, list[0].Saida);
            StringAssert.Contains("2ª saída", list[0].Texto);
        }

        [Test]
        public void MetrosAteOFim_DecrescemComAOrdemDasManobras()
        {
            var route = new List<NavPonto>
            {
                P(0, 0, "A"), P(0, 100, "B", true), P(100, 100, "B"), P(100, 200, "B", true), P(100, 300, "C"), P(108, 300),
            };
            List<Instrucao> list = Navegacao.Calcular(route);

            Assert.AreEqual(3, list.Count);
            Assert.Greater(list[0].MetrosAteOFim, list[1].MetrosAteOFim);
            Assert.Greater(list[1].MetrosAteOFim, list[2].MetrosAteOFim);
            Assert.AreEqual(Manobra.VireADireita, list[0].Manobra);
            Assert.AreEqual(Manobra.VireAEsquerda, list[1].Manobra);
        }

        // ---------- Distâncias ----------

        [TestCase(30, "50 metros")]
        [TestCase(80, "100 metros")]
        [TestCase(180, "200 metros")]
        [TestCase(280, "300 metros")]
        [TestCase(420, "500 metros")]
        [TestCase(700, "800 metros")]
        [TestCase(1000, "1 quilômetro")]
        [TestCase(1700, "1,5 quilômetro")]
        [TestCase(2100, "2 quilômetros")]
        public void DistanciaFalada_Arredonda(double metros, string esperado)
        {
            Assert.AreEqual(esperado, Navegacao.DistanciaFalada(metros));
        }

        [TestCase(34, "30 m")]
        [TestCase(230, "250 m")]
        [TestCase(1240, "1,2 km")]
        public void DistanciaCurta_ParaATela(double metros, string esperado)
        {
            Assert.AreEqual(esperado, Navegacao.DistanciaCurta(metros));
        }

        // ---------- Avisos ----------

        private static Instrucao DireitaNaRuaB() => Navegacao.Calcular(RightTurnRoute())[0];

        /// <summary>Dirige em direção à manobra a speed m/s, em passos de 0,1 s, e devolve os avisos na ordem com a distância em que saíram.</summary>
        private static List<(Aviso aviso, double distancia)> Dirigir(Instrucao inst, double speed, double from = 1200)
        {
            var anunciador = new AnunciadorNavegacao();
            var heard = new List<(Aviso, double)>();

            for (double d = from; d > 0; d -= speed * 0.1)
            {
                Aviso aviso = anunciador.Avaliar(inst, d, speed);

                if (aviso != null)
                {
                    heard.Add((aviso, d));
                }
            }

            return heard;
        }

        [Test]
        public void Avisos_A30kmh_SaemEm600_200E40Metros()
        {
            double v = 30 / 3.6;
            var heard = Dirigir(DireitaNaRuaB(), v);

            CollectionAssert.AreEqual(new[] { EstagioAviso.Longe, EstagioAviso.Perto, EstagioAviso.Agora }, heard.Select(h => h.aviso.Estagio).ToArray());
            Assert.AreEqual(600, heard[0].distancia, v * 0.1 + 0.01);
            Assert.AreEqual(200, heard[1].distancia, v * 0.1 + 0.01);
            Assert.AreEqual(40, heard[2].distancia, v * 0.1 + 0.01);
            StringAssert.Contains("Em 500 metros, vire à direita na Rua B", heard[0].aviso.Texto);
            StringAssert.Contains("Em 200 metros, vire à direita na Rua B", heard[1].aviso.Texto);
            Assert.AreEqual("Vire à direita na Rua B.", heard[2].aviso.Texto);
        }

        [Test]
        public void Avisos_A90kmh_AgoraSaiMaisCedo()
        {
            double slow = 30 / 3.6;
            double fast = 90 / 3.6;
            double agoraDevagar = Dirigir(DireitaNaRuaB(), slow).Last().distancia;
            double agoraRapido = Dirigir(DireitaNaRuaB(), fast).Last().distancia;

            Assert.AreEqual(75.0, agoraRapido, fast * 0.1 + 0.01, "3 s a 25 m/s.");
            Assert.Greater(agoraRapido, agoraDevagar + 25.0, "Em velocidade a ordem vem bem antes da curva.");
        }

        [Test]
        public void Avisos_AMuitaVelocidade_LongeAcompanhaOsDozeSegundos()
        {
            double v = 150 / 3.6; // 41,7 m/s: 12 s = 500 m, ainda abaixo de 600 m
            Assert.AreEqual(600.0, new AnunciadorNavegacao().Longe(v), 0.01);
            Assert.AreEqual(12 * 50.0, new AnunciadorNavegacao().Longe(50.0), 0.01, "A 50 m/s o aviso vem aos 600 m (12 s).");
            Assert.AreEqual(720.0, new AnunciadorNavegacao().Longe(60.0), 0.01, "A 60 m/s vem aos 720 m.");
        }

        [Test]
        public void Avisos_CadaEstagioSaiUmaVezEEmOrdem()
        {
            var heard = Dirigir(DireitaNaRuaB(), 12.0);
            var stages = heard.Select(h => (int)h.aviso.Estagio).ToList();

            Assert.AreEqual(stages.Distinct().Count(), stages.Count);
            CollectionAssert.AreEqual(stages.OrderBy(s => s).ToList(), stages);
        }

        [Test]
        public void Avisos_ManobraQueComecaPertoPulaOsEstagiosLongos()
        {
            var anunciador = new AnunciadorNavegacao();
            Instrucao inst = DireitaNaRuaB();
            Aviso first = anunciador.Avaliar(inst, 150, 8);

            Assert.AreEqual(EstagioAviso.Perto, first.Estagio);
            Assert.IsNull(anunciador.Avaliar(inst, 560, 8), "O estágio longe já passou.");
        }

        [Test]
        public void Avisos_ChegadaFalaOLado()
        {
            var route = new List<NavPonto> { P(0, 0, "Rua A"), P(0, 100, "Rua A"), P(0, 200, "Rua A"), P(8, 200) };
            Instrucao arrival = Navegacao.Calcular(route).Last();
            var anunciador = new AnunciadorNavegacao();

            StringAssert.Contains("destino estará à direita", anunciador.Avaliar(arrival, 190, 8).Texto);
            StringAssert.Contains("Você chegou. O destino está à direita", anunciador.Avaliar(arrival, 20, 8).Texto);
        }

        [Test]
        public void Avisos_NaoFalaDepoisDePassar()
        {
            Assert.IsNull(new AnunciadorNavegacao().Avaliar(DireitaNaRuaB(), -5, 8));
            Assert.IsNull(new AnunciadorNavegacao().Avaliar(null, 50, 8));
        }
    }
}
