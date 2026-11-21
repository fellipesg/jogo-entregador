namespace Entregador.Core
{
    /// <summary>
    /// Números do trânsito e do começo do jogo: moto bob, blitz, fuga, pátio e multas de radar.
    /// Os valores e pontos das infrações ficam em CodigoTransito (são os do CTB).
    /// </summary>
    public sealed class TransitoConfig
    {
        // Moto "bob": comprada barata do "cara da moto", sem documentos.
        public Money BobEntrada = Money.Reais(150);
        public Money BobParcelaSemanal = Money.Reais(100);
        public int BobParcelas = 8;

        // Multa de radar: chega pelo app/correio de 2 a 5 dias depois; a locadora cobra uma taxa para indicar o condutor.
        public int RadarChegadaMinDias = 2;
        public int RadarChegadaMaxDias = 5;
        public Money TaxaIndicacao = Money.Reais(20);

        // Blitz do dia (a Game coloca no mapa). Fim de semana (dias 6 e 7, com o dia 1 sendo segunda) tem mais.
        public double BlitzChance = 0.35;
        public double BlitzChanceFimDeSemana = 0.50;

        // Abordagem
        public double FugaEscapaChance = 0.45;
        public double PlacaAnotadaChance = 0.30;
        public double ProblemaMenorChance = 0.20;
        public int ParadaDelayMinMinutos = 10;
        public int ParadaDelayMaxMinutos = 20;
        public int FugaPegaDelayMinutos = 60;

        // Pátio da moto alugada
        public Money Guincho = Money.Reais(180);
        public Money DiariaPatio = Money.Reais(45);
    }
}
