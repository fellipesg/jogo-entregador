using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class RatingRulesTests
    {
        [Test]
        public void NoHorario_Da5()
        {
            Assert.AreEqual(5, RatingRules.Stars(RunReport.OnTime(3), false, false));
        }

        [TestCase(1.2, 5)]
        [TestCase(1.3, 4)]
        [TestCase(1.5, 4)]
        [TestCase(1.6, 3)]
        public void Atraso_TiraEstrelas(double ratio, int expected)
        {
            var report = new RunReport { TimeRatio = ratio };

            Assert.AreEqual(expected, RatingRules.Stars(report, false, false));
        }

        [TestCase(0.9, 5)]
        [TestCase(0.6, 4)]
        [TestCase(0.3, 3)]
        public void EstadoDaComida_TiraEstrelas(double food, int expected)
        {
            var report = new RunReport { FoodCondition = food };

            Assert.AreEqual(expected, RatingRules.Stars(report, false, false));
        }

        [Test]
        public void Chuva_SemCapaTira1_ComCapaNao()
        {
            RunReport report = RunReport.OnTime(3);

            Assert.AreEqual(4, RatingRules.Stars(report, true, false));
            Assert.AreEqual(5, RatingRules.Stars(report, true, true));
            Assert.AreEqual(5, RatingRules.Stars(report, false, false));
        }

        [Test]
        public void ComidaEstragada_Da1()
        {
            var report = new RunReport { FoodCondition = 0.19 };

            Assert.IsTrue(RatingRules.IsSpoiled(report));
            Assert.AreEqual(1, RatingRules.Stars(report, false, false));
            Assert.IsFalse(RatingRules.IsSpoiled(new RunReport { FoodCondition = 0.2 }));
        }

        [Test]
        public void Nota_NuncaFicaAbaixoDe1()
        {
            var report = new RunReport { TimeRatio = 2, FoodCondition = 0.3 };

            Assert.AreEqual(1, RatingRules.Stars(report, true, false));
        }
    }
}
