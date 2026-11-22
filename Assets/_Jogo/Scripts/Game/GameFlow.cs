using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    public enum FlowState
    {
        DayStart,
        Blocked,
        Offer,
        DrivingToRestaurant,

        /// <summary>Parado no balcão do restaurante, conversando com a atendente (esperar, conferir, informar o código...).</summary>
        NoBalcao,

        DrivingToCustomer,
        FlatTire,
        AtDoor,
        Result,
        Claim,
        ShiftSummary,
        WeekReport,

        /// <summary>Tela de novo jogo: personagem, moto, CNH.</summary>
        Setup,

        /// <summary>A polícia mandou parar: a moto freia e o jogador escolhe Parar ou Fugir.</summary>
        Abordagem,

        /// <summary>Fugiu: a viatura vem atrás por alguns segundos.</summary>
        Perseguicao,

        /// <summary>Resultado da abordagem na tela (multas, atraso), antes de voltar à corrida.</summary>
        AbordagemResultado,

        /// <summary>A moto foi levada ao pátio ou apreendida: o dia acaba.</summary>
        MotoRetida,
    }

    /// <summary>
    /// Máquina de estados do jogo: dona da GameSession, liga as regras (Core) à moto, ao mapa e às telas.
    /// Cada método público de ação corresponde a um botão da interface (e é chamado direto pelos testes).
    /// </summary>
    public sealed partial class GameFlow : MonoBehaviour
    {
        private static readonly string[] DayNames = { "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado", "Domingo" };

        [SerializeField] private CityMap _map;
        [SerializeField] private ArcadeBikeController _bike;
        [SerializeField] private BikeInput _input;
        [SerializeField] private DeliveryRunTracker _tracker;
        [SerializeField] private RouteGuide _route;
        [SerializeField] private FollowCamera _followCamera;
        [SerializeField] private MinimapView _minimap;
        [SerializeField] private GameUI _ui;
        [SerializeField] private GameTuning _tuning;
        [SerializeField] private Light _sun;
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private int _seed;
        [SerializeField] private RainEffect _rain;
        [SerializeField] private WeatherLook _look;

        private IGameRandom _random;
        private Incident _runIncident;
        private Incident _claimIncident;
        private FlowState _stateBeforeIncident;
        private float _offerRemaining;
        private int _lastShownOfferSecond;
        private bool _flatTireRolled;
        private int _delayMinutes;
        private int _deliveriesToday;
        private Place _lastCustomer;

        public GameSession Session { get; private set; }

        public FlowState State { get; private set; }

        public Order CurrentOrder { get; private set; }

        public RunPlan CurrentPlan { get; private set; }

        public DeliveryResult LastResult { get; private set; }

        public WeekReport WeekReport { get; private set; }

        public IAdService AdService { get; set; }

        public GameUI Ui => _ui;

        public ArcadeBikeController Bike => _bike;

        public DeliveryRunTracker Tracker => _tracker;

        public CityMap Map => _map;

        public RouteGuide Route => _route;

        public GameTuning Tuning => _tuning;

        public bool IsDriving => State == FlowState.DrivingToRestaurant || State == FlowState.DrivingToCustomer;

        public void Configure(CityMap map, ArcadeBikeController bike, BikeInput input, DeliveryRunTracker tracker, RouteGuide route,
            FollowCamera followCamera, MinimapView minimap, GameUI ui, GameTuning tuning, Light sun, Camera mainCamera)
        {
            _map = map;
            _bike = bike;
            _input = input;
            _tracker = tracker;
            _route = route;
            _followCamera = followCamera;
            _minimap = minimap;
            _ui = ui;
            _tuning = tuning;
            _sun = sun;
            _mainCamera = mainCamera;
        }

        /// <summary>Liga o efeito de chuva (partículas que seguem a câmera) aos dias de chuva.</summary>
        /// <summary>Liga o visual de clima (skybox, sol, pós-processamento, chão molhado).</summary>
        public void AttachLook(WeatherLook look)
        {
            _look = look;
        }

        public void AttachRain(RainEffect rain)
        {
            _rain = rain;
        }

        private void Awake()
        {
            AdService = GetComponent<FakeAdService>();
        }

        private void OnEnable()
        {
            BlitzSite.BikeEntered += OnBlitzEntered;
        }

        private void OnDisable()
        {
            BlitzSite.BikeEntered -= OnBlitzEntered;

            if (_monitor != null)
            {
                _monitor.FlagranteVisto -= OnFlagrante;
            }
        }

        private void Start()
        {
            _ui.Build(_input, _minimap != null ? _minimap.Texture : null, SkipRun);
            InitFiscalizacao();
            InitRetirada();

            if (ShowSetupOnStart)
            {
                ApplyProfile();
                ShowSetup();
            }
            else
            {
                NewWeek(_seed);
            }
        }

        // ---------- Semana ----------

        /// <summary>Começa uma semana do zero. Seed 0 usa o relógio. Os testes passam a própria fonte de sorte.</summary>
        public void NewWeek(int seed = 0, IGameRandom random = null)
        {
            _random = random ?? new SeededRandom(seed != 0 ? seed : Environment.TickCount);
            Session = new GameSession(_random, EconomyForProfile(), null, null, _perfil.ParaRegras());
            ApplyProfile();
            _tracker.ClearRadar();
            _multasVistas = 0;
            _abordagem = null;
            WeekReport = null;
            CurrentOrder = null;
            LastResult = null;
            _lastCustomer = null;

            _bike.Teleport(_map.BikeSpawn, _map.BikeSpawnYaw);

            if (_followCamera != null)
            {
                _followCamera.Snap();
            }

            ShowDayStart();
        }

        // ---------- Dia ----------

        private void ShowDayStart()
        {
            if (Session.IsWeekOver)
            {
                ShowWeekReport();

                return;
            }

            State = FlowState.DayStart;
            EnterIdle();
            EndOfDayFiscalizacao();
            ApplyWeather(Session.IsRainingToday);

            var body = new StringBuilder();
            body.AppendLine(Session.IsRainingToday
                ? "Previsão: chuva forte hoje. O app paga 30% a mais, mas a pista fica escorregadia."
                : "Previsão: tempo firme hoje.");
            body.AppendLine("Dias de chuva na semana: " + RainyDaysText());
            body.AppendLine();
            body.AppendLine("Saldo: " + Session.Wallet.Balance);
            body.AppendLine(MotoCostLine());
            AppendNotifications(body);
            body.AppendLine($"Entregas na semana: {Session.App.DeliveriesThisWeek}/{Session.App.Config.GoalDeliveries}   Nota: {Rating(Session.App.Rating)}");

            if (Session.IsRainingToday && !Session.HasRaincoat)
            {
                body.AppendLine("Sem capa de chuva você perde uma estrela por entrega.");
            }

            var buttons = new List<PanelButton>();

            if (Session.MotoStatus == MotoStatus.Ok)
            {
                buttons.Add(new PanelButton("Começar turno", BeginShift));
                buttons.Add(new PanelButton("Folgar", TakeDayOff));

                if (!Session.HasRaincoat)
                {
                    buttons.Add(new PanelButton($"Comprar capa ({Session.Economy.Raincoat})", BuyRaincoat));
                }
            }
            else
            {
                AddNoMotoOptions(body, buttons);
            }

            _ui.ShowModal($"Dia {Session.Day}: {DayName(Session.Day)}", body.ToString().TrimEnd(), buttons.ToArray());
        }

        public void BuyRaincoat()
        {
            if (State != FlowState.DayStart)
            {
                return;
            }

            Session.BuyRaincoat();
            ShowDayStart();
        }

        public void TakeDayOff()
        {
            if (State != FlowState.DayStart && State != FlowState.Blocked)
            {
                return;
            }

            Session.TakeDayOff();
            Session.EndDay();
            ShowDayStart();
        }

        public void BeginShift()
        {
            if (State != FlowState.DayStart)
            {
                return;
            }

            if (!Session.StartShift())
            {
                State = FlowState.Blocked;
                _ui.ShowModal(
                    "Conta bloqueada",
                    $"O app bloqueou sua conta hoje por causa da nota baixa ({Rating(Session.App.Rating)}). Sem turno: dia de folga.",
                    new PanelButton("Ok", TakeDayOff));

                return;
            }

            _deliveriesToday = 0;
            OpenDayFiscalizacao();
            OfferNext();
        }

        // ---------- Pedidos ----------

        private void OfferNext()
        {
            Order order = Session.NextOffer();

            if (order == null)
            {
                ShowShiftSummary();

                return;
            }

            CurrentOrder = order;
            CurrentPlan = RunPlanner.Plan(_map, _tuning, order, _bike.Position, _random, _lastCustomer);
            State = FlowState.Offer;
            EnterIdle();
            _offerRemaining = _tuning.OfferSeconds;
            _lastShownOfferSecond = -1;
            ShowOffer();
        }

        private void ShowOffer()
        {
            _ui.ShowModal(
                "Novo pedido",
                OfferBody(),
                new PanelButton("Aceitar", AcceptOffer),
                new PanelButton("Recusar", RejectOffer));
        }

        private string OfferBody()
        {
            var body = new StringBuilder();
            body.AppendLine("Restaurante: " + CurrentPlan.Restaurant.Name);
            body.AppendLine("Distância da entrega: " + Km(CurrentOrder.DistanceKm));
            body.AppendLine("Pagamento: " + CurrentOrder.Pay);

            if (CurrentOrder.IsRaining)
            {
                body.AppendLine("Inclui adicional de chuva.");
            }

            body.AppendLine();
            body.Append($"Responda em {Mathf.CeilToInt(_offerRemaining)} s");

            return body.ToString();
        }

        public void RejectOffer()
        {
            if (State != FlowState.Offer)
            {
                return;
            }

            Session.Reject(CurrentOrder);
            CurrentOrder = null;
            OfferNext();
        }

        public void AcceptOffer()
        {
            if (State != FlowState.Offer)
            {
                return;
            }

            Session.Accept(CurrentOrder);

            float expected = CurrentPlan.TotalMeters / _tuning.ReferenceSpeed * _tuning.ExpectedTimeFactor;
            _tracker.Begin(expected, CurrentPlan.TotalMeters);
            _flatTireRolled = false;
            _delayMinutes = 0;
            _lastCustomer = CurrentPlan.Customer;
            BeginRetirada();

            State = FlowState.DrivingToRestaurant;
            _ui.HideModal();
            _ui.SetHudVisible(true);
            _bike.SetWet(Session.IsRainingToday);
            _bike.SetLocked(false);
            SetTarget(CurrentPlan.Restaurant);
            _ui.Toast($"Pedido aceito! Vá até {CurrentPlan.Restaurant.Name}. {Retirada.TextoCodigo}");
        }

        private void SetTarget(Place place)
        {
            _map.Highlight(place);
            _route.SetTarget(place.Position);
        }

        // ---------- Corrida ----------

        private void Update()
        {
            TickFiscalizacao();
            TickRetirada();

            if (State == FlowState.Offer)
            {
                TickOffer();
            }
            else if (IsDriving)
            {
                TickDriving();
            }
        }

        private void TickOffer()
        {
            _offerRemaining -= Time.deltaTime;

            if (_offerRemaining <= 0f)
            {
                RejectOffer();

                return;
            }

            int second = Mathf.CeilToInt(_offerRemaining);

            if (second != _lastShownOfferSecond)
            {
                _lastShownOfferSecond = second;
                _ui.SetModalBody(OfferBody());
            }
        }

        private void TickDriving()
        {
            Place target = State == FlowState.DrivingToRestaurant ? CurrentPlan.Restaurant : CurrentPlan.Customer;

            _ui.UpdateHud(
                Session.Wallet.Balance.ToString(),
                DayLabel(),
                (State == FlowState.DrivingToRestaurant ? "Retirar em: " : "Entregar em: ") + target.Name,
                _tracker.ElapsedSeconds,
                _tracker.ExpectedSeconds,
                _bike.SpeedKmh,
                _tracker.FoodCondition,
                _monitor != null ? _monitor.LimitKmh : 0,
                _bike.Gear,
                _bike.Rpm);

            if (!_flatTireRolled && _tracker.Progress >= _tuning.FlatTireAtProgress)
            {
                _flatTireRolled = true;
                Incident incident = Session.RollRunIncident(_tracker.BumpsOrHits >= 2);

                if (incident != null)
                {
                    ShowFlatTire(incident);

                    return;
                }
            }

            if (FlatDistance(_bike.Position, target.Position) > _tuning.MarkerRadius)
            {
                return;
            }

            // No restaurante a retirada é no balcão (TickRetirada): entrar na zona não pega o pedido.
            if (State == FlowState.DrivingToCustomer)
            {
                ArriveAtDoor();
            }
        }

        private void ArriveAtDoor()
        {
            State = FlowState.AtDoor;
            _tracker.Pause();
            _bike.SetLocked(true);
            _route.ClearTarget();
            ShowDoorPanel();
        }

        private void ShowDoorPanel()
        {
            string body = $"Você chegou em {CurrentPlan.Customer.Name}." +
                          (_tracker.PhotoTaken ? "\nFoto da entrega tirada." : "\nTire uma foto da entrega: ela protege você se o cliente reclamar.");

            if (_tracker.PhotoTaken)
            {
                _ui.ShowModal("Entrega na porta", body, new PanelButton("Entregar", Deliver));
            }
            else
            {
                _ui.ShowModal("Entrega na porta", body, new PanelButton("Fotografar", TakePhoto), new PanelButton("Entregar", Deliver));
            }
        }

        public void TakePhoto()
        {
            if (State != FlowState.AtDoor)
            {
                return;
            }

            _tracker.PhotoTaken = true;
            ShowDoorPanel();
        }

        public void Deliver()
        {
            if (State != FlowState.AtDoor)
            {
                return;
            }

            RunReport report = _tracker.BuildReport(_tuning.WorldToGameKm);
            report.PenalidadeRetirada = _pickupPenalty;
            report.MinutosNoBalcao = _pickupMinutes;
            DeliveryResult result = Session.FinishRun(CurrentOrder, report);
            _tracker.ClearRadar();
            FinishRun(result);
        }

        /// <summary>Botão "Pular corrida": a entrega é feita automaticamente, pagando menos.</summary>
        public void SkipRun()
        {
            if (!IsDriving && State != FlowState.AtDoor)
            {
                return;
            }

            DeliveryResult result = Session.SkipRun(CurrentOrder, _delayMinutes);
            FinishRun(result);
        }

        private void FinishRun(DeliveryResult result)
        {
            LastResult = result;
            _tracker.Stop();
            EnterIdle();

            if (!result.Spoiled)
            {
                _deliveriesToday++;
            }

            ShowResult(result);
        }

        private void ShowResult(DeliveryResult result)
        {
            State = FlowState.Result;
            string title = result.Skipped ? "Corrida pulada" : result.Spoiled ? "Comida estragada" : "Entrega concluída";
            var body = new StringBuilder();

            if (result.Spoiled)
            {
                body.AppendLine("A comida chegou estragada. O cliente pediu reembolso: você não recebe nada.");
            }
            else
            {
                body.AppendLine("Pagamento: " + result.Pay);
                body.AppendLine("Gorjeta: " + result.Tip);
                body.AppendLine("Total: " + result.Total);

                if (_pickupPenalty > 0 && !result.Skipped)
                {
                    body.AppendLine($"O cliente reclamou de item faltando no pedido (não conferido no balcão): -{_pickupPenalty} estrela(s).");
                }
            }

            body.AppendLine($"Nota do cliente: {result.Stars} de 5 estrelas");

            if (!result.Skipped)
            {
                body.AppendLine($"Tempo {GameUI.FormatTime(_tracker.ElapsedSeconds)} de {GameUI.FormatTime(_tracker.ExpectedSeconds)}   Comida {Mathf.RoundToInt(_tracker.FoodCondition * 100f)}%");
            }

            _ui.ShowModal(title, body.ToString().TrimEnd(), new PanelButton("Continuar", AfterResult));
        }

        public void AfterResult()
        {
            if (State != FlowState.Result)
            {
                return;
            }

            if (LastResult != null && LastResult.AfterDeliveryIncident != null)
            {
                ShowClaim(LastResult.AfterDeliveryIncident);

                return;
            }

            CurrentOrder = null;
            OfferNext();
        }

        // ---------- Imprevistos ----------

        private void ShowFlatTire(Incident incident)
        {
            _runIncident = incident;
            _stateBeforeIncident = State;
            State = FlowState.FlatTire;
            _tracker.Pause();
            _bike.SetLocked(true);

            var buttons = new List<PanelButton>();

            foreach (IncidentChoice choice in incident.Choices)
            {
                IncidentChoice captured = choice;
                buttons.Add(new PanelButton(ChoiceLabel(choice), () => ChooseTireOption(captured)));
            }

            _ui.ShowModal("Pneu furado!", "O pneu furou no meio da corrida. O que você faz?", buttons.ToArray());
        }

        /// <summary>Força o pneu furado agora (QA e testes), sem esperar o sorteio.</summary>
        public void ForceFlatTire()
        {
            if (!IsDriving)
            {
                return;
            }

            _flatTireRolled = true;
            ShowFlatTire(new Incident(IncidentKind.PneuFurado, new[]
            {
                IncidentChoice.EmpurrarAteBorracharia,
                IncidentChoice.VerAnuncio,
                IncidentChoice.CancelarEntrega,
            }));
        }

        public void ChooseTireOption(IncidentChoice choice)
        {
            if (State != FlowState.FlatTire)
            {
                return;
            }

            IncidentOutcome outcome = Session.ResolveRunIncident(CurrentOrder, _runIncident, choice);

            if (outcome.CancelsDelivery)
            {
                CancelRun();

                return;
            }

            if (outcome.RequiresRewardedAd)
            {
                _ui.ShowModal("Anúncio", "Assistindo ao anúncio para consertar o pneu de graça...");
                AdService.ShowRewarded(watched =>
                {
                    if (!watched)
                    {
                        _ui.Toast("Anúncio não concluído. O conserto demorou mais.");
                    }

                    ResumeAfterTire(outcome);
                });

                return;
            }

            ResumeAfterTire(outcome);
        }

        private void ResumeAfterTire(IncidentOutcome outcome)
        {
            _delayMinutes += outcome.DelayMinutes;
            _tracker.AddDelaySeconds(outcome.DelayMinutes * _tuning.SecondsPerGameMinute);
            _tracker.Resume();
            _bike.SetLocked(false);
            _runIncident = null;
            State = _stateBeforeIncident;
            _ui.HideModal();
            _ui.Toast($"Pneu consertado: +{outcome.DelayMinutes} min no relógio." + (outcome.Cost > Money.Zero ? $" Custo {outcome.Cost}." : string.Empty));
        }

        private void CancelRun()
        {
            _tracker.Stop();
            EnterIdle();
            _runIncident = null;
            State = FlowState.Result;
            LastResult = null;
            _ui.ShowModal(
                "Entrega cancelada",
                "Você cancelou a entrega. Sem pagamento, e o cancelamento pesa na sua taxa de aceite.",
                new PanelButton("Continuar", AfterResult));
        }

        private void ShowClaim(Incident incident)
        {
            _claimIncident = incident;
            State = FlowState.Claim;
            var buttons = new List<PanelButton>();

            foreach (IncidentChoice choice in incident.Choices)
            {
                IncidentChoice captured = choice;
                buttons.Add(new PanelButton(ChoiceLabel(choice), () => ChooseClaimOption(captured)));
            }

            _ui.ShowModal("Reclamação do cliente", "O cliente diz que não recebeu o pedido. O app quer saber o que aconteceu.", buttons.ToArray());
        }

        public void ChooseClaimOption(IncidentChoice choice)
        {
            if (State != FlowState.Claim)
            {
                return;
            }

            IncidentOutcome outcome = Session.ResolveAfterDelivery(CurrentOrder, _claimIncident, choice);
            _claimIncident = null;
            State = FlowState.Result;
            LastResult = null;

            string message = outcome.LosesPay
                ? "O app estornou o pagamento da entrega" + (outcome.ForcedStars.HasValue ? " e o cliente deu nota 1." : ".")
                : "O app ficou do seu lado. Você manteve o pagamento.";

            _ui.ShowModal("Reclamação resolvida", message, new PanelButton("Continuar", AfterResult));
        }

        // ---------- Fim do turno e da semana ----------

        private void ShowShiftSummary()
        {
            State = FlowState.ShiftSummary;
            EnterIdle();
            CurrentOrder = null;

            string body = $"Entregas hoje: {_deliveriesToday}\nResultado do dia: {Session.Wallet.NetForDay(Session.Day)}\nSaldo: {Session.Wallet.Balance}\nNota: {Rating(Session.App.Rating)}\n" + TransitoSummary(false);
            string next = Session.Day >= Session.Economy.DaysPerWeek ? "Fechar a semana" : "Próximo dia";

            _ui.ShowModal($"Fim do turno: {DayName(Session.Day)}", body, new PanelButton(next, EndShift));
        }

        public void EndShift()
        {
            if (State != FlowState.ShiftSummary)
            {
                return;
            }

            Session.EndDay();
            ShowDayStart();
        }

        private void ShowWeekReport()
        {
            State = FlowState.WeekReport;
            EnterIdle();

            if (WeekReport == null)
            {
                WeekReport = Session.EndWeek();
            }

            WeekReport report = WeekReport;
            string title = report.Result == WeekResult.SemanaBoa ? "Semana boa!"
                : report.Result == WeekResult.Sobreviveu ? "Deu pra pagar. Semana que vem tem mais."
                : report.Result == WeekResult.SemMoto ? "Sem moto para trabalhar."
                : "No vermelho.";

            var body = new StringBuilder();
            body.AppendLine("Entregas: " + report.Deliveries);
            body.AppendLine("Bônus da meta: " + report.Bonus);
            body.AppendLine("Multas: " + report.Fines);
            body.AppendLine((report.Transito != null && report.Transito.OrigemMoto == MotoOrigem.Bob ? "Parcela da moto: " : "Aluguel da moto: ") + report.Rent);
            body.AppendLine("Caução devolvida: " + report.DepositRefund);
            body.AppendLine("Nota final: " + Rating(report.Rating));
            body.AppendLine("Saldo final: " + report.FinalBalance);
            body.Append(TransitoSummary(true));

            string button = report.Result == WeekResult.NoVermelho || report.Result == WeekResult.SemMoto ? "Tentar de novo" : "Jogar outra semana";
            _ui.ShowModal(title, body.ToString().TrimEnd(), new PanelButton(button, () => NewWeek()), new PanelButton("Novo perfil", ShowSetup));
        }

        // ---------- Utilidades ----------

        /// <summary>Trava a moto, esconde o HUD e apaga a rota (telas de menu).</summary>
        private void EnterIdle()
        {
            _bike.SetLocked(true);
            _input.ClearAll();
            _ui.SetHudVisible(false);
            _route.ClearTarget();
            _map.Highlight(null);
        }

        private void ApplyWeather(bool rain)
        {
            _bike.SetWet(rain);

            if (_look != null)
            {
                _look.Apply(rain ? LookKind.Rain : LookKind.Sunny);

                if (_rain != null)
                {
                    _rain.SetRaining(rain);
                }

                return;
            }

            Color sky = rain ? new Color(0.42f, 0.47f, 0.52f) : new Color(0.55f, 0.75f, 0.95f);

            if (_mainCamera != null)
            {
                _mainCamera.backgroundColor = sky;
            }

            RenderSettings.fogColor = sky;
            RenderSettings.fogStartDistance = rain ? 90f : 260f;
            RenderSettings.fogEndDistance = rain ? 380f : 650f;

            if (_rain != null)
            {
                _rain.SetRaining(rain);
            }

            if (_sun != null)
            {
                _sun.intensity = rain ? 0.7f : 1.2f;
            }
        }

        private string DayLabel() =>
            $"Dia {Session.Day}: {DayName(Session.Day)}" + (Session.IsRainingToday ? " (chuva)" : string.Empty);

        private static string DayName(int day) => DayNames[Mathf.Clamp(day - 1, 0, DayNames.Length - 1)];

        private string RainyDaysText()
        {
            var days = new List<string>();

            for (int day = 1; day <= Session.Economy.DaysPerWeek; day++)
            {
                if (Session.IsRainyDay(day))
                {
                    days.Add(DayName(day));
                }
            }

            return days.Count == 0 ? "nenhum" : string.Join(" e ", days);
        }

        private static string Rating(double rating) =>
            rating.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');

        private static string Km(double km) =>
            km.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " km";

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;

            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static string ChoiceLabel(IncidentChoice choice)
        {
            switch (choice)
            {
                case IncidentChoice.EmpurrarAteBorracharia: return "Empurrar até a borracharia (R$ 40, +15 min)";
                case IncidentChoice.VerAnuncio: return "Ver anúncio e consertar de graça (+5 min)";
                case IncidentChoice.CancelarEntrega: return "Cancelar a entrega";
                case IncidentChoice.MostrarFoto: return "Mostrar a foto da entrega";
                case IncidentChoice.Contestar: return "Contestar (50% de chance)";
                default: return "Aceitar o prejuízo (perde o pagamento)";
            }
        }
    }
}
