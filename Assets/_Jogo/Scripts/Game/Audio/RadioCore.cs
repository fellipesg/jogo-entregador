using System;

namespace Entregador.Game
{
    /// <summary>Lógica pura do rádio "ao vivo": playlist embaralhada por ciclo e posição que avança com o tempo, como no GTA.</summary>
    public static class RadioCore
    {
        /// <summary>Ordem embaralhada (Fisher-Yates) dos n itens, determinística para a semente.</summary>
        public static int[] Embaralhar(int n, uint seed)
        {
            var order = new int[n];

            for (int i = 0; i < n; i++)
            {
                order[i] = i;
            }

            var rng = new AudioRng(seed * 2654435761u + 1u);

            for (int i = n - 1; i > 0; i--)
            {
                int j = (int)(rng.Value() * (i + 1));

                if (j > i)
                {
                    j = i;
                }

                int tmp = order[i];
                order[i] = order[j];
                order[j] = tmp;
            }

            return order;
        }

        /// <summary>
        /// Onde a estação "está" no instante tempo (segundos desde que o rádio existe, já com o deslocamento da estação).
        /// Devolve o índice da faixa (na lista original) e o ponto dentro dela. Cada ciclo (soma das durações) tem uma ordem nova.
        /// </summary>
        public static void Localizar(float[] duracoes, uint semente, double tempo, out int faixa, out float offset)
        {
            faixa = -1;
            offset = 0f;

            if (duracoes == null || duracoes.Length == 0)
            {
                return;
            }

            double total = 0;

            foreach (float d in duracoes)
            {
                total += Math.Max(0.01f, d);
            }

            if (tempo < 0)
            {
                tempo = 0;
            }

            long ciclo = (long)Math.Floor(tempo / total);
            double t = tempo - ciclo * total;
            int[] ordem = Embaralhar(duracoes.Length, semente + (uint)ciclo);

            for (int i = 0; i < ordem.Length; i++)
            {
                float d = Math.Max(0.01f, duracoes[ordem[i]]);

                if (t < d || i == ordem.Length - 1)
                {
                    faixa = ordem[i];
                    offset = (float)Math.Min(t, d - 0.01);

                    return;
                }

                t -= d;
            }
        }

        /// <summary>
        /// Estação seguinte numa roda de n estações mais o "desligado" (-1) no fim: -1 -> 0 -> ... -> n-1 -> -1.
        /// </summary>
        public static int Proxima(int atual, int n)
        {
            if (n <= 0)
            {
                return -1;
            }

            return atual >= n - 1 ? -1 : atual + 1;
        }

        public static int Anterior(int atual, int n)
        {
            if (n <= 0)
            {
                return -1;
            }

            return atual < 0 ? n - 1 : atual - 1;
        }
    }
}
