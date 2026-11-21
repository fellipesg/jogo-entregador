using System;

namespace Entregador.Core
{
    /// <summary>
    /// Fonte de sorte do jogo. As regras nunca usam Random direto, para os testes poderem fixar o resultado.
    /// </summary>
    public interface IGameRandom
    {
        /// <summary>Número entre 0 (incluso) e 1 (excluso).</summary>
        double NextDouble();
    }

    public sealed class SeededRandom : IGameRandom
    {
        private readonly Random _random;

        public SeededRandom(int seed)
        {
            _random = new Random(seed);
        }

        public double NextDouble() => _random.NextDouble();
    }

    public static class GameRandomExtensions
    {
        public static bool Chance(this IGameRandom random, double probability) =>
            random.NextDouble() < probability;

        /// <summary>Número entre min e max, contínuo.</summary>
        public static double Between(this IGameRandom random, double min, double max) =>
            min + (max - min) * random.NextDouble();

        /// <summary>Inteiro entre minInclusive e maxExclusive.</summary>
        public static int Range(this IGameRandom random, int minInclusive, int maxExclusive)
        {
            int value = minInclusive + (int)Math.Floor(random.NextDouble() * (maxExclusive - minInclusive));

            return Math.Min(value, maxExclusive - 1);
        }
    }
}
