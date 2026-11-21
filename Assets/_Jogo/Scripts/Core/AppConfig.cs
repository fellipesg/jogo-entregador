namespace Entregador.Core
{
    /// <summary>
    /// Regras de um app de entrega. Os valores padrão são os do Rangô na fatia vertical (GDD, seção Economia).
    /// Na Unity, um ScriptableObject preenche esta classe para ajustar os números sem mexer no código.
    /// </summary>
    public sealed class AppConfig
    {
        public string Name = "Rangô";

        public Money BasePay = Money.Reais(8);
        public Money PayPerKm = Money.Reais(2);
        public decimal RainPayBonus = 0.30m;
        public decimal SkipPayRatio = 0.85m;

        public double MinOrderKm = 1.5;
        public double MaxOrderKm = 5.5;

        public int OrdersPerShift = 3;
        public int ReducedOrdersPerShift = 2;
        public double AcceptanceThreshold = 0.80;

        public int RatingWindow = 20;
        public double BlockBelowRating = 4.3;

        public int GoalDeliveries = 20;
        public Money GoalBonus = Money.Reais(80);

        public Money MaxTip = Money.Reais(5);

        /// <summary>Gorjeta máxima quando a nota é 4 estrelas.</summary>
        public Money MaxTipGood = Money.Reais(2);
    }
}
