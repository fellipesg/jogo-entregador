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
        private float _lastInfoTime = -10f;
        private bool _cornerPause;
        private bool _cornerSet;

        /// <summary>Andando de moto: corrida (pedido em andamento) ou exploração livre.</summary>
        public bool IsRoaming => IsDriving || State == FlowState.FreeRoam;

        private void InitMenu()
        {
            _ui.SetMenuButton(false, OnCornerButton);
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
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

            if (_followCamera.enabled == orbit)
            {
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

        private void AskEndShift()
        {
            _ui.ShowModal("Encerrar turno?",
                "A entrega em andamento será cancelada, sem pagamento, e o cancelamento pesa na sua taxa de aceite. Os pedidos que faltam ficam para o app.",
                new PanelButton("Encerrar turno", ConfirmEndShift),
                new PanelButton("Voltar", ShowPauseModal));
        }

        public void ConfirmEndShift()
        {
            if (State != FlowState.Paused)
            {
                return;
            }

            Time.timeScale = 1f;
            _tracker.Stop();
            Session.App.Cancel();
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

            _ui.SetSkipVisible(IsDriving);
            _ui.SetHeading(_bike.Yaw);
            TransitoMonitor m = _monitor;
            _ui.SetRacingData(_bike.Rpm, _bike.Gear, _bike.RpmLimit, _bike.Throttle, _bike.Brake, m != null ? m.LimitKmh : 0,
                m != null && m.WrongWay, m != null && m.InBusLane, _bike.SpeedKmh);

            if (IsDriving && CurrentPlan != null)
            {
                Place target = State == FlowState.DrivingToRestaurant ? CurrentPlan.Restaurant : CurrentPlan.Customer;
                float distance = _route != null && _route.HasTarget && _route.RemainingMeters > 0f ? _route.RemainingMeters : FlatDistance(_bike.Position, target.Position);
                _ui.SetNavegacao(NavManeuver(target, distance), distance, target.Name);
            }
            else
            {
                _ui.HideNavegacao();
            }

            if (Time.unscaledTime - _lastInfoTime > 0.1f)
            {
                _lastInfoTime = Time.unscaledTime;
                _ui.SetPainelInfo(BuildInfo());
            }
        }

        /// <summary>Manobra aproximada para o cartão de navegação: ângulo do ponto da rota a uns 20 m à frente em relação à moto.</summary>
        private int NavManeuver(Place target, float distance)
        {
            if (distance < 25f)
            {
                return 7;
            }

            Vector3 aim = target.Position;

            if (_route != null && _route.Route != null)
            {
                foreach (Vector3 p in _route.Route)
                {
                    if (FlatDistance(_bike.Position, p) >= 20f)
                    {
                        aim = p;

                        break;
                    }
                }
            }

            Vector3 dir = aim - _bike.Position;
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.01f)
            {
                return 0;
            }

            float angle = Vector3.SignedAngle(_bike.Forward, dir, Vector3.up);
            float abs = Mathf.Abs(angle);

            if (abs < 18f)
            {
                return 0;
            }

            if (abs < 55f)
            {
                return angle > 0f ? 3 : 4;
            }

            if (abs < 140f)
            {
                return angle > 0f ? 1 : 2;
            }

            return 5;
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
            p.Dia = State == FlowState.FreeRoam || State == FlowState.FreeRoamMenu ? $"Dia {Session.Day}: {DayName(Session.Day)} (turno não iniciado)" : DayLabel();
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
