namespace Entregador.Core
{
    /// <summary>
    /// Preços e regras fora do app: dinheiro inicial, aluguel da moto, combustível e custos dos imprevistos.
    /// Os valores padrão são os do GDD, para ajustar nos testes com jogadores.
    /// </summary>
    public sealed class EconomyConfig
    {
        public Money StartingMoney = Money.Reais(200);

        public Money RentalDeposit = Money.Reais(100);
        public Money WeeklyRent = Money.Reais(350);
        public Money FuelPerKm = Money.Reais(0.25m);

        public Money FlatTireRepair = Money.Reais(40);
        public Money Fine = Money.Reais(50);
        public Money Raincoat = Money.Reais(30);

        public int DaysPerWeek = 7;
        public int RainyDaysPerWeek = 2;

        /// <summary>Tempo previsto de corrida por km, usado quando o jogador pula a corrida.</summary>
        public double MinutesPerKm = 4;

        public double GoodWeekMinRating = 4.7;
    }
}
