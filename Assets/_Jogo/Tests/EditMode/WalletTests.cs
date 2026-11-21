using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class WalletTests
    {
        [Test]
        public void SaldoInicial()
        {
            var wallet = new Wallet(Money.Reais(200));

            Assert.AreEqual(Money.Reais(200), wallet.Balance);
            Assert.IsFalse(wallet.IsInDebt);
            Assert.AreEqual(0, wallet.Entries.Count);
        }

        [Test]
        public void CreditoEDebito_AtualizamSaldoEExtrato()
        {
            var wallet = new Wallet(Money.Reais(200));

            wallet.Credit(1, LedgerCategory.Entrega, Money.Reais(15), "Entrega");
            wallet.Debit(1, LedgerCategory.Combustivel, Money.Reais(1), "Gasolina");

            Assert.AreEqual(Money.Reais(214), wallet.Balance);
            Assert.AreEqual(2, wallet.Entries.Count);
            Assert.AreEqual(Money.Reais(-1), wallet.Entries[1].Amount);
            Assert.AreEqual(LedgerCategory.Combustivel, wallet.Entries[1].Category);
        }

        [Test]
        public void SaldoPodeFicarNegativo()
        {
            var wallet = new Wallet(Money.Reais(10));

            wallet.Debit(7, LedgerCategory.Aluguel, Money.Reais(350), "Aluguel");

            Assert.IsTrue(wallet.IsInDebt);
            Assert.AreEqual(Money.Reais(-340), wallet.Balance);
        }

        [Test]
        public void NetForDay_SomaSoOsLancamentosDoDia()
        {
            var wallet = new Wallet(Money.Zero);

            wallet.Credit(1, LedgerCategory.Entrega, Money.Reais(15), "a");
            wallet.Debit(1, LedgerCategory.Combustivel, Money.Reais(5), "b");
            wallet.Credit(2, LedgerCategory.Entrega, Money.Reais(20), "c");

            Assert.AreEqual(Money.Reais(10), wallet.NetForDay(1));
            Assert.AreEqual(Money.Reais(20), wallet.NetForDay(2));
            Assert.AreEqual(Money.Zero, wallet.NetForDay(3));
        }
    }
}
