using System;
using System.Collections.Generic;

namespace Entregador.Core
{
    /// <summary>
    /// O que vale para o dia inteiro e que os entregadores comentam: zonas quentes (OfertaAgenda) e o restaurante lento. Tudo sai de uma
    /// escolha determinística por dia, então o que a roda de conversa conta é exatamente o que acontece no balcão e no mapa.
    /// </summary>
    public static class MovimentoDoDia
    {
        /// <summary>Hash estável de texto (string.GetHashCode não é estável entre execuções).</summary>
        public static int Hash(string texto)
        {
            unchecked
            {
                int h = (int)2166136261;

                foreach (char c in texto ?? string.Empty)
                {
                    h = (h ^ c) * 16777619;
                }

                return h & 0x7fffffff;
            }
        }

        /// <summary>Gerador do dia, sempre com a mesma semente para o mesmo dia e assunto.</summary>
        public static IGameRandom Aleatorio(int dia, int assunto)
        {
            return new SeededRandom(dia * 7919 + assunto * 104729 + 17);
        }

        /// <summary>As zonas quentes do dia (1 a 3), iguais a cada chamada.</summary>
        public static List<ZonaQuente> ZonasDoDia(int dia, IReadOnlyList<ZonaQuente> candidatas)
        {
            return ZonasQuentes.Sortear(Aleatorio(dia, 1), candidatas, 1, 3);
        }

        /// <summary>Índice do restaurante lento do dia (nunca um dos nomes em evitar, se houver outro), ou -1.</summary>
        public static int IndiceLento(int dia, IReadOnlyList<string> restaurantes, ICollection<string> evitar = null)
        {
            if (restaurantes == null || restaurantes.Count < 2)
            {
                return -1;
            }

            int inicio = Hash("lento" + dia) % restaurantes.Count;

            for (int i = 0; i < restaurantes.Count; i++)
            {
                int k = (inicio + i) % restaurantes.Count;

                if (evitar == null || !evitar.Contains(restaurantes[k]))
                {
                    return k;
                }
            }

            return -1;
        }

        /// <summary>Minutos de espera típicos no restaurante lento do dia (10 a 20).</summary>
        public static int MinutosDeEspera(int dia, string restaurante) => 10 + Hash("espera" + dia + restaurante) % 11;

        /// <summary>
        /// Configuração do balcão do restaurante no dia: no lento, "tá saindo" e fila ficam bem mais prováveis e as esperas giram em
        /// torno de MinutosDeEspera; nos outros, esses casos ficam mais raros.
        /// </summary>
        public static RetiradaConfig ConfigDoBalcao(int dia, string restaurante, IReadOnlyList<string> todos, ICollection<string> evitar = null)
        {
            var config = new RetiradaConfig();
            int lento = IndiceLento(dia, todos, evitar);

            if (lento < 0)
            {
                return config;
            }

            if (todos[lento] == restaurante)
            {
                int m = MinutosDeEspera(dia, restaurante);
                config.PesoSaindo *= 3;
                config.PesoLotado *= 3;
                config.PesoPronto *= 0.5;
                config.EsperaMin = Math.Max(1, m - 2);
                config.EsperaMax = m + 2;
                config.FilaMin = Math.Max(1, m - 2);
                config.FilaMax = m + 2;
            }
            else
            {
                config.PesoSaindo *= 0.5;
                config.PesoLotado *= 0.4;
            }

            return config;
        }
    }
}
