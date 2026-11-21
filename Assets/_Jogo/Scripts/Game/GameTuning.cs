using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Constantes que ligam o mundo da cena (metros) às regras do jogo (km, minutos). Tudo ajustável no Inspector.
    /// </summary>
    public sealed class GameTuning : MonoBehaviour
    {
        [Tooltip("Quilômetros do jogo por metro do mundo. 0,004 faz 3,5 km virarem 875 m de rua.")]
        public float WorldToGameKm = 0.004f;

        [Tooltip("Velocidade de referência (m/s) para calcular o tempo previsto da corrida.")]
        public float ReferenceSpeed = 10f;

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

        public float KmFromMeters(float meters) => meters * WorldToGameKm;
    }
}
