using System.Collections.Generic;
using Entregador.Core;
using NUnit.Framework;

namespace Entregador.Tests
{
    public sealed class MovimentoDoDiaTests
    {
        private static readonly string[] Nomes = { "A", "B", "C", "D" };

        [Test]
        public void ZonasDoDiaSaoIguaisACadaChamada()
        {
            var cand = new List<ZonaQuente>();

            foreach (string n in Nomes)
            {
                cand.Add(new ZonaQuente(n, n[0], 0, 90));
            }

            for (int dia = 1; dia <= 7; dia++)
            {
                var a = MovimentoDoDia.ZonasDoDia(dia, cand);
                var b = MovimentoDoDia.ZonasDoDia(dia, cand);
                Assert.AreEqual(a.Count, b.Count);

                for (int i = 0; i < a.Count; i++)
                {
                    Assert.AreEqual(a[i].Nome, b[i].Nome);
                }
            }
        }

        [Test]
        public void RestauranteLentoEstavelEFogeDasZonas()
        {
            for (int dia = 1; dia <= 7; dia++)
            {
                int i = MovimentoDoDia.IndiceLento(dia, Nomes, new HashSet<string> { "A", "B" });
                Assert.AreEqual(i, MovimentoDoDia.IndiceLento(dia, Nomes, new HashSet<string> { "A", "B" }));
                Assert.That(Nomes[i], Is.EqualTo("C").Or.EqualTo("D"));
                int m = MovimentoDoDia.MinutosDeEspera(dia, Nomes[i]);
                Assert.That(m, Is.InRange(10, 20));
            }
        }

        [Test]
        public void BalcaoDoLentoTemMaisFilaEEsperaPerto()
        {
            int dia = 3;
            int lento = MovimentoDoDia.IndiceLento(dia, Nomes);
            string outro = Nomes[(lento + 1) % Nomes.Length];
            RetiradaConfig cLento = MovimentoDoDia.ConfigDoBalcao(dia, Nomes[lento], Nomes);
            RetiradaConfig cOutro = MovimentoDoDia.ConfigDoBalcao(dia, outro, Nomes);
            int m = MovimentoDoDia.MinutosDeEspera(dia, Nomes[lento]);
            Assert.Greater(cLento.PesoSaindo, cOutro.PesoSaindo * 3);
            Assert.Greater(cLento.PesoLotado, cOutro.PesoLotado);
            Assert.That(cLento.EsperaMin, Is.InRange(m - 2, m + 2));
            Assert.That(cLento.EsperaMax, Is.InRange(m - 2, m + 2));
        }
    }
}
