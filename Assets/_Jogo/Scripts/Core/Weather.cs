namespace Entregador.Core
{
    public static class Weather
    {
        /// <summary>Sorteia quais dias da semana chovem, sem repetir dia.</summary>
        public static bool[] RollWeek(IGameRandom random, int days, int rainyDays)
        {
            var order = new int[days];

            for (int i = 0; i < days; i++)
            {
                order[i] = i;
            }

            // Fisher-Yates: embaralha os dias e pega os primeiros.
            for (int i = days - 1; i > 0; i--)
            {
                int j = random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var rain = new bool[days];

            for (int i = 0; i < rainyDays && i < days; i++)
            {
                rain[order[i]] = true;
            }

            return rain;
        }
    }
}
