using NUnit.Framework;

namespace Entregador.Core.Tests
{
    public class VehicleRentalTests
    {
        private static VehicleRental NewRental() => new VehicleRental(Money.Reais(100), Money.Reais(350));

        [Test]
        public void SemDano_DevolveACaucaoInteira()
        {
            VehicleRental rental = NewRental();

            Assert.AreEqual(0.0, rental.Damage, 0.0001);
            Assert.AreEqual(Money.Reais(100), rental.DepositRefund);
        }

        [Test]
        public void Dano_DescontaProporcionalmenteDaCaucao()
        {
            VehicleRental rental = NewRental();

            rental.AddDamage(0.25);
            rental.AddDamage(0.25);

            Assert.AreEqual(Money.Reais(50), rental.DepositRefund);
        }

        [Test]
        public void Dano_FicaEntre0e1()
        {
            VehicleRental rental = NewRental();

            rental.AddDamage(0.8);
            rental.AddDamage(0.8);

            Assert.AreEqual(1.0, rental.Damage, 0.0001);
            Assert.AreEqual(Money.Zero, rental.DepositRefund);

            rental.AddDamage(-5);

            Assert.AreEqual(0.0, rental.Damage, 0.0001);
        }

        [Test]
        public void Valores_SaoOsDoContrato()
        {
            Assert.AreEqual(Money.Reais(100), NewRental().Deposit);
            Assert.AreEqual(Money.Reais(350), NewRental().WeeklyRent);
        }
    }
}
