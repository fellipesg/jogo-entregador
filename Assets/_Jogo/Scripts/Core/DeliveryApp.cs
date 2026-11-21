using System;
using System.Collections.Generic;
using System.Linq;

namespace Entregador.Core
{
    /// <summary>
    /// A conta do jogador num app: pedidos oferecidos, nota, taxa de aceite, bloqueio e meta da semana.
    /// </summary>
    public sealed class DeliveryApp
    {
        private readonly Queue<int> _ratings = new Queue<int>();
        private int _nextOrderId = 1;

        public DeliveryApp(AppConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));

            // A nota começa em 5,0: a janela já vem cheia de cincos, e cada nota nova empurra um deles para fora.
            for (int i = 0; i < config.RatingWindow; i++)
            {
                _ratings.Enqueue(5);
            }
        }

        public AppConfig Config { get; }

        public int Offered { get; private set; }

        public int Accepted { get; private set; }

        public int DeliveriesThisWeek { get; private set; }

        /// <summary>Dia em que a conta fica bloqueada, ou 0 se não houver bloqueio.</summary>
        public int BlockedDay { get; private set; }

        public double Rating => _ratings.Average();

        public double AcceptanceRate => Offered == 0 ? 1.0 : (double)Accepted / Offered;

        public bool GoalReached => DeliveriesThisWeek >= Config.GoalDeliveries;

        public bool IsBlockedOn(int day) => BlockedDay == day;

        /// <summary>Com taxa de aceite baixa, o app oferece menos pedidos no turno.</summary>
        public int OrdersForShift() =>
            AcceptanceRate < Config.AcceptanceThreshold ? Config.ReducedOrdersPerShift : Config.OrdersPerShift;

        public Order Offer(IGameRandom random, bool isRaining)
        {
            double km = Math.Round(random.Between(Config.MinOrderKm, Config.MaxOrderKm), 1);
            Money pay = Config.BasePay + Config.PayPerKm * (decimal)km;

            if (isRaining)
            {
                pay *= 1m + Config.RainPayBonus;
            }

            Offered++;

            return new Order(_nextOrderId++, km, pay, isRaining);
        }

        public void Accept()
        {
            Accepted++;
        }

        /// <summary>Entrega cancelada depois de aceita conta como recusa.</summary>
        public void Cancel()
        {
            Accepted = Math.Max(0, Accepted - 1);
        }

        public void CountDelivery()
        {
            DeliveriesThisWeek++;
        }

        /// <summary>Registra uma nota de 1 a 5. Se a média cair abaixo do limite, a conta é bloqueada no dia seguinte.</summary>
        public void Rate(int stars, int day)
        {
            _ratings.Enqueue(Math.Max(1, Math.Min(5, stars)));

            while (_ratings.Count > Config.RatingWindow)
            {
                _ratings.Dequeue();
            }

            if (Rating < Config.BlockBelowRating)
            {
                BlockedDay = day + 1;
            }
        }

        public void StartNewWeek()
        {
            DeliveriesThisWeek = 0;
        }
    }
}
