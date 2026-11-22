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

        private readonly AbordagemSystem _abordagens;
        private readonly List<MultaRegistro> _multasPagas = new List<MultaRegistro>();
        private readonly List<MultaRegistro> _multasPendentes = new List<MultaRegistro>();
        private readonly List<AbordagemRegistro> _registrosAbordagem = new List<AbordagemRegistro>();
        private Abordagem _abordagemAtual;
        private int _blitzSorteadoNoDia;
        private bool _blitzHoje;
        private int _patioDia;

        public GameSession(IGameRandom random, EconomyConfig economy = null, AppConfig appConfig = null, bool[] rainyDays = null, PerfilInicial perfil = null)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            Economy = economy ?? new EconomyConfig();
            App = new DeliveryApp(appConfig ?? new AppConfig());

            if (rainyDays != null && rainyDays.Length != Economy.DaysPerWeek)
            {
                throw new ArgumentException("A lista de dias de chuva precisa ter um item por dia da semana.", nameof(rainyDays));
            }

            _rainyDays = rainyDays ?? Weather.RollWeek(_random, Economy.DaysPerWeek, Economy.RainyDaysPerWeek);

            perfil = perfil ?? PerfilInicial.Padrao;
            _abordagens = new AbordagemSystem(Economy.Transito);
            Cnh = new Carteira(perfil.TemCnh);
            Wallet = new Wallet(Economy.StartingMoney);
            Day = 1;

            ComprarMoto(perfil.Origem);
        }

        /// <summary>A CNH do jogador: pontos e suspensão.</summary>
        public Carteira Cnh { get; }

        public MotoOrigem Origem { get; private set; }

        public MotoStatus MotoStatus { get; private set; }

        /// <summary>Parcelas da moto bob que ainda faltam pagar (uma por semana). Zero se não é bob ou se foi apreendida.</summary>
        public int ParcelasRestantes { get; private set; }

        /// <summary>Multas de trânsito já cobradas (radar que chegou, abordagens).</summary>
        public IReadOnlyList<MultaRegistro> MultasPagas => _multasPagas;

        /// <summary>Notificações de radar que ainda não chegaram.</summary>
        public IReadOnlyList<MultaRegistro> MultasPendentes => _multasPendentes;

        public IReadOnlyList<AbordagemRegistro> Abordagens => _registrosAbordagem;

        public Abordagem AbordagemAtual => _abordagemAtual;

        /// <summary>Há blitz hoje? Só vale depois de SortearBlitzDoDia.</summary>
        public bool BlitzHoje => _blitzSorteadoNoDia == Day && _blitzHoje;

        /// <summary>Moto inteira e liberada para o turno.</summary>
        public bool PodeTrabalhar => MotoStatus == MotoStatus.Ok;

        public EconomyConfig Economy { get; }

        public Wallet Wallet { get; }

        public DeliveryApp App { get; }

        /// <summary>Contrato de aluguel. Null quando a moto é bob (sem caução nem aluguel).</summary>
        public VehicleRental Rental { get; private set; }

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

            if (!PodeTrabalhar || App.IsBlockedOn(Day))
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

        /// <summary>
        /// O jogador reclamou no app no balcão do restaurante e cancelou: a entrega acaba sem pagamento nem combustível, e conta como
        /// recusa na taxa de aceite (o app pune quem cancela depois de aceitar).
        /// </summary>
        public void CancelarNaRetirada(Order order)
        {
            EnsureAccepted(order);
            App.Cancel();
            _current = null;
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
            Rental?.AddDamage(report.DamageAdded);
            _pendingFines += report.Fines;

            foreach (Infracao captura in report.Capturas)
            {
                AgendarMulta(captura, true);
            }

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

            if (_abordagemAtual != null)
            {
                throw new InvalidOperationException("Ainda há uma abordagem sem resposta.");
            }

            Day++;
            EntregarMultasPendentes();
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

            Money rent = Money.Zero;
            Money refund = Money.Zero;

            if (Rental != null)
            {
                rent = Rental.WeeklyRent;
                Wallet.Debit(day, LedgerCategory.Aluguel, rent, "Aluguel da moto");

                refund = Rental.DepositRefund;
                Wallet.Credit(day, LedgerCategory.Caucao, refund, "Caução devolvida");
            }
            else if (ParcelasRestantes > 0)
            {
                // A parcela da bob entra no campo Rent do relatório.
                rent = Economy.Transito.BobParcelaSemanal;
                Wallet.Debit(day, LedgerCategory.Parcela, rent, $"Parcela da moto ({ParcelasRestantes} restantes)");
                ParcelasRestantes--;
            }

            Money balance = Wallet.Balance;
            double rating = App.Rating;

            WeekResult result;

            if (MotoStatus != MotoStatus.Ok)
            {
                result = WeekResult.SemMoto;
            }
            else if (balance > Economy.StartingMoney && rating >= Economy.GoodWeekMinRating)
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

            var report = new WeekReport(App.DeliveriesThisWeek, bonus, fines, rent, refund, balance, rating, result);
            report.Transito = BuildTransitoReport();

            return report;
        }

        /// <summary>Sorteia (uma vez por dia) se há blitz hoje. A Game coloca as blitz no mapa quando devolve true.</summary>
        public bool SortearBlitzDoDia()
        {
            EnsureWeekRunning();

            if (_blitzSorteadoNoDia != Day)
            {
                _blitzSorteadoNoDia = Day;
                _blitzHoje = _abordagens.RollBlitz(_random, Day);
            }

            return _blitzHoje;
        }

        /// <summary>O jogador chegou a uma blitz. Devolve a abordagem para a cena mostrar Parar ou Fugir.</summary>
        public Abordagem IniciarBlitz()
        {
            EnsureShiftActive();

            if (!BlitzHoje)
            {
                throw new InvalidOperationException("Não há blitz hoje.");
            }

            return IniciarAbordagem(null);
        }

        /// <summary>A polícia viu uma infração (flagrante): abordagem com a infração no auto.</summary>
        public Abordagem ReportarFlagrante(Infracao infracao)
        {
            EnsureShiftActive();

            return IniciarAbordagem(infracao);
        }

        /// <summary>
        /// Aplica a escolha. Multas e pontos na hora; moto retida encerra o dia e cancela a entrega em andamento.
        /// O atraso (DelayMinutes) fica por conta da cena, que soma ao tempo da corrida, como nos imprevistos.
        /// </summary>
        public AbordagemOutcome ResolverAbordagem(Abordagem abordagem, AbordagemChoice choice)
        {
            EnsureShiftActive();

            if (abordagem == null || abordagem != _abordagemAtual)
            {
                throw new InvalidOperationException("Esta abordagem não está em andamento.");
            }

            AbordagemOutcome outcome = _abordagens.Resolve(abordagem, choice, _random);
            _abordagemAtual = null;

            foreach (Infracao infracao in outcome.Infracoes)
            {
                CobrarNaHora(infracao);
            }

            if (outcome.PlacaAnotada)
            {
                AgendarMulta(Infracao.FugaDeAbordagem, false);
            }

            _registrosAbordagem.Add(new AbordagemRegistro(Day, abordagem.Motivo, choice, outcome.Desfecho));

            if (outcome.MotoApreendida)
            {
                MotoStatus = MotoStatus.Apreendida;
                ParcelasRestantes = 0;
                Rental = null;
            }
            else if (outcome.MotoNoPatio)
            {
                MotoStatus = MotoStatus.NoPatio;
                _patioDia = Day;
            }

            if (outcome.CancelsDelivery && _current != null)
            {
                if (_currentAccepted)
                {
                    App.Cancel();
                }

                _current = null;
            }

            if (outcome.EncerraDia)
            {
                _dayClosed = true;
            }

            return outcome;
        }

        /// <summary>Quanto custa tirar a moto do pátio hoje: guincho mais uma diária por dia lá dentro. Zero se não está no pátio.</summary>
        public Money CustoPatio
        {
            get
            {
                if (MotoStatus != MotoStatus.NoPatio)
                {
                    return Money.Zero;
                }

                int dias = Math.Max(1, Day - _patioDia);

                return Economy.Transito.Guincho + Economy.Transito.DiariaPatio * dias;
            }
        }

        /// <summary>Paga guincho e diárias e recupera a moto. O dia da retirada também se perde. False se não há dinheiro.</summary>
        public bool RetirarDoPatio()
        {
            EnsureWeekRunning();

            if (MotoStatus != MotoStatus.NoPatio)
            {
                throw new InvalidOperationException("A moto não está no pátio.");
            }

            if (_shiftStarted || _dayClosed)
            {
                throw new InvalidOperationException("O dia de hoje já foi decidido.");
            }

            Money custo = CustoPatio;

            if (Wallet.Balance < custo)
            {
                return false;
            }

            Wallet.Debit(Day, LedgerCategory.Patio, custo, "Guincho e diárias do pátio");
            MotoStatus = MotoStatus.Ok;
            _dayClosed = true;

            return true;
        }

        /// <summary>Quanto sai do bolso agora para ter outra moto: caução do aluguel ou a entrada de uma bob.</summary>
        public Money CustoNovaMoto(MotoOrigem origem) =>
            origem == MotoOrigem.Alugada ? Economy.RentalDeposit : Economy.Transito.BobEntrada;

        public bool PodeEscolherNovaMoto(MotoOrigem origem) =>
            MotoStatus == MotoStatus.Apreendida && !IsWeekOver && !IsShiftActive && Wallet.Balance >= CustoNovaMoto(origem);

        /// <summary>Depois da apreensão: aluga outra (precisa da caução; o aluguel vem no fim da semana) ou compra outra bob. False se não há dinheiro.</summary>
        public bool EscolherNovaMoto(MotoOrigem origem)
        {
            EnsureWeekRunning();

            if (MotoStatus != MotoStatus.Apreendida)
            {
                throw new InvalidOperationException("O jogador ainda tem moto.");
            }

            if (IsShiftActive)
            {
                throw new InvalidOperationException("Não dá para trocar de moto no meio do turno.");
            }

            if (Wallet.Balance < CustoNovaMoto(origem))
            {
                return false;
            }

            ComprarMoto(origem);

            return true;
        }

        private void ComprarMoto(MotoOrigem origem)
        {
            Origem = origem;
            MotoStatus = MotoStatus.Ok;

            if (origem == MotoOrigem.Alugada)
            {
                Rental = new VehicleRental(Economy.RentalDeposit, Economy.WeeklyRent);
                ParcelasRestantes = 0;
                Wallet.Debit(Day, LedgerCategory.Caucao, Rental.Deposit, "Caução da moto");
            }
            else
            {
                Rental = null;
                ParcelasRestantes = Economy.Transito.BobParcelas;
                Wallet.Debit(Day, LedgerCategory.Entrada, Economy.Transito.BobEntrada, "Entrada da moto (o cara da moto)");
            }
        }

        private Abordagem IniciarAbordagem(Infracao? flagrante)
        {
            if (_abordagemAtual != null)
            {
                throw new InvalidOperationException("Já há uma abordagem em andamento.");
            }

            _abordagemAtual = _abordagens.Iniciar(Origem, Cnh.Valida, flagrante);

            return _abordagemAtual;
        }

        /// <summary>Multa autuada na hora. Infração do veículo na moto bob não tem dono: não cobra nem pontua.</summary>
        private void CobrarNaHora(Infracao infracao)
        {
            RegraInfracao regra = CodigoTransito.Regra(infracao);

            if (regra.DoVeiculo && Origem == MotoOrigem.Bob)
            {
                return;
            }

            Wallet.Debit(Day, LedgerCategory.Multa, regra.Valor, regra.Descricao);
            Cnh.Registrar(infracao);
            _multasPagas.Add(new MultaRegistro(infracao, Day, Day, regra.Valor, Money.Zero, false));
        }

        /// <summary>Multa que chega depois pelo app/correio. Na moto bob nunca chega; na alugada vem com a taxa de indicação.</summary>
        private void AgendarMulta(Infracao infracao, bool noRadar)
        {
            if (Origem == MotoOrigem.Bob)
            {
                return;
            }

            TransitoConfig t = Economy.Transito;
            int chegada = Day + _random.Range(t.RadarChegadaMinDias, t.RadarChegadaMaxDias + 1);

            _multasPendentes.Add(new MultaRegistro(infracao, Day, chegada, CodigoTransito.Regra(infracao).Valor, t.TaxaIndicacao, noRadar));
        }

        private void EntregarMultasPendentes()
        {
            if (IsWeekOver)
            {
                return;
            }

            for (int i = 0; i < _multasPendentes.Count; i++)
            {
                MultaRegistro multa = _multasPendentes[i];

                if (multa.DiaChegada > Day)
                {
                    continue;
                }

                _multasPendentes.RemoveAt(i--);
                string nome = CodigoTransito.Regra(multa.Infracao).Descricao;

                Wallet.Debit(Day, LedgerCategory.Multa, multa.Valor, "Multa: " + nome);

                if (multa.Taxa > Money.Zero)
                {
                    Wallet.Debit(Day, LedgerCategory.Multa, multa.Taxa, "Taxa de indicação da locadora");
                }

                Cnh.Registrar(multa.Infracao);
                _multasPagas.Add(multa);
            }
        }

        private TransitoReport BuildTransitoReport()
        {
            Money pagas = Money.Zero;
            Money pendentes = Money.Zero;

            foreach (MultaRegistro multa in _multasPagas)
            {
                pagas += multa.Total;
            }

            foreach (MultaRegistro multa in _multasPendentes)
            {
                pendentes += multa.Total;
            }

            return new TransitoReport(pagas, pendentes, _multasPendentes.Count, Cnh.Pontos, Cnh.Status, Origem, MotoStatus,
                CustoPatio, _multasPagas.ToArray(), _registrosAbordagem.ToArray());
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
