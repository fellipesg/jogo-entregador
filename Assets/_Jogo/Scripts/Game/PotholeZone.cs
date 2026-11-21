using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Zona de buraco: quem passa por cima leva um tranco. O Collider é gatilho.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PotholeZone : MonoBehaviour
    {
        [SerializeField] private float _severity = 1f;

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
                bike.RegisterBump(_severity);
            }
        }
    }
}
