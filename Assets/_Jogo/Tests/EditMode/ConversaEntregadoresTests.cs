using System.Collections.Generic;
using System.Linq;
using Entregador.Core;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    /// <summary>Conversa dos entregadores nos pontos: informação só quando perguntam, dica de blitz com a precisão configurada, sem repetir no dia.</summary>
    public sealed class ConversaEntregadoresTests
    {
        private static readonly string[] Nomes = { "Dudu", "Betão", "Rafa", "Careca" };

        private static ContextoFofoca Contexto(bool blitz)
        {
            var c = new ContextoFofoca();

            if (blitz)
            {
                c.BlitzHoje.Add(new LocalFofoca("Rua Adolfo Pinheiro", "perto do Largo"));
            }

            c.BlitzFalsas.Add(new LocalFofoca("Avenida João Dias", "pro lado sul"));
            c.BlitzFalsas.Add(new LocalFofoca("Rua Isabel Schmidt", "pro lado leste"));
            c.Radares.Add(new RadarFofoca("Avenida João Dias", 50));
            c.ChoveHoje = true;
            c.ZonaMovimentada = "perto do Largo Treze";
            c.RestauranteLento = "Hamburgueria";
            c.MinutosDeEspera = 15;

            return c;
        }

        private static bool Escolhe(Conversa conversa, FofocaTema tema)
        {
            for (int i = 0; i < conversa.Opcoes.Count; i++)
            {
                if (conversa.Opcoes[i].Tema == tema && !conversa.Opcoes[i].Sair)
                {
                    conversa.Escolher(i);

                    return true;
                }
            }

            return false;
        }

        /// <summary>Conversa fiada até a pergunta do tema aparecer, e então a faz.</summary>
        private static bool Pergunta(Conversa conversa, FofocaTema tema)
        {
            int guarda = 0;

            while (!conversa.Terminada && !conversa.Opcoes.Any(o => o.Tema == tema) && guarda++ < 8)
            {
                Escolhe(conversa, FofocaTema.Papo);
            }

            return !conversa.Terminada && Escolhe(conversa, tema);
        }

        private static void Sai(Conversa conversa)
        {
            for (int i = 0; i < conversa.Opcoes.Count; i++)
            {
                if (conversa.Opcoes[i].Sair)
                {
                    conversa.Escolher(i);

                    return;
                }
            }
        }

        private static Conversa Nova(int seed, double precisao, bool blitz)
        {
            var gerador = new ConversaEntregadores(new SeededRandom(seed), new ConversaConfig { PrecisaoBlitz = precisao });

            return gerador.Iniciar("g", 1, Nomes, Contexto(blitz));
        }

        [Test]
        public void OpeningHasGreetingAndBanterButNoInformation()
        {
            for (int seed = 1; seed <= 50; seed++)
            {
                Conversa c = Nova(seed, 1.0, true);

                Assert.AreEqual(2, c.Falas.Count);
                Assert.AreEqual(FofocaTema.Saudacao, c.Falas[0].Tema);
                Assert.AreEqual(FofocaTema.Papo, c.Falas[1].Tema);
                Assert.IsEmpty(c.Reveladas, "Sem perguntar, nenhuma informação sai.");
                Assert.That(c.Opcoes.Count, Is.InRange(2, 3));
                Assert.IsTrue(c.Opcoes.Any(o => o.Sair), "Sempre dá pra sair.");
            }
        }

        [Test]
        public void ChattingOnlyNeverRevealsAnything()
        {
            for (int seed = 1; seed <= 30; seed++)
            {
                Conversa c = Nova(seed, 1.0, true);
                int guarda = 0;

                while (!c.Terminada && guarda++ < 8)
                {
                    if (!Escolhe(c, FofocaTema.Papo))
                    {
                        Sai(c);
                    }
                }

                Assert.IsEmpty(c.Reveladas);
                Assert.IsTrue(c.Terminada);
            }
        }

        [Test]
        public void BlitzHintIsTrueWhenAccuracyIsFull()
        {
            int especificas = 0;
            int vagas = 0;

            for (int seed = 1; seed <= 200; seed++)
            {
                Conversa c = Nova(seed, 1.0, true);
                Assert.IsTrue(Escolhe(c, FofocaTema.Blitz), "A pergunta da blitz é a primeira oferecida.");
                InfoRevelada info = c.Reveladas.Single(r => r.Tema == FofocaTema.Blitz);
                Assert.IsTrue(info.Verdadeira);
                string texto = c.Falas.Last(f => f.Tema == FofocaTema.Blitz).Texto;

                if (info.Vaga)
                {
                    vagas++;
                    StringAssert.Contains("perto do Largo", texto);
                    Assert.IsEmpty(info.Local);
                }
                else
                {
                    especificas++;
                    Assert.AreEqual("Rua Adolfo Pinheiro", info.Local);
                    StringAssert.Contains("Adolfo Pinheiro", texto);
                }
            }

            Assert.Greater(especificas, 0);
            Assert.Greater(vagas, 0, "Às vezes a dica é vaga.");
        }

        [Test]
        public void CleanDayIsReportedCleanWhenAccuracyIsFull()
        {
            for (int seed = 1; seed <= 100; seed++)
            {
                Conversa c = Nova(seed, 1.0, false);
                Assert.IsTrue(Escolhe(c, FofocaTema.Blitz));
                InfoRevelada info = c.Reveladas.Single();
                Assert.IsTrue(info.Verdadeira);
                Assert.IsEmpty(info.Local);
                CollectionAssert.Contains(FofocaCatalogo.BlitzLimpa, c.Falas.Last(f => f.Tema == FofocaTema.Blitz).Texto);
            }
        }

        [Test]
        public void BlitzHintIsAlwaysWrongWhenAccuracyIsZero()
        {
            for (int seed = 1; seed <= 100; seed++)
            {
                foreach (bool blitz in new[] { true, false })
                {
                    Conversa c = Nova(seed, 0.0, blitz);
                    Assert.IsTrue(Escolhe(c, FofocaTema.Blitz));
                    InfoRevelada info = c.Reveladas.Single();
                    Assert.IsFalse(info.Verdadeira);

                    if (!string.IsNullOrEmpty(info.Local))
                    {
                        Assert.AreNotEqual("Rua Adolfo Pinheiro", info.Local, "O rumor errado cita um ponto sem blitz.");
                    }
                }
            }
        }

        [Test]
        public void BlitzAccuracyFollowsTheConfiguredRate()
        {
            var gerador = new ConversaEntregadores(new SeededRandom(7), new ConversaConfig { PrecisaoBlitz = 0.8 });
            int certas = 0;
            int total = 0;

            for (int i = 0; i < 3000; i++)
            {
                Conversa c = gerador.Iniciar("g", i, Nomes, Contexto(i % 2 == 0));

                if (!Escolhe(c, FofocaTema.Blitz))
                {
                    continue;
                }

                total++;

                if (c.Reveladas.Single().Verdadeira)
                {
                    certas++;
                }
            }

            double taxa = certas / (double)total;
            Assert.That(taxa, Is.InRange(0.76, 0.84), "Taxa de acerto da dica: " + taxa);
        }

        [Test]
        public void RadarAndRestaurantAndDemandAreAlwaysTrue()
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var gerador = new ConversaEntregadores(new SeededRandom(seed));

                foreach (FofocaTema tema in new[] { FofocaTema.Radar, FofocaTema.Restaurante, FofocaTema.Demanda })
                {
                    Conversa c = gerador.Iniciar("g" + tema, 1, Nomes, Contexto(true));

                    if (!Pergunta(c, tema))
                    {
                        continue;
                    }

                    Assert.IsTrue(c.Reveladas.Last().Verdadeira);
                    string texto = c.Falas.Last(f => f.Tema == tema).Texto;

                    if (tema == FofocaTema.Radar)
                    {
                        StringAssert.Contains("João Dias", texto);
                        StringAssert.Contains("50", texto);
                    }
                    else if (tema == FofocaTema.Restaurante)
                    {
                        StringAssert.Contains("Hamburgueria", texto);
                        StringAssert.Contains("15", texto);
                    }
                    else
                    {
                        StringAssert.Contains("Largo Treze", texto);
                    }
                }
            }
        }

        [Test]
        public void WeatherTalkMatchesTheForecast()
        {
            var gerador = new ConversaEntregadores(new SeededRandom(3));
            int vistas = 0;

            for (int dia = 1; dia <= 60; dia++)
            {
                var seco = new ContextoFofoca();
                var tarde = new ContextoFofoca { ChoveHoje = true };
                var amanha = new ContextoFofoca { ChoveAmanha = true };
                var agora = new ContextoFofoca { ChoveHoje = true, ChovendoAgora = true };
                var casos = new (ContextoFofoca ctx, string[] esperado)[]
                {
                    (seco, FofocaCatalogo.ChuvaNenhuma),
                    (tarde, FofocaCatalogo.ChuvaMaisTarde),
                    (amanha, FofocaCatalogo.ChuvaAmanha),
                    (agora, FofocaCatalogo.ChuvaAgora),
                };

                foreach ((ContextoFofoca ctx, string[] esperado) caso in casos)
                {
                    Conversa c = gerador.Iniciar("g" + vistas, dia, Nomes, caso.ctx);

                    if (Pergunta(c, FofocaTema.Chuva))
                    {
                        vistas++;
                        CollectionAssert.Contains(caso.esperado, c.Falas.Last(f => f.Tema == FofocaTema.Chuva).Texto);
                    }
                }
            }

            Assert.Greater(vistas, 20);
        }

        [Test]
        public void NoRepeatedLinesWithinADayForTheSameGroup()
        {
            for (int seed = 1; seed <= 25; seed++)
            {
                var gerador = new ConversaEntregadores(new SeededRandom(seed));
                var vistas = new HashSet<string>();

                for (int n = 0; n < 3; n++)
                {
                    Conversa c = gerador.Iniciar("praca", 2, Nomes, Contexto(true));
                    int guarda = 0;

                    while (!c.Terminada && guarda++ < 10)
                    {
                        c.Escolher(0);
                    }

                    foreach (FalaConversa fala in c.Falas)
                    {
                        if (fala.Autor >= 0)
                        {
                            Assert.IsTrue(vistas.Add(fala.Texto), "Fala repetida no mesmo dia: " + fala.Texto);
                        }
                    }
                }
            }
        }

        [Test]
        public void ANewDayAllowsLinesAgain()
        {
            var gerador = new ConversaEntregadores(new SeededRandom(5));
            var dia1 = new HashSet<string>();

            for (int n = 0; n < 60; n++)
            {
                foreach (FalaConversa f in gerador.Iniciar("g", 1, Nomes, null).Falas)
                {
                    dia1.Add(f.Texto);
                }
            }

            Assert.AreEqual(FofocaCatalogo.Papo.Length + FofocaCatalogo.Saudacoes.Length, dia1.Count, "Em 60 conversas todo o papo já passou.");
            Assert.AreEqual(2, gerador.Iniciar("g", 2, Nomes, null).Falas.Count);
        }

        [Test]
        public void CatalogHasEnoughVariety()
        {
            Assert.GreaterOrEqual(FofocaCatalogo.Papo.Distinct().Count(), 40);
            int templates = FofocaCatalogo.BlitzEspecifica.Length + FofocaCatalogo.BlitzVaga.Length + FofocaCatalogo.BlitzLimpa.Length +
                            FofocaCatalogo.BlitzFalsa.Length + FofocaCatalogo.Radar.Length + FofocaCatalogo.Demanda.Length +
                            FofocaCatalogo.RestauranteLento.Length + FofocaCatalogo.App.Length + FofocaCatalogo.Gorjeta.Length +
                            FofocaCatalogo.ChuvaAgora.Length + FofocaCatalogo.ChuvaMaisTarde.Length + FofocaCatalogo.ChuvaAmanha.Length;
            Assert.GreaterOrEqual(templates, 40);
        }

        [Test]
        public void ConversationLastsThreeToSixExchangesAndCostsMinutes()
        {
            var vistos = new HashSet<int>();

            for (int seed = 1; seed <= 200; seed++)
            {
                Conversa c = Nova(seed, 0.8, true);
                int minutosAntes = c.MinutosGastos;

                while (!c.Terminada)
                {
                    c.Escolher(0);
                }

                Assert.That(c.Trocas, Is.InRange(3, 6));
                vistos.Add(c.Trocas);
                Assert.Greater(c.MinutosGastos, minutosAntes);
                Assert.IsEmpty(c.Opcoes);
                Assert.AreEqual(FofocaTema.Despedida, c.Falas.Last().Tema);
            }

            Assert.GreaterOrEqual(vistos.Count, 3, "O tamanho da conversa varia.");
        }

        [Test]
        public void LeavingEarlyEndsTheChatWithAGoodbye()
        {
            Conversa c = Nova(2, 0.8, true);
            Sai(c);

            Assert.IsTrue(c.Terminada);
            Assert.AreEqual(0, c.Trocas);
            Assert.AreEqual(FofocaTema.Despedida, c.Falas.Last().Tema);
        }

        [Test]
        public void TipsNeverExceedFiveReais()
        {
            var gerador = new ConversaEntregadores(new SeededRandom(11));
            int vistas = 0;

            for (int dia = 1; dia <= 400; dia++)
            {
                Conversa c = gerador.Iniciar("g", dia, Nomes, null);

                if (!Pergunta(c, FofocaTema.App))
                {
                    continue;
                }

                string texto = c.Falas.Last(f => f.Tema == FofocaTema.App).Texto;
                int pos = texto.IndexOf("R$ ", System.StringComparison.Ordinal);

                if (pos >= 0)
                {
                    vistas++;
                    Assert.That(int.Parse(texto.Substring(pos + 3, 1)), Is.InRange(1, 5));
                }
            }

            Assert.Greater(vistas, 0);
        }

        [Test]
        public void SpeakersAreGroupMembersAndNotTheSameTwiceInARow()
        {
            for (int seed = 1; seed <= 50; seed++)
            {
                Conversa c = Nova(seed, 0.8, true);

                while (!c.Terminada)
                {
                    c.Escolher(0);
                }

                int anterior = -2;

                foreach (FalaConversa f in c.Falas)
                {
                    if (f.Autor < 0)
                    {
                        continue;
                    }

                    Assert.That(f.Autor, Is.InRange(0, Nomes.Length - 1));
                    Assert.AreEqual(Nomes[f.Autor], f.Nome);
                    Assert.AreNotEqual(anterior, f.Autor);
                    anterior = f.Autor;
                }
            }
        }

        [Test]
        public void ASingleCourierCanTalkToo()
        {
            var gerador = new ConversaEntregadores(new SeededRandom(4));
            Conversa c = gerador.Iniciar("g", 1, new[] { "Dudu" }, Contexto(true));

            while (!c.Terminada)
            {
                c.Escolher(0);
            }

            Assert.IsTrue(c.Falas.Where(f => f.Autor >= 0).All(f => f.Nome == "Dudu"));
        }
    }
}
