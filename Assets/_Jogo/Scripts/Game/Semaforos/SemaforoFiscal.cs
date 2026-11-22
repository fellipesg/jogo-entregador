using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Fiscaliza a moto do jogador nos semáforos: ao cruzar a linha de retenção de um braço no vermelho (passada a tolerância de 0,5 s),
    /// indo para a frente ao longo do braço (não é trânsito cruzando), é "Avançar o sinal vermelho". Só vira multa se alguém vê:
    /// o fotossensor do cruzamento (sempre captura; a multa chega dias depois, como a do radar) ou uma viatura de ronda perto
    /// (flagrante, pelo TransitoMonitor). Sem nenhum dos dois, o jogador se safa.
    /// </summary>
    public sealed class SemaforoFiscal : MonoBehaviour
    {
        private const float MinForwardSpeed = 1.5f;
        private const float MinAlignment = 0.5f;

        [SerializeField] private ArcadeBikeController _bike;

        private Vector3 _previous;
        private bool _hasPrevious;
        private TransitoMonitor _monitor;
        private DeliveryRunTracker _tracker;

        /// <summary>Quantas vezes a moto avançou o vermelho (visto ou não).</summary>
        public int Avancos { get; private set; }

        /// <summary>Quantas dessas o fotossensor capturou.</summary>
        public int CapturasFotossensor { get; private set; }

        /// <summary>Quantas a polícia viu.</summary>
        public int FlagrantesPolicia { get; private set; }

        public void Configure(ArcadeBikeController bike)
        {
            Desligar();
            _bike = bike;
            _hasPrevious = false;
            Ligar();
        }

        private void OnEnable()
        {
            Ligar();
        }

        private void OnDisable()
        {
            Desligar();
        }

        private void Ligar()
        {
            if (_bike != null)
            {
                _bike.Teleported -= OnTeleported;
                _bike.Teleported += OnTeleported;
            }
        }

        private void Desligar()
        {
            if (_bike != null)
            {
                _bike.Teleported -= OnTeleported;
            }
        }

        private void OnTeleported()
        {
            _hasPrevious = false;
        }

        private void Start()
        {
            if (_bike == null)
            {
                Configure(FindFirstObjectByType<ArcadeBikeController>());
            }
        }

        private void FixedUpdate()
        {
            if (_bike == null)
            {
                return;
            }

            Vector3 position = _bike.Position;

            if (_hasPrevious)
            {
                Vector3 heading = _bike.Forward;

                foreach (SemaforoControlador controlador in SemaforoControlador.Lista)
                {
                    foreach (SemaforoAproximacao a in controlador.Aproximacoes)
                    {
                        if (a.DistanciaAte(_previous) > 0f && a.DistanciaAte(position) <= 0f && a.NaPista(position, 0.5f)
                            && Vector3.Dot(heading, a.Direcao) > MinAlignment && _bike.ForwardSpeed > MinForwardSpeed)
                        {
                            Cruzou(controlador, a);
                        }
                    }
                }
            }

            _previous = position;
            _hasPrevious = true;
        }

        /// <summary>A moto cruzou a linha deste braço agora (o FixedUpdate chama isto; os testes também). Devolve a infração, se houve.</summary>
        public Infracao? Cruzou(SemaforoControlador controlador, SemaforoAproximacao a)
        {
            Infracao? infracao = SemaforoRegras.ClassificarAvanco(controlador.Estado(a), controlador.SegundosDesdeVermelho(a));

            if (!infracao.HasValue)
            {
                return null;
            }

            Avancos++;

            if (_tracker == null)
            {
                _tracker = _bike.GetComponent<DeliveryRunTracker>();
            }

            if (controlador.Fotossensor)
            {
                if (_tracker != null && _tracker.RegisterRedLight())
                {
                    CapturasFotossensor++;
                    controlador.Capturar(a);
                }

                return infracao;
            }

            if (_monitor == null)
            {
                _monitor = _bike.GetComponent<TransitoMonitor>();
            }

            if (_monitor != null && _monitor.ReportarInfracao(infracao.Value))
            {
                FlagrantesPolicia++;
            }

            return infracao;
        }
    }
}
