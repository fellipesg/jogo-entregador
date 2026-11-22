using System;
using System.Collections.Generic;
using System.Text;
using Entregador.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Entregador.Game
{
    /// <summary>
    /// Parte do GameFlow sobre o ritmo dos pedidos, como num app de verdade: o estado Online (turno começado, sem pedido, andando livre
    /// até o app chamar depois de uma espera sorteada pela OfertaAgenda), as zonas quentes no minimapa, as ofertas no meio da corrida
    /// (cartão compacto que não pausa nem bloqueia a moto, Y/N no teclado) e a fila de "Próximo pedido" (no máximo um, inclusive o pedido
    /// duplo do mesmo restaurante), o menu do celular com "Encerrar turno" e o relógio do jogo (RelogioJogo).
    /// </summary>
    public sealed partial class GameFlow
    {
        private const float ZonaRaio = 90f;

        private IGameRandom _ofertaRandom = new SeededRandom(1);
        private float _agendaRestante;
        private float _agendaTotal;
        private readonly List<ZonaQuente> _zonas = new List<ZonaQuente>();
        private ZonaQuente _zonaAtual;
        private float _zonaAvisoAte;
        private bool _zonaAnunciar;

        // Oferta no meio da corrida.
        private bool _midArmada;
        private bool _midDuploArmado;
        private float _midProgresso;
        private float _pickupStartDist;
        private bool _midVisivel;
        private Order _midOrder;
        private RunPlan _midPlan;
        private bool _midDuplo;
        private float _midRestante;

        // Fila (o Core guarda o pedido; aqui fica o plano de rota).
        private RunPlan _proximoPlan;
        private bool _proximoDuplo;

        /// <summary>Uma oferta de pedido apareceu (parada ou no meio da corrida). O áudio toca o sininho.</summary>
        public event Action OfertaApareceu;

        /// <summary>O jogador aceitou uma oferta (parada, no meio da corrida ou o pedido duplo). O áudio toca o jingle de aceito.</summary>
        public event Action OfertaAceita;

        public OfertaConfig OfertaConfig { get; } = new OfertaConfig();

        /// <summary>
        /// Modo antigo de testes e QA: sem espera entre pedidos, a oferta vem logo, e nenhuma oferta extra aparece sozinha no meio da corrida.
        /// Desligado, vale a agenda de verdade (espera, zonas quentes, ofertas durante a corrida).
        /// </summary>
        public bool OfertasImediatas { get; set; }

        /// <summary>Sorteia sozinho ofertas extras no meio da corrida (padrão). Os testes desligam para controlar quando a oferta vem.</summary>
        public bool OfertasNaCorridaAutomaticas { get; set; } = true;

        /// <summary>
        /// Segundos reais por minuto de jogo. Vale RelogioJogo.SegundosPorMinuto (8); os testes baixam para acelerar esperas.
        /// </summary>
        public float SegundosPorMinuto { get; set; } = RelogioJogo.SegundosPorMinuto;

        /// <summary>O relógio do jogo (dia e hora): aberto para outros sistemas, como os pontos de encontro dos entregadores.</summary>
        public RelogioJogo Relogio => Session != null ? Session.Relogio : null;

        public bool IsOnline => State == FlowState.Online;

        public float AgendaRestante => _agendaRestante;

        public IReadOnlyList<ZonaQuente> ZonasAtuais => _zonas;

        public bool NaZonaQuente => _zonaAtual != null;

        /// <summary>Há um cartão de oferta compacto na tela.</summary>
        public bool OfertaCompactaVisivel => _midVisivel;

        public Order OfertaCompactaPedido => _midOrder;

        public bool OfertaCompactaDupla => _midDuplo;

        /// <summary>Plano do pedido na fila ("Próximo pedido"), ou null.</summary>
        public RunPlan ProximoPlan => Session != null && Session.Proximo != null ? _proximoPlan : null;

        public bool ProximoDuplo => _proximoDuplo && Session != null && Session.Proximo != null;

        // ---------- Estado e relógio ----------

        private void ResetOfertas()
        {
            _agendaRestante = _agendaTotal = 0f;
            _zonas.Clear();
            _zonaAtual = null;
            _pillDispensada = false;
            _midArmada = _midDuploArmado = _midVisivel = false;
            _midOrder = null;
            _midPlan = null;
            ClearProximo();
        }

        private void ClearProximo()
        {
            _proximoPlan = null;
            _proximoDuplo = false;
        }

        /// <summary>O relógio do jogo anda enquanto o jogador está na rua ou numa conversa (menus e painéis o congelam).</summary>
        private void TickRelogio()
        {
            if (Session == null)
            {
                return;
            }

            switch (State)
            {
                case FlowState.FreeRoam:
                case FlowState.Online:
                case FlowState.DrivingToRestaurant:
                case FlowState.DrivingToCustomer:
                case FlowState.NoBalcao:
                case FlowState.AtDoor:
                case FlowState.FlatTire:
                    Session.Relogio.Avancar(Time.deltaTime * RelogioJogo.SegundosPorMinuto / Mathf.Max(0.001f, SegundosPorMinuto));

                    break;
            }
        }

        private OfertaContexto Contexto(bool naZona)
        {
            return new OfertaContexto
            {
                Chuva = Session.IsRainingToday,
                Hora = Session.Relogio.Hora,
                TaxaAceite = Session.App.AcceptanceRate,
                NaZonaQuente = naZona,
            };
        }

        /// <summary>
        /// Online, procurando pedidos: o jogador anda livre e o app chama depois de uma espera. Se o turno acabou (o app ofereceu tudo e
        /// nada está em andamento, ou o tempo esgotou), vai ao resumo do turno.
        /// </summary>
        private void EnterOnline()
        {
            CurrentOrder = null;
            CurrentPlan = null;
            CloseMidOffer(true);

            if (Session.TurnoAcabou)
            {
                ShowShiftSummary();

                return;
            }

            if (OfertasImediatas)
            {
                OfferNext();

                return;
            }

            State = FlowState.Online;
            ClearRetirada();
            _entrega = null;
            _input.ClearAll();
            _route.ClearTarget();
            _map.Highlight(null);
            _ui.HideNavegacao();
            _ui.HideModal();
            _ui.SetHudVisible(true);
            _bike.SetWet(Session.IsRainingToday);
            _bike.SetLocked(false);
            _tracker.Resume();

            RollZonas();
            _zonaAnunciar = _ofertaRandom.Chance(0.6);
            _agendaTotal = _agendaRestante = (float)OfertaAgenda.SortearEspera(_ofertaRandom, Contexto(false), OfertaConfig);
        }

        /// <summary>As zonas quentes do dia: escolha determinística por dia (os entregadores falam dela e é a mesma do minimapa).</summary>
        public List<ZonaQuente> ZonasDoDia()
        {
            var candidates = new List<ZonaQuente>();

            foreach (Place restaurant in _map.Restaurants)
            {
                candidates.Add(new ZonaQuente(restaurant.Name, restaurant.Position.x, restaurant.Position.z, ZonaRaio));
            }

            return candidates.Count == 0 ? candidates : MovimentoDoDia.ZonasDoDia(Session.Day, candidates);
        }

        /// <summary>Nomes dos restaurantes do mapa.</summary>
        public List<string> NomesDosRestaurantes()
        {
            var names = new List<string>();

            foreach (Place restaurant in _map.Restaurants)
            {
                names.Add(restaurant.Name);
            }

            return names;
        }

        /// <summary>Nomes dos restaurantes das zonas quentes do dia.</summary>
        private HashSet<string> NomesDasZonasDoDia()
        {
            var names = new HashSet<string>();

            foreach (ZonaQuente zona in ZonasDoDia())
            {
                names.Add(zona.Nome);
            }

            return names;
        }

        /// <summary>O restaurante lento do dia (nome) e os minutos de espera típicos dele; null se não houver.</summary>
        public string RestauranteLentoDoDia(out int minutos)
        {
            List<string> names = NomesDosRestaurantes();
            int index = MovimentoDoDia.IndiceLento(Session.Day, names, NomesDasZonasDoDia());
            minutos = index >= 0 ? MovimentoDoDia.MinutosDeEspera(Session.Day, names[index]) : 0;

            return index >= 0 ? names[index] : null;
        }

        /// <summary>Pesos e esperas do balcão do restaurante hoje (o lento do dia tem mais "tá saindo" e fila).</summary>
        public RetiradaConfig ConfigDoBalcao(string restaurante)
        {
            return MovimentoDoDia.ConfigDoBalcao(Session.Day, restaurante, NomesDosRestaurantes(), NomesDasZonasDoDia());
        }

        /// <summary>Zonas quentes do dia (de 1 a 3 restaurantes).</summary>
        private void RollZonas()
        {
            _zonas.Clear();
            _zonaAtual = null;
            _zonas.AddRange(ZonasDoDia());
        }

        private void TickOnline()
        {
            if (Session.TurnoAcabou)
            {
                _ui.Toast(Session.TempoEsgotado ? "O tempo do turno acabou." : "Acabaram os pedidos de hoje.", 2.5f);
                ShowShiftSummary();

                return;
            }

            // De vez em quando o app avisa onde o movimento está alto (alguns segundos depois de ficar online, para não cobrir o ganho da entrega).
            if (_zonaAnunciar && _agendaTotal - _agendaRestante >= 6f && _zonas.Count > 0)
            {
                _zonaAnunciar = false;
                _ui.Toast($"Movimento alto perto de {_zonas[_ofertaRandom.Range(0, _zonas.Count)].Nome}", 3.5f);
            }

            ZonaQuente zone = ZonasQuentes.Qual(_zonas, _bike.Position.x, _bike.Position.z);

            if (zone != null && _zonaAtual == null && Time.unscaledTime > _zonaAvisoAte)
            {
                _zonaAvisoAte = Time.unscaledTime + 20f;
                _ui.Toast("Zona quente: os pedidos chegam mais rápido por aqui.", 2.5f);
            }

            _zonaAtual = zone;
            _agendaRestante -= Time.deltaTime * (float)OfertaAgenda.VelocidadeDaEspera(zone != null, OfertaConfig);

            if (_agendaRestante <= 0f)
            {
                OfferNext();
            }
        }

        /// <summary>Faz o app chamar agora (testes e QA): encerra a espera.</summary>
        public void ForcarOfertaAgora()
        {
            if (State == FlowState.Online)
            {
                OfferNext();
            }
        }

        /// <summary>Define quantos segundos faltam para o app chamar (testes e QA).</summary>
        public void ForcarEsperaDaAgenda(float segundos)
        {
            _agendaRestante = _agendaTotal = Mathf.Max(0f, segundos);
        }

        // ---------- Depois da entrega ----------

        /// <summary>Entrega (ou cancelamento) resolvida: segue direto para o próximo pedido da fila ou volta a procurar pedidos.</summary>
        private void ContinueAfterDelivery()
        {
            CurrentOrder = null;

            if (Session.Proximo != null)
            {
                StartQueuedRun();

                return;
            }

            EnterOnline();
        }

        /// <summary>O próximo pedido vira a corrida atual, sem espera. No pedido duplo o restaurante já foi: vai direto ao cliente.</summary>
        private void StartQueuedRun()
        {
            RunPlan plan = _proximoPlan;
            bool duplo = _proximoDuplo;
            Order order = Session.PromoverProximo();
            ClearProximo();
            CurrentOrder = order;
            CurrentPlan = plan;

            float expected = plan.TotalMeters / _tuning.ReferenceSpeed * _tuning.ExpectedTimeFactor;
            _tracker.Begin(expected, plan.TotalMeters);
            _flatTireRolled = false;
            _delayMinutes = 0;
            _pickupPenalty = 0;
            _pickupMinutes = 0;
            _lastCustomer = plan.Customer;
            _ui.HideModal();
            _ui.SetHudVisible(true);
            _bike.SetWet(Session.IsRainingToday);
            _bike.SetLocked(false);
            _input.ClearAll();

            if (duplo)
            {
                ClearRetirada();
                _retirada = null;
                State = FlowState.DrivingToCustomer;
                SetTarget(plan.Customer);
                _ui.Toast($"Segundo pedido na bolsa. Entregue em {plan.Customer.Name}.", 3.5f);
            }
            else
            {
                BeginRetirada();
                State = FlowState.DrivingToRestaurant;
                SetTarget(plan.Restaurant);
                _ui.Toast($"Próximo pedido: vá até {plan.Restaurant.Name}. {Retirada.TextoCodigo}", 3.5f);
            }

            ArmMidRun();
        }

        // ---------- Oferta no meio da corrida ----------

        /// <summary>Ao começar uma corrida decide se vai chegar uma oferta extra (pedido duplo ou próximo pedido) e em que ponto.</summary>
        private void ArmMidRun()
        {
            _midArmada = _midDuploArmado = false;

            if (OfertasImediatas || !OfertasNaCorridaAutomaticas || Session == null || !Session.PodeOferecerAdicional)
            {
                return;
            }

            _pickupStartDist = Mathf.Max(1f, FlatDistance(_bike.Position, CurrentPlan.Restaurant.Position));

            if (State == FlowState.DrivingToRestaurant && OfertaAgenda.SortearPedidoDuplo(_ofertaRandom, OfertaConfig))
            {
                _midDuploArmado = true;

                return;
            }

            if (OfertaAgenda.SortearOfertaNaCorrida(_ofertaRandom, Contexto(false), OfertaConfig))
            {
                _midArmada = true;
                _midProgresso = (float)OfertaAgenda.SortearProgressoDaOferta(_ofertaRandom, OfertaConfig);
            }
        }

        private void TickMidRun()
        {
            if (Session == null)
            {
                return;
            }

            if (_midVisivel)
            {
                TickMidOffer();

                return;
            }

            if ((!_midArmada && !_midDuploArmado) || Time.timeScale <= 0f || _abordagem != null || CurrentPlan == null)
            {
                return;
            }

            if (!Session.PodeOferecerAdicional)
            {
                _midArmada = _midDuploArmado = false;

                return;
            }

            bool counterWait = State == FlowState.NoBalcao && _retirada != null && _retirada.Passo == RetiradaPasso.Espera;
            bool doorWait = State == FlowState.AtDoor && _entrega != null && _entrega.Passo == EntregaPasso.Espera;
            bool waited = _waitTotal > 0f && _waitElapsed >= 0.3f * _waitTotal;
            bool trigger;

            if (_midDuploArmado)
            {
                if (State == FlowState.DrivingToCustomer || doorWait)
                {
                    _midDuploArmado = false;

                    return;
                }

                trigger = (State == FlowState.DrivingToRestaurant && FlatDistance(_bike.Position, CurrentPlan.Restaurant.Position) <= 0.55f * _pickupStartDist)
                          || (counterWait && waited);
            }
            else
            {
                trigger = (State == FlowState.DrivingToCustomer && _tracker.Progress >= _midProgresso) || ((counterWait || doorWait) && waited);
            }

            if (trigger)
            {
                ShowMidOffer(_midDuploArmado);
            }
        }

        private void ShowMidOffer(bool duplo)
        {
            _midArmada = _midDuploArmado = false;
            Order extra = Session.OferecerAdicional();

            if (extra == null)
            {
                return;
            }

            _midPlan = duplo
                ? RunPlanner.PlanSameRestaurant(_map, _tuning, extra, CurrentPlan.Restaurant, _ofertaRandom, CurrentPlan.Customer)
                : RunPlanner.Plan(_map, _tuning, extra, CurrentPlan.Customer.Position, _ofertaRandom, CurrentPlan.Customer);
            _midOrder = extra;
            _midDuplo = duplo;
            _midRestante = (float)OfertaConfig.SegundosOfertaNaCorrida;
            _midVisivel = true;

            _ui.ShowOfferCompact(duplo ? "Pedido duplo" : "Próximo pedido", _midPlan.Restaurant.Name, _midPlan.Customer.Name, Km(extra.DistanceKm),
                extra.Pay.ToString(), extra.IsRaining, _midRestante, (float)OfertaConfig.SegundosOfertaNaCorrida, duplo,
                AcceptMidOffer, RejectMidOffer);
            OfertaApareceu?.Invoke();
        }

        private void TickMidOffer()
        {
            if (Time.timeScale <= 0f)
            {
                return;
            }

            _midRestante -= Time.deltaTime;
            _ui.SetOfferCompactTimer(_midRestante, (float)OfertaConfig.SegundosOfertaNaCorrida);

            if (_midRestante <= 0f)
            {
                RejectMidOffer();

                return;
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null || _ui.ModalVisible)
            {
                return;
            }

            if (keyboard.yKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)
            {
                AcceptMidOffer();
            }
            else if (keyboard.nKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)
            {
                RejectMidOffer();
            }
        }

        /// <summary>Aceita a oferta extra: ela vira o "Próximo pedido" e a corrida atual segue sem parar.</summary>
        public void AcceptMidOffer()
        {
            if (!_midVisivel)
            {
                return;
            }

            Order order = _midOrder;
            Session.AceitarAdicional(order);
            _proximoPlan = _midPlan;
            _proximoDuplo = _midDuplo;
            _midVisivel = false;
            _midOrder = null;
            _ui.HideOfferCompact();
            _ui.Toast(_proximoDuplo
                ? $"Pedido duplo aceito: retire os dois em {_proximoPlan.Restaurant.Name}."
                : $"Próximo pedido aceito: {_proximoPlan.Restaurant.Name}. Termine a entrega atual.", 3f);
            OfertaAceita?.Invoke();
        }

        /// <summary>Recusa (ou deixa vencer) a oferta extra. Pesa na taxa de aceite como qualquer outra.</summary>
        public void RejectMidOffer()
        {
            if (!_midVisivel)
            {
                return;
            }

            CloseMidOffer(true);
            OfferRefused?.Invoke();
        }

        /// <summary>Esconde o cartão. Com refuse, a oferta pendente é recusada no Core (telas que tomam a cena, abordagem, fim do turno).</summary>
        private void CloseMidOffer(bool refuse)
        {
            if (_midVisivel)
            {
                _midVisivel = false;

                if (refuse && Session != null && _midOrder != null && Session.Adicional != null)
                {
                    Session.RecusarAdicional(_midOrder);
                }

                _midOrder = null;
                _ui.HideOfferCompact();
            }

            _midArmada = _midDuploArmado = false;
        }

        /// <summary>Arma a oferta extra para chegar quando a corrida passar deste progresso (testes e QA do gatilho por progresso).</summary>
        public void ArmarOfertaNaCorridaAgora(float progresso)
        {
            _midArmada = true;
            _midDuploArmado = false;
            _midProgresso = progresso;
        }

        /// <summary>Faz uma oferta extra aparecer agora, durante a corrida (testes e QA). Duplo só vale antes de retirar o pedido.</summary>
        public void ForcarOfertaNaCorrida(bool duplo = false)
        {
            if (Session == null || _midVisivel || CurrentPlan == null || !Session.PodeOferecerAdicional)
            {
                return;
            }

            if (State != FlowState.DrivingToCustomer && State != FlowState.DrivingToRestaurant && State != FlowState.NoBalcao && State != FlowState.AtDoor)
            {
                return;
            }

            ShowMidOffer(duplo && State != FlowState.DrivingToCustomer && State != FlowState.AtDoor);
        }

        // ---------- Menu do celular com o turno em andamento ----------

        /// <summary>O celular (M) online e sem pedido: Encerrar turno, Mapa, Fechar. O relógio e a espera ficam parados.</summary>
        public void OpenOnlineMenu()
        {
            if (State != FlowState.Online && State != FlowState.OnlineMenu)
            {
                return;
            }

            State = FlowState.OnlineMenu;
            _bike.SetLocked(true);
            _input.ClearAll();
            Time.timeScale = 0f;

            var body = new StringBuilder();
            body.AppendLine(DayLabel());
            body.AppendLine("Saldo: " + Session.Wallet.Balance);
            body.AppendLine("Entregas hoje: " + _deliveriesToday);
            body.Append("Status: online, procurando pedidos");

            _ui.ShowModal("Menu", body.ToString(),
                new PanelButton("Encerrar turno", AskEndOnlineShift),
                new PanelButton("Mapa", () => _ui.ShowMapa(_minimap != null ? _minimap.Texture : null, OpenOnlineMenu)),
                new PanelButton("Fechar", CloseOnlineMenu));
        }

        public void CloseOnlineMenu()
        {
            if (State != FlowState.OnlineMenu)
            {
                return;
            }

            State = FlowState.Online;
            Time.timeScale = 1f;
            _ui.HideModal();
            _bike.SetLocked(false);
        }

        private void AskEndOnlineShift()
        {
            if (State != FlowState.OnlineMenu)
            {
                return;
            }

            _ui.ShowModal("Encerrar turno?",
                "Você fica offline e o app para de chamar. Sem entrega em andamento, nada é cancelado.",
                new PanelButton("Encerrar turno", ConfirmEndOnlineShift),
                new PanelButton("Voltar", OpenOnlineMenu));
        }

        /// <summary>Encerra o turno estando online e sem pedido.</summary>
        public void ConfirmEndOnlineShift()
        {
            if (State != FlowState.OnlineMenu)
            {
                return;
            }

            Time.timeScale = 1f;
            Session.AbandonarTurno();
            ClearProximo();
            CurrentOrder = null;
            ShowShiftSummary();
        }

        // ---------- HUD ----------

        /// <summary>A pílula "Procurando pedidos…", as zonas quentes no minimapa e a pílula "Começar turno" da exploração livre.</summary>
        private void FeedOfertasHud()
        {
            bool online = State == FlowState.Online;
            _ui.SetProcurando(online, Session.Relogio.Texto, online && _zonaAtual != null);

            if (online && _zonas.Count > 0)
            {
                var centers = new Vector2[_zonas.Count];
                var radii = new float[_zonas.Count];

                for (int i = 0; i < _zonas.Count; i++)
                {
                    centers[i] = new Vector2((float)_zonas[i].X, (float)_zonas[i].Z);
                    radii[i] = (float)_zonas[i].Raio;
                }

                _ui.SetZonasMinimapa(centers, radii, new Vector2(_bike.Position.x, _bike.Position.z), _bike.Yaw);
            }
            else
            {
                _ui.SetZonasMinimapa(null, null, Vector2.zero, 0f);
            }

            if (_pillDia != Session.Day)
            {
                _pillDia = Session.Day;
                _pillDispensada = false;
            }

            _ui.SetComecarPill(State == FlowState.FreeRoam && !_pillDispensada, StartShiftFromMenu, () => _pillDispensada = true);
        }

        private int _pillDia;
        private bool _pillDispensada;
    }
}
