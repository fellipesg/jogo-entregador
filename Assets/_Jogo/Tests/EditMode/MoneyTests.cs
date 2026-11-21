using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class MoneyTests
    {
        [Test]
        public void ToString_FormataEmRealBrasileiro()
        {
            Assert.AreEqual("R$ 1.234,56", Money.Reais(1234.56m).ToString());
            Assert.AreEqual("R$ 1.234.567,89", Money.Reais(1234567.89m).ToString());
            Assert.AreEqual("R$ 0,00", Money.Zero.ToString());
            Assert.AreEqual("R$ 0,05", new Money(5).ToString());
            Assert.AreEqual("R$ 15,00", Money.Reais(15).ToString());
        }

        [Test]
        public void ToString_Negativo()
        {
            Assert.AreEqual("-R$ 12,00", Money.Reais(-12).ToString());
            Assert.AreEqual("-R$ 1.000,50", new Money(-100050).ToString());
        }

        [Test]
        public void Reais_ArredondaMeioParaLongeDoZero()
        {
            Assert.AreEqual(1, Money.Reais(0.005m).Cents);
            Assert.AreEqual(-1, Money.Reais(-0.005m).Cents);
            Assert.AreEqual(1234, Money.Reais(12.344m).Cents);
        }

        [Test]
        public void Multiplicacao_ArredondaEmCentavos()
        {
            Assert.AreEqual(Money.Reais(12.75m), Money.Reais(15) * 0.85m);
            Assert.AreEqual(1, (new Money(1) * 0.5m).Cents);
            Assert.AreEqual(-1, (new Money(-1) * 0.5m).Cents);
            Assert.AreEqual(Money.Reais(19.5m), Money.Reais(15) * 1.3m);
        }

        [Test]
        public void Operadores_SomaSubtracaoNegacao()
        {
            Assert.AreEqual(Money.Reais(7), Money.Reais(10) - Money.Reais(3));
            Assert.AreEqual(Money.Reais(13), Money.Reais(10) + Money.Reais(3));
            Assert.AreEqual(Money.Reais(-3), -Money.Reais(3));
            Assert.IsTrue(Money.Reais(-1).IsNegative);
            Assert.IsFalse(Money.Zero.IsNegative);
        }

        [Test]
        public void Comparacao()
        {
            Assert.IsTrue(Money.Reais(2) > Money.Reais(1));
            Assert.IsTrue(Money.Reais(1) <= Money.Reais(1));
            Assert.IsTrue(Money.Reais(1) == new Money(100));
            Assert.IsTrue(Money.Reais(1) != new Money(101));
            Assert.AreEqual(Money.Reais(1).GetHashCode(), new Money(100).GetHashCode());
        }
    }
}
