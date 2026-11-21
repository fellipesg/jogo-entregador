namespace Entregador.Core
{
    /// <summary>O que uma entrega concluída rendeu. O imprevisto pós-entrega, se houver, vem junto para a cena mostrar.</summary>
    public sealed class DeliveryResult
    {
        public DeliveryResult(Order order, Money pay, Money tip, int stars, bool spoiled, bool skipped, Incident afterDeliveryIncident)
        {
            Order = order;
            Pay = pay;
            Tip = tip;
            Stars = stars;
            Spoiled = spoiled;
            Skipped = skipped;
            AfterDeliveryIncident = afterDeliveryIncident;
        }

        public Order Order { get; }

        public Money Pay { get; }

        public Money Tip { get; }

        public Money Total => Pay + Tip;

        public int Stars { get; }

        /// <summary>A comida chegou estragada: sem pagamento e nota 1.</summary>
        public bool Spoiled { get; }

        /// <summary>A corrida foi pulada (entrega automática).</summary>
        public bool Skipped { get; }

        public Incident AfterDeliveryIncident { get; }
    }

    public enum WeekResult
    {
        SemanaBoa,
        Sobreviveu,
        NoVermelho,
    }

    /// <summary>Fechamento da semana: bônus, multas, aluguel, caução devolvida e o veredito.</summary>
    public sealed class WeekReport
    {
        public WeekReport(int deliveries, Money bonus, Money fines, Money rent, Money depositRefund, Money finalBalance, double rating, WeekResult result)
        {
            Deliveries = deliveries;
            Bonus = bonus;
            Fines = fines;
            Rent = rent;
            DepositRefund = depositRefund;
            FinalBalance = finalBalance;
            Rating = rating;
            Result = result;
        }

        public int Deliveries { get; }

        public Money Bonus { get; }

        public Money Fines { get; }

        public Money Rent { get; }

        public Money DepositRefund { get; }

        public Money FinalBalance { get; }

        public double Rating { get; }

        public WeekResult Result { get; }
    }
}
