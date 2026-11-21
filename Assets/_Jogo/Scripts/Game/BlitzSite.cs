using System;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Ponto de blitz gerado na cidade (cones afunilando a pista, viatura com barra de luz e policiais). Fica desligado e o GameFlow
    /// abre alguns nos dias com blitz. O Collider da raiz é um gatilho: a moto que entra na zona é parada e a abordagem começa.
    /// Os cones começam uns 100 m antes: dá tempo de ver o aparato e desviar por outra rua (o que o motoboy de verdade faz).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BlitzSite : MonoBehaviour
    {
        [SerializeField] private PoliceUnit _unit;

        /// <summary>Alguma moto entrou na zona de uma blitz aberta.</summary>
        public static event Action<BlitzSite, ArcadeBikeController> BikeEntered;

        public PoliceUnit Unit => _unit;

        /// <summary>A blitz já abordou o jogador hoje (não abre outra abordagem no mesmo lugar).</summary>
        public bool Used { get; set; }

        public bool IsOpen => gameObject.activeInHierarchy;

        public Vector3 Position => transform.position;

        /// <summary>Sentido da rua no ponto (frente da zona).</summary>
        public Vector3 Direction => transform.forward;

        public void Configure(PoliceUnit unit)
        {
            _unit = unit;
        }

        /// <summary>Monta a blitz do dia.</summary>
        public void Open()
        {
            Used = false;
            gameObject.SetActive(true);

            if (_unit != null)
            {
                _unit.BeginCheckpoint();
            }
        }

        /// <summary>Desmonta a blitz.</summary>
        public void Close()
        {
            if (_unit != null && _unit.gameObject.activeSelf)
            {
                _unit.Dismiss();
            }

            gameObject.SetActive(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Used)
            {
                return;
            }

            Rigidbody body = other.attachedRigidbody;

            if (body == null)
            {
                return;
            }

            ArcadeBikeController bike = body.GetComponent<ArcadeBikeController>();

            if (bike != null)
            {
                BikeEntered?.Invoke(this, bike);
            }
        }
    }
}
