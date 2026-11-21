using System.Collections.Generic;
using Entregador.Core;

namespace Entregador.Core.Tests
{
    /// <summary>
    /// Sorte controlada: devolve os valores enfileirados na ordem e, quando acabam, um valor constante.
    /// Com o padrão 0,5: pedidos de 3,5 km, nenhum imprevisto, gorjeta no meio do caminho.
    /// </summary>
    public sealed class FakeRandom : IGameRandom
    {
        private readonly Queue<double> _queue = new Queue<double>();
        private readonly double _fallback;

        public FakeRandom(double fallback = 0.5, params double[] queued)
        {
            _fallback = fallback;

            foreach (double value in queued)
            {
                _queue.Enqueue(value);
            }
        }

        public int Remaining => _queue.Count;

        public FakeRandom Enqueue(params double[] values)
        {
            foreach (double value in values)
            {
                _queue.Enqueue(value);
            }

            return this;
        }

        public double NextDouble() => _queue.Count > 0 ? _queue.Dequeue() : _fallback;
    }
}
