using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Marca um bloqueio físico numa rua (barreira de obra, por exemplo). O gerador da cidade marca como bloqueado, no RoadGraph, todo trecho
    /// que passa perto dele: o GPS nunca manda passar por aí. Os testes conferem que todo bloqueio da cena está sobre um trecho bloqueado.
    /// </summary>
    public sealed class ViaBloqueada : MonoBehaviour
    {
        /// <summary>Raio (m) em volta do centro dentro do qual os trechos ficam bloqueados.</summary>
        public const float Raio = 3f;

        public Vector3 Centro => transform.position;
    }
}
