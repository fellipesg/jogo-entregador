namespace Entregador.Core
{
    public sealed class Order
    {
        public Order(int id, double distanceKm, Money pay, bool isRaining)
        {
            Id = id;
            DistanceKm = distanceKm;
            Pay = pay;
            IsRaining = isRaining;
        }

        public int Id { get; }

        /// <summary>Distância do restaurante até o cliente.</summary>
        public double DistanceKm { get; }

        /// <summary>Valor oferecido pelo app, já com o adicional de chuva.</summary>
        public Money Pay { get; }

        public bool IsRaining { get; }
    }
}
