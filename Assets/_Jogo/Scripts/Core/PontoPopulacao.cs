using System;

namespace Entregador.Core
{
    /// <summary>Quantos entregadores ficam num ponto, conforme a hora do dia e a chuva (picos no almoço e no jantar, menos gente na chuva).</summary>
    public static class PontoPopulacao
    {
        public const int Maximo = 6;
        public const int Minimo = 2;

        /// <summary>Hora (0 a 24) negativa quer dizer "não sei": usa o movimento médio.</summary>
        public static int Quantidade(float hora, bool chovendo, double sorte)
        {
            double baseado;

            if (hora < 0f)
            {
                baseado = 4.2;
            }
            else if ((hora >= 11f && hora < 14.5f) || (hora >= 18f && hora < 21.5f))
            {
                baseado = 5.4;
            }
            else if (hora >= 6f && hora < 11f)
            {
                baseado = 3.6;
            }
            else if (hora >= 14.5f && hora < 18f)
            {
                baseado = 4.0;
            }
            else
            {
                baseado = 2.8;
            }

            if (chovendo)
            {
                baseado -= 1.3;
            }

            int valor = (int)Math.Round(baseado + (Math.Max(0.0, Math.Min(1.0, sorte)) - 0.5) * 1.6);

            return Math.Max(Minimo, Math.Min(Maximo, valor));
        }
    }
}
