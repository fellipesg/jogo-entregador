using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Constantes que ligam o mundo da cena (metros) às regras do jogo (km, minutos). Tudo ajustável no Inspector.
    /// </summary>
    public sealed class GameTuning : MonoBehaviour
    {
        [Tooltip("Quilômetros do jogo por metro do mundo. 0,0042 faz 3,5 km virarem cerca de 830 m de rua (no mapa do OSM as rotas entre restaurantes e endereços chegam a ~1.200 m; 1,5 a 5,5 km cabem na faixa).")]
        public float WorldToGameKm = 0.0042f;

        [Tooltip("Velocidade de referência (m/s) para calcular o tempo previsto da corrida. Menor que a velocidade máxima da moto (13 m/s) porque há esquinas, buracos e trânsito.")]
        public float ReferenceSpeed = 7f;

        [Tooltip("Distância mínima (m) da moto até o restaurante; evita pedidos cujo restaurante está colado na moto.")]
        public float MinPickupMeters = 150f;

        [Tooltip("Multiplicador do tempo previsto. 1 = distância / velocidade de referência.")]
        public float ExpectedTimeFactor = 1f;

        [Tooltip("Quantos segundos reais valem 1 minuto de jogo (atrasos dos imprevistos).")]
        public float SecondsPerGameMinute = 5f;

        [Tooltip("Tempo para aceitar um pedido.")]
        public float OfferSeconds = 15f;

        [Tooltip("Raio (m) dos marcadores de restaurante e de cliente.")]
        public float MarkerRadius = 8f;

        [Tooltip("Imprevisto de pneu é sorteado quando a corrida chega nesta fração do percurso.")]
        [Range(0.2f, 0.8f)] public float FlatTireAtProgress = 0.5f;

        [Tooltip("Segundos do anúncio falso.")]
        public float FakeAdSeconds = 2f;

        [Header("Controles")]
        [Tooltip("Esterçar inclinando o celular (acelerômetro), além do arrasto na metade esquerda da tela. Desligado por padrão.")]
        public bool TiltSteering = false;

        [Header("Moto alugada")]
        [Tooltip("Aluguel semanal a mais para o Tornado 250 (a Titã 160 usa o aluguel padrão da economia).")]
        public float TornadoRentExtra = 120f;

        [Tooltip("Caução a mais para o Tornado 250.")]
        public float TornadoDepositExtra = 60f;

        [Header("Fiscalização")]
        [Tooltip("Distância (m) máxima em que a polícia vê uma infração (com linha de visão livre).")]
        public float PoliceSightMeters = 40f;

        [Tooltip("Excesso de velocidade (fração acima do limite da rua) que a polícia autua em flagrante: 0,2 = 20%.")]
        public float PoliceSpeedTolerance = 0.2f;

        [Tooltip("Duração da perseguição (s) depois de escolher Fugir.")]
        public float ChaseSeconds = 20f;

        [Tooltip("Distância (m) em que a viatura alcança a moto na perseguição.")]
        public float CatchMeters = 4f;

        public float KmFromMeters(float meters) => meters * WorldToGameKm;
    }
}
