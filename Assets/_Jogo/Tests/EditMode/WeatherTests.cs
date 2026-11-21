using System.Linq;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class WeatherTests
    {
        [Test]
        public void SorteiaExatamenteODiasDeChuva()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                bool[] week = Weather.RollWeek(new SeededRandom(seed), 7, 2);

                Assert.AreEqual(7, week.Length);
                Assert.AreEqual(2, week.Count(d => d));
            }
        }

        [Test]
        public void ZeroEMaximoDeDiasChuvosos()
        {
            Assert.AreEqual(0, Weather.RollWeek(new SeededRandom(1), 7, 0).Count(d => d));
            Assert.AreEqual(7, Weather.RollWeek(new SeededRandom(1), 7, 7).Count(d => d));
        }

        [Test]
        public void MesmaSemente_MesmaSemana()
        {
            CollectionAssert.AreEqual(
                Weather.RollWeek(new SeededRandom(42), 7, 2),
                Weather.RollWeek(new SeededRandom(42), 7, 2));
        }

        [Test]
        public void SementesDiferentes_VariamOsDias()
        {
            var distinct = Enumerable.Range(0, 50)
                .Select(seed => string.Join(",", Weather.RollWeek(new SeededRandom(seed), 7, 2)))
                .Distinct()
                .Count();

            Assert.Greater(distinct, 5);
        }

        [Test]
        public void TodosOsDiasPodemChover()
        {
            var seen = new bool[7];

            for (int seed = 0; seed < 200; seed++)
            {
                bool[] week = Weather.RollWeek(new SeededRandom(seed), 7, 2);

                for (int i = 0; i < 7; i++)
                {
                    seen[i] |= week[i];
                }
            }

            Assert.IsTrue(seen.All(s => s));
        }
    }
}
