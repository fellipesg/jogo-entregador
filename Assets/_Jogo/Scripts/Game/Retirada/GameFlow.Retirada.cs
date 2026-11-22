using System.Collections.Generic;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Parte do GameFlow sobre a retirada do pedido: parar a moto na zona do balcão (menos de StopKmh), o botão "Retirar pedido" (ou tecla E),
    /// o diálogo com a atendente (Retirada, do Core) e o que sai dele (atraso, nota, cancelamento). Só depois do diálogo o pedido é marcado
    /// como retirado e a rota troca para o cliente. Entrar na zona em velocidade não faz nada; sair sem retirar também não.
    /// </summary>
    public sealed partial class GameFlow
    {
        /// <summary>Velocidade (km/h) abaixo da qual a moto conta como parada no balcão.</summary>
        public const float StopKmh = 3f;

        private const string OlharCodigo = "Olhar o código no app";

        private BalcaoUI _balcao;
        private Retirada _retirada;
        private float _waitTotal;
        private float _waitElapsed;
        private int _pickupPenalty;
        private int _pickupMinutes;

        public Retirada Retirada => _retirada;

        public BalcaoUI Balcao => _balcao;

        /// <summary>Se definida, a próxima retirada começa nesta situação (testes e QA); senão é sorteada.</summary>
        public BalcaoSituacao? SituacaoForcada { get; set; }

        /// <summary>A moto está dentro da zona de parada do restaurante atual.</summary>
        public bool InCounterZone => CurrentPlan != null && State == FlowState.DrivingToRestaurant &&
                                     FlatDistance(_bike.Position, CurrentPlan.Restaurant.Position) <= _tuning.MarkerRadius;

        private void InitRetirada()
        {
            var holder = new GameObject("BalcaoUI");
            holder.transform.SetParent(transform, false);
            _balcao = holder.AddComponent<BalcaoUI>();
            _balcao.Build();
        }

        /// <summary>Fora de uma corrida (menu, exploração livre) não há retirada nem balcão na tela.</summary>
        private void ClearRetirada()
        {
            _retirada = null;

            if (_balcao != null)
            {
                _balcao.HideDialogue();
                _balcao.HidePrompt();
                _balcao.HideHint();
                _balcao.SetCode(null);
            }
        }

        /// <summary>Um pedido aceito: nasce a retirada, com o código que o app mostra.</summary>
        private void BeginRetirada()
        {
            _retirada = new Retirada(CurrentOrder, CurrentPlan.Restaurant.Name, _random);
            _pickupPenalty = 0;
            _pickupMinutes = 0;
            _waitElapsed = 0f;
            _balcao.HideDialogue();
            _balcao.SetCode($"Pedido #{CurrentOrder.Id:000}  Código {_retirada.Codigo}");
        }

        private void TickRetirada()
        {
            if (_balcao == null)
            {
                return;
            }

            if (State == FlowState.DrivingToRestaurant && _retirada != null)
            {
                TickZone();

                return;
            }

            if (State == FlowState.NoBalcao && _retirada != null && _retirada.Passo == RetiradaPasso.Espera)
            {
                TickWait();

                return;
            }

            if (_balcao.PromptVisible)
            {
                _balcao.HidePrompt();
            }

            if (_balcao.HintVisible)
            {
                _balcao.HideHint();
            }

            if (State != FlowState.NoBalcao && State != FlowState.DrivingToRestaurant && _balcao.ChipVisible)
            {
                _balcao.SetCode(null);
            }
        }

        private void TickZone()
        {
            if (!InCounterZone)
            {
                _balcao.HidePrompt();
                _balcao.HideHint();

                return;
            }

            if (_bike.SpeedKmh >= StopKmh)
            {
                _balcao.ShowHint("Pare a moto na frente do balcão para retirar o pedido");

                return;
            }

            if (!_balcao.PromptVisible)
            {
                _balcao.ShowPrompt("Retirar pedido  [E]", BeginCounter);
            }

            if (BalcaoUI.InteractPressed())
            {
                BeginCounter();
            }
        }

        /// <summary>Começa o diálogo no balcão. Só vale parado dentro da zona.</summary>
        public void BeginCounter()
        {
            if (State != FlowState.DrivingToRestaurant || _retirada == null || !InCounterZone || _bike.SpeedKmh >= StopKmh)
            {
                return;
            }

            State = FlowState.NoBalcao;
            _tracker.Pause();
            _bike.SetLocked(true);
            _input.ClearAll();
            _ui.ClearToast();
            _balcao.SetCode(null);

            if (SituacaoForcada.HasValue)
            {
                _retirada.Iniciar(SituacaoForcada.Value);
            }
            else
            {
                _retirada.Iniciar();
            }

            _balcao.ShowDialogue(CurrentPlan.Restaurant.Name, _retirada.Fala, Labels(), OnCounterChoice);
        }

        private List<string> Labels()
        {
            var labels = new List<string>();

            foreach (BalcaoOpcao option in _retirada.Opcoes)
            {
                labels.Add(option.Rotulo);
            }

            if (_retirada.Situacao == BalcaoSituacao.PedeCodigo && _retirada.Passo == RetiradaPasso.Atendimento)
            {
                labels.Add(OlharCodigo);
            }

            return labels;
        }

        private void OnCounterChoice(int index)
        {
            if (State != FlowState.NoBalcao || _retirada == null || _retirada.Passo == RetiradaPasso.Espera)
            {
                return;
            }

            if (index >= _retirada.Opcoes.Count)
            {
                _balcao.SetCodeHint("No app: código " + _retirada.Codigo);

                return;
            }

            BalcaoOpcao option = _retirada.Opcoes[index];
            _retirada.Escolher(option.Acao, option.Indice);
            RefreshCounter();
        }

        private void RefreshCounter()
        {
            switch (_retirada.Passo)
            {
                case RetiradaPasso.Concluida:
                    FinishPickup();

                    break;
                case RetiradaPasso.Cancelada:
                    CancelAtCounter();

                    break;
                case RetiradaPasso.Espera:
                    _waitElapsed = 0f;
                    _waitTotal = Mathf.Max(0.1f, _retirada.EsperaMinutos * _tuning.SecondsPerGameMinute);
                    _balcao.SetCodeHint(null);
                    _balcao.ShowWait(_retirada.Fala, 0f, WaitLabel());

                    break;
                default:
                    _balcao.SetSpeech(_retirada.Fala, Labels(), OnCounterChoice);

                    break;
            }
        }

        private string WaitLabel()
        {
            int left = Mathf.CeilToInt((_waitTotal - _waitElapsed) / Mathf.Max(0.01f, _tuning.SecondsPerGameMinute));

            return $"Aguardando... {Mathf.Max(0, left)} min";
        }

        private void TickWait()
        {
            _waitElapsed += Time.deltaTime;
            _balcao.ShowWait(_retirada.Fala, _waitElapsed / _waitTotal, WaitLabel());

            if (_waitElapsed >= _waitTotal)
            {
                _retirada.ConcluirEspera();
                RefreshCounter();
            }
        }

        private void FinishPickup()
        {
            _pickupMinutes = _retirada.MinutosPerdidos;
            _pickupPenalty = _retirada.PenalidadeEstrelas;

            if (_pickupMinutes > 0)
            {
                _delayMinutes += _pickupMinutes;
                _tracker.AddDelaySeconds(_pickupMinutes * _tuning.SecondsPerGameMinute);
            }

            _balcao.HideDialogue();
            _balcao.SetCode(null);
            State = FlowState.DrivingToCustomer;
            _tracker.Resume();
            _bike.SetLocked(false);
            SetTarget(CurrentPlan.Customer);
            string lost = _pickupMinutes > 0 ? $" ({_pickupMinutes} min no balcão)" : string.Empty;
            _ui.Toast($"Pedido retirado em {CurrentPlan.Restaurant.Name}{lost}. Agora leve até {CurrentPlan.Customer.Name}.", 3.5f);
        }

        private void CancelAtCounter()
        {
            _balcao.HideDialogue();
            _balcao.SetCode(null);
            Session.CancelarNaRetirada(CurrentOrder);
            CancelRun();
        }
    }
}
