using System;

namespace Entregador.Core
{
    /// <summary>
    /// Contrato de aluguel da moto. O dano vai de 0 (inteira) a 1 (perda total) e desconta da caução.
    /// </summary>
    public sealed class VehicleRental
    {
        public VehicleRental(Money deposit, Money weeklyRent)
        {
            Deposit = deposit;
            WeeklyRent = weeklyRent;
        }

        public Money Deposit { get; }

        public Money WeeklyRent { get; }

        public double Damage { get; private set; }

        public void AddDamage(double amount)
        {
            Damage = Math.Max(0, Math.Min(1, Damage + amount));
        }

        public Money DepositRefund => Deposit * (decimal)(1 - Damage);
    }
}
