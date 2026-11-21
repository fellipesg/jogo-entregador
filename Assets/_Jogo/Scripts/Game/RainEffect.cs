using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Chuva leve de partículas que acompanha a câmera. O sistema de partículas é criado pelo CityBuilder;
    /// aqui só se liga e desliga a emissão (dias de chuva) e se mantém a nuvem sobre a câmera.
    /// </summary>
    public sealed class RainEffect : MonoBehaviour
    {
        [SerializeField] private ParticleSystem _particles;
        [SerializeField] private Transform _follow;
        [SerializeField] private float _height = 14f;
        [SerializeField] private float _forwardOffset = 12f;

        public bool Raining { get; private set; }

        public void Configure(ParticleSystem particles, Transform follow)
        {
            _particles = particles;
            _follow = follow;
        }

        public void SetRaining(bool raining)
        {
            Raining = raining;

            if (_particles == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = _particles.emission;
            emission.enabled = raining;

            if (raining)
            {
                if (!_particles.isPlaying)
                {
                    _particles.Play();
                }

                // Pré-aquece para a chuva já aparecer inteira no primeiro quadro.
                _particles.Simulate(1.2f, true, false);
                _particles.Play();
            }
            else
            {
                _particles.Clear();
            }
        }

        private void LateUpdate()
        {
            if (_follow == null)
            {
                return;
            }

            Vector3 forward = _follow.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            transform.position = _follow.position + forward * _forwardOffset + Vector3.up * _height;
            transform.rotation = Quaternion.identity;
        }
    }
}
