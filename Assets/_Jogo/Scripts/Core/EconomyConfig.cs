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

        /// <summary>Trânsito, moto bob, blitz e pátio.</summary>
        public TransitoConfig Transito = new TransitoConfig();

        public int DaysPerWeek = 7;
        public int RainyDaysPerWeek = 2;

        /// <summary>Tempo previsto de corrida por km, usado quando o jogador pula a corrida.</summary>
        public double MinutesPerKm = 4;

        /// <summary>
        /// Orçamento de tempo do turno em minutos de jogo (4 h = uns 32 min reais, ver RelogioJogo). O turno acaba quando o app já ofereceu os
        /// pedidos dele (AppConfig.OrdersPerShift) e tudo foi entregue, OU quando este tempo se esgota (o pedido em andamento ainda termina).
        /// </summary>
        public int ShiftMinutes = 240;

        public double GoodWeekMinRating = 4.7;
    }
}
