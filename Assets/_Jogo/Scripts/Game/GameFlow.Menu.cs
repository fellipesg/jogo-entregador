using System.Collections.Generic;
using System.Text;
using Entregador.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Entregador.Game
{
    /// <summary>
    /// Parte do GameFlow sobre menu principal, exploração livre, menu do celular, pausa e a alimentação do HUD (painel da moto,
    /// navegação e cortina de informações). Exploração livre: o jogador anda pela cidade sem pedidos nem relógio; radares e
    /// polícia continuam valendo. "Começar turno" abre o resumo do dia (o fluxo de sempre).
    /// </summary>
    public sealed partial class GameFlow
    {
        private const float OrbitSeconds = 60f;

        private bool _fromFreeRoam;
        private FlowState _stateBeforePause;
        private float _orbitAngle;
        private bool _orbiting;
        private float _lastInfoTime = -10f;
        private bool _cornerPause;
        private bool _cornerSet;

        /// <summary>
        /// Mostra o botão de debug "Pular corrida". Desligado no jogo normal; liga com a variável de ambiente JOGO_DEBUG=1 ou por código.
        /// </summary>
        public static bool DebugPularCorrida = System.Environment.GetEnvironmentVariable("JOGO_DEBUG") == "1";

        /// <summary>Andando de moto: corrida (pedido em andamento) ou exploração livre.</summary>
        public bool IsRoaming => IsDriving || State == FlowState.FreeRoam || State == FlowState.Online;

        private void InitMenu()
        {
            _ui.SetMenuButton(false, OnCornerButton);

            if (_route != null)
            {
                _route.Anunciou += OnRouteAnnounced;
            }
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;

            if (_route != null)
            {
                _route.Anunciou -= OnRouteAnnounced;
            }

            if (_radioHooked != null)
            {
                _radioHooked.EstacaoMudou -= OnRadioStation;
            }
        }

        private RadioPlayer _radioHooked;

        /// <summary>O rádio nasce com o AudioDirector (depois da cena carregar): liga o aviso de troca de estação assim que existir.</summary>
        private void HookRadio()
        {
            if (_radioHooked == null && RadioPlayer.Instance != null)
            {
                _radioHooked = RadioPlayer.Instance;
                _radioHooked.EstacaoMudou += OnRadioStation;
            }
        }

        private void OnRadioStation(int index, RadioEstacao station)
        {
            if (_ui == null || !_ui.HudVisible)
            {
                return;
            }

            _ui.Toast(station != null ? "Rádio: " + station.Nome : "Rádio desligado", 2f);
        }

        /// <summary>"Recalculando rota" do GPS vira um aviso na tela (a voz fala o mesmo texto).</summary>
        private void OnRouteAnnounced(string text, Manobra manobra)
        {
            if (manobra == Manobra.Nenhuma && IsDriving && _ui != null)
            {
                _ui.Toast("Recalculando rota", 2f);
            }
        }

        // ---------- Menu principal ----------

        /// <summary>Tela inicial: título sobre a câmera que gira pela cidade. Continuar só existe se há uma partida.</summary>
        public void ShowMainMenu()
        {
            State = FlowState.MainMenu;
            Time.timeScale = 1f;
            EnterIdle();
            CloseBlitz();
            ApplyProfile();
            _ui.ClearToast();
            _ui.ShowMainMenu(Session != null, ShowSetup, ContinueFromMenu, () => _ui.ShowOptions(ShowMainMenu), () => _ui.ShowCredits(ShowMainMenu), QuitGame);
        }

        private void ContinueFromMenu()
        {
            if (Session == null)
            {
                return;
            }

            EnterFreeRoam();
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void LateUpdate()
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    OnEscape();
                }

                if (keyboard.mKey.wasPressedThisFrame)
                {
                    OnCornerButton();
                }
            }

            HookRadio();
            TickMenuCamera();
            FeedHud();
        }

        /// <summary>Câmera que gira devagar em volta da moto atrás do menu principal.</summary>
        private void TickMenuCamera()
        {
            if (_followCamera == null || _mainCamera == null)
            {
                return;
            }

            bool orbit = State == FlowState.MainMenu;

            // Só mexe na câmera que segue quando o menu principal abre ou fecha (quem a desliga de fora, como os testes de captura, manda).
            if (orbit != _orbiting)
            {
                _orbiting = orbit;
                _followCamera.enabled = !orbit;

                if (!orbit)
                {
                    _followCamera.Snap();
                }
            }

            if (!orbit)
            {
                return;
            }

            _orbitAngle += 360f / OrbitSeconds * Time.unscaledDeltaTime;
            Vector3 center = _bike.Position + Vector3.up * 1.6f;
            Vector3 offset = Quaternion.Euler(0f, _orbitAngle, 0f) * new Vector3(0f, 0f, -22f);
            _mainCamera.transform.position = center + offset + Vector3.up * 7f;
            _mainCamera.transform.LookAt(center);
        }

        // ---------- Exploração livre ----------

        /// <summary>Solta a moto na cidade: sem pedido, sem relógio. Radares e polícia seguem ativos.</summary>
        public void EnterFreeRoam()
        {
            if (Session.MotoStatus != MotoStatus.Ok)
            {
                ShowDayStart();

                return;
            }

            _fromFreeRoam = false;
            Time.timeScale = 1f;
            ClearRetirada();
            EndOfDayFiscalizacao();
            ApplyWeather(Session.IsRainingToday);
            CurrentOrder = null;
            _route.ClearTarget();
            _map.Highlight(null);
            _ui.HideNavegacao();
            _ui.HideModal();
            _ui.ClearToast();
            State = FlowState.FreeRoam;
            _tracker.Resume();
            _bike.SetWet(Session.IsRainingToday);
            _bike.SetLocked(false);
            _ui.SetHudVisible(true);
        }

        /// <summary>Do "Começar" da tela de novo jogo: exploração livre com o cartão do primeiro dia.</summary>
        private void EnterFreeRoamFromSetup()
        {
            EnterFreeRoam();
            _ui.ShowDayTitle($"Dia {Session.Day} · {DayName(Session.Day)}", "Explore a cidade. Abra o celular para começar o turno");
        }

        /// <summary>Depois do relatório do turno: volta a explorar (o próximo dia só começa pelo celular).</summary>
        private void AfterShiftEnded()
        {
            if (Session.IsWeekOver)
            {
                ShowDayStart();

                return;
            }

            EnterFreeRoam();
            _ui.ShowDayTitle($"Dia {Session.Day} · {DayName(Session.Day)}", Session.IsRainingToday ? "Previsão: chuva forte" : "Previsão: tempo firme");
        }

        private void AddFreeRoamBack(List<PanelButton> buttons)
        {
            if (_fromFreeRoam && Session.MotoStatus == MotoStatus.Ok)
            {
                buttons.Add(new PanelButton("Voltar ao mapa", EnterFreeRoam));
            }
        }

        // ---------- Botão do canto e teclas ----------

        private void OnCornerButton()
        {
            if (State == FlowState.FreeRoam)
            {
                OpenPhoneMenu();
            }
            else if (State == FlowState.FreeRoamMenu)
            {
                CloseFreeRoamMenu();
            }
            else if (State == FlowState.Online)
            {
                OpenOnlineMenu();
            }
            else if (State == FlowState.OnlineMenu)
            {
                CloseOnlineMenu();
            }
            else if (IsDriving)
            {
                PauseGame();
            }
        }

        private void OnEscape()
        {
            if (IsDriving)
            {
                PauseGame();
            }
            else if (State == FlowState.Paused)
            {
                ResumeGame();
            }
            else if (State == FlowState.FreeRoamMenu)
            {
                CloseFreeRoamMenu();
            }
            else if (State == FlowState.FreeRoam)
            {
                OpenPhoneMenu();
            }
            else if (State == FlowState.Online)
            {
                OpenOnlineMenu();
            }
            else if (State == FlowState.OnlineMenu)
            {
                CloseOnlineMenu();
            }
        }

        // ---------- Menu do celular (exploração livre) ----------

        public void OpenPhoneMenu()
        {
            if (State != FlowState.FreeRoam && State != FlowState.FreeRoamMenu)
            {
                return;
            }

            State = FlowState.FreeRoamMenu;
            _bike.SetLocked(true);
            _input.ClearAll();
            Time.timeScale = 0f;

            var body = new StringBuilder();
            body.AppendLine(DayLabel());
            body.AppendLine("Saldo: " + Session.Wallet.Balance);
            body.AppendLine($"Entregas na semana: {Session.App.DeliveriesThisWeek}/{Session.App.Config.GoalDeliveries}");
            body.Append("Nota: " + Rating(Session.App.Rating));

            _ui.ShowModal("Menu", body.ToString(),
                new PanelButton("Começar turno", StartShiftFromMenu),
                new PanelButton("Mapa", () => _ui.ShowMapa(_minimap != null ? _minimap.Texture : null, OpenPhoneMenu)),
                new PanelButton("Garagem e perfil", ShowGarage),
                new PanelButton("Voltar ao menu principal", ShowMainMenu),
                new PanelButton("Fechar", CloseFreeRoamMenu));
        }

        public void CloseFreeRoamMenu()
        {
            if (State != FlowState.FreeRoamMenu)
            {
                return;
            }

            State = FlowState.FreeRoam;
            Time.timeScale = 1f;
            _ui.HideModal();
            _bike.SetLocked(false);
        }

        /// <summary>"Começar turno" do celular: abre o resumo do dia (previsão, folga, capa) como sempre.</summary>
        public void StartShiftFromMenu()
        {
            if (State != FlowState.FreeRoamMenu && State != FlowState.FreeRoam)
            {
                return;
            }

            Time.timeScale = 1f;
            _tracker.Stop();
            _fromFreeRoam = true;
            ShowDayStart();
        }

        private void ShowGarage()
        {
            var body = new StringBuilder();
            body.AppendLine("Piloto: " + MotoCatalogo.Config(_perfil.Personagem).Nome);
            body.AppendLine("Moto: " + MotoCatalogo.Nome(_bike.Modelo));
            body.AppendLine(Session.Origem == MotoOrigem.Bob ? "Situação: bob, sem documentos" : "Situação: alugada, documentada");
            body.AppendLine("CNH: " + (Session.Cnh.TemCnh ? "categoria A, " + Session.Cnh.Pontos + " pontos" : "sem CNH"));
            body.Append("Combustível: " + Mathf.RoundToInt(_ui.Painel.Combustivel * 100f) + "%");
            _ui.ShowModal("Garagem e perfil", body.ToString(), new PanelButton("Voltar", OpenPhoneMenu));
        }

        // ---------- Pausa (durante o turno) ----------

        public void PauseGame()
        {
            if (!IsDriving)
            {
                return;
            }

            _stateBeforePause = State;
            State = FlowState.Paused;
            Time.timeScale = 0f;
            _bike.SetLocked(true);
            _tracker.Pause();
            _input.ClearAll();
            ShowPauseModal();
        }

        private void ShowPauseModal()
        {
            var body = new StringBuilder();
            body.AppendLine(DayLabel());
            body.AppendLine($"Tempo: {GameUI.FormatTime(_tracker.ElapsedSeconds)} de {GameUI.FormatTime(_tracker.ExpectedSeconds)}");
            body.Append("Entregas hoje: " + _deliveriesToday);
            _ui.ShowModal("Pausa", body.ToString(),
                new PanelButton("Continuar", ResumeGame),
                new PanelButton("Opções", () => _ui.ShowOptions(ShowPauseModal)),
                new PanelButton("Encerrar turno", AskEndShift));
        }

        public void ResumeGame()
        {
            if (State != FlowState.Paused)
            {
                return;
            }

            State = _stateBeforePause;
            Time.timeScale = 1f;
            _ui.HideModal();
            _tracker.Resume();
            _bike.SetLocked(false);
        }

        /// <summary>Com entrega em andamento o app pede para terminá-la primeiro; abandonar cancela e pesa na taxa de aceite.</summary>
        private void AskEndShift()
        {
            _ui.ShowModal("Termine a entrega primeiro",
                "Você tem uma entrega em andamento. Termine antes de encerrar o turno. Se abandonar agora, ela (e o próximo pedido, se houver) será cancelada, sem pagamento, e pesa na sua taxa de aceite.",
                new PanelButton("Voltar", ShowPauseModal),
                new PanelButton("Cancelar entrega e encerrar", ConfirmEndShift));
        }

        public void ConfirmEndShift()
        {
            if (State != FlowState.Paused)
            {
                return;
            }

            Time.timeScale = 1f;
            _tracker.Stop();
            CloseMidOffer(true);
            Session.AbandonarTurno();
            ClearProximo();
            CurrentOrder = null;
            ShowShiftSummary();
        }

        // ---------- Novo jogo (dados da tela) ----------

        private SetupDados BuildSetupData()
        {
            EconomyConfig economy = new EconomyConfig();
            Money rentTitan = economy.WeeklyRent;
            Money depTitan = economy.RentalDeposit;
            Money rentTwister = economy.WeeklyRent;
            Money depTwister = economy.RentalDeposit;

            if (_tuning != null)
            {
                rentTwister += Money.Reais((decimal)_tuning.TornadoRentExtra);
                depTwister += Money.Reais((decimal)_tuning.TornadoDepositExtra);
            }

            TransitoConfig t = economy.Transito;
            bool rented = _perfil.Origem == MotoOrigem.Alugada;
            var data = new SetupDados
            {
                Personagem = _perfil.Personagem,
                Modelo = _perfil.Modelo,
                Origem = _perfil.Origem,
                TemCnh = _perfil.TemCnh,
                OnPersonagem = SelectCharacter,
                OnOrigem = SetOrigin,
                OnModelo = SetModel,
                OnCnh = SetCnh,
                OnComecar = StartFromSetup,
            };

            data.TextoOrigem = rented
                ? $"Moto alugada, com documentos: caução {EconomyForProfile().RentalDeposit} e aluguel {EconomyForProfile().WeeklyRent} por semana. Segura contra blitz."
                : $"Moto bob: entrada {t.BobEntrada} e parcela {t.BobParcelaSemanal} por semana. Barata, mas sem documentos: se a polícia parar, você perde a moto.";
            data.TextoCnh = _perfil.TemCnh
                ? "Em dia: a polícia confere os documentos e segue."
                : "Sem CNH: qualquer parada da polícia termina com a moto retida e multa pesada.";
            data.NotaTitan = rented ? $"Aluguel {rentTitan} por semana. Caução {depTitan}." : $"Entrada {t.BobEntrada}. Parcela {t.BobParcelaSemanal} por semana.";
            data.NotaTwister = rented ? $"Aluguel {rentTwister} por semana. Caução {depTwister}." : "Só existe como aluguel.";

            return data;
        }

        public void SelectCharacter(Personagem personagem)
        {
            _perfil.Personagem = personagem;
            ShowSetup();
        }

        public void SetOrigin(MotoOrigem origem)
        {
            _perfil.Origem = origem;
            ShowSetup();
        }

        public void SetModel(MotoModelo modelo)
        {
            if (_perfil.Origem != MotoOrigem.Alugada)
            {
                return;
            }

            _perfil.Modelo = modelo;
            ShowSetup();
        }

        public void SetCnh(bool temCnh)
        {
            _perfil.TemCnh = temCnh;
            ShowSetup();
        }

        // ---------- Alimentação do HUD ----------

        /// <summary>A cada quadro: painel da moto, navegação, botão do canto e (a cada 0,1 s) os dados da cortina de informações.</summary>
        private void FeedHud()
        {
            if (_ui == null || !_ui.HudVisible || _bike == null)
            {
                return;
            }

            bool pause = IsDriving || State == FlowState.Paused;

            if (!_cornerSet || pause != _cornerPause)
            {
                _cornerSet = true;
                _cornerPause = pause;
                _ui.SetMenuButton(pause, OnCornerButton);
            }

            _ui.SetSkipVisible(IsDriving && DebugPularCorrida);
            _ui.SetHeading(_bike.Yaw);
            TransitoMonitor m = _monitor;
            _ui.SetRacingData(_bike.Rpm, _bike.Gear, _bike.RpmLimit, _bike.Throttle, _bike.Brake, m != null ? m.LimitKmh : 0,
                m != null && m.WrongWay, m != null && m.InBusLane, _bike.SpeedKmh, _bike.CortandoGiro, _bike.NoLimitador);

            FeedNavegacao();
            FeedSinal();

            if (Session != null)
            {
                FeedOfertasHud();
            }

            if (Time.unscaledTime - _lastInfoTime > 0.1f)
            {
                _lastInfoTime = Time.unscaledTime;
                _ui.SetPainelInfo(BuildInfo());
            }
        }

        /// <summary>Sinal vermelho à frente da moto (até 60 m): o chip perto da placa de limite, mais a câmera se há fotossensor.</summary>
        private void FeedSinal()
        {
            SemaforoLeitura leitura = IsRoaming ? Semaforo.EstadoPara(_bike.Position, _bike.Forward, 60f) : default;
            bool red = leitura.Existe && leitura.Estado == SemaforoEstado.Vermelho && leitura.Distancia <= 60f;
            _ui.SetSinal(red, leitura.Distancia, leitura.Fotossensor);
        }

        /// <summary>Cartão de navegação do topo: a próxima instrução do RouteGuide (e a seguinte como "Depois"), todo quadro durante a corrida.</summary>
        private void FeedNavegacao()
        {
            if (!IsDriving || CurrentPlan == null || _route == null || !_route.HasTarget)
            {
                _ui.HideNavegacao();

                return;
            }

            Place target = State == FlowState.DrivingToRestaurant ? CurrentPlan.Restaurant : CurrentPlan.Customer;
            InstrucaoGps next = _route.ProximaInstrucao;

            if (!next.Existe)
            {
                float remaining = _route.RemainingMeters > 0f ? _route.RemainingMeters : FlatDistance(_bike.Position, target.Position);
                _ui.SetNavegacao(0, remaining, target.Name);

                return;
            }

            InstrucaoGps then = _route.SegundaInstrucao;
            string street = next.Manobra == Manobra.Chegada || string.IsNullOrEmpty(next.Rua) ? target.Name : next.Rua;
            _ui.SetNavegacao(ManobraParaUi(next.Manobra), (float)next.Distancia, street, then.Existe ? then.Texto : null);
        }

        /// <summary>Manobra do Core para o ícone do cartão: 0 em frente, 1 direita, 2 esquerda, 3 mantenha à direita, 4 mantenha à esquerda, 5 retorno, 6 rotatória, 7 chegada.</summary>
        public static int ManobraParaUi(Manobra manobra)
        {
            switch (manobra)
            {
                case Manobra.VireADireita: return 1;
                case Manobra.VireAEsquerda: return 2;
                case Manobra.MantenhaADireita: return 3;
                case Manobra.MantenhaAEsquerda: return 4;
                case Manobra.Retorno: return 5;
                case Manobra.RotatoriaSaida: return 6;
                case Manobra.Chegada: return 7;
                default: return 0;
            }
        }

        private InfoPainel BuildInfo()
        {
            var p = new InfoPainel();

            if (Session == null)
            {
                return p;
            }

            if (IsDriving && CurrentPlan != null && CurrentOrder != null)
            {
                bool toRestaurant = State == FlowState.DrivingToRestaurant;
                p.Etapa = toRestaurant ? "Retirar" : "Entregar";
                p.Restaurante = CurrentPlan.Restaurant.Name;
                p.Cliente = CurrentPlan.Customer.Name;
                p.DistanciaM = _route != null && _route.HasTarget ? _route.RemainingMeters : 0f;
                p.DecorridoS = _tracker.ElapsedSeconds;
                p.EsperadoS = _tracker.ExpectedSeconds;
                p.Pagamento = CurrentOrder.Pay.ToString();
                p.Gorjeta = CurrentOrder.IsRaining ? "Inclui adicional de chuva" : string.Empty;
                p.Comida = _tracker.FoodCondition;
            }

            p.Saldo = Session.Wallet.Balance.ToString();
            p.Dia = State == FlowState.FreeRoam || State == FlowState.FreeRoamMenu ? $"Dia {Session.Day}: {DayName(Session.Day)} · {Session.Relogio.Texto} (turno não iniciado)" : DayLabel();

            if (State == FlowState.Online || State == FlowState.OnlineMenu)
            {
                p.Modo = "Online: procurando pedidos";
            }

            if (Session.Proximo != null && _proximoPlan != null)
            {
                p.Proximo = (_proximoDuplo ? "Duplo: " : string.Empty) + _proximoPlan.Restaurant.Name + " para " + _proximoPlan.Customer.Name + "  ·  " + Session.Proximo.Pay;
            }
            p.EntregasFeitas = Session.App.DeliveriesThisWeek;
            p.EntregasMeta = Session.App.Config.GoalDeliveries;
            p.Nota = (float)Session.App.Rating;

            Money fines = Money.Zero;

            foreach (MultaRegistro multa in Session.MultasPagas)
            {
                if (multa.DiaInfracao == Session.Day)
                {
                    fines += multa.Valor + multa.Taxa;
                }
            }

            p.Multas = fines > Money.Zero ? fines.ToString() : string.Empty;
            p.PontosCnh = Session.Cnh.TemCnh ? Session.Cnh.Pontos + " de 20 pontos" : "Sem CNH";
            p.CnhStatus = !Session.Cnh.TemCnh ? "Não tenho" : Session.Cnh.Suspensa ? "Suspensa" : "Válida, categoria A";
            p.MotoStatus = MotoCatalogo.Nome(_bike.Modelo) + (Session.Origem == MotoOrigem.Bob ? " · bob, sem documentos" : " · alugada, documentada");

            return p;
        }
    }
}
