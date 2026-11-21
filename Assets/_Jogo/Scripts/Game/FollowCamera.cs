using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Câmera de terceira pessoa: atrás e acima da moto, suave, com o campo de visão abrindo com a velocidade.</summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private ArcadeBikeController _target;
        [SerializeField] private float _distance = 6.5f;
        [SerializeField] private float _height = 3.2f;
        [SerializeField] private float _lookAhead = 5f;
        [SerializeField] private float _positionSharpness = 7f;
        [SerializeField] private float _headingSharpness = 3f;
        [SerializeField] private float _baseFov = 62f;
        [SerializeField] private float _speedFov = 12f;

        private float _heading;
        private Camera _camera;

        public void Configure(ArcadeBikeController target)
        {
            _target = target;
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void Start()
        {
            Snap();
        }

        /// <summary>Coloca a câmera direto atrás da moto, sem suavização (depois de um teleporte).</summary>
        public void Snap()
        {
            if (_target == null)
            {
                return;
            }

            _heading = _target.Yaw;
            transform.position = DesiredPosition();
            transform.rotation = DesiredRotation();
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            _heading = Mathf.LerpAngle(_heading, _target.Yaw, 1f - Mathf.Exp(-_headingSharpness * dt));

            // Em alta velocidade a câmera segue mais colada (senão a moto fugiria do quadro) e olha mais longe.
            float ratio = Mathf.Clamp01(_target.Speed / _target.TopSpeed);
            float sharpness = Mathf.Lerp(_positionSharpness, _positionSharpness * 2.2f, ratio);
            transform.position = Vector3.Lerp(transform.position, DesiredPosition(), 1f - Mathf.Exp(-sharpness * dt));
            transform.rotation = DesiredRotation();

            if (_camera != null)
            {
                float speedRatio = ratio;
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, _baseFov + _speedFov * speedRatio, 1f - Mathf.Exp(-3f * dt));
            }
        }

        private Vector3 DesiredPosition()
        {
            Vector3 back = Quaternion.Euler(0f, _heading, 0f) * Vector3.back;

            float ratio = Mathf.Clamp01(_target.Speed / _target.TopSpeed);

            return _target.Position + back * (_distance * (1f + 0.25f * ratio)) + Vector3.up * _height;
        }

        private Quaternion DesiredRotation()
        {
            float ratio = Mathf.Clamp01(_target.Speed / _target.TopSpeed);
            Vector3 ahead = Quaternion.Euler(0f, _heading, 0f) * Vector3.forward * (_lookAhead * (1f + ratio));
            Vector3 lookPoint = _target.Position + ahead + Vector3.up * 1.2f;

            return Quaternion.LookRotation(lookPoint - DesiredPosition(), Vector3.up);
        }
    }
}
