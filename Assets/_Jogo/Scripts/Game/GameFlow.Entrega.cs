using System.Collections.Generic;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Parte do GameFlow sobre a entrega na porta do cliente. Como na retirada: parar a moto (menos de StopKmh) na zona do endereço,
    /// o botão "Entregar pedido [E]" e o diálogo (BalcaoUI, com o cliente ou o porteiro) conduzido pela Entrega do Core. As esperas
    /// (cliente descendo, interfone, subir ao apartamento) correm de verdade, com barra de progresso e a fala mudando no meio. Só depois
    /// do diálogo o pedido é entregue e o ganho entra. Entrar na zona em velocidade não entrega.
    /// </summary>
    public sealed partial class GameFlow
    {
        private Entrega _entrega;

        public Entrega Entrega => _entrega;

        /// <summary>Se definida, a próxima entrega começa nesta situação (testes e QA); senão é sorteada.</summary>
        public EntregaSituacao? EntregaForcada { get; set; }

        /// <summary>A moto está dentro da zona do endereço do cliente.</summary>
        public bool InDeliveryZone => CurrentPlan != null && State == FlowState.DrivingToCustomer &&
                                      FlatDistance(_bike.Position, CurrentPlan.Customer.Position) <= _tuning.MarkerRadius;

        private void TickEntrega()
        {
            if (State == FlowState.AtDoor)
            {
                if (_entrega != null && _entrega.Passo == EntregaPasso.Espera)
                {
                    TickEntregaWait();
                }

                return;
            }

            if (!InDeliveryZone)
            {
                _balcao.HidePrompt();
                _balcao.HideHint();

                return;
            }

            if (_bike.SpeedKmh >= StopKmh)
            {
                _balcao.ShowHint("Pare a moto perto do endereço para entregar o pedido");

                return;
            }

            if (!_balcao.PromptVisible)
            {
                _balcao.ShowPrompt("Entregar pedido  [E]", BeginDelivery);
            }

            if (BalcaoUI.InteractPressed())
            {
                BeginDelivery();
            }
        }

        /// <summary>Começa o diálogo da entrega. Só vale parado dentro da zona do cliente.</summary>
        public void BeginDelivery()
        {
            if (State != FlowState.DrivingToCustomer || CurrentPlan == null || !InDeliveryZone || _bike.SpeedKmh >= StopKmh)
            {
                return;
            }

            State = FlowState.AtDoor;
            _tracker.Pause();
            _bike.SetLocked(true);
            _input.ClearAll();
            _ui.ClearToast();
            _route.ClearTarget();
            _balcao.HidePrompt();
            _balcao.HideHint();

            _entrega = new Entrega(CurrentOrder, CurrentPlan.Customer.Name, _ofertaRandom);

            if (EntregaForcada.HasValue)
            {
                _entrega.Iniciar(EntregaForcada.Value);
            }
            else
            {
                _entrega.Iniciar();
            }

            _balcao.ShowDialogue(CurrentPlan.Customer.Name, _entrega.Fala, EntregaLabels(), OnEntregaChoice, _entrega.Interlocutor);
            RefreshEntrega();
        }

        private List<string> EntregaLabels()
        {
            var labels = new List<string>();

            foreach (EntregaOpcao option in _entrega.Opcoes)
            {
                labels.Add(option.Rotulo);
            }

            return labels;
        }

        private void OnEntregaChoice(int index)
        {
            if (State != FlowState.AtDoor || _entrega == null || _entrega.Passo != EntregaPasso.Atendimento)
            {
                return;
            }

            IReadOnlyList<EntregaOpcao> options = _entrega.Opcoes;

            if (index < 0 || index >= options.Count)
            {
                return;
            }

            EntregaOpcao option = options[index];
            _entrega.Escolher(option.Acao, option.Indice);

            if (option.Acao == EntregaAcao.Fotografar)
            {
                _tracker.PhotoTaken = true;
                _ui.Toast("Foto da entrega tirada.", 1.5f);
            }

            RefreshEntrega();
        }

        private void RefreshEntrega()
        {
            _balcao.SetSubtitle(_entrega.Interlocutor);

            switch (_entrega.Passo)
            {
                case EntregaPasso.Concluida:
                    FinishDelivery();

                    break;
                case EntregaPasso.Espera:
                    _waitElapsed = 0f;
                    _waitTotal = Mathf.Max(0.1f, _entrega.EsperaMinutos * SegundosPorMinuto);
                    _balcao.SetCodeHint(null);
                    _balcao.ShowWait(_entrega.FalaEspera(0.0), 0f, WaitLabel());

                    break;
                default:
                    _balcao.SetSpeech(_entrega.Fala, EntregaLabels(), OnEntregaChoice);

                    break;
            }
        }

        private void TickEntregaWait()
        {
            _waitElapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(_waitElapsed / _waitTotal);
            _balcao.ShowWait(_entrega.FalaEspera(progress), progress, WaitLabel());

            if (_waitElapsed >= _waitTotal)
            {
                _entrega.ConcluirEspera();
                RefreshEntrega();
            }
        }

        /// <summary>O diálogo terminou: soma o tempo perdido e fecha a entrega (o aviso de ganho vem do FinishRun).</summary>
        private void FinishDelivery()
        {
            ApplyEntregaTime();
            CompleteDelivery();
        }

        private void ApplyEntregaTime()
        {
            if (_entrega == null)
            {
                return;
            }

            int minutes = _entrega.MinutosPerdidos;

            if (minutes > 0)
            {
                _delayMinutes += minutes;
                _tracker.AddDelaySeconds(minutes * SegundosPorMinuto);
                _tracker.ExtendExpected(minutes * SegundosPorMinuto * DelayTolerance);
            }
        }

        private void CompleteDelivery()
        {
            RunReport report = _tracker.BuildReport(_tuning.WorldToGameKm);
            report.PenalidadeRetirada = _pickupPenalty;
            report.MinutosNoBalcao = _pickupMinutes;

            if (_entrega != null)
            {
                report.MinutosNaEntrega = _entrega.MinutosPerdidos;
                report.PenalidadeEntrega = _entrega.PenalidadeEstrelas;
                report.FatorGorjeta = _entrega.FatorGorjeta;
                report.RiscoNaoRecebeu = _entrega.RiscoNaoRecebeu;
                report.PhotoTaken |= _entrega.FotoTirada;
            }

            _balcao.HideDialogue();
            DeliveryResult result = Session.FinishRun(CurrentOrder, report);
            _tracker.ClearRadar();
            FinishRun(result);
        }

        public void TakePhoto()
        {
            if (State != FlowState.AtDoor)
            {
                return;
            }

            _tracker.PhotoTaken = true;

            if (_entrega != null && _entrega.Passo == EntregaPasso.Atendimento)
            {
                foreach (EntregaOpcao option in _entrega.Opcoes)
                {
                    if (option.Acao == EntregaAcao.Fotografar)
                    {
                        _entrega.Escolher(EntregaAcao.Fotografar);
                        RefreshEntrega();

                        break;
                    }
                }
            }
        }

        /// <summary>Atalho de QA e dos testes: fecha a entrega agora, como se o diálogo tivesse terminado (sem a gorjeta extra de uma conversa).</summary>
        public void Deliver()
        {
            if (State != FlowState.AtDoor)
            {
                return;
            }

            ApplyEntregaTime();
            CompleteDelivery();
        }
    }
}
