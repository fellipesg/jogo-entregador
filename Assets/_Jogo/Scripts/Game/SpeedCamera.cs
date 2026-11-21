using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Radar fixo (fiscalização eletrônica). O Collider é um gatilho que atravessa a pista: ao passar, mede a velocidade; acima do limite
    /// (com a tolerância do CTB, que o Core aplica) a infração vai para a corrida e a câmera dá um flash. O jogador não recebe aviso nenhum
    /// na hora: a multa chega dias depois pela GameSession (e só na moto alugada).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SpeedCamera : MonoBehaviour
    {
        private const float FlashSeconds = 0.18f;

        [SerializeField] private int _limitKmh = 50;
        [SerializeField] private Light _flashLight;
        [SerializeField] private Renderer _flashLens;

        private float _flashUntil;

        /// <summary>Limite de velocidade da via (km/h), também mostrado na placa antes do radar.</summary>
        public int LimitKmh => _limitKmh;

        /// <summary>Última velocidade medida (km/h); 0 se ninguém passou ainda.</summary>
        public float LastMeasuredKmh { get; private set; }

        /// <summary>Quantas vezes este radar capturou uma infração.</summary>
        public int Captures { get; private set; }

        public void Configure(int limitKmh, Light flashLight, Renderer flashLens)
        {
            _limitKmh = limitKmh;
            _flashLight = flashLight;
            _flashLens = flashLens;
            SetFlash(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;

            if (body == null)
            {
                return;
            }

            ArcadeBikeController bike = body.GetComponent<ArcadeBikeController>();

            if (bike != null)
            {
                Measure(bike);
            }
        }

        /// <summary>Mede a moto agora (o gatilho chama isto; os testes também).</summary>
        public void Measure(ArcadeBikeController bike)
        {
            float measured = bike.SpeedKmh;
            LastMeasuredKmh = measured;
            DeliveryRunTracker tracker = bike.GetComponent<DeliveryRunTracker>();

            if (tracker != null && tracker.RegisterRadar(measured, _limitKmh))
            {
                Captures++;
                _flashUntil = Time.time + FlashSeconds;
                SetFlash(true);
            }
        }

        private void Update()
        {
            if (_flashUntil > 0f && Time.time >= _flashUntil)
            {
                _flashUntil = 0f;
                SetFlash(false);
            }
        }

        private void SetFlash(bool on)
        {
            if (_flashLight != null)
            {
                _flashLight.enabled = on;
            }

            if (_flashLens != null)
            {
                _flashLens.enabled = on;
            }
        }
    }
}
