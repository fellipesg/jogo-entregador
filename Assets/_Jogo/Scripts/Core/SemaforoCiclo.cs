using System;

namespace Entregador.Core
{
    public enum SemaforoEstado
    {
        Verde,
        Amarelo,
        Vermelho,
    }

    /// <summary>Tempos de um cruzamento de dois eixos (grupo 0 e grupo 1), em segundos.</summary>
    public sealed class SemaforoConfig
    {
        /// <summary>Verde do grupo 0 (o eixo da via maior).</summary>
        public double VerdeA = 26;

        /// <summary>Verde do grupo 1.</summary>
        public double VerdeB = 22;

        /// <summary>Amarelo (3 s no Brasil).</summary>
        public double Amarelo = 3;

        /// <summary>Todos vermelhos entre uma fase e a outra.</summary>
        public double TodoVermelho = 1.5;

        /// <summary>Quanto o ciclo está adiantado em relação ao relógio (cada cruzamento tem o seu).</summary>
        public double Deslocamento;

        /// <summary>Duração total do ciclo.</summary>
        public double Duracao => VerdeA + VerdeB + 2 * (Amarelo + TodoVermelho);
    }

    /// <summary>
    /// Ciclo puro de um semáforo de dois grupos: verde do A, amarelo, todos vermelhos, verde do B, amarelo, todos vermelhos.
    /// Dois grupos conflitantes nunca ficam verdes (nem amarelos) ao mesmo tempo.
    /// </summary>
    public static class SemaforoCiclo
    {
        /// <summary>Tempo (s) dentro do ciclo do grupo, com o verde do grupo começando em 0.</summary>
        private static double Local(SemaforoConfig config, double tempo, int grupo)
        {
            double duracao = config.Duracao;
            double t = tempo + config.Deslocamento;

            if (grupo == 1)
            {
                t -= config.VerdeA + config.Amarelo + config.TodoVermelho;
            }

            t %= duracao;

            return t < 0 ? t + duracao : t;
        }

        private static double Verde(SemaforoConfig config, int grupo) => grupo == 0 ? config.VerdeA : config.VerdeB;

        public static SemaforoEstado Estado(SemaforoConfig config, double tempo, int grupo)
        {
            double local = Local(config, tempo, grupo);
            double verde = Verde(config, grupo);

            if (local < verde)
            {
                return SemaforoEstado.Verde;
            }

            return local < verde + config.Amarelo ? SemaforoEstado.Amarelo : SemaforoEstado.Vermelho;
        }

        /// <summary>Há quantos segundos o grupo está no estado atual.</summary>
        public static double SegundosNoEstado(SemaforoConfig config, double tempo, int grupo)
        {
            double local = Local(config, tempo, grupo);
            double verde = Verde(config, grupo);

            if (local < verde)
            {
                return local;
            }

            return local < verde + config.Amarelo ? local - verde : local - verde - config.Amarelo;
        }

        /// <summary>Quantos segundos faltam para o grupo mudar de estado.</summary>
        public static double SegundosParaMudar(SemaforoConfig config, double tempo, int grupo)
        {
            double local = Local(config, tempo, grupo);
            double verde = Verde(config, grupo);

            if (local < verde)
            {
                return verde - local;
            }

            if (local < verde + config.Amarelo)
            {
                return verde + config.Amarelo - local;
            }

            return config.Duracao - local;
        }

        /// <summary>Segundos desde que o grupo ficou vermelho, ou -1 se não está vermelho.</summary>
        public static double SegundosDesdeVermelho(SemaforoConfig config, double tempo, int grupo) =>
            Estado(config, tempo, grupo) == SemaforoEstado.Vermelho ? SegundosNoEstado(config, tempo, grupo) : -1;

        /// <summary>
        /// Instante (dentro do ciclo, sem deslocamento) em que o grupo entra no estado, com "dentro" segundos de folga.
        /// Os testes e o QA usam para fixar um cruzamento num estado conhecido.
        /// </summary>
        public static double InicioDoEstado(SemaforoConfig config, int grupo, SemaforoEstado estado)
        {
            double inicio = grupo == 0 ? 0 : config.VerdeA + config.Amarelo + config.TodoVermelho;
            double verde = Verde(config, grupo);

            switch (estado)
            {
                case SemaforoEstado.Verde:
                    return inicio;

                case SemaforoEstado.Amarelo:
                    return inicio + verde;

                default:
                    return inicio + verde + config.Amarelo;
            }
        }
    }

    /// <summary>Regras de quem obedece (ou não) o semáforo: infração de avanço e a decisão do carro de parar.</summary>
    public static class SemaforoRegras
    {
        /// <summary>Tolerância depois que o sinal fecha: cruzar a linha até 0,5 s depois não é infração no jogo.</summary>
        public const double GracaVermelhoSegundos = 0.5;

        /// <summary>Desaceleração confortável (m/s2) com que um carro decide se consegue parar.</summary>
        public const double FreadaConfortavel = 4.5;

        /// <summary>Cruzar a linha de retenção avançou o sinal? Só no vermelho (nunca no amarelo), passada a tolerância.</summary>
        public static bool AvancouVermelho(SemaforoEstado estado, double segundosDesdeVermelho) =>
            estado == SemaforoEstado.Vermelho && segundosDesdeVermelho >= GracaVermelhoSegundos;

        /// <summary>A infração do avanço, ou null se cruzou em verde, em amarelo ou dentro da tolerância.</summary>
        public static Infracao? ClassificarAvanco(SemaforoEstado estado, double segundosDesdeVermelho) =>
            AvancouVermelho(estado, segundosDesdeVermelho) ? Infracao.AvancarSinalVermelho : (Infracao?)null;

        /// <summary>Dá para parar na linha a essa velocidade (m/s) e distância (m) com a freada confortável?</summary>
        public static bool PodeParar(double velocidade, double distancia) =>
            distancia >= velocidade * velocidade / (2 * FreadaConfortavel);

        /// <summary>O carro deve parar na linha? Vermelho sempre; amarelo só se consegue parar com segurança; verde nunca.</summary>
        public static bool DeveParar(SemaforoEstado estado, double velocidade, double distancia)
        {
            switch (estado)
            {
                case SemaforoEstado.Vermelho:
                    return true;

                case SemaforoEstado.Amarelo:
                    return PodeParar(velocidade, distancia);

                default:
                    return false;
            }
        }

        /// <summary>Velocidade (m/s) para chegar parado a "distancia" metros da linha, desacelerando a "freada" m/s2.</summary>
        public static double VelocidadeParaParar(double distancia, double freada) =>
            distancia <= 0 ? 0 : Math.Sqrt(2 * freada * distancia);
    }
}
