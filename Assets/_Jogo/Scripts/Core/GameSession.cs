using System;
using System.Collections.Generic;

namespace Entregador.Core
{
    /// <summary>
    /// Uma semana da fatia vertical: 7 dias, um turno por dia. Junta carteira, app, aluguel, clima e imprevistos.
    /// A cena chama os métodos na ordem do dia; chamadas fora de ordem lançam InvalidOperationException.
    /// </summary>
    public sealed class GameSession
    {
        private readonly IGameRandom _random;
        private readonly IncidentSystem _incidents = new IncidentSystem();
        private readonly bool[] _rainyDays;
        private readonly Dictionary<int, Money> _paid = new Dictionary<int, Money>();

        private bool _shiftStarted;
        private bool _dayClosed;
        private int _ordersOffered;
        private int _ordersInShift;
        private Order _current;
        private bool _currentAccepted;
        private int _pendingFines;
        private bool _weekEnded;

        public GameSession(IGameRandom random, EconomyConfig economy = null, AppConfig appConfig = null, bool[] rainyDays = null)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            Economy = economy ?? new EconomyConfig();
            App = new DeliveryApp(appConfig ?? new AppConfig());

            if (rainyDays != null && rainyDays.Length != Economy.DaysPerWeek)
            {
                throw new ArgumentException("A lista de dias de chuva precisa ter um item por dia da semana.", nameof(rainyDays));
            }

            _rainyDays = rainyDays ?? Weather.RollWeek(_random, Economy.DaysPerWeek, Economy.RainyDaysPerWeek);

            Wallet = new Wallet(Economy.StartingMoney);
            Rental = new VehicleRental(Economy.RentalDeposit, Economy.WeeklyRent);
            Day = 1;

            Wallet.Debit(Day, LedgerCategory.Caucao, Rental.Deposit, "Caução da moto");
        }

        public EconomyConfig Economy { get; }

        public Wallet Wallet { get; }

        public DeliveryApp App { get; }

        public VehicleRental Rental { get; }

        /// <summary>Dia atual, de 1 a DaysPerWeek. Depois do último EndDay vale DaysPerWeek + 1.</summary>
        public int Day { get; private set; }

        public bool IsWeekOver => Day > Economy.DaysPerWeek;

        public bool HasRaincoat { get; private set; }

        public bool IsRainingToday => !IsWeekOver && _rainyDays[Day - 1];

        public bool IsShiftActive => _shiftStarted && !_dayClosed;

        /// <summary>Multas acumuladas, cobradas no fim da semana.</summary>
        public int PendingFines => _pendingFines;

        public bool IsRainyDay(int day) => day >= 1 && day <= _rainyDays.Length && _rainyDays[day - 1];

        /// <summary>Compra a capa de chuva (uma vez só). Devolve false se já tem.</summary>
        public bool BuyRaincoat()
        {
            EnsureWeekRunning();

            if (HasRaincoat)
            {
                return false;
            }

            Wallet.Debit(Day, LedgerCategory.Compra, Economy.Raincoat, "Capa de chuva");
            HasRaincoat = true;

            return true;
        }

        /// <summary>Começa o turno. Devolve false se o app bloqueou a conta hoje.</summary>
        public bool StartShift()
        {
            EnsureWeekRunning();

            if (_shiftStarted || _dayClosed)
            {
                throw new InvalidOperationException("O turno de hoje já foi decidido.");
            }

            if (App.IsBlockedOn(Day))
            {
                return false;
            }

            _shiftStarted = true;
            _ordersOffered = 0;
            _ordersInShift = App.OrdersForShift();

            return true;
        }

        /// <summary>Folga: o dia passa sem turno.</summary>
        public void TakeDayOff()
        {
            EnsureWeekRunning();

            if (_shiftStarted || _dayClosed)
            {
                throw new InvalidOperationException("O turno de hoje já foi decidido.");
            }

            _dayClosed = true;
        }

        /// <summary>Próximo pedido do turno, ou null quando acabaram.</summary>
        public Order NextOffer()
        {
            EnsureShiftActive();

            if (_current != null)
            {
                throw new InvalidOperationException("Ainda há um pedido sem resposta.");
            }

            if (_ordersOffered >= _ordersInShift)
            {
                return null;
            }

            _ordersOffered++;
            _current = App.Offer(_random, IsRainingToday);
            _currentAccepted = false;

            return _current;
        }

        public void Accept(Order order)
        {
            EnsureOffered(order);
            App.Accept();
            _currentAccepted = true;
        }

        public void Reject(Order order)
        {
            EnsureOffered(order);
            _current = null;
        }

        /// <summary>Sorteia um imprevisto durante a corrida dirigida. Null se nada aconteceu.</summary>
        public Incident RollRunIncident(bool bumpyRoad)
        {
            EnsureAccepted(null);

            return _incidents.RollDuringRun(_random, bumpyRoad);
        }

        /// <summary>Aplica a escolha do imprevisto da corrida. Cancelar encerra a entrega sem pagamento.</summary>
        public IncidentOutcome ResolveRunIncident(Order order, Incident incident, IncidentChoice choice)
        {
            EnsureAccepted(order);

            if (incident == null || incident.Kind != IncidentKind.PneuFurado)
            {
                throw new InvalidOperationException("Este imprevisto não acontece durante a corrida.");
            }

            IncidentOutcome outcome = _incidents.Resolve(incident, choice, Economy.FlatTireRepair, _random);

            if (outcome.Cost > Money.Zero)
            {
                Wallet.Debit(Day, LedgerCategory.Conserto, outcome.Cost, "Conserto do pneu");
            }

            if (outcome.CancelsDelivery)
            {
                App.Cancel();
                _current = null;
            }

            return outcome;
        }

        /// <summary>Fecha uma corrida dirigida: combustível, dano, multas, pagamento, gorjeta e nota.</summary>
        public DeliveryResult FinishRun(Order order, RunReport report)
        {
            EnsureAccepted(order);

            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            DebitFuel(report.KmDriven);
            Rental.AddDamage(report.DamageAdded);
            _pendingFines += report.Fines;
            _current = null;

            if (RatingRules.IsSpoiled(report))
            {
                App.Rate(1, Day);

                return new DeliveryResult(order, Money.Zero, Money.Zero, 1, true, false, null);
            }

            int stars = RatingRules.Stars(report, order.IsRaining, HasRaincoat);
            Money tip = RollTip(stars, report.TimeRatio);

            return Complete(order, order.Pay, tip, stars, false, report.PhotoTaken);
        }

        /// <summary>Entrega automática: paga menos, sem gorjeta nem foto, e a nota depende só do atraso.</summary>
        public DeliveryResult SkipRun(Order order, int delayMinutes = 0)
        {
            EnsureAccepted(order);

            DebitFuel(order.DistanceKm);
            _current = null;

            double expectedMinutes = Math.Max(0.1, order.DistanceKm * Economy.MinutesPerKm);
            var delayReport = new RunReport
            {
                KmDriven = order.DistanceKm,
                TimeRatio = 1 + Math.Max(0, delayMinutes) / expectedMinutes,
                PhotoTaken = false,
            };

            int baseStars = _random.Chance(0.5) ? 5 : 4;
            int delayPenalty = 5 - RatingRules.Stars(delayReport, false, false);
            int stars = Math.Max(1, baseStars - delayPenalty);

            Money pay = order.Pay * App.Config.SkipPayRatio;

            return Complete(order, pay, Money.Zero, stars, true, false);
        }

        /// <summary>Aplica a escolha do imprevisto pós-entrega: estorno do que foi pago e/ou nota forçada.</summary>
        public IncidentOutcome ResolveAfterDelivery(Order order, Incident incident, IncidentChoice choice)
        {
            EnsureWeekRunning();

            if (order == null || !_paid.TryGetValue(order.Id, out Money paid))
            {
                throw new InvalidOperationException("Não há imprevisto pendente para este pedido.");
            }

            if (incident == null || incident.Kind != IncidentKind.ClienteDizQueNaoRecebeu)
            {
                throw new InvalidOperationException("Este imprevisto não acontece depois da entrega.");
            }

            IncidentOutcome outcome = _incidents.Resolve(incident, choice, Economy.FlatTireRepair, _random);
            _paid.Remove(order.Id);

            if (outcome.LosesPay && paid > Money.Zero)
            {
                Wallet.Debit(Day, LedgerCategory.Estorno, paid, "Estorno: cliente disse que não recebeu");
            }

            if (outcome.ForcedStars.HasValue)
            {
                App.Rate(outcome.ForcedStars.Value, Day);
            }

            return outcome;
        }

        /// <summary>Fecha o turno (ou a folga) e avança o dia.</summary>
        public void EndDay()
        {
            EnsureWeekRunning();

            if (_current != null)
            {
                throw new InvalidOperationException("Ainda há um pedido sem resposta.");
            }

            Day++;
            _shiftStarted = false;
            _dayClosed = false;
            _ordersOffered = 0;
            _ordersInShift = 0;
        }

        /// <summary>Fecha a semana: bônus da meta, multas, aluguel e devolução da caução.</summary>
        public WeekReport EndWeek()
        {
            if (!IsWeekOver)
            {
                throw new InvalidOperationException("A semana ainda não acabou.");
            }

            if (_weekEnded)
            {
                throw new InvalidOperationException("A semana já foi fechada.");
            }

            _weekEnded = true;
            int day = Economy.DaysPerWeek;

            Money bonus = Money.Zero;

            if (App.GoalReached)
            {
                bonus = App.Config.GoalBonus;
                Wallet.Credit(day, LedgerCategory.Bonus, bonus, "Bônus da meta semanal");
            }

            Money fines = Economy.Fine * _pendingFines;

            if (fines > Money.Zero)
            {
                Wallet.Debit(day, LedgerCategory.Multa, fines, "Multas da semana");
            }

            Wallet.Debit(day, LedgerCategory.Aluguel, Rental.WeeklyRent, "Aluguel da moto");

            Money refund = Rental.DepositRefund;
            Wallet.Credit(day, LedgerCategory.Caucao, refund, "Caução devolvida");

            Money balance = Wallet.Balance;
            double rating = App.Rating;

            WeekResult result;

            if (balance > Economy.StartingMoney && rating >= Economy.GoodWeekMinRating)
            {
                result = WeekResult.SemanaBoa;
            }
            else if (balance.IsNegative)
            {
                result = WeekResult.NoVermelho;
            }
            else
            {
                result = WeekResult.Sobreviveu;
            }

            return new WeekReport(App.DeliveriesThisWeek, bonus, fines, Rental.WeeklyRent, refund, balance, rating, result);
        }

        private DeliveryResult Complete(Order order, Money pay, Money tip, int stars, bool skipped, bool photoTaken)
        {
            Wallet.Credit(Day, LedgerCategory.Entrega, pay, $"Entrega #{order.Id}");

            if (tip > Money.Zero)
            {
                Wallet.Credit(Day, LedgerCategory.Gorjeta, tip, $"Gorjeta #{order.Id}");
            }

            App.CountDelivery();
            App.Rate(stars, Day);

            _paid[order.Id] = pay + tip;

            Incident incident = _incidents.RollAfterDelivery(_random, photoTaken);

            return new DeliveryResult(order, pay, tip, stars, false, skipped, incident);
        }

        private Money RollTip(int stars, double timeRatio)
        {
            Money max;

            if (stars >= 5 && timeRatio <= 1)
            {
                max = App.Config.MaxTip;
            }
            else if (stars == 4)
            {
                max = App.Config.MaxTipGood;
            }
            else
            {
                return Money.Zero;
            }

            return max * (decimal)_random.NextDouble();
        }

        private void DebitFuel(double km)
        {
            Money fuel = Economy.FuelPerKm * (decimal)km;

            if (fuel > Money.Zero)
            {
                Wallet.Debit(Day, LedgerCategory.Combustivel, fuel, "Combustível");
            }
        }

        private void EnsureWeekRunning()
        {
            if (IsWeekOver)
            {
                throw new InvalidOperationException("A semana acabou.");
            }
        }

        private void EnsureShiftActive()
        {
            EnsureWeekRunning();

            if (!IsShiftActive)
            {
                throw new InvalidOperationException("Não há turno em andamento.");
            }
        }

        private void EnsureOffered(Order order)
        {
            EnsureShiftActive();

            if (_current == null || _currentAccepted || order == null || order.Id != _current.Id)
            {
                throw new InvalidOperationException("Este pedido não está aguardando resposta.");
            }
        }

        private void EnsureAccepted(Order order)
        {
            EnsureShiftActive();

            if (_current == null || !_currentAccepted || (order != null && order.Id != _current.Id))
            {
                throw new InvalidOperationException("Não há entrega aceita em andamento.");
            }
        }
    }
}
