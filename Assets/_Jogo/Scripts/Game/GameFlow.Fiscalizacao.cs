using System.Collections.Generic;
using System.Text;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Parte do GameFlow sobre a fiscalização: blitz do dia, viatura de ronda (flagrante), abordagem (Parar ou Fugir), perseguição
    /// e as consequências (multas, atraso, moto no pátio ou apreendida).
    ///
    /// Quem decide se a fuga dá certo é o Core: GameSession.ResolverAbordagem(Fugir) sorteia (FugaEscapaChance) NA HORA da escolha,
    /// e o resultado (AbordagemOutcome.Escapou) é a única fonte da verdade. A perseguição que o jogador vê é só apresentação: a viatura
    /// recebe o resultado já decidido (willCatch) e se comporta de acordo: se vai pegar, alcança a moto (menos de CatchMeters) ou, no
    /// pior caso, o tempo estoura e ela a encurrala; se não vai, mantém distância e desiste depois de ChaseSeconds.
    /// </summary>
    public sealed partial class GameFlow
    {
        private const float StopPanelMaxDelay = 3f;

        private TransitoMonitor _monitor;
        private BlitzSite[] _blitzSites = new BlitzSite[0];
        private PoliceUnit _patrol;
        private Abordagem _abordagem;
        private PoliceUnit _abordagemUnit;
        private FlowState _stateBeforeAbordagem;
        private bool _abordagemPanelShown;
        private float _abordagemTimer;
        private AbordagemOutcome _pendingOutcome;
        private AbordagemChoice _lastChoice;
        private float _chaseTimer;
        private bool _chaseWillCatch;

        private readonly List<PoliceUnit> _unitsScratch = new List<PoliceUnit>();

        public TransitoMonitor Monitor => _monitor;

        public Abordagem AbordagemAtual => _abordagem;

        /// <summary>O painel Parar/Fugir já apareceu (a moto parou ou passou o tempo máximo).</summary>
        public bool AbordagemPanelShown => _abordagemPanelShown;

        public AbordagemOutcome LastOutcome { get; private set; }

        public IReadOnlyList<BlitzSite> BlitzSites => _blitzSites;

        public PoliceUnit Patrol => _patrol;

        /// <summary>Quantas blitz estão abertas agora.</summary>
        public int OpenBlitzCount
        {
            get
            {
                int count = 0;

                foreach (BlitzSite site in _blitzSites)
                {
                    count += site.IsOpen ? 1 : 0;
                }

                return count;
            }
        }

        private void InitFiscalizacao()
        {
            _monitor = _bike.GetComponent<TransitoMonitor>();

            if (_monitor == null)
            {
                _monitor = _bike.gameObject.AddComponent<TransitoMonitor>();
            }

            _monitor.Configure(_bike, _map, _tuning);
            _monitor.FlagranteVisto -= OnFlagrante;
            _monitor.FlagranteVisto += OnFlagrante;
            PoliceUnit.Desistiu -= OnPoliceGaveUp;
            PoliceUnit.Desistiu += OnPoliceGaveUp;

            _blitzSites = FindObjectsByType<BlitzSite>(FindObjectsInactive.Include);
            System.Array.Sort(_blitzSites, (a, b) => string.CompareOrdinal(a.name, b.name));
            _patrol = null;

            foreach (PoliceUnit unit in FindObjectsByType<PoliceUnit>(FindObjectsInactive.Include))
            {
                if (unit.HasPatrolRoute)
                {
                    _patrol = unit;
                }
            }
        }

        // ---------- Dia ----------

        /// <summary>Início de dia (e de semana): desmonta blitz, a ronda volta a rodar e a moto não é mais vigiada.</summary>
        private void EndOfDayFiscalizacao()
        {
            CloseBlitz();
            _abordagem = null;
            _abordagemUnit = null;

            if (_monitor != null)
            {
                _monitor.Active = false;
                _monitor.ResetState();
            }

            if (_patrol != null)
            {
                _patrol.BeginPatrol();
            }
        }

        private void CloseBlitz()
        {
            foreach (BlitzSite site in _blitzSites)
            {
                if (site != null && site.IsOpen)
                {
                    site.Close();
                }
            }
        }

        /// <summary>Começo do turno: o Core sorteia se há blitz hoje; se sim, abre 1 ou 2 pontos longe da moto.</summary>
        private void OpenDayFiscalizacao()
        {
            if (!Session.SortearBlitzDoDia())
            {
                return;
            }

            OpenBlitz(_random.Chance(0.4) ? 2 : 1);
            _ui.Toast("Aviso no grupo dos motoboys: tem blitz hoje na região. Fique esperto.", 4f);
        }

        /// <summary>Abre até count blitz, escolhidas ao acaso entre as que ficam a mais de 120 m da moto.</summary>
        public void OpenBlitz(int count)
        {
            var candidates = new List<BlitzSite>();

            foreach (BlitzSite site in _blitzSites)
            {
                if (Vector3.Distance(site.Position, _bike.Position) > 120f)
                {
                    candidates.Add(site);
                }
            }

            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int pick = _random.Range(0, candidates.Count);
                candidates[pick].Open();
                candidates.RemoveAt(pick);
            }
        }

        // ---------- Gatilhos ----------

        private void OnBlitzEntered(BlitzSite site, ArcadeBikeController bike)
        {
            if (bike != _bike || !IsDriving || _abordagem != null || site.Used || Session == null || !Session.BlitzHoje)
            {
                return;
            }

            site.Used = true;
            BeginAbordagem(Session.IniciarBlitz(), site.Unit);
        }

        private void OnFlagrante(Infracao infracao, PoliceUnit unit)
        {
            if (!IsRoaming || _abordagem != null || Session == null)
            {
                return;
            }

            BeginAbordagem(Session.ReportarFlagrante(infracao), unit);
        }

        /// <summary>Força uma abordagem por flagrante (testes e QA), como se a viatura tivesse visto a infração.</summary>
        public void ForceFlagrante(Infracao infracao, PoliceUnit unit = null)
        {
            OnFlagrante(infracao, unit != null ? unit : _patrol);
        }

        // ---------- Abordagem ----------

        private void BeginAbordagem(Abordagem abordagem, PoliceUnit unit)
        {
            _abordagem = abordagem;
            _abordagemUnit = unit;
            _stateBeforeAbordagem = State;
            State = FlowState.Abordagem;
            _abordagemPanelShown = false;
            _abordagemTimer = 0f;
            _tracker.Pause();
            _bike.SetLocked(true);
            _input.ClearAll();

            if (unit != null && abordagem.Motivo == AbordagemMotivo.Flagrante)
            {
                unit.StartChase(_bike, true);
            }

            _ui.Toast(abordagem.Motivo == AbordagemMotivo.Blitz ? "Blitz! Os policiais mandam você parar." : "Viatura te seguindo — encoste a moto", 3f);
        }

        private void TickFiscalizacao()
        {
            if (_monitor != null)
            {
                _monitor.Active = IsRoaming;
            }

            ClearOrphanChases();

            if (State == FlowState.Abordagem)
            {
                _abordagemTimer += Time.deltaTime;

                if (_abordagemUnit != null && _abordagemUnit.Mode == PoliceMode.Perseguicao && _abordagemUnit.ReachedStandoff && _bike.Speed < 1.5f)
                {
                    _abordagemUnit.HaltBehind();
                }

                if (!_abordagemPanelShown && (_bike.Speed < 1.2f || _abordagemTimer > StopPanelMaxDelay))
                {
                    ShowAbordagemPanel();
                }
            }
            else if (State == FlowState.Perseguicao)
            {
                TickChase();
            }
        }

        /// <summary>
        /// Toda perseguição acaba: uma viatura com sirene só existe enquanto o fluxo está numa abordagem ou fuga dela. Qualquer outra em
        /// perseguição (o fluxo mudou de estado, o dia virou, outro código a acionou) é dispensada na hora.
        /// </summary>
        private void ClearOrphanChases()
        {
            bool handling = State == FlowState.Abordagem || State == FlowState.Perseguicao || State == FlowState.AbordagemResultado
                            || State == FlowState.MotoRetida;
            _unitsScratch.Clear();

            foreach (PoliceUnit unit in PoliceUnit.Active)
            {
                if (unit != null && unit.Mode == PoliceMode.Perseguicao && !(handling && unit == _abordagemUnit))
                {
                    _unitsScratch.Add(unit);
                }
            }

            foreach (PoliceUnit unit in _unitsScratch)
            {
                unit.Dismiss();

                if (unit == _abordagemUnit)
                {
                    _abordagemUnit = null;
                }
            }

            _unitsScratch.Clear();
        }

        /// <summary>A viatura desistiu (moto longe ou fora de vista): na fuga, é o "despistou" de sempre, com as consequências já decididas.</summary>
        private void OnPoliceGaveUp(PoliceUnit unit)
        {
            if (unit == _abordagemUnit && State == FlowState.Perseguicao && !_chaseWillCatch)
            {
                EndChase(false);
            }
        }

        /// <summary>Chip vermelho pulsando no HUD enquanto alguma viatura persegue a moto (a sirene nunca vem "do nada").</summary>
        private void FeedPolicia()
        {
            bool chasing = false;

            foreach (PoliceUnit unit in PoliceUnit.Active)
            {
                if (unit != null && unit.Mode == PoliceMode.Perseguicao)
                {
                    chasing = true;

                    break;
                }
            }

            _ui.SetPolicia(chasing);
        }

        private void ShowAbordagemPanel()
        {
            _abordagemPanelShown = true;
            _ui.ClearToast();
            var body = new StringBuilder();

            if (_abordagem.Motivo == AbordagemMotivo.Blitz)
            {
                body.AppendLine("Blitz da polícia. Um policial manda você encostar e pede os documentos.");
            }
            else
            {
                string what = _abordagem.Flagrante.HasValue ? CodigoTransito.Regra(_abordagem.Flagrante.Value).Descricao : "uma infração";
                body.AppendLine($"A polícia viu você cometer: {what}. A viatura acendeu a sirene e mandou parar.");
            }

            body.AppendLine();
            body.AppendLine("Parar: conferem os documentos. Perde alguns minutos e pode vir multa.");
            body.AppendLine("Fugir: pode dar certo, mas se te pegarem a multa é pesada, a moto é levada e você perde mais de uma hora.");

            if (!_abordagem.CnhValida)
            {
                body.AppendLine("Você está sem CNH válida: parar quase certamente significa perder a moto.");
            }

            if (_abordagem.Origem == MotoOrigem.Bob)
            {
                body.AppendLine("Sua moto é bob, sem documentos: se ela for parada, será apreendida.");
            }

            _ui.ShowModal(_abordagem.Motivo == AbordagemMotivo.Blitz ? "Blitz!" : "Pare a moto!", body.ToString().TrimEnd(),
                new PanelButton("Parar", () => ChooseAbordagem(AbordagemChoice.Parar)),
                new PanelButton("Fugir", () => ChooseAbordagem(AbordagemChoice.Fugir)));
        }

        /// <summary>Parar ou Fugir. O Core resolve (inclusive o sorteio da fuga) na hora; a cena só apresenta.</summary>
        public void ChooseAbordagem(AbordagemChoice choice)
        {
            if (State != FlowState.Abordagem || !_abordagemPanelShown || _abordagem == null)
            {
                return;
            }

            _lastChoice = choice;
            AbordagemOutcome outcome = Session.ResolverAbordagem(_abordagem, choice);
            _abordagem = null;
            _ui.HideModal();

            if (choice == AbordagemChoice.Parar)
            {
                ShowAbordagemResult(outcome);

                return;
            }

            // Fugir: a viatura vem atrás. O resultado já está decidido pelo Core.
            _pendingOutcome = outcome;
            _chaseWillCatch = !outcome.Escapou;
            _chaseTimer = 0f;
            State = FlowState.Perseguicao;
            _bike.SetLocked(false);
            _tracker.Resume();

            if (_abordagemUnit == null)
            {
                _abordagemUnit = _patrol;
            }

            if (_abordagemUnit != null)
            {
                _abordagemUnit.StartChase(_bike, _chaseWillCatch);
            }

            _ui.Toast("Você acelerou! A viatura vem atrás.", 2.5f);
        }

        private void TickChase()
        {
            _chaseTimer += Time.deltaTime;
            float gap = _abordagemUnit != null ? _abordagemUnit.GapToTarget : float.MaxValue;

            if (_chaseWillCatch)
            {
                bool timeout = _chaseTimer > _tuning.ChaseSeconds + 8f;

                if (gap < Mathf.Max(_tuning.CatchMeters, PoliceUnit.StandoffMeters + 1.5f) || _abordagemUnit == null || timeout)
                {
                    EndChase(true);
                }
            }
            else if (_chaseTimer >= _tuning.ChaseSeconds)
            {
                EndChase(false);
            }
        }

        /// <summary>Fim da perseguição (a cena só mostra o que o Core já decidiu).</summary>
        private void EndChase(bool caught)
        {
            if (_abordagemUnit != null)
            {
                if (caught)
                {
                    _abordagemUnit.HaltBehind();
                }
                else
                {
                    _abordagemUnit.Dismiss();
                    _abordagemUnit = null;
                }
            }

            if (caught)
            {
                _bike.SetLocked(true);
                _tracker.Pause();
                ShowAbordagemResult(_pendingOutcome);

                return;
            }

            // Escapou: segue a corrida. Se a moto é alugada e a placa foi anotada, a multa chega depois (Core), sem aviso.
            State = _stateBeforeAbordagem;
            _monitor.Cooldown(TransitoMonitor.FlagranteCooldownSeconds);
            LastOutcome = _pendingOutcome;
            _multasVistas = Session.MultasPagas.Count;
            _ui.Toast("Você despistou a viatura", 3f);
        }

        private void ShowAbordagemResult(AbordagemOutcome outcome)
        {
            // A viatura encosta e cala a sirene enquanto o resultado está na tela.
            if (_abordagemUnit != null && _abordagemUnit.Mode == PoliceMode.Perseguicao)
            {
                _abordagemUnit.HaltBehind();
            }

            LastOutcome = outcome;
            _pendingOutcome = outcome;
            _multasVistas = Session.MultasPagas.Count;
            _tracker.Pause();
            _bike.SetLocked(true);

            var body = new StringBuilder();

            if (_lastChoice == AbordagemChoice.Fugir)
            {
                body.AppendLine("A viatura te alcançou.");
            }

            foreach (Infracao infraction in outcome.Infracoes)
            {
                RegraInfracao rule = CodigoTransito.Regra(infraction);

                if (rule.DoVeiculo && Session.Origem == MotoOrigem.Bob && outcome.MotoApreendida)
                {
                    body.AppendLine($"{rule.Descricao}: multa do veículo, sem dono no registro. Não chega a você.");
                }
                else
                {
                    string points = Session.Cnh.TemCnh ? $", {rule.Pontos} pontos na CNH" : string.Empty;
                    body.AppendLine($"{rule.Descricao}: {rule.Valor}{points}.");
                }
            }

            if (outcome.Infracoes.Count == 0)
            {
                body.AppendLine("Documentos em ordem. Nenhuma multa.");
            }

            if (outcome.MotoApreendida)
            {
                BeginRetained(outcome, "Moto apreendida", body.AppendLine("A moto foi apreendida de vez: sem documentos, você não recebe de volta. As parcelas acabam. A entrega foi cancelada e o dia termina aqui.").ToString());

                return;
            }

            if (outcome.MotoNoPatio)
            {
                body.AppendLine($"A moto foi levada ao pátio. Para retirar: {Session.CustoPatio} (guincho e diárias). A entrega foi cancelada e o dia termina aqui.");
                BeginRetained(outcome, "Moto levada ao pátio", body.ToString());

                return;
            }

            if (outcome.DelayMinutes > 0)
            {
                _delayMinutes += outcome.DelayMinutes;
                _tracker.AddDelaySeconds(outcome.DelayMinutes * SegundosPorMinuto);
                body.AppendLine($"Você perdeu {outcome.DelayMinutes} min na abordagem.");
            }

            State = FlowState.AbordagemResultado;
            _ui.ShowModal("Abordagem encerrada", body.ToString().TrimEnd(), new PanelButton("Seguir viagem", ResumeAfterAbordagem));
        }

        public void ResumeAfterAbordagem()
        {
            if (State != FlowState.AbordagemResultado)
            {
                return;
            }

            if (_abordagemUnit != null)
            {
                _abordagemUnit.Dismiss();
                _abordagemUnit = null;
            }

            _monitor.Cooldown(TransitoMonitor.FlagranteCooldownSeconds);
            State = _stateBeforeAbordagem;
            _ui.HideModal();
            _tracker.Resume();
            _bike.SetLocked(false);
        }

        private void BeginRetained(AbordagemOutcome outcome, string title, string body)
        {
            _tracker.Stop();
            EnterIdle();
            CurrentOrder = null;
            State = FlowState.MotoRetida;

            if (_abordagemUnit != null)
            {
                _abordagemUnit.Dismiss();
                _abordagemUnit = null;
            }

            _ui.ShowModal(title, body.TrimEnd(), new PanelButton("Continuar", AfterRetained));
        }

        /// <summary>Depois do painel da moto retida: fecha o dia (o Core já o encerrou) e segue para o próximo.</summary>
        public void AfterRetained()
        {
            if (State != FlowState.MotoRetida)
            {
                return;
            }

            Session.EndDay();
            ShowDayStart();
        }
    }
}
